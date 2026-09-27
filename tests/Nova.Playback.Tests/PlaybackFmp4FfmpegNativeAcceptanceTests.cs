using System.Buffers.Binary;
using Xunit.Abstractions;

namespace PeerCast.Nova.Playback.Tests;

public sealed class FfmpegNativeAcceptanceFactAttribute : FactAttribute
{
    public FfmpegNativeAcceptanceFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
        {
            Skip = "Requires the Windows x64 FFmpeg 8.1 native ABI boundary.";
            return;
        }

        var runtimeDirectory = Environment.GetEnvironmentVariable(
            PlaybackFmp4FfmpegNativeAcceptanceTests.RuntimeDirectoryEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(runtimeDirectory) ||
            !Directory.Exists(runtimeDirectory))
        {
            Skip = "Set NOVA_FMP4_FFMPEG_TEST_RUNTIME to a directory matching the test inventory.";
        }
    }
}

public sealed class PlaybackFmp4FfmpegNativeAcceptanceTests(ITestOutputHelper output)
{
    public const string RuntimeDirectoryEnvironmentVariable =
        "NOVA_FMP4_FFMPEG_TEST_RUNTIME";

    private static readonly PlaybackFfmpegExternalRuntimeManifest TestRuntimeManifest =
        CreateTestRuntimeManifest();

    [FfmpegNativeAcceptanceFact]
    public async Task RealFragmentedMp4_DecodesAvc1AndAacThroughManagedSessionAndSink()
    {
        var runtimeDirectory = Environment.GetEnvironmentVariable(
            RuntimeDirectoryEnvironmentVariable)!;
        var runtimeResult = await PlaybackDecoderBackendInstalledRuntimeLocator
            .LocateAndVerifyAsync(runtimeDirectory, TestRuntimeManifest.RuntimeInventory);
        Assert.Equal(
            PlaybackDecoderBackendInstalledRuntimeLocation.Verified,
            runtimeResult.Location);
        using var runtimeLease = Assert.IsType<VerifiedPlaybackDecoderRuntimeLease>(
            runtimeResult.Lease);
        var nativeGate = new PlaybackDecoderFfmpegAbiSymbolGate().Acquire(runtimeLease);
        output.WriteLine($"Runtime ABI/symbol gate: {nativeGate.Result}");
        nativeGate.Lease?.Dispose();
        Assert.Equal(PlaybackDecoderFfmpegAbiSymbolGateResult.Verified, nativeGate.Result);
        Assert.True(TestRuntimeManifest.IsHostSupplied);
        Assert.Equal(PlaybackFfmpegExternalRuntimeAuthorization.HostAttested,
            TestRuntimeManifest.Authorization);
        Assert.Equal(PlaybackFfmpegExternalRuntimeReview.Unreviewed,
            TestRuntimeManifest.Review);

        var fixture = ReadFixture();
        var (initializationSegment, mediaFragments) = SplitFixture(fixture);
        var previousSequence = BinaryPrimitives.ReadUInt32BigEndian(
            mediaFragments[^1].AsSpan(20, 4));
        var discontinuousFragment = CreateSequenceGapFragment(
            mediaFragments[0],
            checked(previousSequence + 2));
        var createAdapterResult = PlaybackFmp4LiveEncodedSampleBatchAdapter.Create(
            initializationSegment,
            out var adapter);
        Assert.Equal(
            PlaybackFmp4LiveEncodedSampleBatchAdapterCreateResult.Created,
            createAdapterResult);
        var ownedAdapter = Assert.IsType<PlaybackFmp4LiveEncodedSampleBatchAdapter>(adapter);
        var videoTrack = ownedAdapter.StreamDescription.TrackDescriptions.Single(
            static track => track.MediaRole == PlaybackEncodedMediaRole.Video);
        var audioTrack = ownedAdapter.StreamDescription.TrackDescriptions.Single(
            static track => track.MediaRole == PlaybackEncodedMediaRole.Audio);
        Assert.Equal(PlaybackEncodedCodecVariant.Avc1, videoTrack.CodecVariant);
        Assert.Equal(PlaybackEncodedCodecVariant.Aac, audioTrack.CodecVariant);
        Assert.Equal(new PlaybackEncodedTimeBase(1, 90_000), videoTrack.TimeBase);
        Assert.Equal(new PlaybackEncodedTimeBase(1, 48_000), audioTrack.TimeBase);

        var sink = new CapturingDecodedFrameSink();
        var factory = new PlaybackFmp4FfmpegDecoderFactory(runtimeLease);
        Assert.Equal(
            PlaybackFmp4LiveDecoderSessionCreateResult.Created,
            PlaybackFmp4LiveDecoderSession.Create(
                ownedAdapter.StreamDescription,
                factory,
                out var session,
                sink));
        var ownedSession = Assert.IsType<PlaybackFmp4LiveDecoderSession>(session);
        await using (ownedSession.ConfigureAwait(false))
        {
            var submittedPackets = 0;
            var reorderedVideoPackets = 0;
            var submittedFragments = mediaFragments.Append(discontinuousFragment).ToArray();
            var videoFramesBeforeReset = -1;
            var audioFramesBeforeReset = -1;
            for (var fragmentIndex = 0; fragmentIndex < submittedFragments.Length; fragmentIndex++)
            {
                var fragment = submittedFragments[fragmentIndex];
                Assert.Equal(
                    PlaybackEncodedSampleBatchCreationResult.Created,
                    ownedAdapter.CreateBatch(fragment, out var batch));
                using var ownedBatch = Assert.IsType<PlaybackEncodedSampleBatch>(batch);
                Assert.Equal(
                    fragmentIndex == submittedFragments.Length - 1
                        ? PlaybackEncodedDiscontinuity.Global
                        : PlaybackEncodedDiscontinuity.None,
                    ownedBatch.Discontinuity);
                submittedPackets = checked(submittedPackets + ownedBatch.Samples.Count);
                reorderedVideoPackets = checked(
                    reorderedVideoPackets +
                    ownedBatch.Samples.Count(sample =>
                        sample.TrackId == videoTrack.TrackId &&
                        sample.PtsTicks != sample.DtsTicks));
                if (fragmentIndex == submittedFragments.Length - 1)
                {
                    videoFramesBeforeReset = sink.VideoFrameCount;
                    audioFramesBeforeReset = sink.AudioFrameCount;
                }

                await ownedSession.DecodeBatchAsync(ownedBatch);
            }

            await ownedSession.CompleteAsync();
            Assert.True(sink.VideoFrameCount > 0, "FFmpeg must emit a decoded H.264 frame.");
            Assert.True(sink.AudioFrameCount > 0, "FFmpeg must emit a decoded AAC frame.");
            Assert.True(videoFramesBeforeReset >= 0 &&
                sink.VideoFrameCount > videoFramesBeforeReset,
                "The discontinuity reset must leave the video decoder usable.");
            Assert.True(audioFramesBeforeReset >= 0 &&
                sink.AudioFrameCount > audioFramesBeforeReset,
                "The discontinuity reset must leave the audio decoder usable.");
            Assert.True(sink.ResetVideoFrameCount + sink.ResetAudioFrameCount > 0,
                "At least one decoded frame must carry the reset boundary.");
            Assert.NotNull(sink.FirstVideoFrame);
            Assert.NotNull(sink.FirstAudioFrame);
            var firstVideoFrame = sink.FirstVideoFrame!.Value;
            var firstAudioFrame = sink.FirstAudioFrame!.Value;
            Assert.Equal(64, firstVideoFrame.Width);
            Assert.Equal(48, firstVideoFrame.Height);
            Assert.Equal(PlaybackDecodedVideoPixelFormat.Bgra32, firstVideoFrame.PixelFormat);
            Assert.Equal(48_000, firstAudioFrame.SampleRate);
            Assert.Equal(2, firstAudioFrame.ChannelCount);
            Assert.True(firstAudioFrame.SampleCount > 0);
            Assert.Equal(PlaybackDecodedFrameDiscontinuityKind.None,
                firstVideoFrame.Discontinuity);
            Assert.Equal(PlaybackDecodedFrameDiscontinuityKind.None,
                firstAudioFrame.Discontinuity);

            output.WriteLine($"Fixture SHA-256: {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fixture))}");
            output.WriteLine($"Runtime manifest: {TestRuntimeManifest.BuildId}; {TestRuntimeManifest.Version}; host-attested/unreviewed");
            output.WriteLine($"Video setup: H.264/Avc1, avcC {videoTrack.CodecConfiguration.Count} bytes, {videoTrack.TimeBase.Numerator}/{videoTrack.TimeBase.Denominator}, context opened");
            output.WriteLine($"Audio setup: AAC-LC, AudioSpecificConfig {audioTrack.CodecConfiguration.Count} bytes, {audioTrack.TimeBase.Numerator}/{audioTrack.TimeBase.Denominator}, context opened");
            output.WriteLine($"Packet form: AVCC length-prefixed video and raw AAC access units; no Annex-B or ADTS conversion");
            output.WriteLine($"Encoded packet callbacks: {submittedPackets}; PTS != DTS video packets: {reorderedVideoPackets}");
            output.WriteLine($"Video frames: {sink.VideoFrameCount} total; {sink.VideoFrameCount - videoFramesBeforeReset} after reset; first: {firstVideoFrame.Width}x{firstVideoFrame.Height} {firstVideoFrame.PixelFormat} ts={firstVideoFrame.Timestamp}");
            output.WriteLine($"Audio frames: {sink.AudioFrameCount} total; {sink.AudioFrameCount - audioFramesBeforeReset} after reset; first: {firstAudioFrame.SampleRate} Hz / {firstAudioFrame.ChannelCount} ch / {firstAudioFrame.SampleCount} samples {firstAudioFrame.SampleFormat} ts={firstAudioFrame.Timestamp}");
        }
    }

    private static byte[] ReadFixture()
    {
        using var stream = typeof(PlaybackFmp4FfmpegNativeAcceptanceTests)
            .Assembly
            .GetManifestResourceStream(
                "PeerCast.Nova.Playback.Tests.Fixtures.fmp4-avc1-aac.mp4");
        Assert.NotNull(stream);
        using var ownedStream = stream!;
        using var bytes = new MemoryStream();
        ownedStream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static PlaybackFfmpegExternalRuntimeManifest CreateTestRuntimeManifest()
    {
        var creation = PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied(
            "btbn.ffmpeg.8.1.3.autobuild.2026-09-26-13-03",
            "n8.1.3-20260926",
            PlaybackFfmpegExternalRuntimeManifest.RequiredDeclaredLicenseIdentifier,
            PlaybackFfmpegExternalRuntimeBuildConfiguration.Required,
            [
                new(
                    PlaybackFfmpegExternalRuntimeLibraryRole.AvCodec,
                    "avcodec-62.dll",
                    62,
                    90_991_616,
                    "6B5E1673069C80A56CA0E13239280A65D7D2BAABC57777C58E898E3770C0B9C4"),
                new(
                    PlaybackFfmpegExternalRuntimeLibraryRole.AvFormat,
                    "avformat-62.dll",
                    62,
                    22_722_048,
                    "460FB6AEDDB48B3A4310EC8CB53EEDD8F54C72C986E8CEF6444A3FD257C81CCC"),
                new(
                    PlaybackFfmpegExternalRuntimeLibraryRole.AvUtil,
                    "avutil-60.dll",
                    60,
                    3_013_120,
                    "28CB4FC673DD0FF47BB66D0A33DE29CB44ED906CA52E50DBE65D5F71135247E0"),
                new(
                    PlaybackFfmpegExternalRuntimeLibraryRole.SwResample,
                    "swresample-6.dll",
                    6,
                    734_720,
                    "106A35D51FEB30E0F72887E87058F0F49497FAB00350EC0EBDE9F32C72F93222"),
                new(
                    PlaybackFfmpegExternalRuntimeLibraryRole.SwScale,
                    "swscale-9.dll",
                    9,
                    12_900_864,
                    "6F7FFA0C8EBF31DD807ECB96E03D52D44783A889A8D819E5B5F4AC3585C86AAA"),
            ]);
        return Assert.IsType<PlaybackFfmpegExternalRuntimeManifest>(creation.Manifest);
    }

    private static (byte[] InitializationSegment, IReadOnlyList<byte[]> MediaFragments)
        SplitFixture(ReadOnlyMemory<byte> fixture)
    {
        var bytes = fixture.Span;
        var boxes = new List<TopLevelBox>();
        var offset = 0;
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 8)
            {
                throw new InvalidDataException("The fixture has a truncated top-level box.");
            }

            var size = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
            var type = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset + 4, 4));
            var headerSize = 8;
            ulong boxSize = size;
            if (size == 1)
            {
                if (bytes.Length - offset < 16)
                {
                    throw new InvalidDataException("The fixture has a truncated large box.");
                }

                boxSize = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(offset + 8, 8));
                headerSize = 16;
            }
            else if (size == 0)
            {
                boxSize = checked((ulong)(bytes.Length - offset));
            }

            if (boxSize < (ulong)headerSize || boxSize > (ulong)(bytes.Length - offset))
            {
                throw new InvalidDataException("The fixture has an invalid top-level box length.");
            }

            boxes.Add(new(offset, checked((int)boxSize), type));
            offset = checked(offset + (int)boxSize);
        }

        var firstMediaIndex = boxes.FindIndex(static box => box.Type == FourCc("moof"));
        if (firstMediaIndex <= 0 ||
            !boxes.Take(firstMediaIndex).Any(static box => box.Type == FourCc("ftyp")) ||
            !boxes.Take(firstMediaIndex).Any(static box => box.Type == FourCc("moov")))
        {
            throw new InvalidDataException("The fixture is missing its fMP4 initialization boxes.");
        }

        var initializationLength = boxes[firstMediaIndex].Offset;
        var initialization = bytes[..initializationLength].ToArray();
        var media = new List<byte[]>();
        for (var index = firstMediaIndex; index < boxes.Count;)
        {
            var moof = boxes[index];
            if (moof.Type != FourCc("moof") || index + 1 >= boxes.Count ||
                boxes[index + 1].Type != FourCc("mdat"))
            {
                throw new InvalidDataException("The fixture has an unsupported media-fragment layout.");
            }

            var mdat = boxes[index + 1];
            var end = checked(mdat.Offset + mdat.Length);
            media.Add(bytes[moof.Offset..end].ToArray());
            index += 2;
        }

        if (media.Count == 0)
        {
            throw new InvalidDataException("The fixture has no media fragments.");
        }

        return (initialization, media.AsReadOnly());
    }

    private static byte[] CreateSequenceGapFragment(
        ReadOnlySpan<byte> fragment,
        uint sequenceNumber)
    {
        var copy = fragment.ToArray();
        if (copy.Length < 24)
        {
            throw new InvalidDataException("The fixture media fragment is too short.");
        }

        var moofLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(copy));
        if (moofLength > copy.Length || moofLength < 24)
        {
            throw new InvalidDataException("The fixture moof box is invalid.");
        }

        var cursor = 8;
        var sequencePatched = false;
        while (cursor < moofLength)
        {
            if (moofLength - cursor < 8)
            {
                throw new InvalidDataException("The fixture moof has a truncated child box.");
            }

            var boxLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
                copy.AsSpan(cursor, 4)));
            var type = BinaryPrimitives.ReadUInt32BigEndian(copy.AsSpan(cursor + 4, 4));
            if (boxLength < 8 || boxLength > moofLength - cursor)
            {
                throw new InvalidDataException("The fixture moof child length is invalid.");
            }

            if (type == FourCc("mfhd"))
            {
                if (boxLength < 16)
                {
                    throw new InvalidDataException("The fixture mfhd box is invalid.");
                }

                var sequenceOffset = cursor + 12;
                BinaryPrimitives.WriteUInt32BigEndian(
                    copy.AsSpan(sequenceOffset, 4),
                    sequenceNumber);
                sequencePatched = true;
                break;
            }

            cursor += boxLength;
        }

        if (!sequencePatched)
        {
            throw new InvalidDataException("The fixture is missing mfhd.");
        }

        return copy;
    }

    private static uint FourCc(string value) =>
        BinaryPrimitives.ReadUInt32BigEndian(System.Text.Encoding.ASCII.GetBytes(value));

    private readonly record struct TopLevelBox(int Offset, int Length, uint Type);

    private sealed class CapturingDecodedFrameSink : IPlaybackDecodedFrameSink
    {
        public int VideoFrameCount { get; private set; }

        public int AudioFrameCount { get; private set; }

        public int ResetVideoFrameCount { get; private set; }

        public int ResetAudioFrameCount { get; private set; }

        public PlaybackDecodedVideoFrame? FirstVideoFrame { get; private set; }

        public PlaybackDecodedAudioFrame? FirstAudioFrame { get; private set; }

        public ValueTask WriteAudioAsync(
            PlaybackDecodedAudioFrame frame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AudioFrameCount++;
            if (frame.Discontinuity == PlaybackDecodedFrameDiscontinuityKind.DecoderReset)
            {
                ResetAudioFrameCount++;
            }

            FirstAudioFrame ??= PlaybackDecodedAudioFrame.Create(
                frame.Sequence,
                frame.Timestamp,
                frame.Duration,
                frame.Discontinuity,
                frame.SampleFormat,
                frame.SampleRate,
                frame.ChannelCount,
                frame.SampleCount,
                frame.Payload.ToArray());
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteVideoAsync(
            PlaybackDecodedVideoFrame frame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VideoFrameCount++;
            if (frame.Discontinuity == PlaybackDecodedFrameDiscontinuityKind.DecoderReset)
            {
                ResetVideoFrameCount++;
            }

            FirstVideoFrame ??= PlaybackDecodedVideoFrame.Create(
                frame.Sequence,
                frame.Timestamp,
                frame.Duration,
                frame.Discontinuity,
                frame.PixelFormat,
                frame.Width,
                frame.Height,
                frame.Stride,
                frame.Payload.ToArray());
            return ValueTask.CompletedTask;
        }
    }

}
