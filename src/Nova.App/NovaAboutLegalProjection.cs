namespace PeerCast.Nova.App;

public enum NovaAboutLegalProjectionResult
{
    None,
    Projected,
    PlatformUnsupported,
    MaterialRejected,
    DispatcherRejected,
    TargetRejected,
    Cancelled,
    Faulted,
}

public enum NovaAboutLegalDispatcherResult
{
    None,
    Completed,
    Cancelled,
    Rejected,
    Faulted,
}

public sealed record NovaAboutLegalDispatcherInvocation(
    NovaAboutLegalDispatcherResult Result,
    IAsyncDisposable? Projection);

public interface INovaAboutLegalDispatcher
{
    ValueTask<NovaAboutLegalDispatcherInvocation> InvokeAsync(
        Func<CancellationToken, ValueTask<IAsyncDisposable?>> operation,
        CancellationToken cancellationToken);
}

public interface INovaAboutLegalProjectionTarget
{
    ValueTask<IAsyncDisposable?> OpenAsync(
        NovaAboutLegalModel model,
        CancellationToken cancellationToken);
}

public sealed record NovaAboutLegalProjection(
    NovaAboutLegalProjectionResult Result,
    IAsyncDisposable? Lifetime)
{
    public static async ValueTask<NovaAboutLegalProjection> OpenAsync(
        INovaAboutLegalDispatcher dispatcher,
        INovaAboutLegalProjectionTarget target,
        CancellationToken cancellationToken = default) =>
        await OpenAsync(
            dispatcher,
            target,
            static () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17_763),
            cancellationToken).ConfigureAwait(false);

    internal static async ValueTask<NovaAboutLegalProjection> OpenAsync(
        INovaAboutLegalDispatcher dispatcher,
        INovaAboutLegalProjectionTarget target,
        Func<bool> isSupportedPlatform,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(isSupportedPlatform);

        if (!isSupportedPlatform())
        {
            return new(NovaAboutLegalProjectionResult.PlatformUnsupported, null);
        }

        var material = NovaAboutLegalMaterial.Load();
        if (material.Result != NovaAboutLegalMaterialResult.Loaded ||
            material.Model is null)
        {
            return new(NovaAboutLegalProjectionResult.MaterialRejected, null);
        }

        NovaAboutLegalDispatcherInvocation invocation;
        try
        {
            invocation = await dispatcher.InvokeAsync(
                token => target.OpenAsync(material.Model, token),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(NovaAboutLegalProjectionResult.Cancelled, null);
        }
        catch
        {
            return new(NovaAboutLegalProjectionResult.Faulted, null);
        }

        return invocation.Result switch
        {
            NovaAboutLegalDispatcherResult.Completed
                when invocation.Projection is not null =>
                    new(NovaAboutLegalProjectionResult.Projected, invocation.Projection),
            NovaAboutLegalDispatcherResult.Completed =>
                new(NovaAboutLegalProjectionResult.TargetRejected, null),
            NovaAboutLegalDispatcherResult.Cancelled =>
                new(NovaAboutLegalProjectionResult.Cancelled, null),
            NovaAboutLegalDispatcherResult.Faulted =>
                new(NovaAboutLegalProjectionResult.Faulted, null),
            _ => new(NovaAboutLegalProjectionResult.DispatcherRejected, null),
        };
    }
}
