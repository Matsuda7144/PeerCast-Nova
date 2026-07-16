using System.Runtime.ExceptionServices;

namespace PeerCast.Nova.Playback;

/// <summary>
/// Owns one replaceable decoder lifetime over the bounded playback input.
/// Decoder callbacks remain sequential. Cancellation starts a finite stop
/// deadline; a callback that ignores cancellation retains at most its current
/// payload lease until that callback actually completes.
/// </summary>
public sealed class PlaybackDecoderRunner : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly IPlaybackDecoderFactory _factory;
    private readonly PlaybackDecoderRunnerOptions _options;
    private readonly IPlaybackDecodedFrameSink _outputSink;
    private readonly CancellationTokenSource _ownerCancellation = new();
    private Task<PlaybackDecoderRunResult>? _runTask;
    private Task? _deferredCleanupTask;
    private int _disposed;

    public PlaybackDecoderRunner(
        IPlaybackDecoderFactory factory,
        PlaybackDecoderRunnerOptions? options = null,
        IPlaybackDecodedFrameSink? outputSink = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        _options = options is null
            ? new PlaybackDecoderRunnerOptions()
            : options with { };
        _options.Validate();
        _outputSink = outputSink ?? NullPlaybackDecodedFrameSink.Instance;
    }

    public bool DeferredCleanupPending
    {
        get
        {
            lock (_gate)
            {
                return _deferredCleanupTask is { IsCompleted: false };
            }
        }
    }

    public Task<PlaybackDecoderRunResult> RunAsync(
        PcpPlaybackLatencyBuffer input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);

            if (_runTask is not null)
            {
                throw new InvalidOperationException(
                    "A decoder runner can be started only once.");
            }

            _runTask = RunCoreAsync(input, cancellationToken);
            return _runTask;
        }
    }

    public async ValueTask StopAsync(
        CancellationToken cancellationToken = default)
    {
        Task<PlaybackDecoderRunResult>? runTask;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            runTask = _runTask;
        }

        try
        {
            _ownerCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            throw new ObjectDisposedException(
                nameof(PlaybackDecoderRunner));
        }

        if (runTask is not null)
        {
            _ = await runTask.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task<PlaybackDecoderRunResult>? runTask;

        lock (_gate)
        {
            if (_disposed != 0)
            {
                return;
            }

            _disposed = 1;
            runTask = _runTask;
        }

        _ownerCancellation.Cancel();

        try
        {
            if (runTask is not null)
            {
                try
                {
                    _ = await runTask.ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }
        }
        finally
        {
            _ownerCancellation.Dispose();
        }
    }

    private async Task<PlaybackDecoderRunResult> RunCoreAsync(
        PcpPlaybackLatencyBuffer input,
        CancellationToken callerCancellationToken)
    {
        using var linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                callerCancellationToken,
                _ownerCancellation.Token);
        using var classifier = new PlaybackMediaClassifier(
            _options.CreateClassificationOptions());
        PlaybackDecoderBootstrapBuffer? bootstrap =
            new(_options.MaxBootstrapBytes);
        IPlaybackDecoder? decoder = null;
        var metrics = new PlaybackDecoderRunMetrics();
        PlaybackDecoderRunEndReason? endReason = null;
        ExceptionDispatchInfo? failure = null;
        var callerCanceled = false;
        var cleanupDeferred = false;
        var cancellationToken = linkedCancellation.Token;

        try
        {
            while (endReason is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PlaybackPacketLease? lease = null;

                try
                {
                    lease = await input.ReadAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (lease is null)
                    {
                        if (decoder is null)
                        {
                            _ = classifier.Complete();
                            endReason =
                                PlaybackDecoderRunEndReason.MediaUnrecognized;
                            continue;
                        }

                        var completeCancellation =
                            CancellationTokenSource.CreateLinkedTokenSource(
                                cancellationToken);
                        var completeTask = CompleteDecoderAsync(
                            decoder,
                            completeCancellation,
                            cancellationToken);
                        var completeWait =
                            await WaitForFixedDeadlineAsync(completeTask)
                                .ConfigureAwait(false);

                        if (completeWait ==
                            PlaybackDecoderOperationWait.Deferred)
                        {
                            TryCancel(completeCancellation);

                            if (completeTask.IsCompleted)
                            {
                                await completeTask.ConfigureAwait(false);
                                endReason =
                                    PlaybackDecoderRunEndReason.SourceCompleted;
                                continue;
                            }

                            RegisterDeferredCleanup(
                                ObserveOperationAndDisposeDecoderAsync(
                                    completeTask,
                                    decoder));
                            decoder = null;
                            cleanupDeferred = true;
                            callerCanceled =
                                callerCancellationToken.IsCancellationRequested;
                            endReason = callerCanceled ||
                                _ownerCancellation.IsCancellationRequested
                                ? PlaybackDecoderRunEndReason.OwnerStopped
                                : PlaybackDecoderRunEndReason.SourceCompleted;
                            continue;
                        }

                        endReason =
                            PlaybackDecoderRunEndReason.SourceCompleted;
                        continue;
                    }

                    if (decoder is null)
                    {
                        var collectingBootstrap = bootstrap ??
                            throw new InvalidOperationException(
                                "The decoder bootstrap is unavailable.");

                        if (lease.Discontinuity !=
                            PlaybackDiscontinuityKind.None)
                        {
                            collectingBootstrap.Clear();
                        }

                        var classification = classifier.Observe(
                            lease.Payload.Span,
                            lease.Discontinuity);

                        if (classification.State ==
                            PlaybackMediaClassificationState.LimitReached)
                        {
                            endReason =
                                PlaybackDecoderRunEndReason.MediaUnrecognized;
                            continue;
                        }

                        var copiedFromCurrent = collectingBootstrap.Append(lease);

                        if (classification.State ==
                            PlaybackMediaClassificationState.Collecting)
                        {
                            continue;
                        }

                        if (classification.State !=
                                PlaybackMediaClassificationState.Recognized ||
                            classification.Basis is null)
                        {
                            throw new InvalidOperationException(
                                "The media classifier returned an unexpected state.");
                        }

                        var context = new PlaybackDecoderCreationContext(
                            classification.Container,
                            classification.Basis.Value,
                            _outputSink);
                        decoder = _factory.Create(context);

                        if (decoder is null)
                        {
                            endReason =
                                PlaybackDecoderRunEndReason.DecoderUnsupported;
                            continue;
                        }

                        var bootstrapInput = collectingBootstrap.CreateInput();
                        PlaybackEncodedInput? remainderInput = null;
                        PlaybackPacketLease? transferredLease = null;

                        if (copiedFromCurrent < lease.Payload.Length)
                        {
                            remainderInput = new PlaybackEncodedInput(
                                PlaybackEncodedInputKind.Packet,
                                lease.Sequence,
                                AdvanceStreamPosition(
                                    lease.StreamPosition,
                                    copiedFromCurrent),
                                PlaybackDiscontinuityKind.None,
                                lease.Payload[copiedFromCurrent..]);
                            transferredLease = lease;
                            lease = null;
                        }

                        metrics.RecordSubmitted(bootstrapInput.Payload.Length);

                        if (remainderInput is not null)
                        {
                            metrics.RecordSubmitted(
                                remainderInput.Value.Payload.Length);
                        }

                        var transferredBootstrap = collectingBootstrap;
                        bootstrap = null;
                        var initialTask = ProcessInitialInputAsync(
                            decoder,
                            transferredBootstrap,
                            transferredLease,
                            bootstrapInput,
                            remainderInput,
                            cancellationToken);
                        var initialWait = await WaitForActiveOperationAsync(
                            initialTask,
                            cancellationToken).ConfigureAwait(false);

                        if (initialWait ==
                            PlaybackDecoderOperationWait.Deferred)
                        {
                            RegisterDeferredCleanup(
                                ObserveOperationAndDisposeDecoderAsync(
                                    initialTask,
                                    decoder));
                            decoder = null;
                            cleanupDeferred = true;
                            callerCanceled =
                                callerCancellationToken.IsCancellationRequested;
                            endReason =
                                PlaybackDecoderRunEndReason.OwnerStopped;
                        }

                        continue;
                    }

                    var resetBeforeDecode = lease.Discontinuity !=
                        PlaybackDiscontinuityKind.None;
                    var inputPacket = new PlaybackEncodedInput(
                        PlaybackEncodedInputKind.Packet,
                        lease.Sequence,
                        lease.StreamPosition,
                        lease.Discontinuity,
                        lease.Payload);

                    if (resetBeforeDecode)
                    {
                        metrics.RecordReset();
                    }

                    metrics.RecordSubmitted(inputPacket.Payload.Length);
                    var packetTask = ProcessPacketAsync(
                        decoder,
                        lease,
                        inputPacket,
                        resetBeforeDecode,
                        cancellationToken);
                    lease = null;
                    var packetWait = await WaitForActiveOperationAsync(
                        packetTask,
                        cancellationToken).ConfigureAwait(false);

                    if (packetWait ==
                        PlaybackDecoderOperationWait.Deferred)
                    {
                        RegisterDeferredCleanup(
                            ObserveOperationAndDisposeDecoderAsync(
                                packetTask,
                                decoder));
                        decoder = null;
                        cleanupDeferred = true;
                        callerCanceled =
                            callerCancellationToken.IsCancellationRequested;
                        endReason =
                            PlaybackDecoderRunEndReason.OwnerStopped;
                    }
                }
                finally
                {
                    lease?.Dispose();
                }
            }
        }
        catch (OperationCanceledException)
            when (callerCancellationToken.IsCancellationRequested ||
                _ownerCancellation.IsCancellationRequested)
        {
            callerCanceled =
                callerCancellationToken.IsCancellationRequested;
            endReason = PlaybackDecoderRunEndReason.OwnerStopped;
        }
        catch (Exception exception)
        {
            failure = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            bootstrap?.Dispose();
        }

        if (decoder is not null)
        {
            try
            {
                cleanupDeferred |= await DisposeDecoderWithinDeadlineAsync(
                    decoder).ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                if (failure is null &&
                    !callerCanceled &&
                    endReason != PlaybackDecoderRunEndReason.OwnerStopped)
                {
                    failure = ExceptionDispatchInfo.Capture(cleanupException);
                }
            }
        }

        if (callerCanceled)
        {
            throw new OperationCanceledException(
                callerCancellationToken);
        }

        failure?.Throw();

        var finalClassification = classifier.Snapshot;
        return new PlaybackDecoderRunResult(
            endReason ?? PlaybackDecoderRunEndReason.OwnerStopped,
            finalClassification.State,
            finalClassification.Container,
            finalClassification.Basis,
            metrics.SubmittedInputCount,
            metrics.SubmittedByteCount,
            metrics.ResetCount,
            cleanupDeferred);
    }

    private async Task<PlaybackDecoderOperationWait>
        WaitForActiveOperationAsync(
            Task operation,
            CancellationToken cancellationToken)
    {
        try
        {
            await operation.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return PlaybackDecoderOperationWait.Completed;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            var deadline = Task.Delay(
                _options.StopTimeout,
                CancellationToken.None);
            var completed = await Task.WhenAny(operation, deadline)
                .ConfigureAwait(false);

            if (completed == operation)
            {
                await operation.ConfigureAwait(false);
                return PlaybackDecoderOperationWait.Completed;
            }

            return PlaybackDecoderOperationWait.Deferred;
        }
    }

    private async Task<PlaybackDecoderOperationWait>
        WaitForFixedDeadlineAsync(Task operation)
    {
        var deadline = Task.Delay(_options.StopTimeout);
        var completed = await Task.WhenAny(operation, deadline)
            .ConfigureAwait(false);

        if (completed == operation)
        {
            await operation.ConfigureAwait(false);
            return PlaybackDecoderOperationWait.Completed;
        }

        return PlaybackDecoderOperationWait.Deferred;
    }

    private async Task<bool> DisposeDecoderWithinDeadlineAsync(
        IPlaybackDecoder decoder)
    {
        var disposeTask = DisposeDecoderAsync(decoder);
        var deadline = Task.Delay(_options.StopTimeout);
        var completed = await Task.WhenAny(disposeTask, deadline)
            .ConfigureAwait(false);

        if (completed == disposeTask)
        {
            await disposeTask.ConfigureAwait(false);
            return false;
        }

        RegisterDeferredCleanup(ObserveOperationAsync(disposeTask));
        return true;
    }

    private void RegisterDeferredCleanup(Task cleanupTask)
    {
        lock (_gate)
        {
            _deferredCleanupTask = _deferredCleanupTask is null
                ? cleanupTask
                : Task.WhenAll(_deferredCleanupTask, cleanupTask);
        }
    }

    private static async Task ProcessInitialInputAsync(
        IPlaybackDecoder decoder,
        PlaybackDecoderBootstrapBuffer bootstrap,
        PlaybackPacketLease? lease,
        PlaybackEncodedInput bootstrapInput,
        PlaybackEncodedInput? remainderInput,
        CancellationToken cancellationToken)
    {
        try
        {
            await decoder.DecodeAsync(
                bootstrapInput,
                cancellationToken).ConfigureAwait(false);

            if (remainderInput is not null)
            {
                await decoder.DecodeAsync(
                    remainderInput.Value,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            lease?.Dispose();
            bootstrap.Dispose();
        }
    }

    private static async Task ProcessPacketAsync(
        IPlaybackDecoder decoder,
        PlaybackPacketLease lease,
        PlaybackEncodedInput input,
        bool resetBeforeDecode,
        CancellationToken cancellationToken)
    {
        try
        {
            if (resetBeforeDecode)
            {
                await decoder.ResetAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            await decoder.DecodeAsync(input, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            lease.Dispose();
        }
    }

    private static async Task CompleteDecoderAsync(
        IPlaybackDecoder decoder,
        CancellationTokenSource cancellation,
        CancellationToken ownerCancellationToken)
    {
        try
        {
            await decoder.CompleteAsync(cancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellation.IsCancellationRequested &&
                !ownerCancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private static void TryCancel(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static async Task DisposeDecoderAsync(
        IPlaybackDecoder decoder)
    {
        await decoder.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task ObserveOperationAndDisposeDecoderAsync(
        Task operation,
        IPlaybackDecoder decoder)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        try
        {
            await decoder.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private static async Task ObserveOperationAsync(Task operation)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private static long? AdvanceStreamPosition(
        long? streamPosition,
        int byteCount)
    {
        if (streamPosition is null ||
            streamPosition > long.MaxValue - byteCount)
        {
            return null;
        }

        return streamPosition.Value + byteCount;
    }

    private enum PlaybackDecoderOperationWait
    {
        Completed,
        Deferred,
    }

    private sealed class PlaybackDecoderRunMetrics
    {
        public long SubmittedInputCount { get; private set; }

        public long SubmittedByteCount { get; private set; }

        public long ResetCount { get; private set; }

        public void RecordSubmitted(int byteCount)
        {
            SubmittedInputCount = checked(SubmittedInputCount + 1);
            SubmittedByteCount = checked(SubmittedByteCount + byteCount);
        }

        public void RecordReset()
        {
            ResetCount = checked(ResetCount + 1);
        }
    }
}
