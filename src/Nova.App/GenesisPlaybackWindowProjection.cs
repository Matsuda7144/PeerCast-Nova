namespace PeerCast.Nova.App;

internal interface IGenesisPlaybackWindowSurfaceTarget
{
    ValueTask<IAsyncDisposable?> OpenAsync(
        long ownerWindowToken,
        GenesisPlaybackWindowState state,
        CancellationToken cancellationToken);
}

internal interface IGenesisPlaybackWindowCloseAwareLifetime : IAsyncDisposable
{
    Task<bool> ClosedByUser { get; }
}

internal enum GenesisPlaybackWindowProjectionResult
{
    None,
    Projected,
    InvalidWindow,
    InvalidState,
    DispatcherRejected,
    TargetRejected,
    Cancelled,
    Faulted,
}

internal sealed record GenesisPlaybackWindowProjection(
    GenesisPlaybackWindowProjectionResult Result,
    IAsyncDisposable? Lifetime)
{
    public static async ValueTask<GenesisPlaybackWindowProjection> OpenAsync(
        IGenesisMainWindowDispatcher dispatcher,
        IGenesisPlaybackWindowSurfaceTarget target,
        GenesisPlaybackWindowState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(state);

        if (dispatcher.WindowToken == 0)
        {
            return Rejected(GenesisPlaybackWindowProjectionResult.InvalidWindow);
        }

        if (!state.IsOpen || string.IsNullOrWhiteSpace(state.Source))
        {
            return Rejected(GenesisPlaybackWindowProjectionResult.InvalidState);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Rejected(GenesisPlaybackWindowProjectionResult.Cancelled);
        }

        try
        {
            var invocation = await dispatcher.InvokeGenesisAsync(
                (ownerWindowToken, operationCancellationToken) =>
                    target.OpenAsync(
                        ownerWindowToken,
                        state,
                        operationCancellationToken),
                cancellationToken).ConfigureAwait(false);

            if (invocation.Result != GenesisMainWindowDispatcherResult.Completed &&
                invocation.Surface is not null)
            {
                try
                {
                    await invocation.Surface.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // A rejected dispatcher invocation does not transfer ownership.
                    // Cleanup is best-effort and must preserve the bounded result.
                }
            }

            return invocation.Result switch
            {
                GenesisMainWindowDispatcherResult.Completed
                    when invocation.Surface is not null =>
                    new(
                        GenesisPlaybackWindowProjectionResult.Projected,
                        invocation.Surface),
                GenesisMainWindowDispatcherResult.Completed =>
                    Rejected(GenesisPlaybackWindowProjectionResult.TargetRejected),
                GenesisMainWindowDispatcherResult.Cancelled =>
                    Rejected(GenesisPlaybackWindowProjectionResult.Cancelled),
                GenesisMainWindowDispatcherResult.Shutdown =>
                    Rejected(GenesisPlaybackWindowProjectionResult.DispatcherRejected),
                _ => Rejected(GenesisPlaybackWindowProjectionResult.Faulted),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Rejected(GenesisPlaybackWindowProjectionResult.Cancelled);
        }
        catch
        {
            return Rejected(GenesisPlaybackWindowProjectionResult.Faulted);
        }
    }

    private static GenesisPlaybackWindowProjection Rejected(
        GenesisPlaybackWindowProjectionResult result) => new(result, null);
}
