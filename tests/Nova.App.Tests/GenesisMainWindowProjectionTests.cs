namespace PeerCast.Nova.App.Tests;

public sealed class GenesisMainWindowProjectionTests
{
    [Fact]
    public async Task ProjectionUsesExistingWindowTokenAndReturnsFiniteLifetime()
    {
        var lifetime = new FakeLifetime();
        var dispatcher = new FakeDispatcher(7144, lifetime);
        var target = new FakeTarget(lifetime);
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());

        var projection = await GenesisMainWindowProjection.OpenAsync(
            dispatcher,
            target,
            viewModel);

        Assert.Equal(GenesisMainWindowProjectionResult.Projected, projection.Result);
        Assert.Same(lifetime, projection.Lifetime);
        Assert.Equal(7144, dispatcher.LastWindowToken);
        Assert.Equal(7144, target.LastWindowToken);
        Assert.Same(viewModel, target.LastViewModel);
        Assert.Equal(1, dispatcher.InvocationCount);
        Assert.Equal(1, target.OpenCount);

        await projection.Lifetime!.DisposeAsync();
        Assert.True(lifetime.IsDisposed);
    }

    [Fact]
    public async Task ZeroWindowTokenRejectsBeforeDispatcherOrTarget()
    {
        var lifetime = new FakeLifetime();
        var dispatcher = new FakeDispatcher(0, lifetime);
        var target = new FakeTarget(lifetime);

        var projection = await GenesisMainWindowProjection.OpenAsync(
            dispatcher,
            target,
            new GenesisMainViewModel(new FakePlaybackSessionController()));

        Assert.Equal(GenesisMainWindowProjectionResult.InvalidWindow, projection.Result);
        Assert.Null(projection.Lifetime);
        Assert.Equal(0, dispatcher.InvocationCount);
        Assert.Equal(0, target.OpenCount);
    }

    [Fact]
    public async Task TargetRejectionIsBoundedWithoutLifetime()
    {
        var dispatcher = new FakeDispatcher(42, null);
        var target = new FakeTarget(null);

        var projection = await GenesisMainWindowProjection.OpenAsync(
            dispatcher,
            target,
            new GenesisMainViewModel(new FakePlaybackSessionController()));

        Assert.Equal(GenesisMainWindowProjectionResult.TargetRejected, projection.Result);
        Assert.Null(projection.Lifetime);
        Assert.Equal(1, dispatcher.InvocationCount);
        Assert.Equal(1, target.OpenCount);
    }

    [Fact]
    public async Task DispatcherShutdownDoesNotInvokeTarget()
    {
        var dispatcher = new FakeDispatcher(
            42,
            null,
            GenesisMainWindowDispatcherResult.Shutdown,
            invokeOperation: false);
        var target = new FakeTarget(new FakeLifetime());

        var projection = await GenesisMainWindowProjection.OpenAsync(
            dispatcher,
            target,
            new GenesisMainViewModel(new FakePlaybackSessionController()));

        Assert.Equal(
            GenesisMainWindowProjectionResult.DispatcherRejected,
            projection.Result);
        Assert.Null(projection.Lifetime);
        Assert.Equal(1, dispatcher.InvocationCount);
        Assert.Equal(0, target.OpenCount);
    }

    [Fact]
    public async Task DispatcherShutdownDisposesUnexpectedSurface()
    {
        var lifetime = new FakeLifetime();
        var dispatcher = new FakeDispatcher(
            42,
            lifetime,
            GenesisMainWindowDispatcherResult.Shutdown);
        var target = new FakeTarget(lifetime);

        var projection = await GenesisMainWindowProjection.OpenAsync(
            dispatcher,
            target,
            new GenesisMainViewModel(new FakePlaybackSessionController()));

        Assert.Equal(
            GenesisMainWindowProjectionResult.DispatcherRejected,
            projection.Result);
        Assert.Null(projection.Lifetime);
        Assert.Equal(1, dispatcher.InvocationCount);
        Assert.Equal(1, target.OpenCount);
        Assert.True(lifetime.IsDisposed);
    }

    [Fact]
    public async Task CancellationAfterTargetOpenDisposesSurface()
    {
        using var cancellationSource = new CancellationTokenSource();
        var lifetime = new FakeLifetime();
        var dispatcher = new FakeDispatcher(7144, lifetime);
        var target = new FakeTarget(lifetime, cancellationSource.Cancel);

        var projection = await GenesisMainWindowProjection.OpenAsync(
            dispatcher,
            target,
            new GenesisMainViewModel(new FakePlaybackSessionController()),
            cancellationSource.Token);

        Assert.Equal(GenesisMainWindowProjectionResult.Cancelled, projection.Result);
        Assert.Null(projection.Lifetime);
        Assert.Equal(1, dispatcher.InvocationCount);
        Assert.Equal(1, target.OpenCount);
        Assert.True(lifetime.IsDisposed);
    }

    private sealed class FakeDispatcher(
        long windowToken,
        IAsyncDisposable? surface,
        GenesisMainWindowDispatcherResult result = GenesisMainWindowDispatcherResult.Completed,
        bool invokeOperation = true) : IGenesisMainWindowDispatcher
    {
        public long WindowToken { get; } = windowToken;

        public int InvocationCount { get; private set; }

        public long LastWindowToken { get; private set; }

        public async ValueTask<GenesisMainWindowDispatcherInvocation> InvokeGenesisAsync(
            Func<long, CancellationToken, ValueTask<IAsyncDisposable?>> operation,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            LastWindowToken = WindowToken;
            if (!invokeOperation)
            {
                return new(result, null);
            }

            var projected = await operation(WindowToken, cancellationToken);
            return new(result, surface ?? projected);
        }
    }

    private sealed class FakeTarget(
        IAsyncDisposable? lifetime,
        Action? afterOpen = null) : IGenesisMainWindowSurfaceTarget
    {
        public int OpenCount { get; private set; }

        public long LastWindowToken { get; private set; }

        public GenesisMainViewModel? LastViewModel { get; private set; }

        public ValueTask<IAsyncDisposable?> OpenAsync(
            long windowToken,
            GenesisMainViewModel viewModel,
            CancellationToken cancellationToken)
        {
            OpenCount++;
            LastWindowToken = windowToken;
            LastViewModel = viewModel;
            afterOpen?.Invoke();
            return ValueTask.FromResult(lifetime);
        }
    }

    private sealed class FakeLifetime : IAsyncDisposable
    {
        public bool IsDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
