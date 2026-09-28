using System.Net;
using System.Security.Cryptography;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Playback.Tests;

internal sealed record Fmp4PcpLoopbackFixture(
    byte[] Bytes,
    byte[] InitializationSegment,
    byte[][] MediaSegments);

internal static class Fmp4PcpLoopbackAcceptanceFixture
{
    public const int ExpectedLength = 14_777;
    public const string ExpectedSha256 =
        "3780066C30D70C5063368D708C3850410CAD03840E0B302966206C94AA0425EC";

    public const uint HeadPacketType = 0x64616568;
    public const uint DataPacketType = 0x61746164;

    public static Fmp4PcpLoopbackFixture Load()
    {
        using var stream = typeof(Fmp4PcpLoopbackAcceptanceFixture)
            .Assembly
            .GetManifestResourceStream(
                "PeerCast.Nova.Playback.Tests.Fixtures.fmp4-avc1-aac.mp4");
        ArgumentNullException.ThrowIfNull(stream);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        if (bytes.Length != ExpectedLength ||
            !string.Equals(
                Convert.ToHexString(SHA256.HashData(bytes)),
                ExpectedSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The retained fMP4 loopback fixture does not match its accepted identity.");
        }

        var boxes = new List<byte[]>();
        var offset = 0;
        while (offset < bytes.Length)
        {
            var result = PlaybackIsoBmffBoxSequenceReader.ReadNext(
                bytes,
                offset,
                out var box);
            if (result != PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
                box is not { } location)
            {
                throw new InvalidDataException(
                    "The retained fMP4 fixture has an invalid top-level box sequence.");
            }

            boxes.Add(bytes.AsSpan(offset, location.BoxLength).ToArray());
            offset = location.NextOffset;
        }

        if (boxes.Count != 6)
        {
            throw new InvalidDataException(
                "The retained fMP4 fixture must contain ftyp, moov, and two moof/mdat pairs.");
        }

        return new(
            bytes,
            [.. boxes[0], .. boxes[1]],
            [
                [.. boxes[2], .. boxes[3]],
                [.. boxes[4], .. boxes[5]],
            ]);
    }

    public static IReadOnlyList<PcpChannelPacket> CreatePacketPlan(
        Fmp4PcpLoopbackFixture fixture,
        ChannelId channelId,
        ChannelSessionId sessionId)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(sessionId);

        var firstMedia = fixture.MediaSegments[0];
        var firstMoofLength = ReadBoxLength(firstMedia, 0);
        if (firstMedia.Length <= firstMoofLength + 13)
        {
            throw new InvalidDataException(
                "The first retained media fragment cannot support the accepted split plan.");
        }

        var packets = new List<PcpChannelPacket>(6);
        long sequence = 0;
        uint wirePosition = 0;

        void Add(uint type, ReadOnlyMemory<byte> payload)
        {
            packets.Add(new PcpChannelPacket(
                channelId,
                sessionId,
                sequence++,
                type,
                wirePosition,
                wirePosition,
                payload,
                discontinuity: null));
            wirePosition = checked(wirePosition + (uint)payload.Length);
        }

        Add(HeadPacketType, fixture.InitializationSegment.AsMemory(0, 5));
        Add(HeadPacketType, fixture.InitializationSegment.AsMemory(5));
        Add(DataPacketType, firstMedia.AsMemory(0, 11));
        Add(
            DataPacketType,
            firstMedia.AsMemory(11, firstMoofLength + 2));
        var mediaTailAndNextHead = firstMedia
            .AsMemory(firstMoofLength + 13)
            .ToArray()
            .Concat(fixture.MediaSegments[1].AsMemory(0, 13).ToArray())
            .ToArray();
        Add(DataPacketType, mediaTailAndNextHead);
        Add(DataPacketType, fixture.MediaSegments[1].AsMemory(13));

        if (packets.Sum(static packet => packet.Payload.Length) != fixture.Bytes.Length)
        {
            throw new InvalidDataException(
                "The loopback packet plan does not preserve every fixture byte.");
        }

        return packets;
    }

    private static int ReadBoxLength(ReadOnlySpan<byte> bytes, int offset)
    {
        var result = PlaybackIsoBmffBoxHeaderParser.Parse(
            bytes[offset..],
            out var header);
        if (result != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header?.BoxSize is not { } size || size > int.MaxValue)
        {
            throw new InvalidDataException(
                "The retained fMP4 media fragment has an invalid moof header.");
        }

        return checked((int)size);
    }
}

public sealed class PlaybackFmp4PcpLoopbackAcceptanceTests
{
    private static readonly Guid ChannelGuid =
        Guid.ParseExact("642ED9E0B91349E0ABCE5B4E6EC1CC38", "N");
    private static readonly Guid SessionGuid =
        Guid.ParseExact("8A54CA9872E84D18B26940BF1375F224", "N");

    [Fact]
    public async Task Real_loopback_packetized_fixture_reaches_both_tracks_without_synthetic_eos()
    {
        var run = await RunManagedLoopbackAsync(callerCancellation: false);
        var result = run.RunResult;
        var session = Assert.IsType<PcpChannelSessionResult>(result.Session);
        var composition = result.Composition;
        var server = run.Server;

        Assert.True(result.DurationLimitReached);
        Assert.False(result.CallerCancelled);
        Assert.Null(result.CompletionFailure);
        Assert.Equal(PcpChannelSessionEndReason.Stopped, session.EndReason);
        Assert.Equal(6, session.ChannelMessageCount);
        Assert.Equal(6, session.PacketCount);
        Assert.Equal(Fmp4PcpLoopbackAcceptanceFixture.ExpectedLength,
            session.PayloadByteCount);
        // The runner captures this result snapshot immediately before its awaited disposal.
        Assert.Equal(PlaybackPcpFmp4CompositionState.Accepting, composition.State);
        Assert.Equal(PlaybackPcpFmp4CompositionResult.PacketAccepted,
            composition.Result);
        Assert.Equal(6, composition.AcceptedPacketCount);
        Assert.Equal(2, composition.HeadPacketCount);
        Assert.Equal(4, composition.DataPacketCount);
        Assert.Equal(Fmp4PcpLoopbackAcceptanceFixture.ExpectedLength,
            composition.PayloadByteCount);
        Assert.Equal(1, composition.InitializationGenerationCount);
        Assert.Equal(2, composition.MediaSegmentCount);
        Assert.Equal(2, composition.BatchCount);
        Assert.Equal(2, composition.DecoderSubmissionCount);
        Assert.Equal(0, composition.DiscontinuityCount);
        Assert.Equal(0, composition.AssemblerCarryBytes);

        Assert.Equal(1, server.AcceptedConnectionCount);
        Assert.Equal(1, server.AcceptedHandshakeCount);
        Assert.Equal(6, server.DeliveredPacketCount);
        Assert.Equal(Fmp4PcpLoopbackAcceptanceFixture.ExpectedLength,
            server.DeliveredByteCount);
        Assert.Equal(0, server.DiscardedPacketCount);
        Assert.Equal(0, server.ProtocolRejectedCount);
        Assert.Equal(0, server.CleanupIncompleteCount);
        Assert.Equal(0, server.InternalFailureCount);
        Assert.Equal(0, server.ActiveConnectionCount);

        var decoder = Assert.Single(run.Decoders.Decoders);
        Assert.Equal(PlaybackMediaContainer.FragmentedMp4, decoder.Context.Container);
        Assert.Contains(PlaybackEncodedMediaRole.Video, decoder.DecodedRoles);
        Assert.Contains(PlaybackEncodedMediaRole.Audio, decoder.DecodedRoles);
        Assert.Equal(0, decoder.CompleteCount);
        Assert.Equal(1, decoder.DisposeCount);

        var output = Assert.Single(run.Outputs.Generations);
        Assert.True(output.VideoFrameCount > 0);
        Assert.True(output.AudioFrameCount > 0);
        Assert.Equal(0, output.CompleteCount);
        Assert.Equal(1, output.DisposeCount);
    }

    [Fact]
    public async Task Caller_cancellation_after_loopback_media_does_not_synthesize_eos()
    {
        var run = await RunManagedLoopbackAsync(callerCancellation: true);
        var result = run.RunResult;

        Assert.True(result.CallerCancelled);
        Assert.False(result.DurationLimitReached);
        Assert.Null(result.Session);
        Assert.Null(result.CompletionFailure);
        // The completed RunAsync task awaits disposal after capturing this snapshot.
        Assert.Equal(PlaybackPcpFmp4CompositionState.Accepting,
            result.Composition.State);
        Assert.Equal(6, result.Composition.AcceptedPacketCount);
        Assert.Equal(2, result.Composition.MediaSegmentCount);
        Assert.Equal(0, result.Composition.AssemblerCarryBytes);

        var decoder = Assert.Single(run.Decoders.Decoders);
        Assert.Equal(0, decoder.CompleteCount);
        Assert.Equal(1, decoder.DisposeCount);
        var output = Assert.Single(run.Outputs.Generations);
        Assert.Equal(0, output.CompleteCount);
        Assert.Equal(1, output.DisposeCount);
        Assert.Equal(1, run.Server.AcceptedHandshakeCount);
        Assert.Equal(0, run.Server.CleanupIncompleteCount);
        Assert.Equal(0, run.Server.InternalFailureCount);
    }

    private static async Task<ManagedLoopbackRun> RunManagedLoopbackAsync(
        bool callerCancellation)
    {
        var fixture = Fmp4PcpLoopbackAcceptanceFixture.Load();
        var channelId = new ChannelId(ChannelGuid);
        var sessionId = new ChannelSessionId(SessionGuid);
        var packetPlan = Fmp4PcpLoopbackAcceptanceFixture.CreatePacketPlan(
            fixture,
            channelId,
            sessionId);
        var decoders = new RecordingDecoderFactory();
        var outputs = new RecordingPresentationFactory();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await using var fanout = new PcpRelayFanout();
        await using var server = new PcpDownstreamRelayServer(
            new IPEndPoint(IPAddress.Loopback, 0),
            fanout,
            channelId,
            SessionGuid);
        var serverTask = server.RunAsync();
        var endpoint = server.LocalEndPoint;
        var sourceUri =
            $"pcp://127.0.0.1:{endpoint.Port}/channel/{ChannelGuid:N}";
        Assert.True(PlaybackPcpFmp4LiveSource.TryParse(
            sourceUri,
            out var source));
        await using var runner = new PlaybackPcpFmp4LiveSessionRunner(
            Assert.IsType<PlaybackPcpFmp4LiveSource>(source),
            decoders,
            outputs,
            new PlaybackPcpFmp4LiveSessionOptions
            {
                MaximumRunDuration = callerCancellation
                    ? TimeSpan.FromSeconds(12)
                    : TimeSpan.FromSeconds(2),
                ConnectTimeout = TimeSpan.FromSeconds(2),
                Session = new PcpChannelSessionOptions
                {
                    HandshakeTimeout = TimeSpan.FromSeconds(3),
                    MaxReconnectAttempts = 0,
                    ReconnectDelay = TimeSpan.Zero,
                },
            });
        using var callerStop = new CancellationTokenSource();
        var runnerTask = runner.RunAsync(
            callerCancellation ? callerStop.Token : CancellationToken.None);

        await WaitForChildRegistrationAsync(
            fanout,
            runnerTask,
            deadline.Token);
        foreach (var packet in packetPlan)
        {
            await fanout.ConsumeAsync(packet, deadline.Token);
        }

        Assert.True(await fanout.WaitForIdleAsync(
            TimeSpan.FromSeconds(5),
            deadline.Token));
        await outputs.BothTracksPresented.Task.WaitAsync(deadline.Token);

        if (callerCancellation)
        {
            callerStop.Cancel();
        }

        var runResult = await runnerTask.WaitAsync(deadline.Token);
        Assert.True(runnerTask.IsCompleted);
        await server.StopAsync(deadline.Token);
        var serverResult = await serverTask.WaitAsync(deadline.Token);
        Assert.Equal(PcpDownstreamRelayServerEndReason.Stopped,
            serverResult.EndReason);
        Assert.Equal(0, fanout.ActiveChildCount);

        return new(
            sourceUri,
            packetPlan,
            runResult,
            serverResult.Snapshot,
            decoders,
            outputs);
    }

    private static async Task WaitForChildRegistrationAsync(
        PcpRelayFanout fanout,
        Task runnerTask,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        while (fanout.ActiveChildCount != 1)
        {
            if (runnerTask.IsCompleted)
            {
                await runnerTask.ConfigureAwait(false);
                throw new InvalidOperationException(
                    "The receiver stopped before a PCP child subscribed.");
            }

            if (!await timer.WaitForNextTickAsync(cancellationToken)
                    .ConfigureAwait(false))
            {
                throw new TimeoutException(
                    "The PCP downstream child did not register before the deadline.");
            }
        }
    }

    private sealed record ManagedLoopbackRun(
        string SourceUri,
        IReadOnlyList<PcpChannelPacket> PacketPlan,
        PlaybackPcpFmp4LiveSessionRunResult RunResult,
        PcpDownstreamRelayServerSnapshot Server,
        RecordingDecoderFactory Decoders,
        RecordingPresentationFactory Outputs);

    private sealed class RecordingDecoderFactory : IPlaybackDecoderFactory
    {
        public List<RecordingDecoder> Decoders { get; } = [];

        public IPlaybackDecoder? Create(PlaybackDecoderCreationContext context)
        {
            var decoder = new RecordingDecoder(context);
            Decoders.Add(decoder);
            return decoder;
        }
    }

    private sealed class RecordingDecoder : IPlaybackDecoder
    {
        private readonly PlaybackDecoderCreationContext _context;

        public RecordingDecoder(PlaybackDecoderCreationContext context)
        {
            _context = context;
            Context = context;
        }

        public PlaybackDecoderCreationContext Context { get; }

        public List<PlaybackEncodedMediaRole> DecodedRoles { get; } = [];

        public int CompleteCount { get; private set; }

        public int DisposeCount { get; private set; }

        public async ValueTask DecodeAsync(
            PlaybackEncodedInput input,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = input.Fmp4SampleMetadata ??
                throw new InvalidOperationException(
                    "The loopback fMP4 sample has no track metadata.");
            DecodedRoles.Add(metadata.MediaRole);
            if (metadata.MediaRole == PlaybackEncodedMediaRole.Video)
            {
                await _context.OutputSink.WriteVideoAsync(
                    PlaybackDecodedVideoFrame.Create(
                        input.Sequence,
                        TimeSpan.FromMilliseconds(1),
                        TimeSpan.FromMilliseconds(1),
                        PlaybackDecodedFrameDiscontinuityKind.None,
                        PlaybackDecodedVideoPixelFormat.Bgra32,
                        1,
                        1,
                        4,
                        new byte[4]),
                    cancellationToken);
            }
            else
            {
                await _context.OutputSink.WriteAudioAsync(
                    PlaybackDecodedAudioFrame.Create(
                        input.Sequence,
                        TimeSpan.FromMilliseconds(1),
                        TimeSpan.FromMilliseconds(20),
                        PlaybackDecodedFrameDiscontinuityKind.None,
                        PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
                        48_000,
                        2,
                        1,
                        new byte[4]),
                    cancellationToken);
            }
        }

        public ValueTask ResetAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask CompleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompleteCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingPresentationFactory :
        IPlaybackPcpFmp4PresentationOutputGenerationFactory
    {
        private readonly TaskCompletionSource _bothTracksPresented =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<RecordingPresentationGeneration> Generations { get; } = [];

        public TaskCompletionSource BothTracksPresented => _bothTracksPresented;

        public IPlaybackPcpFmp4PresentationOutputGeneration Create(long generation)
        {
            var output = new RecordingPresentationGeneration(
                generation,
                _bothTracksPresented);
            Generations.Add(output);
            return output;
        }
    }

    private sealed class RecordingPresentationGeneration(
        long generation,
        TaskCompletionSource bothTracksPresented) :
        IPlaybackPcpFmp4PresentationOutputGeneration
    {
        public long Generation { get; } = generation;

        public int AudioFrameCount { get; private set; }

        public int VideoFrameCount { get; private set; }

        public int CompleteCount { get; private set; }

        public int DisposeCount { get; private set; }

        public ValueTask WriteAudioAsync(
            PlaybackDecodedAudioFrame frame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AudioFrameCount++;
            SignalIfBothTracksObserved();
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteVideoAsync(
            PlaybackDecodedVideoFrame frame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VideoFrameCount++;
            SignalIfBothTracksObserved();
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompleteCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        private void SignalIfBothTracksObserved()
        {
            if (AudioFrameCount > 0 && VideoFrameCount > 0)
            {
                bothTracksPresented.TrySetResult();
            }
        }
    }
}
