using System.Net.Sockets;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisNetworkReachabilitySessionTests
{
    [Fact]
    public async Task SynchronousCheckerCompletesWithoutLockReentry()
    {
        var request = CreateRequest();
        var checker = new DelegateReachabilityChecker(
            (current, _) => ValueTask.FromResult(
                GenesisNetworkReachabilityResult.Open(
                    current,
                    [7144],
                    TimeSpan.FromMilliseconds(1),
                    DateTimeOffset.UnixEpoch)));
        await using var session = new GenesisNetworkReachabilitySession(
            checker,
            request);

        var result = await session.CheckAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(GenesisNetworkReachabilityOutcome.Open, result.Outcome);
        Assert.Equal(GenesisNetworkReachabilityState.Open, session.State);
        Assert.Same(result, session.LastResult);
        Assert.Equal(1, checker.CallCount);
    }

    [Fact]
    public async Task ConcurrentChecksShareOneProviderOperation()
    {
        var request = CreateRequest();
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var checker = new DelegateReachabilityChecker(
            async (current, cancellationToken) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return GenesisNetworkReachabilityResult.Closed(
                    current,
                    TimeSpan.FromMilliseconds(5),
                    DateTimeOffset.UnixEpoch);
            });
        await using var session = new GenesisNetworkReachabilitySession(
            checker,
            request);

        var first = session.CheckAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = session.CheckAsync();

        Assert.Same(first, second);
        Assert.Equal(GenesisNetworkReachabilityState.Checking, session.State);
        Assert.Equal(1, checker.CallCount);

        release.TrySetResult();
        var result = await first.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(GenesisNetworkReachabilityOutcome.Closed, result.Outcome);
        Assert.Equal(GenesisNetworkReachabilityState.Closed, session.State);
        Assert.Same(result, session.LastResult);
    }

    [Fact]
    public async Task CheckerFaultBecomesBoundedFailedResult()
    {
        var request = CreateRequest();
        var checker = new DelegateReachabilityChecker(
            (_, _) => throw new InvalidOperationException("provider detail"));
        await using var session = new GenesisNetworkReachabilitySession(
            checker,
            request);

        var result = await session.CheckAsync();

        Assert.Equal(GenesisNetworkReachabilityOutcome.Failed, result.Outcome);
        Assert.Equal("checker-fault", result.DiagnosticCode);
        Assert.Equal(GenesisNetworkReachabilityState.Failed, session.State);
        Assert.Same(result, session.LastResult);
    }

    [Fact]
    public async Task CheckerReturningAnotherRequestFailsClosed()
    {
        var request = CreateRequest();
        var unrelated = new GenesisNetworkReachabilityRequest(
            AddressFamily.InterNetwork,
            [7145]);
        var checker = new DelegateReachabilityChecker(
            (_, _) => ValueTask.FromResult(
                GenesisNetworkReachabilityResult.Open(
                    unrelated,
                    [7145],
                    TimeSpan.Zero,
                    DateTimeOffset.UnixEpoch)));
        await using var session = new GenesisNetworkReachabilitySession(
            checker,
            request);

        var result = await session.CheckAsync();

        Assert.Equal(GenesisNetworkReachabilityOutcome.Failed, result.Outcome);
        Assert.Equal("checker-fault", result.DiagnosticCode);
        Assert.Same(request, result.Request);
        Assert.Equal(GenesisNetworkReachabilityState.Failed, session.State);
    }

    [Fact]
    public async Task CancelledRecheckPreservesPreviousEvidence()
    {
        var request = CreateRequest();
        var secondEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var call = 0;
        var checker = new DelegateReachabilityChecker(
            (current, cancellationToken) => checkerCall(
                current,
                cancellationToken));
        await using var session = new GenesisNetworkReachabilitySession(
            checker,
            request);

        var open = await session.CheckAsync();
        using var cancellation = new CancellationTokenSource();
        var recheck = session.CheckAsync(cancellation.Token);
        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        var cancelled = await recheck.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(GenesisNetworkReachabilityOutcome.Open, open.Outcome);
        Assert.Equal(GenesisNetworkReachabilityOutcome.Cancelled, cancelled.Outcome);
        Assert.Equal(GenesisNetworkReachabilityState.Open, session.State);
        Assert.Same(open, session.LastResult);

        ValueTask<GenesisNetworkReachabilityResult> checkerCall(
            GenesisNetworkReachabilityRequest current,
            CancellationToken cancellationToken)
        {
            var currentCall = Interlocked.Increment(ref call);
            if (currentCall == 1)
            {
                return ValueTask.FromResult(
                    GenesisNetworkReachabilityResult.Open(
                        current,
                        [7144],
                        TimeSpan.Zero,
                        DateTimeOffset.UnixEpoch));
            }

            secondEntered.TrySetResult();
            return WaitForCancellationAsync(current, cancellationToken);
        }
    }

    [Fact]
    public async Task DisposeCancelsAndDrainsActiveCheck()
    {
        var request = CreateRequest();
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var checker = new DelegateReachabilityChecker(
            async (current, cancellationToken) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                    .ConfigureAwait(false);
                return GenesisNetworkReachabilityResult.Closed(
                    current,
                    TimeSpan.Zero,
                    DateTimeOffset.UnixEpoch);
            });
        var session = new GenesisNetworkReachabilitySession(checker, request);
        var check = session.CheckAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await session.DisposeAsync();
        var result = await check.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(GenesisNetworkReachabilityOutcome.Cancelled, result.Outcome);
        Assert.Equal(GenesisNetworkReachabilityState.Unknown, session.State);
        Assert.Null(session.LastResult);
        Assert.Throws<ObjectDisposedException>(
            () =>
            {
                _ = session.CheckAsync();
            });
    }

    private static GenesisNetworkReachabilityRequest CreateRequest()
    {
        return new GenesisNetworkReachabilityRequest(
            AddressFamily.InterNetwork,
            [7144]);
    }

    private static async ValueTask<GenesisNetworkReachabilityResult>
        WaitForCancellationAsync(
        GenesisNetworkReachabilityRequest request,
        CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
            .ConfigureAwait(false);
        return GenesisNetworkReachabilityResult.Closed(
            request,
            TimeSpan.Zero,
            DateTimeOffset.UnixEpoch);
    }

    private sealed class DelegateReachabilityChecker :
        IGenesisNetworkReachabilityChecker
    {
        private readonly Func<
            GenesisNetworkReachabilityRequest,
            CancellationToken,
            ValueTask<GenesisNetworkReachabilityResult>> _check;
        private int _callCount;

        public DelegateReachabilityChecker(
            Func<
                GenesisNetworkReachabilityRequest,
                CancellationToken,
                ValueTask<GenesisNetworkReachabilityResult>> check)
        {
            _check = check;
        }

        public int CallCount => Volatile.Read(ref _callCount);

        public ValueTask<GenesisNetworkReachabilityResult> CheckAsync(
            GenesisNetworkReachabilityRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return _check(request, cancellationToken);
        }
    }
}
