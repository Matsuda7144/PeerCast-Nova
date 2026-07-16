using System.Globalization;
using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Playback;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Cli;

internal enum PlaybackSignatureStopTrigger
{
    Recognized,
    ProbeLimit,
    SessionEnded,
    Timeout,
    CallerCanceled,
    Failure,
}

internal sealed record PlaybackSignatureSummary(
    PlaybackMediaClassificationState ClassificationState,
    PlaybackMediaContainer Container,
    PlaybackMediaClassificationBasis? Basis,
    PlaybackMediaClassificationLimit? Limit,
    long ObservedPacketCount,
    long ExaminedByteCount,
    int DiscontinuityCount,
    PcpChannelSessionEndReason? SessionEndReason,
    int AttemptCount,
    int ReconnectCount,
    int? HttpStatusCode,
    long SessionPacketCount,
    long SessionPayloadByteCount,
    long BufferSkippedPacketCount,
    long BufferSkippedByteCount,
    int BufferPendingPacketCount,
    int BufferOutstandingPacketCount,
    bool ShutdownCompleted)
{
    public bool IsAccepted(PlaybackSignatureStopTrigger stopTrigger)
    {
        return stopTrigger == PlaybackSignatureStopTrigger.Recognized &&
            ClassificationState ==
                PlaybackMediaClassificationState.Recognized &&
            Container != PlaybackMediaContainer.Unknown &&
            SessionEndReason is not null &&
            HttpStatusCode == 200 &&
            BufferSkippedPacketCount == 0 &&
            BufferSkippedByteCount == 0 &&
            BufferPendingPacketCount == 0 &&
            BufferOutstandingPacketCount == 0 &&
            ShutdownCompleted;
    }
}

internal sealed record PlaybackSignatureCommandResult(
    int ExitCode,
    bool AcceptancePassed,
    PlaybackSignatureStopTrigger StopTrigger,
    PlaybackSignatureSummary Summary);

internal static class PlaybackSignatureCommand
{
    public static async Task<PlaybackSignatureCommandResult> RunAsync(
        PlaybackSignatureCommandOptions options,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);

        var operationTimeout = TimeSpan.FromMilliseconds(
            Math.Min(options.Duration.TotalMilliseconds / 2, 5_000));
        var transportOptions = new TcpTransportOptions
        {
            ConnectTimeout = operationTimeout,
        };
        var tracker = new ChannelStatusTracker(
            new ChannelId(options.ChannelId),
            new ChannelSessionId(Guid.NewGuid()));
        await using var buffer = new PcpPlaybackLatencyBuffer(
            new PlaybackLatencyBufferOptions
            {
                MaxPendingPackets = 64,
                MaxPendingBytes = 8L * 1024 * 1024,
            });
        using var classifier = new PlaybackMediaClassifier(
            new PlaybackMediaClassificationOptions
            {
                MaxProbeBytes = options.MaxProbeBytes,
                MaxDiscontinuities = options.MaxDiscontinuities,
            });
        await using var session = new PcpChannelSession(
            new TcpTransportConnector(transportOptions),
            tracker,
            buffer,
            new PcpChannelSessionOptions
            {
                HandshakeTimeout = operationTimeout,
                MaxReconnectAttempts = options.Reconnects,
                ReconnectDelay = TimeSpan.FromMilliseconds(100),
                Stream = new PcpChannelStreamOptions
                {
                    ReadIdleTimeout = options.IdleTimeout,
                },
            });

        var readerTask = ClassifyAsync(buffer, classifier);
        var sessionTask = session.RunAsync(
            options.UpstreamHost,
            options.UpstreamPort,
            options.InitialPosition,
            cancellationToken);
        using var timeoutSource = new CancellationTokenSource();
        var timeoutTask = Task.Delay(
            options.Duration,
            timeoutSource.Token);
        var first = await Task.WhenAny(
            readerTask,
            sessionTask,
            timeoutTask).ConfigureAwait(false);
        var timeoutWon = ReferenceEquals(first, timeoutTask);

        if (timeoutWon || ReferenceEquals(first, readerTask))
        {
            session.RequestStop();
        }

        PcpChannelSessionResult? sessionResult = null;
        var failed = false;

        try
        {
            sessionResult = await sessionTask.ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception is
                TimeoutException or
                SocketException or
                IOException or
                PcpProtocolException or
                PcpChannelConsumerException or
                InvalidOperationException or
                OverflowException or
                OperationCanceledException)
        {
            failed = true;
        }
        finally
        {
            timeoutSource.Cancel();
        }

        if (!readerTask.IsCompleted)
        {
            buffer.Complete();
        }

        var classification = await readerTask.ConfigureAwait(false);
        // The classifier may finish while packets remain queued. Discarding
        // those packets during shutdown is not evidence that it fell behind.
        var classificationBufferStatus = buffer.Snapshot;
        buffer.Stop();
        var finalBufferStatus = buffer.Snapshot;
        var stopTrigger = SelectStopTrigger(
            classification,
            failed,
            timeoutWon,
            cancellationToken.IsCancellationRequested);
        var summary = CreateSummary(
            classification,
            sessionResult,
            classificationBufferStatus,
            finalBufferStatus,
            sessionTask.IsCompleted);
        var accepted = summary.IsAccepted(stopTrigger);

        await WriteSummaryAsync(
            output,
            stopTrigger,
            summary,
            accepted).ConfigureAwait(false);

        return new PlaybackSignatureCommandResult(
            accepted ? 0 : timeoutWon ? 2 : 1,
            accepted,
            stopTrigger,
            summary);
    }

    private static async Task<PlaybackMediaClassificationSnapshot>
        ClassifyAsync(
            PcpPlaybackLatencyBuffer buffer,
            PlaybackMediaClassifier classifier)
    {
        while (true)
        {
            using var packet = await buffer.ReadAsync()
                .ConfigureAwait(false);

            if (packet is null)
            {
                return classifier.Complete();
            }

            var result = classifier.Observe(
                packet.Payload.Span,
                packet.Discontinuity);

            if (result.State !=
                PlaybackMediaClassificationState.Collecting)
            {
                return result;
            }
        }
    }

    private static PlaybackSignatureStopTrigger SelectStopTrigger(
        PlaybackMediaClassificationSnapshot classification,
        bool failed,
        bool timeoutWon,
        bool callerCanceled)
    {
        if (callerCanceled)
        {
            return PlaybackSignatureStopTrigger.CallerCanceled;
        }

        if (timeoutWon)
        {
            return PlaybackSignatureStopTrigger.Timeout;
        }

        if (failed)
        {
            return PlaybackSignatureStopTrigger.Failure;
        }

        return classification.State switch
        {
            PlaybackMediaClassificationState.Recognized =>
                PlaybackSignatureStopTrigger.Recognized,
            PlaybackMediaClassificationState.LimitReached =>
                PlaybackSignatureStopTrigger.ProbeLimit,
            _ => PlaybackSignatureStopTrigger.SessionEnded,
        };
    }

    private static PlaybackSignatureSummary CreateSummary(
        PlaybackMediaClassificationSnapshot classification,
        PcpChannelSessionResult? session,
        PlaybackInputStatusSnapshot classificationBuffer,
        PlaybackInputStatusSnapshot finalBuffer,
        bool sessionCompleted)
    {
        return new PlaybackSignatureSummary(
            classification.State,
            classification.Container,
            classification.Basis,
            classification.Limit,
            classification.ObservedPacketCount,
            classification.ExaminedByteCount,
            classification.DiscontinuityCount,
            session?.EndReason,
            session?.AttemptCount ?? 0,
            session?.ReconnectCount ?? 0,
            session?.HttpStatusCode,
            session?.PacketCount ?? 0,
            session?.PayloadByteCount ?? 0,
            classificationBuffer.SkippedPacketCount,
            classificationBuffer.SkippedByteCount,
            finalBuffer.PendingPacketCount,
            finalBuffer.OutstandingPacketCount,
            sessionCompleted &&
                finalBuffer.PendingPacketCount == 0 &&
                finalBuffer.OutstandingPacketCount == 0 &&
                classification.RetainedPrefixByteCount == 0);
    }

    private static async Task WriteSummaryAsync(
        TextWriter output,
        PlaybackSignatureStopTrigger stopTrigger,
        PlaybackSignatureSummary summary,
        bool accepted)
    {
        await output.WriteLineAsync(
            $"Probe Stop: {stopTrigger}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Classification: {summary.ClassificationState}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Container: {summary.Container}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Basis: {summary.Basis?.ToString() ?? "(none)"}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Limit: {summary.Limit?.ToString() ?? "(none)"}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Observed Packets: {summary.ObservedPacketCount.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Examined Bytes: {summary.ExaminedByteCount.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Discontinuities: {summary.DiscontinuityCount.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Session End: {summary.SessionEndReason?.ToString() ?? "(none)"}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Attempts: {summary.AttemptCount.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Reconnects: {summary.ReconnectCount.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"HTTP: {summary.HttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "(none)"}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Session Packets: {summary.SessionPacketCount.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Session Payload Bytes: {summary.SessionPayloadByteCount.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Buffer Skipped Packets: {summary.BufferSkippedPacketCount.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Buffer Skipped Bytes: {summary.BufferSkippedByteCount.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Buffer Pending Packets: {summary.BufferPendingPacketCount.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Buffer Outstanding Packets: {summary.BufferOutstandingPacketCount.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Shutdown Completed: {summary.ShutdownCompleted}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Classification Gate: {(accepted ? "Passed" : "Failed")}").ConfigureAwait(false);
    }
}
