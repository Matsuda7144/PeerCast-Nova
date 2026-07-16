using System.Globalization;
using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Playback;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Cli;

internal enum PlaybackDecoderLifecycleStopTrigger
{
    InputLimit,
    MediaUnrecognized,
    DecoderUnsupported,
    SessionEnded,
    Timeout,
    CallerCanceled,
    Failure,
}

internal sealed record PlaybackDecoderLifecycleSummary(
    PlaybackMediaClassificationState ClassificationState,
    PlaybackMediaContainer Container,
    PlaybackMediaClassificationBasis? Basis,
    PlaybackDecoderRunEndReason? RunnerEndReason,
    long SubmittedInputCount,
    long SubmittedByteCount,
    long RunnerResetCount,
    int FactoryCreateCount,
    long DecoderInputCount,
    long DecoderByteCount,
    long DecoderResetCount,
    int DecoderCompleteCount,
    int DecoderDisposeCount,
    bool CleanupDeferred,
    bool DeferredCleanupPending,
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
    public bool IsAccepted(
        PlaybackDecoderLifecycleStopTrigger stopTrigger,
        int requiredInputCount)
    {
        return requiredInputCount > 0 &&
            stopTrigger ==
                PlaybackDecoderLifecycleStopTrigger.InputLimit &&
            ClassificationState ==
                PlaybackMediaClassificationState.Recognized &&
            Container != PlaybackMediaContainer.Unknown &&
            Basis is not null &&
            RunnerEndReason ==
                PlaybackDecoderRunEndReason.OwnerStopped &&
            SubmittedInputCount >= requiredInputCount &&
            SubmittedInputCount == DecoderInputCount &&
            SubmittedByteCount == DecoderByteCount &&
            RunnerResetCount == DecoderResetCount &&
            FactoryCreateCount == 1 &&
            DecoderCompleteCount == 0 &&
            DecoderDisposeCount == 1 &&
            !CleanupDeferred &&
            !DeferredCleanupPending &&
            SessionEndReason ==
                PcpChannelSessionEndReason.Stopped &&
            AttemptCount > 0 &&
            ReconnectCount >= 0 &&
            ReconnectCount < AttemptCount &&
            HttpStatusCode == 200 &&
            SessionPayloadByteCount >= SubmittedByteCount &&
            BufferSkippedPacketCount == 0 &&
            BufferSkippedByteCount == 0 &&
            BufferPendingPacketCount == 0 &&
            BufferOutstandingPacketCount == 0 &&
            ShutdownCompleted;
    }
}

internal sealed record PlaybackDecoderLifecycleProbeResult(
    int ExitCode,
    bool AcceptancePassed,
    PlaybackDecoderLifecycleStopTrigger StopTrigger,
    PlaybackDecoderLifecycleSummary Summary);

internal static class PlaybackDecoderLifecycleProbeCommand
{
    public static async Task<PlaybackDecoderLifecycleProbeResult> RunAsync(
        PlaybackDecoderLifecycleProbeOptions options,
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
        var inputLimitReached = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var probeBackend = new PlaybackLifecycleProbeDecoderFactory(
            count =>
            {
                if (count >= options.InputLimit)
                {
                    inputLimitReached.TrySetResult(true);
                }
            });
        var factory = new PlaybackDecoderBackendFactory([probeBackend]);
        await using var runner = new PlaybackDecoderRunner(
            factory,
            new PlaybackDecoderRunnerOptions
            {
                MaxBootstrapBytes = options.MaxProbeBytes,
                MaxDiscontinuities = options.MaxDiscontinuities,
                StopTimeout = options.DecoderStopTimeout,
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

        var runnerTask = runner.RunAsync(
            buffer,
            CancellationToken.None);
        var sessionTask = session.RunAsync(
            options.UpstreamHost,
            options.UpstreamPort,
            options.InitialPosition,
            cancellationToken);
        using var timeoutSource = new CancellationTokenSource();
        var timeoutTask = Task.Delay(
            options.Duration,
            timeoutSource.Token);
        var callerCancellationReached = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var callerCancellationRegistration =
            cancellationToken.Register(
                static state =>
                    ((TaskCompletionSource<bool>)state!).TrySetResult(true),
                callerCancellationReached);
        var first = await Task.WhenAny(
            inputLimitReached.Task,
            runnerTask,
            sessionTask,
            timeoutTask,
            callerCancellationReached.Task).ConfigureAwait(false);
        var inputLimitWon = ReferenceEquals(
            first,
            inputLimitReached.Task);
        var sessionWon = ReferenceEquals(first, sessionTask);
        var timeoutWon = ReferenceEquals(first, timeoutTask);
        var callerCanceled = cancellationToken.IsCancellationRequested;
        timeoutSource.Cancel();
        var failed = false;

        if (!sessionWon)
        {
            session.RequestStop();
        }

        if (inputLimitWon || timeoutWon || callerCanceled)
        {
            try
            {
                await runner.StopAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
                when (exception is
                    InvalidOperationException or
                    OverflowException)
            {
                failed = true;
            }
        }

        PcpChannelSessionResult? sessionResult = null;

        try
        {
            sessionResult = await sessionTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
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

        if (sessionWon && !runnerTask.IsCompleted)
        {
            buffer.Complete();
        }

        PlaybackDecoderRunResult? runnerResult = null;

        try
        {
            runnerResult = await runnerTask.ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception is
                InvalidOperationException or
                OverflowException or
                OperationCanceledException)
        {
            failed = true;
        }

        var activeBufferStatus = buffer.Snapshot;
        buffer.Stop();
        var finalBufferStatus = buffer.Snapshot;
        var stopTrigger = SelectStopTrigger(
            runnerResult,
            failed,
            inputLimitWon,
            timeoutWon,
            callerCanceled);
        var summary = CreateSummary(
            runnerResult,
            probeBackend,
            runner,
            sessionResult,
            activeBufferStatus,
            finalBufferStatus,
            sessionTask.IsCompleted,
            runnerTask.IsCompleted);
        var accepted = summary.IsAccepted(
            stopTrigger,
            options.InputLimit);

        await WriteSummaryAsync(
            output,
            stopTrigger,
            summary,
            accepted).ConfigureAwait(false);

        return new PlaybackDecoderLifecycleProbeResult(
            accepted ? 0 : timeoutWon ? 2 : 1,
            accepted,
            stopTrigger,
            summary);
    }

    private static PlaybackDecoderLifecycleStopTrigger SelectStopTrigger(
        PlaybackDecoderRunResult? runnerResult,
        bool failed,
        bool inputLimitWon,
        bool timeoutWon,
        bool callerCanceled)
    {
        if (callerCanceled)
        {
            return PlaybackDecoderLifecycleStopTrigger.CallerCanceled;
        }

        if (timeoutWon)
        {
            return PlaybackDecoderLifecycleStopTrigger.Timeout;
        }

        if (failed)
        {
            return PlaybackDecoderLifecycleStopTrigger.Failure;
        }

        if (inputLimitWon)
        {
            return PlaybackDecoderLifecycleStopTrigger.InputLimit;
        }

        return runnerResult?.EndReason switch
        {
            PlaybackDecoderRunEndReason.MediaUnrecognized =>
                PlaybackDecoderLifecycleStopTrigger.MediaUnrecognized,
            PlaybackDecoderRunEndReason.DecoderUnsupported =>
                PlaybackDecoderLifecycleStopTrigger.DecoderUnsupported,
            _ => PlaybackDecoderLifecycleStopTrigger.SessionEnded,
        };
    }

    private static PlaybackDecoderLifecycleSummary CreateSummary(
        PlaybackDecoderRunResult? runnerResult,
        PlaybackLifecycleProbeDecoderFactory probeBackend,
        PlaybackDecoderRunner runner,
        PcpChannelSessionResult? session,
        PlaybackInputStatusSnapshot activeBuffer,
        PlaybackInputStatusSnapshot finalBuffer,
        bool sessionCompleted,
        bool runnerCompleted)
    {
        var decoder = probeBackend.LastCreated;

        return new PlaybackDecoderLifecycleSummary(
            runnerResult?.ClassificationState ??
                PlaybackMediaClassificationState.Collecting,
            runnerResult?.Container ??
                PlaybackMediaContainer.Unknown,
            runnerResult?.Basis,
            runnerResult?.EndReason,
            runnerResult?.SubmittedInputCount ?? 0,
            runnerResult?.SubmittedByteCount ?? 0,
            runnerResult?.ResetCount ?? 0,
            probeBackend.CreateCount,
            decoder?.InputCount ?? 0,
            decoder?.ByteCount ?? 0,
            decoder?.ResetCount ?? 0,
            decoder?.CompleteCount ?? 0,
            decoder?.DisposeCount ?? 0,
            runnerResult?.CleanupDeferred ?? false,
            runner.DeferredCleanupPending,
            session?.EndReason,
            session?.AttemptCount ?? 0,
            session?.ReconnectCount ?? 0,
            session?.HttpStatusCode,
            session?.PacketCount ?? 0,
            session?.PayloadByteCount ?? 0,
            activeBuffer.SkippedPacketCount,
            activeBuffer.SkippedByteCount,
            finalBuffer.PendingPacketCount,
            finalBuffer.OutstandingPacketCount,
            sessionCompleted &&
                runnerCompleted &&
                !runner.DeferredCleanupPending &&
                finalBuffer.PendingPacketCount == 0 &&
                finalBuffer.OutstandingPacketCount == 0);
    }

    private static async Task WriteSummaryAsync(
        TextWriter output,
        PlaybackDecoderLifecycleStopTrigger stopTrigger,
        PlaybackDecoderLifecycleSummary summary,
        bool accepted)
    {
        await output.WriteLineAsync(
            $"Probe Stop: {stopTrigger}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Classification: {summary.ClassificationState}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Container: {summary.Container}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Basis: {summary.Basis?.ToString() ?? "(none)"}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Runner End: {summary.RunnerEndReason?.ToString() ?? "(none)"}")
            .ConfigureAwait(false);
        await WriteNumberAsync(
            output,
            "Submitted Inputs",
            summary.SubmittedInputCount).ConfigureAwait(false);
        await WriteNumberAsync(
            output,
            "Submitted Bytes",
            summary.SubmittedByteCount).ConfigureAwait(false);
        await WriteNumberAsync(
            output,
            "Runner Resets",
            summary.RunnerResetCount).ConfigureAwait(false);
        await WriteNumberAsync(
            output,
            "Factory Creates",
            summary.FactoryCreateCount).ConfigureAwait(false);
        await WriteNumberAsync(
            output,
            "Decoder Inputs",
            summary.DecoderInputCount).ConfigureAwait(false);
        await WriteNumberAsync(
            output,
            "Decoder Bytes",
            summary.DecoderByteCount).ConfigureAwait(false);
        await WriteNumberAsync(
            output,
            "Decoder Resets",
            summary.DecoderResetCount).ConfigureAwait(false);
        await WriteNumberAsync(
            output,
            "Decoder Completes",
            summary.DecoderCompleteCount).ConfigureAwait(false);
        await WriteNumberAsync(
            output,
            "Decoder Disposes",
            summary.DecoderDisposeCount).ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Cleanup Deferred: {summary.CleanupDeferred}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Deferred Cleanup Pending: {summary.DeferredCleanupPending}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Session End: {summary.SessionEndReason?.ToString() ?? "(none)"}")
            .ConfigureAwait(false);
        await WriteNumberAsync(
            output,
            "Attempts",
            summary.AttemptCount).ConfigureAwait(false);
        await WriteNumberAsync(
            output,
            "Reconnects",
            summary.ReconnectCount).ConfigureAwait(false);
        await output.WriteLineAsync(
            $"HTTP: {summary.HttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "(none)"}")
            .ConfigureAwait(false);
        await WriteNumberAsync(
            output,
            "Session Packets",
            summary.SessionPacketCount).ConfigureAwait(false);
        await WriteNumberAsync(
            output,
            "Session Payload Bytes",
            summary.SessionPayloadByteCount).ConfigureAwait(false);
        await WriteNumberAsync(
            output,
            "Buffer Skipped Packets",
            summary.BufferSkippedPacketCount).ConfigureAwait(false);
        await WriteNumberAsync(
            output,
            "Buffer Skipped Bytes",
            summary.BufferSkippedByteCount).ConfigureAwait(false);
        await WriteNumberAsync(
            output,
            "Buffer Pending Packets",
            summary.BufferPendingPacketCount).ConfigureAwait(false);
        await WriteNumberAsync(
            output,
            "Buffer Outstanding Packets",
            summary.BufferOutstandingPacketCount).ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Shutdown Completed: {summary.ShutdownCompleted}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Lifecycle Gate: {(accepted ? "Passed" : "Failed")}")
            .ConfigureAwait(false);
    }

    private static Task WriteNumberAsync(
        TextWriter output,
        string label,
        long value)
    {
        return output.WriteLineAsync(
            $"{label}: {value.ToString(CultureInfo.InvariantCulture)}");
    }
}
