namespace PeerCast.Nova.Playback;

public enum PlaybackWindowsOutputDispatcherInvocationResult
{
    Completed,
    Cancelled,
    Shutdown,
    Faulted,
}

public readonly record struct PlaybackWindowsOutputDispatcherInvocation<T>(
    PlaybackWindowsOutputDispatcherInvocationResult Result,
    T Value);

/// <summary>
/// Application-owned finite dispatch seam for the Windows output owner thread.
/// Implementations must not retain operations after completion and must reject
/// new work after application dispatcher shutdown.
/// </summary>
public interface IPlaybackWindowsOutputDispatcher
{
    bool HasAccess { get; }

    ValueTask<PlaybackWindowsOutputDispatcherInvocation<T>> InvokeAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default);
}
