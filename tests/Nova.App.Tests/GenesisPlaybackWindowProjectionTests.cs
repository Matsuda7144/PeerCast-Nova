namespace PeerCast.Nova.App.Tests;

public sealed class GenesisPlaybackWindowProjectionTests
{
    [Fact]
    public async Task OpenPlaybackWindowUsesExistingOwnerWindowDispatcher()
    {
        var dispatcher = new RecordingDispatcher();
        var target = new RecordingTarget();
        var state = GenesisPlaybackWindowState.Open("pcp://example/channel");

        var projection = await GenesisPlaybackWindowProjection.OpenAsync(
            dispatcher,
            target,
            state);

        Assert.Equal(GenesisPlaybackWindowProjectionResult.Projected, projection.Result);
        Assert.NotNull(projection.Lifetime);
        Assert.Equal(7144, target.OwnerWindowToken);
        Assert.Same(state, target.State);
        Assert.Equal(1, dispatcher.InvocationCount);
        await projection.Lifetime.DisposeAsync();
        Assert.True(target.Lifetime.Disposed);
    }

    [Fact]
    public async Task ClosedPlaybackStateRejectsBeforeDispatcherInvocation()
    {
        var dispatcher = new RecordingDispatcher();

        var projection = await GenesisPlaybackWindowProjection.OpenAsync(
            dispatcher,
            new RecordingTarget(),
            GenesisPlaybackWindowState.Closed);

        Assert.Equal(GenesisPlaybackWindowProjectionResult.InvalidState, projection.Result);
        Assert.Null(projection.Lifetime);
        Assert.Equal(0, dispatcher.InvocationCount);
    }

    [Fact]
    public async Task ZeroOwnerWindowRejectsBeforeTargetInvocation()
    {
        var dispatcher = new RecordingDispatcher { WindowToken = 0 };
        var target = new RecordingTarget();

        var projection = await GenesisPlaybackWindowProjection.OpenAsync(
            dispatcher,
            target,
            GenesisPlaybackWindowState.Open("pcp://example/channel"));

        Assert.Equal(GenesisPlaybackWindowProjectionResult.InvalidWindow, projection.Result);
        Assert.Equal(0, target.OpenCount);
    }

    [Fact]
    public async Task DispatcherShutdownFailsClosed()
    {
        var dispatcher = new RecordingDispatcher
        {
            Result = GenesisMainWindowDispatcherResult.Shutdown,
        };

        var projection = await GenesisPlaybackWindowProjection.OpenAsync(
            dispatcher,
            new RecordingTarget(),
            GenesisPlaybackWindowState.Open("pcp://example/channel"));

        Assert.Equal(
            GenesisPlaybackWindowProjectionResult.DispatcherRejected,
            projection.Result);
        Assert.Null(projection.Lifetime);
    }

    [Fact]
    public async Task DispatcherShutdownDisposesUnexpectedSurface()
    {
        var dispatcher = new RejectingAfterOpenDispatcher();
        var target = new RecordingTarget();

        var projection = await GenesisPlaybackWindowProjection.OpenAsync(
            dispatcher,
            target,
            GenesisPlaybackWindowState.Open("pcp://example/channel"));

        Assert.Equal(
            GenesisPlaybackWindowProjectionResult.DispatcherRejected,
            projection.Result);
        Assert.Null(projection.Lifetime);
        Assert.Equal(1, target.OpenCount);
        Assert.True(target.Lifetime.Disposed);
    }

    private sealed class RecordingDispatcher : IGenesisMainWindowDispatcher
    {
        public long WindowToken { get; set; } = 7144;

        public GenesisMainWindowDispatcherResult Result { get; init; } =
            GenesisMainWindowDispatcherResult.Completed;

        public int InvocationCount { get; private set; }

        public async ValueTask<GenesisMainWindowDispatcherInvocation> InvokeGenesisAsync(
            Func<long, CancellationToken, ValueTask<IAsyncDisposable?>> operation,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            if (Result != GenesisMainWindowDispatcherResult.Completed)
            {
                return new(Result, null);
            }

            var lifetime = await operation(WindowToken, cancellationToken)
                .ConfigureAwait(false);
            return new(Result, lifetime);
        }
    }

    private sealed class RejectingAfterOpenDispatcher : IGenesisMainWindowDispatcher
    {
        public long WindowToken => 7144;

        public async ValueTask<GenesisMainWindowDispatcherInvocation> InvokeGenesisAsync(
            Func<long, CancellationToken, ValueTask<IAsyncDisposable?>> operation,
            CancellationToken cancellationToken)
        {
            var lifetime = await operation(WindowToken, cancellationToken)
                .ConfigureAwait(false);
            return new(GenesisMainWindowDispatcherResult.Shutdown, lifetime);
        }
    }

    private sealed class RecordingTarget : IGenesisPlaybackWindowSurfaceTarget
    {
        public RecordingLifetime Lifetime { get; } = new();

        public int OpenCount { get; private set; }

        public long OwnerWindowToken { get; private set; }

        public GenesisPlaybackWindowState? State { get; private set; }

        public ValueTask<IAsyncDisposable?> OpenAsync(
            long ownerWindowToken,
            GenesisPlaybackWindowState state,
            CancellationToken cancellationToken)
        {
            OpenCount++;
            OwnerWindowToken = ownerWindowToken;
            State = state;
            return ValueTask.FromResult<IAsyncDisposable?>(Lifetime);
        }
    }

    private sealed class RecordingLifetime : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
