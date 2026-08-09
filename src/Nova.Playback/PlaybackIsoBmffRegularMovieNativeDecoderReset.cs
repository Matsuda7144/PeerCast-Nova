namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffRegularMovieNativeDecoderResetCreationResult
{
    Created,
    MissingSession,
    SessionUnavailable,
    EmptySession,
}

public enum PlaybackIsoBmffRegularMovieNativeDecoderResetState
{
    Ready,
    Faulted,
    Disposed,
}

public enum PlaybackIsoBmffRegularMovieNativeDecoderResetResult
{
    None,
    Reset,
    Cancelled,
    AlreadyRunning,
    ResetRejected,
    PartialResetFault,
    Faulted,
    Disposed,
}

public sealed record PlaybackIsoBmffRegularMovieNativeDecoderResetSnapshot(
    PlaybackIsoBmffRegularMovieNativeDecoderResetState State,
    PlaybackIsoBmffRegularMovieNativeDecoderResetResult Result,
    long ResetCount,
    bool HasActiveReset,
    bool HasVideo,
    bool HasAudio);

internal interface IPlaybackIsoBmffRegularMovieNativeDecoderResetOperations
{
    PlaybackDecoderFfmpegDiscontinuityResetResult Reset(
        PlaybackIsoBmffRegularMovieNativeDecoderSession session,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role);
}

internal sealed class PlaybackIsoBmffRegularMovieNativeDecoderResetOperations :
    IPlaybackIsoBmffRegularMovieNativeDecoderResetOperations
{
    private readonly PlaybackDecoderFfmpegDiscontinuityResetBoundary _reset =
        new();

    public PlaybackDecoderFfmpegDiscontinuityResetResult Reset(
        PlaybackIsoBmffRegularMovieNativeDecoderSession session,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role)
    {
        var context = role switch
        {
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video =>
                session.VideoContext,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Audio =>
                session.AudioContext,
            _ => null,
        };
        return context is null
            ? PlaybackDecoderFfmpegDiscontinuityResetResult.ContextUnavailable
            : _reset.Reset(context);
    }
}

public sealed class PlaybackIsoBmffRegularMovieNativeDecoderReset : IDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PlaybackIsoBmffRegularMovieNativeDecoderSession _session;
    private readonly IPlaybackIsoBmffRegularMovieNativeDecoderResetOperations
        _operations;
    private PlaybackIsoBmffRegularMovieNativeDecoderResetState _state =
        PlaybackIsoBmffRegularMovieNativeDecoderResetState.Ready;
    private PlaybackIsoBmffRegularMovieNativeDecoderResetResult _result =
        PlaybackIsoBmffRegularMovieNativeDecoderResetResult.None;
    private CancellationTokenSource? _activeCancellation;
    private long _resetCount;
    private bool _hasActiveReset;

    internal PlaybackIsoBmffRegularMovieNativeDecoderReset(
        PlaybackIsoBmffRegularMovieNativeDecoderSession session,
        IPlaybackIsoBmffRegularMovieNativeDecoderResetOperations operations)
    {
        _session = session;
        _operations = operations;
    }

    public PlaybackIsoBmffRegularMovieNativeDecoderResetSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(
                    _state,
                    _result,
                    _resetCount,
                    _hasActiveReset,
                    _session.HasVideo,
                    _session.HasAudio);
            }
        }
    }

    internal bool UsesSession(
        PlaybackIsoBmffRegularMovieNativeDecoderSession session) =>
        ReferenceEquals(_session, session);

    public async ValueTask<
        PlaybackIsoBmffRegularMovieNativeDecoderResetResult> ResetAsync(
            CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_state ==
                PlaybackIsoBmffRegularMovieNativeDecoderResetState.Disposed)
            {
                return SetResultLocked(
                    PlaybackIsoBmffRegularMovieNativeDecoderResetResult
                        .Disposed);
            }

            if (_state ==
                PlaybackIsoBmffRegularMovieNativeDecoderResetState.Faulted)
            {
                return _result;
            }
        }

        bool entered;
        try
        {
            entered = await _gate.WaitAsync(0, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return SetResult(
                PlaybackIsoBmffRegularMovieNativeDecoderResetResult.Cancelled);
        }

        if (!entered)
        {
            return SetResult(
                PlaybackIsoBmffRegularMovieNativeDecoderResetResult
                    .AlreadyRunning);
        }

        CancellationTokenSource operation;
        lock (_sync)
        {
            if (_state ==
                PlaybackIsoBmffRegularMovieNativeDecoderResetState.Disposed)
            {
                _gate.Release();
                return SetResultLocked(
                    PlaybackIsoBmffRegularMovieNativeDecoderResetResult
                        .Disposed);
            }

            operation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            _activeCancellation = operation;
            _hasActiveReset = true;
        }

        var completedRoleCount = 0;
        try
        {
            if (_session.IsDisposed)
            {
                return Fault(
                    PlaybackIsoBmffRegularMovieNativeDecoderResetResult
                        .Faulted);
            }

            if (_session.HasAudio)
            {
                var audio = ResetRole(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole
                        .Audio,
                    operation.Token);
                if (audio !=
                    PlaybackIsoBmffRegularMovieNativeDecoderResetResult.Reset)
                {
                    return audio ==
                        PlaybackIsoBmffRegularMovieNativeDecoderResetResult
                            .Cancelled
                        ? SetResult(audio)
                        : Fault(
                            PlaybackIsoBmffRegularMovieNativeDecoderResetResult
                                .ResetRejected);
                }

                completedRoleCount++;
            }

            if (operation.IsCancellationRequested)
            {
                return completedRoleCount == 0
                    ? SetResult(
                        PlaybackIsoBmffRegularMovieNativeDecoderResetResult
                            .Cancelled)
                    : Fault(
                        PlaybackIsoBmffRegularMovieNativeDecoderResetResult
                            .PartialResetFault);
            }

            if (_session.HasVideo)
            {
                var video = ResetRole(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole
                        .Video,
                    operation.Token);
                if (video !=
                    PlaybackIsoBmffRegularMovieNativeDecoderResetResult.Reset)
                {
                    if (video ==
                            PlaybackIsoBmffRegularMovieNativeDecoderResetResult
                                .Cancelled &&
                        completedRoleCount == 0)
                    {
                        return SetResult(video);
                    }

                    return Fault(
                        completedRoleCount == 0
                            ? PlaybackIsoBmffRegularMovieNativeDecoderResetResult
                                .ResetRejected
                            : PlaybackIsoBmffRegularMovieNativeDecoderResetResult
                                .PartialResetFault);
                }

                completedRoleCount++;
            }

            lock (_sync)
            {
                _resetCount = checked(_resetCount + 1);
                return SetResultLocked(
                    PlaybackIsoBmffRegularMovieNativeDecoderResetResult.Reset);
            }
        }
        catch (OverflowException)
        {
            return Fault(
                PlaybackIsoBmffRegularMovieNativeDecoderResetResult.Faulted);
        }
        catch
        {
            return Fault(
                completedRoleCount == 0
                    ? PlaybackIsoBmffRegularMovieNativeDecoderResetResult
                        .ResetRejected
                    : PlaybackIsoBmffRegularMovieNativeDecoderResetResult
                        .PartialResetFault);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_activeCancellation, operation))
                {
                    _activeCancellation = null;
                }

                _hasActiveReset = false;
            }

            operation.Dispose();
            _gate.Release();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_state ==
                PlaybackIsoBmffRegularMovieNativeDecoderResetState.Disposed)
            {
                return;
            }

            _state =
                PlaybackIsoBmffRegularMovieNativeDecoderResetState.Disposed;
            _result =
                PlaybackIsoBmffRegularMovieNativeDecoderResetResult.Disposed;
            _activeCancellation?.Cancel();
        }

        _gate.Wait();
        _gate.Release();
        GC.SuppressFinalize(this);
    }

    private PlaybackIsoBmffRegularMovieNativeDecoderResetResult ResetRole(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return PlaybackIsoBmffRegularMovieNativeDecoderResetResult
                .Cancelled;
        }

        var reset = _operations.Reset(_session, role);
        return reset == PlaybackDecoderFfmpegDiscontinuityResetResult.Reset
            ? PlaybackIsoBmffRegularMovieNativeDecoderResetResult.Reset
            : PlaybackIsoBmffRegularMovieNativeDecoderResetResult
                .ResetRejected;
    }

    private PlaybackIsoBmffRegularMovieNativeDecoderResetResult Fault(
        PlaybackIsoBmffRegularMovieNativeDecoderResetResult result)
    {
        lock (_sync)
        {
            _state = PlaybackIsoBmffRegularMovieNativeDecoderResetState.Faulted;
            SetResultLocked(result);
        }

        _session.Dispose();
        return result;
    }

    private PlaybackIsoBmffRegularMovieNativeDecoderResetResult SetResult(
        PlaybackIsoBmffRegularMovieNativeDecoderResetResult result)
    {
        lock (_sync)
        {
            return SetResultLocked(result);
        }
    }

    private PlaybackIsoBmffRegularMovieNativeDecoderResetResult SetResultLocked(
        PlaybackIsoBmffRegularMovieNativeDecoderResetResult result)
    {
        _result = result;
        return result;
    }
}

public sealed record PlaybackIsoBmffRegularMovieNativeDecoderResetCreation(
    PlaybackIsoBmffRegularMovieNativeDecoderResetCreationResult Result,
    PlaybackIsoBmffRegularMovieNativeDecoderReset? Reset);

public sealed class PlaybackIsoBmffRegularMovieNativeDecoderResetFactory
{
    private readonly IPlaybackIsoBmffRegularMovieNativeDecoderResetOperations
        _operations;

    public PlaybackIsoBmffRegularMovieNativeDecoderResetFactory()
        : this(new PlaybackIsoBmffRegularMovieNativeDecoderResetOperations())
    {
    }

    internal PlaybackIsoBmffRegularMovieNativeDecoderResetFactory(
        IPlaybackIsoBmffRegularMovieNativeDecoderResetOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public PlaybackIsoBmffRegularMovieNativeDecoderResetCreation Create(
        PlaybackIsoBmffRegularMovieNativeDecoderSession? session)
    {
        if (session is null)
        {
            return new(
                PlaybackIsoBmffRegularMovieNativeDecoderResetCreationResult
                    .MissingSession,
                null);
        }

        if (session.IsDisposed)
        {
            return new(
                PlaybackIsoBmffRegularMovieNativeDecoderResetCreationResult
                    .SessionUnavailable,
                null);
        }

        if (!session.HasVideo && !session.HasAudio)
        {
            return new(
                PlaybackIsoBmffRegularMovieNativeDecoderResetCreationResult
                    .EmptySession,
                null);
        }

        return new(
            PlaybackIsoBmffRegularMovieNativeDecoderResetCreationResult.Created,
            new PlaybackIsoBmffRegularMovieNativeDecoderReset(
                session,
                _operations));
    }
}
