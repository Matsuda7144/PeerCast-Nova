using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using PeerCast.Nova.Core.Media;
using PeerCast.Nova.MediaEncoding;
using Xunit.Abstractions;

namespace PeerCast.Nova.Playback.Tests;

public sealed class MediaEncodingNativeAcceptanceTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };
    private const int ExpectedAacInputBlocks = 150;
    private const int AacSamplesPerBlock = 1024;
    private const int ExpectedAacPrimingSamples = 1024;
    private const int ExpectedAacPackets = 151;
    private const int ExpectedAacFlushPackets = 2;
    // Fixed raw input ends at 153600 native ticks; retained priming adds 1024 decoded samples.
    private const long ExpectedAacNativeEnd = (long)ExpectedAacInputBlocks * AacSamplesPerBlock;
    private const long ExpectedDecodedPcmSamples = ExpectedAacNativeEnd + ExpectedAacPrimingSamples;

    [FfmpegNativeAcceptanceFact]
    public async Task InvalidInputAndUnavailableRuntimeAreRejectedBeforeEncoding()
    {
        using var runtime = await EncodingAcceptanceRuntime.LocateAsync();
        using var video = new H264FfmpegEncoderSession(runtime, new(64, 48));
        using var audio = new AacFfmpegEncoderSession(runtime, new());
        Assert.Throws<ArgumentException>(() =>
            video.Encode(new RawYuv420VideoFrame(16, 16, 0, new byte[384])));
        Assert.Throws<ArgumentException>(() => video.Encode(SyntheticEncodingSource.Video(1)));
        Assert.Throws<ArgumentException>(() => audio.Encode(SyntheticEncodingSource.Audio(1)));
        Assert.Equal(0, video.Snapshot.SubmittedFrameCount);
        Assert.Equal(0, audio.Snapshot.SubmittedFrameCount);
        Assert.NotEmpty(video.Encode(SyntheticEncodingSource.Video(0)).Concat(video.Complete()));
        Assert.NotEmpty(audio.Encode(SyntheticEncodingSource.Audio(0)).Concat(audio.Complete()));
        video.Dispose();
        audio.Dispose();
        Assert.Throws<ObjectDisposedException>(() => video.Encode(SyntheticEncodingSource.Video(1)));
        Assert.Throws<ObjectDisposedException>(() => audio.Encode(SyntheticEncodingSource.Audio(1)));
        runtime.Dispose();
        Assert.Throws<InvalidOperationException>(() => new H264FfmpegEncoderSession(runtime, new(64, 48)));
        Assert.Throws<InvalidOperationException>(() => new AacFfmpegEncoderSession(runtime, new()));
    }
    [FfmpegNativeAcceptanceFact]
    public async Task SyntheticRawSourcesEncodeAndDirectlyDecodeWithoutMux()
    {
        var encoded = await EncodeAsync();
        await AssertDirectDecodeAsync(encoded, videoOnly: true);
        await AssertDirectDecodeAsync(encoded, videoOnly: false);
    }

    [FfmpegNativeAcceptanceFact]
    public async Task SyntheticEncodedSamplesMuxReparseAndNativeDecode()
    {
        var encoded = await EncodeAsync();
        // These are distinct from decode-after-mux and must succeed first.
        await AssertDirectDecodeAsync(encoded, videoOnly: true);
        await AssertDirectDecodeAsync(encoded, videoOnly: false);
        var samples = ToMuxSamples(encoded);
        var chunks = samples.GroupBy(sample => Math.Min(3, sample.DtsTicks / 38_400))
            .Select(static group => group.ToArray()).ToArray();
        Assert.Equal(4, chunks.Length);
        Assert.All(chunks, chunk => Assert.InRange(chunk.Length, 1, 64));
        Assert.Equal(Fmp4MuxerCreateResult.Created, Fmp4Muxer.Create(
            [
                new(1, Fmp4MuxMediaKind.Video, Fmp4MuxCodec.Avc1, 48_000,
                    encoded.Avc.ConfigurationRecord, width: 64, height: 48),
                new(2, Fmp4MuxMediaKind.Audio, Fmp4MuxCodec.AacLc, 48_000,
                    encoded.Asc, sampleRate: 48_000, channelCount: 2),
            ], out var created));
        using var muxer = Assert.IsType<Fmp4Muxer>(created);
        Assert.Equal(Fmp4MuxerResult.Produced, muxer.GenerateInitializationSegment(out var initialization));
        var init = Assert.IsType<byte[]>(initialization);
        var fragments = new List<byte[]>();
        foreach (var chunk in chunks)
        {
            Assert.Equal(Fmp4MuxerResult.Produced, muxer.MuxFragment(chunk, out var media));
            fragments.Add(Assert.IsType<Fmp4MuxMediaSegment>(media).Bytes);
        }
        Assert.Equal(PlaybackFmp4LiveEncodedSampleBatchAdapterCreateResult.Created,
            PlaybackFmp4LiveEncodedSampleBatchAdapter.Create(init, out var createdAdapter));
        var adapter = Assert.IsType<PlaybackFmp4LiveEncodedSampleBatchAdapter>(createdAdapter);
        Assert.Equal(encoded.Avc.ConfigurationRecord.ToArray(),
            adapter.StreamDescription.TrackDescriptions.Single(t => t.TrackId == 1).CodecConfiguration);
        Assert.Equal(encoded.Asc.ToArray(),
            adapter.StreamDescription.TrackDescriptions.Single(t => t.TrackId == 2).CodecConfiguration);
        Assert.All(adapter.StreamDescription.TrackDescriptions, track =>
            Assert.Equal(new PlaybackEncodedTimeBase(1, 48_000), track.TimeBase));
        for (var fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
        {
            Assert.Equal(PlaybackEncodedSampleBatchCreationResult.Created,
                adapter.CreateBatch(fragments[fragmentIndex], out var createdBatch));
            using var batch = Assert.IsType<PlaybackEncodedSampleBatch>(createdBatch);
            Assert.Equal(PlaybackEncodedDiscontinuity.None, batch.Discontinuity);
            Assert.Equal(chunks[fragmentIndex].Length, batch.Samples.Count);
            // BMFF serializes track runs; compare semantic identity independently
            // of cross-track arrival order. The decoder session schedules by DTS.
            var expectedSamples = chunks[fragmentIndex].OrderBy(static sample => sample.TrackId)
                .ThenBy(static sample => sample.DtsTicks).ToArray();
            var actualSamples = batch.Samples.OrderBy(static sample => sample.TrackId)
                .ThenBy(static sample => sample.DtsTicks).ToArray();
            for (var index = 0; index < batch.Samples.Count; index++)
            {
                var expected = expectedSamples[index];
                var actual = actualSamples[index];
                Assert.Equal(expected.TrackId, actual.TrackId);
                Assert.Equal(expected.DtsTicks, actual.DtsTicks);
                Assert.Equal(expected.PtsTicks, actual.PtsTicks);
                Assert.Equal(expected.DurationTicks, actual.DurationTicks);
                Assert.Equal(MapRandomAccess(expected), actual.RandomAccessClassification);
                var payload = new byte[actual.PayloadSlice.Length];
                batch.CopyPayloadTo(actual, payload);
                Assert.Equal(expected.Payload.ToArray(), payload);
            }
        }

        var sink = new EncodingDecodedSink();
        Assert.Equal(PlaybackFmp4LiveEncodedSampleBatchAdapterCreateResult.Created,
            PlaybackFmp4LiveEncodedSampleBatchAdapter.Create(init, out var decodeAdapter));
        var ownedDecodeAdapter = Assert.IsType<PlaybackFmp4LiveEncodedSampleBatchAdapter>(decodeAdapter);
        using var runtime = await EncodingAcceptanceRuntime.LocateAsync();
        Assert.Equal(PlaybackFmp4LiveDecoderSessionCreateResult.Created,
            PlaybackFmp4LiveDecoderSession.Create(ownedDecodeAdapter.StreamDescription,
                new PlaybackFmp4FfmpegDecoderFactory(runtime), out var createdSession, sink));
        await using var session = Assert.IsType<PlaybackFmp4LiveDecoderSession>(createdSession);
        foreach (var fragment in fragments)
        {
            Assert.Equal(PlaybackEncodedSampleBatchCreationResult.Created,
                ownedDecodeAdapter.CreateBatch(fragment, out var createdBatch));
            using var batch = Assert.IsType<PlaybackEncodedSampleBatch>(createdBatch);
            Assert.Equal(PlaybackEncodedDiscontinuity.None, batch.Discontinuity);
            await session.DecodeBatchAsync(batch);
        }
        await session.CompleteAsync();
        Assert.Equal(encoded.Video.Length, sink.VideoFrames);
        Assert.Equal(ExpectedAacPackets, sink.AudioFrames);
        Assert.Equal(ExpectedDecodedPcmSamples, sink.AudioSamples);
        Assert.True(sink.DistinctVideoHashes.Count > 1);
        Assert.True(sink.MaximumAudioAmplitude > 0.05f);
        output.WriteLine($"Muxed native decode: video={sink.VideoFrames}; audio={sink.AudioFrames}; PCM={sink.AudioSamples}; EOS=PASS");
        output.WriteLine($"Generated init: {init.Length} bytes SHA256 {Hash(init)}");
        for (var index = 0; index < fragments.Count; index++)
        {
            output.WriteLine($"Generated fragment seq={index + 1}: {fragments[index].Length} bytes SHA256 {Hash(fragments[index])}");
        }
        var directory = Environment.GetEnvironmentVariable("NOVA_ENCODING_ACCEPTANCE_OUTPUT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Assert.False(Directory.Exists(directory), "Refusing to overwrite an earlier acceptance generation.");
            Directory.CreateDirectory(directory);
            var combined = init.Concat(fragments.SelectMany(static fragment => fragment)).ToArray();
            File.WriteAllBytes(Path.Combine(directory, "init.mp4"), init);
            for (var index = 0; index < fragments.Count; index++)
            {
                File.WriteAllBytes(Path.Combine(directory, $"fragment-{index + 1:D4}.m4s"), fragments[index]);
            }
            File.WriteAllBytes(Path.Combine(directory, "nova-raw-encoded-playback.mp4"), combined);
            File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(new
            {
                generationId = Path.GetFileName(directory),
                head = Environment.GetEnvironmentVariable("NOVA_ENCODING_ACCEPTANCE_HEAD"),
                rawVideoFrames = 96,
                rawAudioFrames = 150,
                rawPcmSamples = 153600,
                encoded.VideoSnapshot,
                encoded.AudioSnapshot,
                videoTimeBase = "1/30",
                audioTimeBase = "1/48000",
                muxTimeBase = "1/48000",
                timelineShiftSamples = encoded.InitialPadding,
                firstNativeAudioDts = encoded.Audio[0].DecodeTimestamp,
                sps = encoded.Avc.SequenceParameterSets.Select(nal => new { length = nal.Length, sha256 = Hash(nal.Span) }),
                pps = encoded.Avc.PictureParameterSets.Select(nal => new { length = nal.Length, sha256 = Hash(nal.Span) }),
                encoded.Avc.Profile,
                encoded.Avc.Compatibility,
                encoded.Avc.Level,
                avcC = new { length = encoded.Avc.ConfigurationRecord.Length, sha256 = Hash(encoded.Avc.ConfigurationRecord.Span) },
                asc = new { length = encoded.Asc.Length, sha256 = Hash(encoded.Asc.Span) },
                init = new { length = init.Length, sha256 = Hash(init) },
                fragments = fragments.Select((fragment, index) => new
                {
                    sequence = index + 1,
                    length = fragment.Length,
                    sha256 = Hash(fragment),
                    videoSamples = chunks[index].Count(static sample => sample.TrackId == 1),
                    audioSamples = chunks[index].Count(static sample => sample.TrackId == 2),
                }),
                combined = new { length = combined.Length, sha256 = Hash(combined) },
                directVideoDecode = "PASS",
                directAudioDecode = "PASS",
                semanticReparse = "PASS",
                muxedNativeVideoFrames = sink.VideoFrames,
                muxedNativeAudioFrames = sink.AudioFrames,
                muxedNativePcmSamples = sink.AudioSamples,
                retainedEncodedFixtureBytesUsedAsEncoderOutput = false,
            }, ManifestJsonOptions));
            output.WriteLine($"Generated combined: {combined.Length} bytes SHA256 {Hash(combined)}; path={directory}");
        }
    }

    private async Task<SyntheticEncodedStreams> EncodeAsync()
    {
        using var runtime = await EncodingAcceptanceRuntime.LocateAsync();
        var encodingGate = new PlaybackDecoderFfmpegAbiSymbolGate().AcquireForEncoding(runtime);
        Assert.Equal(PlaybackDecoderFfmpegAbiSymbolGateResult.Verified, encodingGate.Result);
        encodingGate.Lease!.Dispose();
        using var video = new H264FfmpegEncoderSession(runtime, new(64, 48));
        using var audio = new AacFfmpegEncoderSession(runtime, new());
        Assert.Equal(66, video.CodecConfiguration.Profile);
        Assert.Equal(4, video.CodecConfiguration.NalLengthSize);
        Assert.NotEmpty(video.CodecConfiguration.SequenceParameterSets);
        Assert.NotEmpty(video.CodecConfiguration.PictureParameterSets);
        Assert.NotEmpty(audio.CodecConfiguration.ToArray());
        Assert.Equal(AacSamplesPerBlock, audio.SamplesPerInputFrame);
        Assert.Equal(ExpectedAacPrimingSamples, audio.InitialPaddingSamples);
        Assert.Equal(ExpectedAacInputBlocks, SyntheticEncodingSource.AudioFrameCount);
        var videoPackets = new List<MediaEncodedSample>();
        var audioPackets = new List<MediaEncodedSample>();
        byte[]? firstVideo = null;
        byte[]? firstAudio = null;
        for (var index = 0; index < SyntheticEncodingSource.VideoFrameCount; index++)
        {
            videoPackets.AddRange(video.Encode(SyntheticEncodingSource.Video(index)));
            firstVideo ??= videoPackets.FirstOrDefault()?.Payload.ToArray();
        }

        for (var index = 0; index < SyntheticEncodingSource.AudioFrameCount; index++)
        {
            audioPackets.AddRange(audio.Encode(SyntheticEncodingSource.Audio(index)));
            firstAudio ??= audioPackets.FirstOrDefault()?.Payload.ToArray();
        }

        videoPackets.AddRange(video.Complete());
        audioPackets.AddRange(audio.Complete());
        Assert.Empty(video.Complete());
        Assert.Empty(audio.Complete());
        Assert.Equal(96, videoPackets.Count);
        // Exact pinned-scenario expectations prevent a shortened EOS tail from validating itself.
        Assert.Equal(ExpectedAacInputBlocks, audio.Snapshot.SubmittedFrameCount);
        Assert.Equal(ExpectedAacPackets, audioPackets.Count);
        Assert.Equal(ExpectedAacPackets, audio.Snapshot.EncodedPacketCount);
        Assert.Equal(firstVideo, videoPackets[0].Payload.ToArray());
        Assert.Equal(firstAudio, audioPackets[0].Payload.ToArray());
        Assert.Contains(videoPackets, packet => packet.IsKeyFrame);
        Assert.Equal(ExpectedAacFlushPackets, audio.Snapshot.FlushPacketCount);
        Assert.Equal(-audio.InitialPaddingSamples, audioPackets[0].DecodeTimestamp);
        Assert.Equal(1, video.TimeBaseNumerator);
        Assert.Equal(30, video.TimeBaseDenominator);
        Assert.Equal(48_000, audio.TimeBaseDenominator);
        AssertPacketTiming(videoPackets, 1);
        AssertPacketTiming(audioPackets, AacSamplesPerBlock);
        Assert.Equal(ExpectedAacNativeEnd,
            checked(audioPackets[^1].DecodeTimestamp + audioPackets[^1].Duration));
        Assert.Equal(ExpectedAacNativeEnd,
            checked(audioPackets[^1].PresentationTimestamp + audioPackets[^1].Duration));
        Assert.All(audioPackets, packet => Assert.False(IsAdts(packet.Payload.Span)));
        Assert.Throws<InvalidOperationException>(() => video.Encode(SyntheticEncodingSource.Video(96)));
        Assert.Throws<InvalidOperationException>(() => audio.Encode(SyntheticEncodingSource.Audio(150)));
        output.WriteLine($"H264: libopenh264; YUV420P 64x48; 1/30; raw={video.Snapshot.SubmittedFrameCount}; packets={video.Snapshot.EncodedPacketCount}; flush={video.Snapshot.FlushPacketCount}; native framing={video.NativePacketFraming}; output=AVCC4");
        output.WriteLine($"AAC: AAC-LC; 48000/2; PCM interleaved -> FLTP; 1/48000; raw={audio.Snapshot.SubmittedFrameCount}; packets={audio.Snapshot.EncodedPacketCount}; flush={audio.Snapshot.FlushPacketCount}; initial padding={audio.InitialPaddingSamples}; output=raw AAC");
        output.WriteLine($"AAC terminal: DTS={audioPackets[^1].DecodeTimestamp}; PTS={audioPackets[^1].PresentationTimestamp}; duration={audioPackets[^1].Duration}; native end={ExpectedAacNativeEnd}; expected decoded PCM={ExpectedDecodedPcmSamples}");
        var result = new SyntheticEncodedStreams(video.CodecConfiguration,
            audio.CodecConfiguration, videoPackets.ToArray(), audioPackets.ToArray(),
            video.Snapshot, audio.Snapshot, audio.InitialPaddingSamples);
        video.Dispose();
        audio.Dispose();
        video.Dispose();
        audio.Dispose();
        Assert.Throws<ObjectDisposedException>(() => video.Complete());
        Assert.Throws<ObjectDisposedException>(() => audio.Complete());
        return result;
    }

    private async Task AssertDirectDecodeAsync(SyntheticEncodedStreams encoded, bool videoOnly)
    {
        using var runtime = await EncodingAcceptanceRuntime.LocateAsync();
        var stream = CreateStream(encoded);
        var sink = new EncodingDecodedSink();
        var factory = new PlaybackFmp4FfmpegDecoderFactory(runtime);
        await using var decoder = Assert.IsAssignableFrom<IPlaybackDecoder>(
            factory.Create(PlaybackDecoderCreationContext.CreateForFmp4(stream, sink)));
        var samples = ToMuxSamples(encoded).Where(sample => sample.TrackId == (videoOnly ? 1u : 2u)).ToArray();
        for (var index = 0; index < samples.Length; index++)
        {
            var sample = samples[index];
            var track = stream.TrackDescriptions.Single(t => t.TrackId == sample.TrackId);
            await decoder.DecodeAsync(PlaybackEncodedInput.CreateFmp4Sample(index,
                PlaybackDiscontinuityKind.None, sample.Payload, new(stream.StreamGenerationId,
                    track.TrackId, track.MediaRole, track.CodecKind, track.CodecVariant,
                    track.TimeBase, 0, 1, index, sample.DtsTicks, sample.PtsTicks, sample.DurationTicks,
                    MapRandomAccess(sample))), CancellationToken.None);
        }

        await decoder.CompleteAsync(CancellationToken.None);
        if (videoOnly)
        {
            Assert.Equal(encoded.Video.Length, sink.VideoFrames);
            Assert.Equal(0, sink.AudioFrames);
            Assert.True(sink.DistinctVideoHashes.Count > 1);
        }
        else
        {
            Assert.Equal(ExpectedAacPackets, sink.AudioFrames);
            Assert.Equal(ExpectedDecodedPcmSamples, sink.AudioSamples);
            Assert.Equal(0, sink.VideoFrames);
            Assert.True(sink.MaximumAudioAmplitude > 0.05f);
        }

        output.WriteLine($"Direct encoder -> decoder {(videoOnly ? "Video" : "Audio")}: frames={samples.Length}; PCM={sink.AudioSamples}; EOS=PASS");
    }

    private static PlaybackEncodedStreamDescription CreateStream(SyntheticEncodedStreams encoded) =>
        new(Guid.NewGuid(), [
            new(1, PlaybackEncodedMediaRole.Video, PlaybackEncodedCodecKind.Avc,
                PlaybackEncodedCodecVariant.Avc1, encoded.Avc.ConfigurationRecord.ToArray(),
                new(1, 48_000), nalLengthSize: 4, width: 64, height: 48),
            new(2, PlaybackEncodedMediaRole.Audio, PlaybackEncodedCodecKind.Aac,
                PlaybackEncodedCodecVariant.Aac, encoded.Asc.ToArray(),
                new(1, 48_000), sampleRate: 48_000, channelCount: 2),
        ]);

    private static Fmp4MuxEncodedSample[] ToMuxSamples(SyntheticEncodedStreams encoded)
    {
        // Preserve every AAC packet. Shift both tracks by the reported AAC priming
        // at a common exact 48 kHz timescale instead of clipping negative packets.
        return encoded.Video.Select(packet => new Fmp4MuxEncodedSample(1,
            checked(packet.DecodeTimestamp * 1600 + encoded.InitialPadding),
            checked(packet.PresentationTimestamp * 1600 + encoded.InitialPadding),
            checked(packet.Duration * 1600), packet.Payload,
            packet.IsKeyFrame ? Fmp4MuxRandomAccessKind.IndependentlyDecodable :
                Fmp4MuxRandomAccessKind.NotIndependentlyDecodable))
            .Concat(encoded.Audio.Select(packet => new Fmp4MuxEncodedSample(2,
                checked(packet.DecodeTimestamp + encoded.InitialPadding),
                checked(packet.PresentationTimestamp + encoded.InitialPadding),
                packet.Duration, packet.Payload, Fmp4MuxRandomAccessKind.NotApplicable)))
            .OrderBy(static sample => sample.DtsTicks).ThenBy(static sample => sample.TrackId).ToArray();
    }

    private static PlaybackEncodedRandomAccessClassification MapRandomAccess(Fmp4MuxEncodedSample sample) =>
        sample.RandomAccessKind switch
        {
            Fmp4MuxRandomAccessKind.IndependentlyDecodable => PlaybackEncodedRandomAccessClassification.IndependentlyDecodable,
            Fmp4MuxRandomAccessKind.NotIndependentlyDecodable => PlaybackEncodedRandomAccessClassification.NotIndependentlyDecodable,
            _ => PlaybackEncodedRandomAccessClassification.NotApplicable,
        };

    private static void AssertPacketTiming(List<MediaEncodedSample> packets, long duration)
    {
        Assert.All(packets, packet => Assert.Equal(duration, packet.Duration));
        for (var index = 1; index < packets.Count; index++)
        {
            Assert.Equal(packets[index - 1].DecodeTimestamp + duration, packets[index].DecodeTimestamp);
            Assert.True(packets[index].PresentationTimestamp > packets[index - 1].PresentationTimestamp);
        }
    }

    private static bool IsAdts(ReadOnlySpan<byte> payload) =>
        payload.Length >= 7 && payload[0] == 0xff && (payload[1] & 0xf6) == 0xf0 &&
        (((payload[3] & 3) << 11) | (payload[4] << 3) | (payload[5] >> 5)) == payload.Length;

    private static string Hash(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data));

    private sealed record SyntheticEncodedStreams(H264EncoderCodecConfiguration Avc,
        ReadOnlyMemory<byte> Asc, MediaEncodedSample[] Video, MediaEncodedSample[] Audio,
        MediaEncoderSnapshot VideoSnapshot, MediaEncoderSnapshot AudioSnapshot, int InitialPadding);

    private sealed class EncodingDecodedSink : IPlaybackDecodedFrameSink
    {
        public int VideoFrames { get; private set; }
        public int AudioFrames { get; private set; }
        public long AudioSamples { get; private set; }
        public float MaximumAudioAmplitude { get; private set; }
        public HashSet<string> DistinctVideoHashes { get; } = [];

        public ValueTask WriteVideoAsync(PlaybackDecodedVideoFrame frame, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VideoFrames++;
            Assert.Equal(64, frame.Width);
            Assert.Equal(48, frame.Height);
            DistinctVideoHashes.Add(Hash(frame.Payload.Span));
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteAudioAsync(PlaybackDecodedAudioFrame frame, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AudioFrames++;
            AudioSamples += frame.SampleCount;
            Assert.Equal(48_000, frame.SampleRate);
            Assert.Equal(2, frame.ChannelCount);
            Assert.Equal(PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved, frame.SampleFormat);
            foreach (var sample in MemoryMarshal.Cast<byte, float>(frame.Payload.Span))
            {
                Assert.True(float.IsFinite(sample));
                MaximumAudioAmplitude = Math.Max(MaximumAudioAmplitude, Math.Abs(sample));
            }

            return ValueTask.CompletedTask;
        }
    }
}

internal static class EncodingAcceptanceRuntime
{
    public static async Task<VerifiedPlaybackDecoderRuntimeLease> LocateAsync()
    {
        var directory = Environment.GetEnvironmentVariable("NOVA_FMP4_FFMPEG_TEST_RUNTIME");
        var manifest = PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied(
            "btbn.ffmpeg.8.1.3.autobuild.2026-09-26-13-03", "n8.1.3-20260926",
            PlaybackFfmpegExternalRuntimeManifest.RequiredDeclaredLicenseIdentifier,
            PlaybackFfmpegExternalRuntimeBuildConfiguration.Required, [
                new(PlaybackFfmpegExternalRuntimeLibraryRole.AvCodec, "avcodec-62.dll", 62, 90_991_616,
                    "6B5E1673069C80A56CA0E13239280A65D7D2BAABC57777C58E898E3770C0B9C4"),
                new(PlaybackFfmpegExternalRuntimeLibraryRole.AvFormat, "avformat-62.dll", 62, 22_722_048,
                    "460FB6AEDDB48B3A4310EC8CB53EEDD8F54C72C986E8CEF6444A3FD257C81CCC"),
                new(PlaybackFfmpegExternalRuntimeLibraryRole.AvUtil, "avutil-60.dll", 60, 3_013_120,
                    "28CB4FC673DD0FF47BB66D0A33DE29CB44ED906CA52E50DBE65D5F71135247E0"),
                new(PlaybackFfmpegExternalRuntimeLibraryRole.SwResample, "swresample-6.dll", 6, 734_720,
                    "106A35D51FEB30E0F72887E87058F0F49497FAB00350EC0EBDE9F32C72F93222"),
                new(PlaybackFfmpegExternalRuntimeLibraryRole.SwScale, "swscale-9.dll", 9, 12_900_864,
                    "6F7FFA0C8EBF31DD807ECB96E03D52D44783A889A8D819E5B5F4AC3585C86AAA"),
            ]).Manifest!;
        var located = await PlaybackDecoderBackendInstalledRuntimeLocator.LocateAndVerifyAsync(
            directory!, manifest.RuntimeInventory);
        Assert.Equal(PlaybackDecoderBackendInstalledRuntimeLocation.Verified, located.Location);
        return Assert.IsType<VerifiedPlaybackDecoderRuntimeLease>(located.Lease);
    }
}
