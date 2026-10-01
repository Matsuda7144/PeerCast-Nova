using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using PeerCast.Nova.MediaEncoding;
using Xunit.Abstractions;

namespace PeerCast.Nova.Playback.Tests;

public sealed class LiveEncodingNativeAcceptanceTests(ITestOutputHelper output)
{
    private const int ScenarioVideoFrames = 192;
    private const int ScenarioAudioBlocks = 300;

    [Theory]
    [InlineData(191, 300, 1, false)] // Nearly complete pre-generation.
    [InlineData(191, 299, 2, false)] // Nearly complete while both old track checks pass.
    [InlineData(50, 80, 362, false)] // Delayed first delivery.
    [InlineData(10, 16, 466, false)] // Premature first delivery.
    [InlineData(25, 40, 1, false)] // Inconsistent remaining counter.
    [InlineData(26, 39, 427, false)] // Same total, wrong individual counters.
    [InlineData(25, 40, 427, true)] // Accepted deterministic boundary.
    public void First_publish_assertions_reject_shifted_and_inconsistent_boundaries(
        int videoConsumed, int audioConsumed, int remaining, bool accepted)
    {
        var error = Record.Exception(() => AssertFirstPublishBoundary(videoConsumed, audioConsumed, remaining));
        if (accepted) { Assert.Null(error); }
        else { Assert.IsAssignableFrom<Xunit.Sdk.XunitException>(error); }
    }

    [FfmpegNativeAcceptanceFact]
    public Task Paced_native_encoding_traverses_incremental_pcp_wire_and_managed_parser() => RunAsync(false);

    [FfmpegNativeAcceptanceFact]
    public Task Paced_native_encoding_traverses_incremental_pcp_wire_parser_and_native_decoder() => RunAsync(true);

    internal async Task RunAsync(bool nativeDecode,
        IPlaybackPcpFmp4PresentationOutputGenerationFactory? downstream = null,
        VerifiedPlaybackDecoderRuntimeLease? borrowedRuntime = null,
        Action? completed = null, CancellationToken cancellationToken = default)
    {
        using var ownedRuntime = borrowedRuntime is null ? await EncodingAcceptanceRuntime.LocateAsync() : null;
        var runtime = borrowedRuntime ?? ownedRuntime!;
        var encoder = new CapturingLiveEncoder(new NativeLiveEncodingSession(runtime));
        foreach (var track in encoder.Tracks)
        {
            output.WriteLine($"Live codec track={track.TrackId}; bytes={track.CodecConfiguration.Length}; sha256={Convert.ToHexString(SHA256.HashData(track.CodecConfiguration.Span))}");
        }
        var decoder = new LiveIdentityDecoderFactory(encoder,
            nativeDecode ? new PlaybackFmp4FfmpegDecoderFactory(runtime) : null);
        var decoded = new LiveDecodedOutput(downstream);
        await using var wire = new LivePcpWireLoopback(decoder, decoded);
        var source = new PacedSyntheticMediaSource();
        var fragments = new List<LiveEncodingFragmentSnapshot>();
        var elapsed = Stopwatch.StartNew();
        var firstPublishSeconds = -1d;
        var publisher = new LiveEncodingPublisher(source, wire.Channel, wire.Session, wire,
            fragmentPublished: fragment =>
            {
                if (fragments.Count == 0)
                {
                    firstPublishSeconds = elapsed.Elapsed.TotalSeconds;
                    var progress = source.Snapshot;
                    Assert.False(progress.Completed);
                    AssertFirstPublishBoundary(progress.VideoFrames, progress.AudioBlocks,
                        ScenarioVideoFrames + ScenarioAudioBlocks - progress.VideoFrames - progress.AudioBlocks);
                }
                fragments.Add(fragment);
                output.WriteLine(JsonSerializer.Serialize(fragment));
            });
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var result = await publisher.RunCoreAsync(() => encoder, deadline.Token);
        Assert.True(result.Completed); Assert.False(result.Cancelled);
        Assert.True(result.Source.Completed); Assert.True(result.Source.Stopped);
        Assert.Equal(ScenarioVideoFrames, result.Source.VideoFrames); Assert.Equal(ScenarioAudioBlocks, result.Source.AudioBlocks);
        Assert.Equal(192, result.Video.EncodedPacketCount);
        Assert.Equal(301, result.Audio.EncodedPacketCount); Assert.Equal(2, result.Audio.FlushPacketCount);
        Assert.True(result.Video.Completed); Assert.True(result.Audio.Completed);
        Assert.Equal(1024, result.InitialPaddingSamples);
        AssertFirstPublishBoundary(result.FirstPublishVideoFrames, result.FirstPublishAudioBlocks,
            result.SourceRemainingAtFirstPublish);
        Assert.True(result.SourceRemainingAtFirstPublish > 0);
        Assert.True(result.FirstPublishVideoFrames < 192); Assert.True(result.FirstPublishAudioBlocks < 300);
        Assert.InRange(firstPublishSeconds, 0.7, 15);
        Assert.InRange(elapsed.Elapsed.TotalSeconds, 6.2, 25);
        Assert.Equal(8, result.Fragments);
        Assert.Equal(Enumerable.Range(1, 8).Select(i => (uint)i), fragments.Select(f => f.Sequence));
        Assert.Equal(192, fragments.Sum(f => f.Video?.Samples ?? 0));
        Assert.Equal(301, fragments.Sum(f => f.Audio?.Samples ?? 0));
        Assert.All(fragments, f => Assert.InRange((f.Video?.Samples ?? 0) + (f.Audio?.Samples ?? 0), 1, 64));
        Assert.Equal(0, fragments[0].Audio!.FirstDts); Assert.Equal(1024, fragments[0].Video!.FirstDts);
        Assert.Equal(1, result.Publisher.HeadPacketCount);
        Assert.InRange(result.PeakPendingSamples, 1, 128);
        Assert.InRange(result.PeakPendingPayloadBytes, 1, 1024 * 1024);
        var receive = await wire.CompleteAsync(deadline.Token);
        Assert.Equal(wire.SentPackets, receive.PacketCount);
        Assert.Equal(9, receive.PacketCount);
        Assert.Equal(1, wire.Composition.Snapshot.HeadPacketCount);
        Assert.Equal(8, wire.Composition.Snapshot.DataPacketCount);
        Assert.Equal(493, decoder.Received.Count);
        Assert.Equal(encoder.Expected.Keys.Order(), decoder.Received.Order());
        Assert.Equal(8, wire.Composition.Snapshot.MediaSegmentCount);
        Assert.Equal(8, wire.Composition.Snapshot.DecoderSubmissionCount);
        Assert.Equal(1, wire.Composition.Snapshot.InitializationGenerationCount);
        Assert.Equal(0, wire.Composition.Snapshot.AssemblerCarryBytes);
        Assert.Equal(0, wire.Composition.Snapshot.DiscontinuityCount);
        Assert.Equal(1, decoder.Completions); Assert.Equal(1, decoded.Completions);
        if (nativeDecode)
        {
            Assert.Equal(192, decoded.VideoFrames); Assert.Equal(301, decoded.AudioFrames);
            Assert.Equal(308_224, decoded.AudioSamples);
            Assert.True(decoded.VideoHashes.Count > 1); Assert.True(decoded.MaximumAmplitude > 0.05f);
        }
        else { Assert.Equal(0, decoded.VideoFrames); Assert.Equal(0, decoded.AudioFrames); }
        completed?.Invoke(); // Observe completed/drained Windows output before composition disposal.
        await wire.DisposeAsync(); Assert.True(decoder.Disposed); Assert.True(decoded.Disposed);
        output.WriteLine($"nativeDecode={nativeDecode}; channel={wire.Channel}; session={wire.Session}; elapsedSeconds={elapsed.Elapsed.TotalSeconds:F3}; firstPublishSeconds={firstPublishSeconds:F3}");
        output.WriteLine(JsonSerializer.Serialize(result));
        output.WriteLine($"PCP={receive.EndReason}; packets={receive.PacketCount}; samples={decoder.Received.Count}; videoFrames={decoded.VideoFrames}; audioFrames={decoded.AudioFrames}; PCM={decoded.AudioSamples}; EOS=PASS; TerminalConsumerBound={downstream is not null}");
    }

    private static void AssertFirstPublishBoundary(int videoConsumed, int audioConsumed, int remaining)
    {
        // Pinned 0.8s cutoff = 38400 ticks at 48kHz, with 1024 AAC priming.
        // Video index 24 first reaches that cutoff (24*1600+1024), needing 25 inputs.
        // AAC index 38 reaches it (38*1024); encoder delay requires 40 raw blocks.
        // These expectations belong to this deterministic scenario, not production policy.
        Assert.Equal(25, videoConsumed);
        Assert.Equal(40, audioConsumed);
        Assert.Equal(427, remaining);
        var consumed = checked(videoConsumed + audioConsumed);
        Assert.Equal(65, consumed);
        Assert.Equal(ScenarioVideoFrames + ScenarioAudioBlocks - consumed, remaining);
    }
}
