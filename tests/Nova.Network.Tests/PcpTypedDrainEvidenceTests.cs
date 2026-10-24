using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpTypedDrainEvidenceTests
{
    private static readonly ChannelId Channel = new(Guid.NewGuid());
    private static readonly ChannelSessionId Session = new(Guid.NewGuid());
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(3);
    private static readonly byte[] Header = [0x46, 0x4c, 0x56, 1, 5, 0, 0, 0, 9, 0, 0, 0, 0];

    [Fact]
    public async Task Fanout_EmptyQueueIsNotInFlightSendCompletion()
    {
        await using var fanout = new PcpRelayFanout();
        var sink = new BlockedSink();
        var child = fanout.AddChild(sink);
        try
        {
            await fanout.ConsumeAsync(Packet([1, 2, 3]), default);
            await sink.Entered.Task.WaitAsync(Limit);
            Assert.True(fanout.Evidence.QueuesEmpty);
            Assert.False(fanout.Evidence.SendsSettled);
            Assert.Equal(1, child.Snapshot.InFlightPacketCount);
            var completion = fanout.CompleteAfterUpstreamEndAsync(Limit);
            Assert.Same(completion, fanout.CompleteAfterUpstreamEndAsync(Limit));
            Assert.Throws<ObjectDisposedException>(() => fanout.AddChild(new ImmediateSink()));
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await fanout.ConsumeAsync(Packet([4]), default));
            Assert.False(completion.IsCompleted);
            sink.Release.TrySetResult();
            var result = await completion.WaitAsync(Limit);
            Assert.Equal(PcpRelayCompletionOutcome.Completed, result.Outcome);
            Assert.True(result.SendsCompleted);
            Assert.True(result.ChildrenStopped);
            Assert.True(result.CleanupCompleted);
            Assert.Equal(1, result.Evidence.EndedChildCount);
            Assert.Equal(0, result.Evidence.RemainingChildCount);
            Assert.Equal(3, result.Evidence.DeliveredByteCount);
        }
        finally { sink.Release.TrySetResult(); await child.Completion.WaitAsync(Limit); }
    }

    [Fact]
    public async Task Fanout_RetiredFailuresSurviveSlotReuseAndRepeatedSnapshots()
    {
        await using var fanout = new PcpRelayFanout(new() { MaxChildren = 1 });
        for (var i = 0; i < 20; i++)
        {
            var failed = fanout.AddChild(new FailingSink());
            await fanout.ConsumeAsync(Packet([1, 2]), default);
            await failed.Completion.WaitAsync(Limit);
        }
        var healthy = fanout.AddChild(new ImmediateSink());
        await fanout.ConsumeAsync(Packet([3]), default);
        var result = await fanout.CompleteAfterUpstreamEndAsync(Limit);
        Assert.Equal(PcpRelayCompletionOutcome.DeliveryFailed, result.Outcome);
        Assert.False(result.SendsCompleted);
        Assert.True(result.CleanupCompleted);
        Assert.Equal(21, result.Evidence.RegisteredChildCount);
        Assert.Equal(21, result.Evidence.EndedChildCount);
        Assert.Equal(20, result.Evidence.SinkFailedChildCount);
        Assert.Equal(20, result.Evidence.DiscardedPacketCount);
        Assert.Equal(40, result.Evidence.DiscardedByteCount);
        Assert.Equal(1, result.Evidence.DeliveredPacketCount);
        Assert.Equal(result.Evidence, fanout.Evidence);
        Assert.Equal(result.Evidence, fanout.Evidence);
        await healthy.Completion.WaitAsync(Limit);
    }

    [Fact]
    public async Task Fanout_RetiredSlowConsumerRetainsDiscardedPackets()
    {
        await using var fanout = new PcpRelayFanout(new() { MaxPendingPacketsPerChild = 1 });
        var sink = new BlockedSink(cooperative: true);
        var child = fanout.AddChild(sink);
        await fanout.ConsumeAsync(Packet([1]), default);
        await sink.Entered.Task.WaitAsync(Limit);
        await fanout.ConsumeAsync(Packet([2, 3]), default);
        await child.Completion.WaitAsync(Limit);
        var result = await fanout.CompleteAfterUpstreamEndAsync(Limit);
        Assert.Equal(PcpRelayCompletionOutcome.DeliveryFailed, result.Outcome);
        Assert.Equal(1, result.Evidence.SlowConsumerChildCount);
        Assert.Equal(2, result.Evidence.DiscardedPacketCount);
        Assert.Equal(3, result.Evidence.DiscardedByteCount);
        Assert.True(result.ChildrenStopped);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fanout_TimeoutOrCancelDoesNotPretendBlockedSendWasCleaned(bool cancel)
    {
        await using var fanout = new PcpRelayFanout();
        var sink = new BlockedSink();
        var child = fanout.AddChild(sink);
        using var cancellation = new CancellationTokenSource();
        try
        {
            await fanout.ConsumeAsync(Packet([1]), default);
            await sink.Entered.Task.WaitAsync(Limit);
            var completion = fanout.CompleteAfterUpstreamEndAsync(cancel ? Limit : TimeSpan.FromMilliseconds(50), cancellation.Token);
            if (cancel) cancellation.Cancel();
            var result = await completion.WaitAsync(Limit);
            Assert.Equal(cancel ? PcpRelayCompletionOutcome.Cancelled : PcpRelayCompletionOutcome.TimedOut, result.Outcome);
            Assert.False(result.SendsCompleted);
            Assert.False(result.ChildrenStopped);
            Assert.False(result.CleanupCompleted);
            Assert.False(sink.Token.IsCancellationRequested);
            Assert.Same(completion, fanout.CompleteAfterUpstreamEndAsync(Limit));
        }
        finally { sink.Release.TrySetResult(); await fanout.DisposeAsync(); await child.Completion.WaitAsync(Limit); }
    }

    [Fact]
    public async Task Fanout_ConcurrentForcedDisposeIsNotSuccessfulDrain()
    {
        await using var fanout = new PcpRelayFanout();
        var sink = new BlockedSink(cooperative: true);
        var child = fanout.AddChild(sink);
        await fanout.ConsumeAsync(Packet([1]), default);
        await sink.Entered.Task.WaitAsync(Limit);
        var completion = fanout.CompleteAfterUpstreamEndAsync(Limit);
        await fanout.DisposeAsync();
        var result = await completion.WaitAsync(Limit);
        Assert.Equal(PcpRelayCompletionOutcome.Cancelled, result.Outcome);
        Assert.False(result.SendsCompleted);
        Assert.Equal(1, result.Evidence.DiscardedPacketCount);
        Assert.True((await fanout.CleanupCompletion!).CleanupCompleted);
        await child.Completion.WaitAsync(Limit);
    }

    [Fact]
    public async Task Fanout_QueuesAndSendsSettledButCleanupCallbackBlockedIsIncomplete()
    {
        var fanout = new PcpRelayFanout(new() { ChildStopTimeout = TimeSpan.FromMilliseconds(50) });
        using var sink = new CleanupSink();
        var child = fanout.AddChild(sink);
        try
        {
            await fanout.ConsumeAsync(Packet([1]), default);
            Assert.True(await fanout.WaitForIdleAsync(Limit));
            var result = await fanout.CompleteAfterUpstreamEndAsync(Limit).WaitAsync(Limit);
            Assert.Equal(PcpRelayCompletionOutcome.CleanupIncomplete, result.Outcome);
            Assert.True(result.Evidence.SendsSettled);
            Assert.True(result.SendsCompleted);
            Assert.False(result.ChildrenStopped);
            Assert.False(result.CleanupCompleted);
            Assert.False(child.Completion.IsCompleted);
            await fanout.DisposeAsync();
            Assert.Equal(PcpRelayCleanupOutcome.Incomplete, (await fanout.CleanupCompletion!).Outcome);
        }
        finally { sink.Release.Set(); await child.Completion.WaitAsync(Limit); await fanout.DisposeAsync(); }
    }

    [Fact]
    public async Task Fanout_RetiredCancellationCallbackFailureIsNotLostOrThrownUpstream()
    {
        var fanout = new PcpRelayFanout();
        using var sink = new CleanupSink(fail: true);
        var child = fanout.AddChild(sink);
        await fanout.ConsumeAsync(Packet([1]), default);
        Assert.True(await fanout.WaitForIdleAsync(Limit));
        await child.DisposeAsync();
        Assert.Equal(0, fanout.ActiveChildCount);
        var result = await fanout.CompleteAfterUpstreamEndAsync(Limit);
        Assert.Equal(PcpRelayCompletionOutcome.Failed, result.Outcome);
        Assert.True(result.ChildrenStopped);
        Assert.False(result.CleanupCompleted);
        Assert.Equal(1, result.Evidence.CleanupFailedChildCount);
        Assert.Equal(1, result.Evidence.EndedChildCount);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fanout.DisposeAsync());
    }

    [Fact]
    public async Task Fanout_PreCancelledOrInvalidRequestDoesNotCloseAdmission()
    {
        await using var fanout = new PcpRelayFanout();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = fanout.CompleteAfterUpstreamEndAsync(TimeSpan.Zero); });
        Assert.Equal(PcpRelayCompletionOutcome.Cancelled, (await fanout.CompleteAfterUpstreamEndAsync(Limit, cancellation.Token)).Outcome);
        Assert.False(fanout.Evidence.AdmissionClosed);
        fanout.AddChild(new ImmediateSink());
        Assert.Equal(PcpRelayCompletionOutcome.Completed, (await fanout.CompleteAfterUpstreamEndAsync(Limit)).Outcome);
    }

    [Fact]
    public async Task Http_NormalCompletionAndLegacyCallShareResultAndRemoveSourceExactlyOnce()
    {
        var removed = 0;
        await using var http = await CreateHttpAsync(_ => Interlocked.Increment(ref removed));
        Assert.True(http.TryAddClient(new ImmediateSink(), out var child, out _));
        await http.ConsumeAsync(Packet(Header, head: true), default);
        var completion = http.CompleteWithEvidenceAsync(Limit);
        Assert.Same(completion, http.CompleteWithEvidenceAsync(Limit));
        var legacy = http.CompleteAsync(Limit, default);
        Assert.Same(legacy, http.CompleteAsync(Limit, default));
        var result = await completion.WaitAsync(Limit);
        Assert.Equal(PcpRelayCompletionOutcome.Completed, result.Outcome);
        Assert.True(result.SourceRemoved);
        Assert.True(result.CleanupCompleted);
        Assert.True(result.Fanout.SendsCompleted);
        Assert.Equal(Header.Length, result.Fanout.Evidence.DeliveredByteCount);
        Assert.True(await legacy);
        await http.DisposeAsync();
        Assert.Equal(1, removed);
        await child!.Completion.WaitAsync(Limit);
    }

    [Fact]
    public async Task Http_RetiredDeliveryFailureMakesLegacyCompleteFalse()
    {
        await using var http = await CreateHttpAsync();
        Assert.True(http.TryAddClient(new FailingSink(), out var child, out _));
        await http.ConsumeAsync(Packet(Header, head: true), default);
        await child!.Completion.WaitAsync(Limit);
        var result = await http.CompleteWithEvidenceAsync(Limit);
        Assert.Equal(PcpRelayCompletionOutcome.DeliveryFailed, result.Outcome);
        Assert.True(result.CleanupCompleted);
        Assert.True(result.SourceRemoved);
        Assert.Equal(1, result.Fanout.Evidence.SinkFailedChildCount);
        Assert.False(await http.CompleteAsync(Limit, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Http_TimeoutOrCancelBoundsWaitAndDoesNotProvePendingCleanup(bool cancel)
    {
        var options = new PcpHttpFlvPlaybackOptions { Fanout = new() { ChildStopTimeout = TimeSpan.FromMilliseconds(50) } };
        var http = await CreateHttpAsync(options: options);
        var sink = new BlockedSink();
        Assert.True(http.TryAddClient(sink, out var child, out _));
        using var cancellation = new CancellationTokenSource();
        try
        {
            await http.ConsumeAsync(Packet(Header, head: true), default);
            await sink.Entered.Task.WaitAsync(Limit);
            var completion = http.CompleteWithEvidenceAsync(cancel ? Limit : TimeSpan.FromMilliseconds(50), cancellation.Token);
            if (cancel) cancellation.Cancel();
            var result = await completion.WaitAsync(Limit);
            Assert.Equal(cancel ? PcpRelayCompletionOutcome.Cancelled : PcpRelayCompletionOutcome.TimedOut, result.Outcome);
            Assert.False(result.Fanout.SendsCompleted);
            Assert.False(result.CleanupCompleted);
            Assert.False(result.Fanout.ChildrenStopped);
            Assert.False(child!.Completion.IsCompleted);
            await http.DisposeAsync();
            Assert.False(child.Completion.IsCompleted);
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => http.CompleteAsync(Limit, default));
            else Assert.False(await http.CompleteAsync(Limit, default));
        }
        finally { sink.Release.TrySetResult(); await child!.Completion.WaitAsync(Limit); await http.DisposeAsync(); }
    }

    [Fact]
    public async Task Http_SourceRemovalIsNotChildCleanupProof()
    {
        var options = new PcpHttpFlvPlaybackOptions { Fanout = new() { ChildStopTimeout = TimeSpan.FromMilliseconds(50) } };
        var http = await CreateHttpAsync(options: options);
        using var sink = new CleanupSink();
        Assert.True(http.TryAddClient(sink, out var child, out _));
        try
        {
            await http.ConsumeAsync(Packet(Header, head: true), default);
            var result = await http.CompleteWithEvidenceAsync(Limit).WaitAsync(Limit);
            Assert.Equal(PcpRelayCompletionOutcome.CleanupIncomplete, result.Outcome);
            Assert.True(result.SourceRemoved);
            Assert.True(result.Fanout.SendsCompleted);
            Assert.False(result.CleanupCompleted);
            Assert.False(result.Fanout.ChildrenStopped);
        }
        finally { sink.Release.Set(); await child!.Completion.WaitAsync(Limit); await http.DisposeAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Http_SourceRemovalFailureOrBlockedCallbackNeverProvesCleanup(bool blocked)
    {
        using var release = new ManualResetEventSlim();
        var http = await CreateHttpAsync(_ =>
        {
            if (blocked) release.Wait();
            else throw new IOException("private path must not escape in typed evidence");
        });
        try
        {
            var result = await http.CompleteWithEvidenceAsync(TimeSpan.FromMilliseconds(100)).WaitAsync(Limit);
            Assert.Equal(blocked ? PcpRelayCompletionOutcome.TimedOut : PcpRelayCompletionOutcome.CleanupIncomplete, result.Outcome);
            Assert.False(result.SourceRemoved);
            Assert.False(result.CleanupCompleted);
            Assert.True(result.Fanout.ChildrenStopped);
        }
        finally
        {
            release.Set();
            if (blocked) await http.DisposeAsync();
            else await Assert.ThrowsAsync<IOException>(async () => await http.DisposeAsync());
        }
    }

    [Fact]
    public async Task Http_PreCancelledLegacyRequestDoesNotPoisonLaterCompletion()
    {
        await using var http = await CreateHttpAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = http.CompleteWithEvidenceAsync(TimeSpan.Zero); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => http.CompleteAsync(Limit, cancellation.Token));
        Assert.True(http.TryAddClient(new ImmediateSink(), out _, out _));
        Assert.True(await http.CompleteAsync(Limit, default));
    }

    [Fact]
    public async Task Http_LegacyCompletionAfterForcedDisposalRemainsFalse()
    {
        var http = await CreateHttpAsync();
        await http.DisposeAsync();
        Assert.False(await http.CompleteAsync(Limit, default));
        Assert.Equal(PcpRelayCompletionOutcome.Cancelled, (await http.CompleteWithEvidenceAsync(Limit)).Outcome);
    }

    [Fact]
    public async Task Http_ExternalDisposeDuringDrainIsNotCompletedDelivery()
    {
        var http = await CreateHttpAsync();
        var sink = new BlockedSink(cooperative: true);
        Assert.True(http.TryAddClient(sink, out var child, out _));
        await http.ConsumeAsync(Packet(Header, head: true), default);
        await sink.Entered.Task.WaitAsync(Limit);
        var completion = http.CompleteWithEvidenceAsync(Limit);
        await http.DisposeAsync();
        var result = await completion.WaitAsync(Limit);
        Assert.Equal(PcpRelayCompletionOutcome.Cancelled, result.Outcome);
        Assert.False(result.Fanout.SendsCompleted);
        await child!.Completion.WaitAsync(Limit);
    }

    [Fact]
    public void Evidence_ContainsOnlyFixedCountsAndFlags()
    {
        Assert.All(typeof(PcpRelayFanoutEvidence).GetProperties(), property =>
            Assert.Contains(property.PropertyType, new[] { typeof(bool), typeof(int), typeof(long) }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Http_InitialSendMustFinishEvenWithoutLivePackets(bool fail)
    {
        await using var http = await CreateHttpAsync();
        var sink = new StartupSink();
        Assert.True(http.TryAddClient(sink, out var child, out _));
        try
        {
            Assert.True(http.Evidence.QueuesEmpty);
            Assert.False(http.Evidence.SendsSettled);
            Assert.Equal(1, http.Evidence.StartupPendingChildCount);
            var completion = http.CompleteWithEvidenceAsync(Limit);
            Assert.False(completion.IsCompleted);
            if (fail) sink.Ready.TrySetException(new IOException("private bootstrap failure"));
            else sink.Ready.TrySetResult();
            var result = await completion.WaitAsync(Limit);
            Assert.Equal(fail ? PcpRelayCompletionOutcome.DeliveryFailed : PcpRelayCompletionOutcome.Completed, result.Outcome);
            Assert.Equal(fail ? 1 : 0, result.Fanout.Evidence.StartupFailedChildCount);
            Assert.True(result.Fanout.ChildrenStopped);
            Assert.True(result.CleanupCompleted);
        }
        finally { sink.Ready.TrySetResult(); await http.DisposeAsync(); await child!.Completion.WaitAsync(Limit); }
    }

    [Fact]
    public async Task Http_BlockedInitialSendCannotBeMadeCompleteByCancellationRequestAlone()
    {
        var http = await CreateHttpAsync(options: new() { Fanout = new() { ChildStopTimeout = TimeSpan.FromMilliseconds(50) } });
        var sink = new StartupSink();
        Assert.True(http.TryAddClient(sink, out var child, out _));
        try
        {
            var result = await http.CompleteWithEvidenceAsync(TimeSpan.FromMilliseconds(50)).WaitAsync(Limit);
            Assert.Equal(PcpRelayCompletionOutcome.TimedOut, result.Outcome);
            Assert.False(result.CleanupCompleted);
            Assert.False(result.Fanout.SendsCompleted);
            await sink.CancelRequested.Task.WaitAsync(Limit);
            await http.DisposeAsync();
            Assert.False(child!.Completion.IsCompleted);
        }
        finally { sink.Ready.TrySetCanceled(); await child!.Completion.WaitAsync(Limit); await http.DisposeAsync(); }
    }

    [Fact]
    public async Task Fanout_AlreadyFailedStartupIsRetiredOnceAndReleasesItsSlot()
    {
        await using var fanout = new PcpRelayFanout(new() { MaxChildren = 1 });
        var sink = new StartupSink();
        sink.Ready.SetException(new IOException("synthetic initial failure"));
        var child = fanout.AddChild(sink);
        await child.Completion.WaitAsync(Limit);
        fanout.AddChild(new ImmediateSink());
        var result = await fanout.CompleteAfterUpstreamEndAsync(Limit);
        Assert.Equal(PcpRelayCompletionOutcome.DeliveryFailed, result.Outcome);
        Assert.Equal(2, result.Evidence.RegisteredChildCount);
        Assert.Equal(2, result.Evidence.EndedChildCount);
        Assert.Equal(1, result.Evidence.StartupFailedChildCount);
        Assert.Equal(1, result.Evidence.SinkFailedChildCount);
        Assert.Equal(0, result.Evidence.RemainingChildCount);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Startup_CancelAndEndKeepBarrierPendingUntilActualWriteEnds(bool cancelFirst)
    {
        var owner = new PcpHttpFlvStartupLifetime();
        var entered = Gate();
        var release = Gate();
        var write = owner.WriteAsync(async _ =>
        {
            entered.TrySetResult();
            await release.Task;
        }, PcpHttpFlvStartupPoint.BeforeHeaderWrite);
        await entered.Task.WaitAsync(Limit);
        if (cancelFirst) { owner.RequestCancellation(); owner.EndStartup(); }
        else { owner.EndStartup(); owner.RequestCancellation(); }
        Assert.False(owner.StartupCompletion.IsCompleted);
        Assert.False(owner.IsDisposed);
        release.TrySetResult();
        await write.WaitAsync(Limit);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.StartupCompletion);
        await owner.DisposeAfterWorkerAsync(Task.CompletedTask).WaitAsync(Limit);
        Assert.True(owner.IsDisposed);
    }

    [Fact]
    public async Task Startup_ActivateCannotCompleteAnActualPendingWrite()
    {
        var owner = new PcpHttpFlvStartupLifetime();
        var release = Gate();
        var write = owner.WriteAsync(_ => release.Task, PcpHttpFlvStartupPoint.BeforeBootstrapWrite);
        owner.Activate();
        Assert.False(owner.StartupCompletion.IsCompleted);
        release.TrySetResult();
        await write.WaitAsync(Limit);
        await owner.StartupCompletion.WaitAsync(Limit);
        await owner.DisposeAfterWorkerAsync(Task.CompletedTask);
        Assert.True(owner.IsDisposed);
    }

    [Fact]
    public async Task Startup_CancelRequestAloneDoesNotClaimWriteTermination()
    {
        var owner = new PcpHttpFlvStartupLifetime();
        owner.RequestCancellation();
        await owner.CancellationCompletion.WaitAsync(Limit);
        Assert.False(owner.StartupCompletion.IsCompleted);
        owner.EndStartup();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.StartupCompletion);
        await owner.DisposeAfterWorkerAsync(Task.CompletedTask);
        owner.RequestCancellation(); // Late request cannot touch disposed CTS.
        Assert.True(owner.IsDisposed);
    }

    [Fact]
    public async Task Startup_CompletedEndIsNotRewrittenByLaterCancellation()
    {
        var owner = new PcpHttpFlvStartupLifetime();
        owner.EndStartup();
        await Assert.ThrowsAsync<IOException>(() => owner.StartupCompletion);
        owner.RequestCancellation();
        await owner.CancellationCompletion;
        await Assert.ThrowsAsync<IOException>(() => owner.StartupCompletion);
        await owner.DisposeAfterWorkerAsync(Task.CompletedTask);
    }

    [Fact]
    public async Task Startup_BoundedWorkerStopCannotDisposeCtsDuringRealCancellationCallback()
    {
        await using var fanout = new PcpRelayFanout(new() { MaxChildren = 1, ChildStopTimeout = TimeSpan.FromMilliseconds(40) });
        var owner = new PcpHttpFlvStartupLifetime();
        var callbackEntered = Gate();
        using var releaseCallback = new ManualResetEventSlim();
        using var registration = owner.Token.Register(() =>
        {
            callbackEntered.TrySetResult();
            releaseCallback.Wait();
        });
        var child = fanout.AddChild(new OwnedStartupSink(owner));
        try
        {
            var stopping = child.StopAsync().AsTask();
            await callbackEntered.Task.WaitAsync(Limit);
            owner.EndStartup();
            Assert.False(await stopping.WaitAsync(Limit));
            Assert.False(child.Completion.IsCompleted);
            var cleanup = owner.DisposeAfterWorkerAsync(child.Completion);
            Assert.False(cleanup.IsCompleted);
            Assert.False(owner.IsDisposed);
            Assert.Equal(1, fanout.Evidence.RemainingChildCount);
            Assert.False(fanout.TryAddChild(new ImmediateSink(), out var other));
            Assert.Null(other);
            releaseCallback.Set();
            await child.Completion.WaitAsync(Limit);
            await cleanup.WaitAsync(Limit);
            Assert.True(owner.IsDisposed);
            Assert.Equal(0, fanout.Evidence.CleanupFailedChildCount);
        }
        finally
        {
            releaseCallback.Set();
            owner.EndStartup();
            await child.Completion.WaitAsync(Limit);
            await owner.DisposeAfterWorkerAsync(child.Completion);
        }
    }

    [Fact]
    public async Task Startup_ActualCancellationFailureIsObservedAndClassifiedAsCleanupFailure()
    {
        var fanout = new PcpRelayFanout();
        var cancelEntered = Gate();
        var owner = new PcpHttpFlvStartupLifetime(new PcpHttpFlvStartupObserver(point =>
        {
            if (point == PcpHttpFlvStartupPoint.BeforeCancellation) cancelEntered.TrySetResult();
            return ValueTask.CompletedTask;
        }));
        using var registration = owner.Token.Register(() => throw new IOException("private callback detail"));
        var child = fanout.AddChild(new OwnedStartupSink(owner));
        try
        {
            var stop = child.StopAsync().AsTask();
            await cancelEntered.Task.WaitAsync(Limit);
            await Assert.ThrowsAsync<AggregateException>(() => owner.CancellationCompletion.WaitAsync(Limit));
            owner.EndStartup();
            Assert.True(await stop.WaitAsync(Limit));
            await child.Completion.WaitAsync(Limit);
            Assert.True(child.Snapshot.CleanupFailed);
            Assert.Equal(1, fanout.Evidence.CleanupFailedChildCount);
            await Assert.ThrowsAsync<AggregateException>(() => owner.DisposeAfterWorkerAsync(child.Completion));
            Assert.True(owner.IsDisposed);
            var result = await fanout.CompleteAfterUpstreamEndAsync(Limit);
            Assert.Equal(PcpRelayCompletionOutcome.Failed, result.Outcome);
            Assert.False(result.CleanupCompleted);
        }
        finally
        {
            owner.EndStartup();
            await child.Completion.WaitAsync(Limit);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await fanout.DisposeAsync());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpHost_ActualStartupBarrierIncludesHeaderAndBootstrap(bool bootstrap)
    {
        var entered = Gate();
        var release = Gate();
        var target = bootstrap ? PcpHttpFlvStartupPoint.BeforeBootstrapWrite : PcpHttpFlvStartupPoint.BeforeHeaderWrite;
        var observer = new PcpHttpFlvStartupObserver(async point =>
        {
            if (point == target) { entered.TrySetResult(); await release.Task; }
        });
        await using var host = new PcpHttpFlvPlaybackHost() { StartupObserver = observer };
        host.Start();
        var source = Assert.IsType<PcpHttpFlvPlaybackSession>(host.Create(Channel, Session));
        await source.ConsumeAsync(Packet(Header, head: true), default);
        using var client = new HttpClient();
        using var deadline = new CancellationTokenSource(Limit);
        var get = client.GetAsync(host.StreamUrlFor(Channel), HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        try
        {
            await entered.Task.WaitAsync(deadline.Token);
            Assert.True(source.Evidence.QueuesEmpty);
            Assert.False(source.Evidence.SendsSettled);
            Assert.Equal(1, source.Evidence.StartupPendingChildCount);
            var completion = source.CompleteWithEvidenceAsync(Limit);
            Assert.False(completion.IsCompleted);
            release.TrySetResult();
            using var response = await get;
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            var bytes = await response.Content.ReadAsByteArrayAsync(deadline.Token);
            Assert.Equal(Header, bytes);
            var result = await completion.WaitAsync(deadline.Token);
            Assert.Equal(PcpRelayCompletionOutcome.Completed, result.Outcome);
            Assert.True(result.CleanupCompleted);
            Assert.Equal(0, result.Fanout.Evidence.DeliveredPacketCount);
            Assert.Equal(0, result.Fanout.Evidence.DeliveredByteCount);
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task HttpHost_ActualInitialWriteFailureSurvivesRetirement(bool bootstrap, bool timeout)
    {
        var entered = Gate();
        var target = bootstrap ? PcpHttpFlvStartupPoint.BeforeBootstrapWrite : PcpHttpFlvStartupPoint.BeforeHeaderWrite;
        await using var host = new PcpHttpFlvPlaybackHost()
        {
            StartupObserver = new PcpHttpFlvStartupObserver(point =>
            {
                if (point != target) return ValueTask.CompletedTask;
                entered.TrySetResult();
                return ValueTask.FromException(timeout ? new OperationCanceledException("initial write timeout signal") : new IOException("private initial write detail"));
            })
        };
        host.Start();
        var source = Assert.IsType<PcpHttpFlvPlaybackSession>(host.Create(Channel, Session));
        await source.ConsumeAsync(Packet(Header, head: true), default);
        using var deadline = new CancellationTokenSource(Limit);
        // A failing pre-header response must be a single request. HttpClient may
        // reconnect after an early EOF, which creates additional real children.
        await using var connection = await new TcpTransportConnector()
            .ConnectAsync(host.LocalEndPoint.Address.ToString(), host.LocalEndPoint.Port, deadline.Token);
        var path = new Uri(host.StreamUrlFor(Channel)).PathAndQuery;
        await connection.WriteAsync(System.Text.Encoding.ASCII.GetBytes(
            $"GET {path} HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n"), deadline.Token);
        await entered.Task.WaitAsync(deadline.Token);
        var result = await source.CompleteWithEvidenceAsync(Limit).WaitAsync(deadline.Token);
        Assert.Equal(PcpRelayCompletionOutcome.DeliveryFailed, result.Outcome);
        Assert.Equal(1, result.Fanout.Evidence.RegisteredChildCount);
        Assert.Equal(1, result.Fanout.Evidence.EndedChildCount);
        Assert.Equal(1, result.Fanout.Evidence.StartupFailedChildCount);
        Assert.True(result.CleanupCompleted);
        var buffer = new byte[1024];
        while (await connection.ReadAsync(buffer, deadline.Token) != 0) { }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Http_LateRemovalFaultIsObservedWithoutUpgradingTimedOutOrCancelledResult(bool cancel)
    {
        var entered = Gate();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var http = await CreateHttpAsync(_ =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            release.Wait();
            throw new IOException("private late removal detail");
        });
        using var cancellation = new CancellationTokenSource();
        try
        {
            var completion = http.CompleteWithEvidenceAsync(cancel ? Limit : TimeSpan.FromMilliseconds(200), cancellation.Token);
            await entered.Task.WaitAsync(Limit);
            if (cancel) cancellation.Cancel();
            var result = await completion.WaitAsync(Limit);
            Assert.Equal(cancel ? PcpRelayCompletionOutcome.Cancelled : PcpRelayCompletionOutcome.TimedOut, result.Outcome);
            Assert.False(result.SourceRemoved);
            Assert.False(result.SourceCleanupFailed);
            Assert.False(result.CleanupCompleted);
            release.Set();
            var disposal = http.DisposeAsync().AsTask();
            await Assert.ThrowsAsync<IOException>(() => disposal);
            Assert.Same(disposal, http.DisposeAsync().AsTask());
            Assert.True(http.SourceCleanupFailed);
            Assert.False(result.SourceCleanupFailed); // Immutable deadline snapshot.
            Assert.Same(completion, http.CompleteWithEvidenceAsync(Limit));
            Assert.Equal(1, calls);
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => http.CompleteAsync(Limit, default));
            else Assert.False(await http.CompleteAsync(Limit, default));
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task Http_LegacyExceptionUsesFirstAdmittedCancellationToken()
    {
        var http = await CreateHttpAsync();
        var sink = new BlockedSink();
        Assert.True(http.TryAddClient(sink, out var child, out _));
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();
        try
        {
            await http.ConsumeAsync(Packet(Header, head: true), default);
            await sink.Entered.Task.WaitAsync(Limit);
            var typed = http.CompleteWithEvidenceAsync(Limit, first.Token);
            var legacy = http.CompleteAsync(Limit, second.Token);
            first.Cancel();
            Assert.Equal(PcpRelayCompletionOutcome.Cancelled, (await typed.WaitAsync(Limit)).Outcome);
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => legacy);
            Assert.Equal(first.Token, error.CancellationToken);
        }
        finally { sink.Release.TrySetResult(); await child!.Completion.WaitAsync(Limit); await http.DisposeAsync(); }
    }


    [Fact]
    public async Task Fanout_CompletionReleasesRetainedHeadOwnership()
    {
        await using var fanout = new PcpRelayFanout(replayHeadForNewChildren: true);
        await fanout.ConsumeAsync(Packet(Header, head: true), default);
        Assert.True(fanout.HasRetainedHead);
        var child = fanout.AddChild(new ImmediateSink());
        var result = await fanout.CompleteAfterUpstreamEndAsync(Limit);
        Assert.Equal(PcpRelayCompletionOutcome.Completed, result.Outcome);
        Assert.False(fanout.HasRetainedHead);
        Assert.Equal(1, result.Evidence.DeliveredPacketCount);
        Assert.Equal(Header.Length, result.Evidence.DeliveredByteCount);
        await child.Completion.WaitAsync(Limit);
    }

    [Fact]
    public async Task Http_StartupCleanupIsOwnedAcrossConcurrentHostDispose()
    {
        var entered = Gate();
        var release = Gate();
        var host = new PcpHttpFlvPlaybackHost()
        {
            StartupObserver = new PcpHttpFlvStartupObserver(async point =>
            {
                if (point == PcpHttpFlvStartupPoint.BeforeBootstrapWrite)
                {
                    entered.TrySetResult();
                    await release.Task;
                }
            })
        };
        host.Start();
        var source = Assert.IsType<PcpHttpFlvPlaybackSession>(host.Create(Channel, Session));
        await source.ConsumeAsync(Packet(Header, head: true), default);
        using var client = new HttpClient();
        using var deadline = new CancellationTokenSource(Limit);
        var request = client.GetAsync(host.StreamUrlFor(Channel), HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        try
        {
            await entered.Task.WaitAsync(deadline.Token);
            var disposal = host.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            release.TrySetResult();
            await disposal.WaitAsync(deadline.Token);
            Assert.Equal(0, host.ActiveConnectionCount);
            Assert.Equal(0, host.ActiveSourceCount);
            try { using var response = await request; await response.Content.ReadAsByteArrayAsync(deadline.Token); }
            catch (HttpRequestException) { }
        }
        finally { release.TrySetResult(); await host.DisposeAsync(); }
    }


    [Fact]
    public async Task Startup_LateCancellationFaultIsVisibleWhileInitialWriteStillPending()
    {
        var fanout = new PcpRelayFanout(new() { ChildStopTimeout = TimeSpan.FromMilliseconds(40) });
        var cancelEntered = Gate();
        var owner = new PcpHttpFlvStartupLifetime(new PcpHttpFlvStartupObserver(point =>
        {
            if (point == PcpHttpFlvStartupPoint.BeforeCancellation) cancelEntered.TrySetResult();
            return ValueTask.CompletedTask;
        }));
        using var callback = owner.Token.Register(() => throw new IOException("private late callback"));
        var release = Gate();
        var write = owner.WriteAsync(_ => release.Task, PcpHttpFlvStartupPoint.BeforeHeaderWrite);
        var child = fanout.AddChild(new OwnedStartupSink(owner));
        try
        {
            var stop = child.StopAsync().AsTask();
            await cancelEntered.Task.WaitAsync(Limit);
            await Assert.ThrowsAsync<AggregateException>(() => owner.CancellationCompletion);
            Assert.True(owner.CancellationFailed);
            Assert.True(child.Snapshot.StartupPending);
            Assert.True(child.Snapshot.CleanupFailed);
            Assert.False(await stop.WaitAsync(Limit));
            var result = await fanout.CompleteAfterUpstreamEndAsync(TimeSpan.FromMilliseconds(40));
            Assert.Equal(PcpRelayCompletionOutcome.TimedOut, result.Outcome);
            Assert.Equal(1, result.Evidence.CleanupFailedChildCount);
            Assert.False(result.SendsCompleted);
            release.TrySetResult();
            await write;
            owner.EndStartup();
            await child.Completion.WaitAsync(Limit);
            await Assert.ThrowsAsync<AggregateException>(() => owner.DisposeAfterWorkerAsync(child.Completion));
            Assert.True(owner.IsDisposed);
            Assert.Equal(PcpRelayCompletionOutcome.TimedOut, result.Outcome);
        }
        finally
        {
            release.TrySetResult();
            await write;
            owner.EndStartup();
            await child.Completion.WaitAsync(Limit);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await fanout.DisposeAsync());
        }
    }

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class OwnedStartupSink(PcpHttpFlvStartupLifetime owner) : IPcpRelayChildSink, IPcpRelayChildStartupBoundary
    {
        public Task StartupCompletion => owner.StartupCompletion;
        public Task StartupCancellationCompletion => owner.CancellationCompletion;
        public void RequestStartupCancellation() => owner.RequestCancellation();
        public ValueTask SendAsync(PcpRelayPacket packet, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private static async Task<PcpHttpFlvPlaybackSession> CreateHttpAsync(
        Action<PcpHttpFlvPlaybackSession>? removed = null, PcpHttpFlvPlaybackOptions? options = null)
    {
        var http = new PcpHttpFlvPlaybackSession(Channel, Session, options ?? new(), removed ?? (_ => { }), () => { });
        await http.ConsumeAsync(Packet(Header, head: true), default);
        return http;
    }

    private static PcpChannelPacket Packet(byte[] payload, bool head = false) =>
        new(Channel, Session, 0, head ? 0x64616568U : 0x61746164U, 0, 0, payload, null);

    private sealed class ImmediateSink : IPcpRelayChildSink
    {
        public ValueTask SendAsync(PcpRelayPacket packet, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class StartupSink : IPcpRelayChildSink, IPcpRelayChildStartupBoundary
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancelRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task StartupCompletion => Ready.Task;
        public Task StartupCancellationCompletion => Task.CompletedTask;
        public void RequestStartupCancellation() => CancelRequested.TrySetResult();
        public ValueTask SendAsync(PcpRelayPacket packet, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class FailingSink : IPcpRelayChildSink
    {
        public ValueTask SendAsync(PcpRelayPacket packet, CancellationToken cancellationToken) =>
            throw new IOException("synthetic private detail");
    }

    private sealed class BlockedSink(bool cooperative = false) : IPcpRelayChildSink
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public async ValueTask SendAsync(PcpRelayPacket packet, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            Entered.TrySetResult();
            if (cooperative) await Release.Task.WaitAsync(cancellationToken);
            else await Release.Task;
        }
    }

    private sealed class CleanupSink(bool fail = false) : IPcpRelayChildSink, IDisposable
    {
        public ManualResetEventSlim Release { get; } = new();
        private CancellationTokenRegistration _registration;
        public ValueTask SendAsync(PcpRelayPacket packet, CancellationToken cancellationToken)
        {
            _registration = cancellationToken.Register(() =>
            {
                if (fail) throw new IOException("synthetic callback failure");
                Release.Wait();
            });
            return ValueTask.CompletedTask;
        }
        public void Dispose() { Release.Set(); _registration.Dispose(); Release.Dispose(); }
    }
}


public sealed partial class PcpDownstreamRelayServerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypedCleanup_ScopedDrainSeparatesCallbackFailureAndReservationRelease(bool unrelated)
    {
        var sinks = new List<TypedCallbackFailureSink>();
        var writers = new List<TypedCallbackFailureWriter>();
        var fanout = new PcpRelayFanout();
        using var policy = new PeerCast.Nova.Network.Policies.RelayAdmissionPolicy();
        using var owner = policy.OpenChannel(ChannelId, UpstreamSessionId, PeerCast.Nova.Network.Policies.RelayChannelKind.Broadcast);
        var admission = new PolicyAdmission(owner);
        await using var server = new PcpDownstreamRelayServer(
            new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0),
            fanout, ChannelId, LocalSessionId, CreateTransportOptions(), admission: admission)
        {
            PacketWriterFactory = connection =>
            {
                if (unrelated) return null;
                var writer = new TypedCallbackFailureWriter(connection);
                writers.Add(writer);
                return writer;
            }
        };
        using var deadline = new CancellationTokenSource(TestTimeout);
        _ = server.RunAsync(deadline.Token);
        PcpRelayChildRegistration? other = null;
        try
        {
            await using var peer = await ConnectPeerAsync(server.LocalEndPoint, CreateTransportOptions(), deadline.Token);
            if (unrelated)
            {
                var sink = new TypedCallbackFailureSink(new TypedImmediateSink());
                sinks.Add(sink);
                other = fanout.AddChild(sink);
            }
            await fanout.ConsumeAsync(CreatePacket([1]), deadline.Token);
            await peer.ReadChannelPayloadAsync(deadline.Token);
            Assert.True(await fanout.WaitForIdleAsync(TestTimeout, deadline.Token));
            if (other is not null) await other.StopAsync(deadline.Token);
            var result = await server.DrainAfterUpstreamEndAsync(TestTimeout).WaitAsync(deadline.Token);
            Assert.Equal(unrelated ? PcpRelayQuiescenceOutcome.Drained : PcpRelayQuiescenceOutcome.CleanupFailed, result.Outcome);
            Assert.Equal(unrelated, result.CleanupCompleted);
            Assert.Equal(unrelated ? 0 : 1, result.Snapshot.CleanupIncompleteCount);
            Assert.Equal(0, policy.ReservedCount);
            Assert.Equal(1, admission.ReleaseCalls);
            Assert.Equal(0, result.Snapshot.AdmissionCleanupPendingCount);
            Assert.Equal(0, result.Snapshot.AdmissionCleanupFailedCount);
            Assert.Equal(1, fanout.Evidence.CleanupFailedChildCount);
        }
        finally
        {
            foreach (var sink in sinks) sink.Dispose();
            foreach (var writer in writers) writer.Dispose();
            if (other is not null) await other.DisposeAsync();
            // New global typed cleanup correctly preserves this fixture's failure.
            try { await fanout.DisposeAsync(); } catch (InvalidOperationException) { }
        }
    }

    private sealed class TypedCallbackFailureSink(IPcpRelayChildSink inner) : IPcpRelayChildSink, IDisposable
    {
        private CancellationTokenRegistration _registration;
        private bool _registered;
        public ValueTask SendAsync(PcpRelayPacket packet, CancellationToken cancellationToken)
        {
            if (!_registered)
            {
                _registered = true;
                _registration = cancellationToken.Register(() => throw new IOException("private callback detail"));
            }
            return inner.SendAsync(packet, cancellationToken);
        }
        public void Dispose() => _registration.Dispose();
    }

    private sealed class TypedCallbackFailureWriter(TcpTransportConnection connection) :
        IPcpDownstreamPacketWriter, IPcpDownstreamWorkerLifetimeObserver, IDisposable
    {
        private CancellationTokenRegistration _registration;
        private bool _registered;
        public void ObserveWorkerLifetime(CancellationToken cancellationToken)
        {
            if (_registered) return;
            _registered = true;
            _registration = cancellationToken.Register(() => throw new IOException("private worker callback"));
        }
        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
            => connection.WriteAsync(buffer, cancellationToken);
        public void Dispose() => _registration.Dispose();
    }

    private sealed class TypedImmediateSink : IPcpRelayChildSink
    {
        public ValueTask SendAsync(PcpRelayPacket packet, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
