namespace PeerCast.Nova.App;

internal enum GenesisMainWindowDispatcherResult
{
    None,
    Completed,
    Cancelled,
    Shutdown,
    Faulted,
}

internal sealed record GenesisMainWindowDispatcherInvocation(
    GenesisMainWindowDispatcherResult Result,
    IAsyncDisposable? Surface);

internal interface IGenesisMainWindowDispatcher
{
    long WindowToken { get; }

    ValueTask<GenesisMainWindowDispatcherInvocation> InvokeGenesisAsync(
        Func<long, CancellationToken, ValueTask<IAsyncDisposable?>> operation,
        CancellationToken cancellationToken);
}

internal interface IGenesisMainWindowSurfaceTarget
{
    ValueTask<IAsyncDisposable?> OpenAsync(
        long windowToken,
        GenesisMainViewModel viewModel,
        CancellationToken cancellationToken);
}

internal enum GenesisMainWindowProjectionResult
{
    None,
    Projected,
    InvalidWindow,
    DispatcherRejected,
    TargetRejected,
    Cancelled,
    Faulted,
}

internal sealed record GenesisMainWindowProjection(
    GenesisMainWindowProjectionResult Result,
    IAsyncDisposable? Lifetime)
{
    public static async ValueTask<GenesisMainWindowProjection> OpenAsync(
        IGenesisMainWindowDispatcher dispatcher,
        IGenesisMainWindowSurfaceTarget target,
        GenesisMainViewModel viewModel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(viewModel);

        if (dispatcher.WindowToken == 0)
        {
            return Rejected(GenesisMainWindowProjectionResult.InvalidWindow);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Rejected(GenesisMainWindowProjectionResult.Cancelled);
        }

        try
        {
            var invocation = await dispatcher.InvokeGenesisAsync(
                (windowToken, operationCancellationToken) =>
                    target.OpenAsync(windowToken, viewModel, operationCancellationToken),
                cancellationToken).ConfigureAwait(false);

            if (invocation.Result == GenesisMainWindowDispatcherResult.Completed &&
                cancellationToken.IsCancellationRequested)
            {
                if (invocation.Surface is not null)
                {
                    try
                    {
                        await invocation.Surface.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // Cancellation keeps ownership with the projection boundary.
                        // Cleanup is best-effort and must preserve the cancelled result.
                    }
                }

                return Rejected(GenesisMainWindowProjectionResult.Cancelled);
            }

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
                    new(GenesisMainWindowProjectionResult.Projected, invocation.Surface),
                GenesisMainWindowDispatcherResult.Completed =>
                    Rejected(GenesisMainWindowProjectionResult.TargetRejected),
                GenesisMainWindowDispatcherResult.Cancelled =>
                    Rejected(GenesisMainWindowProjectionResult.Cancelled),
                GenesisMainWindowDispatcherResult.Shutdown =>
                    Rejected(GenesisMainWindowProjectionResult.DispatcherRejected),
                _ => Rejected(GenesisMainWindowProjectionResult.Faulted),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Rejected(GenesisMainWindowProjectionResult.Cancelled);
        }
        catch
        {
            return Rejected(GenesisMainWindowProjectionResult.Faulted);
        }
    }

    private static GenesisMainWindowProjection Rejected(
        GenesisMainWindowProjectionResult result) => new(result, null);
}
