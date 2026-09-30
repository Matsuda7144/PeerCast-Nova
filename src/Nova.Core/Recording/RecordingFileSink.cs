using System.Runtime.ExceptionServices;
using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Concrete recorder backend that writes injected readable streams into
/// injected file paths. It has no PCP, playback, GUI, or default-path policy.
/// </summary>
public sealed class RecordingFileSink :
    IRecordingSink,
    IRecordingSinkSessionMonitor
{
    public const int DefaultMaximumActiveSessionCount = 4096;

    private readonly object gate = new();
    private readonly IRecordingStreamSourceProvider sourceProvider;
    private readonly IRecordingFilePathProvider pathProvider;
    private readonly int maximumActiveSessionCount;
    private readonly Dictionary<ChannelId, ActiveRecording> activeByChannel = [];

    public RecordingFileSink(
        IRecordingStreamSourceProvider sourceProvider,
        IRecordingFilePathProvider pathProvider,
        int maximumActiveSessionCount = DefaultMaximumActiveSessionCount)
    {
        ArgumentNullException.ThrowIfNull(sourceProvider);
        ArgumentNullException.ThrowIfNull(pathProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumActiveSessionCount);

        this.sourceProvider = sourceProvider;
        this.pathProvider = pathProvider;
        this.maximumActiveSessionCount = maximumActiveSessionCount;
    }

    public async ValueTask<RecordingSinkStartResult> StartAsync(
        RecordingSinkStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        ActiveRecording active;

        lock (gate)
        {
            if (activeByChannel.TryGetValue(
                    request.Candidate.ChannelId,
                    out var existing))
            {
                return RecordingSinkStartResult.AlreadyRunning(
                    existing.Session);
            }

            if (activeByChannel.Count >= maximumActiveSessionCount)
            {
                throw new InvalidOperationException(
                    "The recording file sink active session limit has been reached.");
            }

            var sessionId = new RecordingSessionId(Guid.NewGuid());
            var session = request.Origin switch
            {
                RecordingSessionOrigin.Manual =>
                    RecordingSinkSession.Manual(
                        sessionId,
                        request.Candidate.ChannelId),

                RecordingSessionOrigin.AutomaticRule =>
                    new RecordingSinkSession(
                        sessionId,
                        request.Candidate.ChannelId,
                        request.RuleId ?? throw new InvalidOperationException(
                            "An automatic recording request must select a rule.")),

                _ => throw new InvalidOperationException(
                    $"Unsupported recording session origin: {request.Origin}."),
            };

            active = new ActiveRecording(session);
            activeByChannel.Add(
                request.Candidate.ChannelId,
                active);
        }

        Stream? source = null;

        try
        {
            var path = pathProvider.GetPath(
                request,
                active.Session.SessionId);

            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            using (var sourceOpenLifetime =
                   CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    active.StoppingToken))
            {
                source = await sourceProvider.OpenReadAsync(
                    request,
                    sourceOpenLifetime.Token).ConfigureAwait(false);
            }

            ArgumentNullException.ThrowIfNull(source);

            if (!source.CanRead)
            {
                var unreadableSource = source;
                source = null;
                await unreadableSource.DisposeAsync().ConfigureAwait(false);
                throw new InvalidOperationException(
                    "The recording source provider returned an unreadable stream.");
            }

            if (string.Equals(
                    System.IO.Path.GetExtension(path),
                    ".flv",
                    StringComparison.OrdinalIgnoreCase))
            {
                source = new FlvRecordingTimestampNormalizingStream(
                    new FlvRecordingDecodableStartStream(source));
            }

            if (!active.TryStart(
                source,
                path))
            {
                var stoppedSource = source;
                source = null;
                await stoppedSource.DisposeAsync().ConfigureAwait(false);

                active.CompleteStartWithoutWrite();
                RemoveIfCurrent(active);
                active.Dispose();

                return RecordingSinkStartResult.Rejected;
            }

            source = null;

            return RecordingSinkStartResult.Started(
                active.Session);
        }
        catch (Exception exception)
        {
            var disposeFailure = source is null
                ? null
                : await TryDisposeSourceAsync(source).ConfigureAwait(false);
            source = null;

            if (exception is OperationCanceledException &&
                disposeFailure is null &&
                !cancellationToken.IsCancellationRequested &&
                active.StopRequested)
            {
                active.CompleteStartWithoutWrite();
                RemoveIfCurrent(active);
                active.Dispose();

                return RecordingSinkStartResult.Rejected;
            }

            var failure = disposeFailure is null
                ? exception
                : new AggregateException(
                    "Recording source acquisition and disposal both failed.",
                    exception,
                    disposeFailure);

            active.CompleteStartFailure(failure);
            RemoveIfCurrent(active);
            active.Dispose();

            if (disposeFailure is not null)
            {
                throw failure;
            }

            throw;
        }
    }

    public async ValueTask<RecordingSinkStopResult> StopAsync(
        RecordingSinkStopRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        ActiveRecording? active;

        lock (gate)
        {
            _ = activeByChannel.TryGetValue(
                request.Session.ChannelId,
                out active);

            if (active is null ||
                active.Session.SessionId != request.Session.SessionId)
            {
                return RecordingSinkStopResult.AlreadyStopped;
            }
        }

        active.RequestStop();

        try
        {
            await active.WaitForCompletionAsync(
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var fault = active.GetFault() ??
                RecordingFaultDescriptor.FromException(exception);
            RemoveIfCurrent(active);
            active.Dispose();
            return RecordingSinkStopResult.FromFault(fault);
        }

        RemoveIfCurrent(active);
        var noDecodableVideoStart = active.NoDecodableVideoStart;
        active.Dispose();

        return noDecodableVideoStart
            ? RecordingSinkStopResult.NoDecodableVideoStart
            : RecordingSinkStopResult.Stopped;
    }

    public bool TryGetStatus(
        RecordingSinkSession session,
        out RecordingFileSinkSessionStatus? status)
    {
        ArgumentNullException.ThrowIfNull(session);

        ActiveRecording? active;

        lock (gate)
        {
            _ = activeByChannel.TryGetValue(
                session.ChannelId,
                out active);

            if (active is null ||
                active.Session.SessionId != session.SessionId)
            {
                status = null;
                return false;
            }
        }

        status = active.GetStatus();
        return true;
    }

    public bool TryGetSessionStatus(
        RecordingSinkSession session,
        out RecordingSinkSessionLifecycleStatus? status)
    {
        if (!TryGetStatus(
                session,
                out var fileStatus))
        {
            status = null;
            return false;
        }

        var kind = fileStatus!.Kind switch
        {
            RecordingFileSinkSessionStatusKind.Starting =>
                RecordingSinkSessionLifecycleKind.Active,

            RecordingFileSinkSessionStatusKind.Writing =>
                RecordingSinkSessionLifecycleKind.Active,

            RecordingFileSinkSessionStatusKind.Completed =>
                RecordingSinkSessionLifecycleKind.Completed,

            RecordingFileSinkSessionStatusKind.NoDecodableVideoStart =>
                RecordingSinkSessionLifecycleKind.NoDecodableVideoStart,

            RecordingFileSinkSessionStatusKind.Faulted =>
                RecordingSinkSessionLifecycleKind.Faulted,

            _ => throw new InvalidOperationException(
                $"Unsupported recording file session status: {fileStatus.Kind}."),
        };

        status = new RecordingSinkSessionLifecycleStatus(
            session,
            kind,
            fileStatus.Fault);
        return true;
    }

    private void RemoveIfCurrent(ActiveRecording active)
    {
        lock (gate)
        {
            if (activeByChannel.TryGetValue(
                    active.Session.ChannelId,
                    out var current) &&
                ReferenceEquals(current, active))
            {
                activeByChannel.Remove(
                    active.Session.ChannelId);
            }
        }
    }

    private static async ValueTask<Exception?> TryDisposeSourceAsync(
        Stream source)
    {
        try
        {
            await source.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class ActiveRecording : IDisposable
    {
        private readonly object gate = new();
        private readonly CancellationTokenSource stopping = new();
        private readonly TaskCompletionSource<Task<RecordingFileWriteOutcome>?>
            startCompletion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
        private Task<RecordingFileWriteOutcome>? writeTask;
        private RecordingFileWriteOutcome? writeOutcome;
        private RecordingFaultDescriptor? fault;
        private Exception? startFailure;
        private bool faulted;
        private bool stopRequested;
        private bool isDisposed;

        public ActiveRecording(
            RecordingSinkSession session)
        {
            Session = session;
        }

        public RecordingSinkSession Session { get; }

        public CancellationToken StoppingToken => stopping.Token;

        public bool StopRequested
        {
            get
            {
                lock (gate)
                {
                    return stopRequested;
                }
            }
        }

        public bool TryStart(
            Stream source,
            string path)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            lock (gate)
            {
                if (isDisposed || stopRequested)
                {
                    return false;
                }

                if (writeTask is not null)
                {
                    throw new InvalidOperationException(
                        "The recording file session has already started.");
                }

                writeTask = RunAsync(
                    source,
                    path);
                startCompletion.TrySetResult(writeTask);
                return true;
            }
        }

        public void CompleteStartWithoutWrite()
        {
            lock (gate)
            {
                if (writeTask is null)
                {
                    startCompletion.TrySetResult(null);
                }
            }
        }

        public void CompleteStartFailure(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);

            lock (gate)
            {
                if (writeTask is null)
                {
                    startFailure = exception;
                    fault = RecordingFaultDescriptor.FromException(exception);
                    startCompletion.TrySetResult(null);
                }
            }
        }

        public void RequestStop()
        {
            lock (gate)
            {
                if (isDisposed)
                {
                    return;
                }

                stopRequested = true;
            }

            try
            {
                stopping.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // A concurrent completion may dispose the token source.
            }
        }

        public async ValueTask WaitForCompletionAsync(
            CancellationToken cancellationToken)
        {
            var task = await startCompletion.Task.WaitAsync(
                cancellationToken).ConfigureAwait(false);

            if (task is null)
            {
                Exception? failure;

                lock (gate)
                {
                    failure = startFailure;
                }

                if (failure is not null)
                {
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }

                return;
            }

            _ = await task.WaitAsync(
                cancellationToken).ConfigureAwait(false);
        }

        public RecordingFileSinkSessionStatus GetStatus()
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(
                    isDisposed,
                    this);

                if (writeTask is null)
                {
                    return new RecordingFileSinkSessionStatus(
                        Session,
                        RecordingFileSinkSessionStatusKind.Starting,
                        writeResult: null);
                }

                if (!writeTask.IsCompleted)
                {
                    return new RecordingFileSinkSessionStatus(
                        Session,
                        RecordingFileSinkSessionStatusKind.Writing,
                        writeResult: null);
                }

                if (faulted)
                {
                    return new RecordingFileSinkSessionStatus(
                        Session,
                        RecordingFileSinkSessionStatusKind.Faulted,
                        writeResult: null,
                        fault);
                }

                if (writeOutcome?.IsNoDecodableVideoStart == true)
                {
                    return new RecordingFileSinkSessionStatus(
                        Session,
                        RecordingFileSinkSessionStatusKind.NoDecodableVideoStart,
                        writeResult: null);
                }

                return new RecordingFileSinkSessionStatus(
                    Session,
                    RecordingFileSinkSessionStatusKind.Completed,
                    writeOutcome?.WriteResult);
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (isDisposed)
                {
                    return;
                }

                isDisposed = true;
            }

            stopping.Dispose();
        }

        public RecordingFaultDescriptor? GetFault()
        {
            lock (gate)
            {
                return fault;
            }
        }

        public bool NoDecodableVideoStart
        {
            get
            {
                lock (gate)
                {
                    return writeOutcome?.IsNoDecodableVideoStart == true;
                }
            }
        }

        private async Task<RecordingFileWriteOutcome> RunAsync(
            Stream source,
            string path)
        {
            try
            {
                await using (source.ConfigureAwait(false))
                {
                    var result = await RecordingStreamFileWriter.WriteForFileSinkAsync(
                        source,
                        path,
                        (stage, exception) =>
                        {
                            lock (gate)
                            {
                                fault = RecordingFaultDescriptor.FromException(
                                    stage,
                                    exception);
                            }
                        },
                        stopping.Token).ConfigureAwait(false);

                    lock (gate)
                    {
                        writeOutcome = result;
                    }

                    return result;
                }
            }
            catch (Exception exception)
            {
                lock (gate)
                {
                    faulted = true;
                    fault ??= RecordingFaultDescriptor.FromException(exception);
                }

                throw;
            }
        }
    }
}
