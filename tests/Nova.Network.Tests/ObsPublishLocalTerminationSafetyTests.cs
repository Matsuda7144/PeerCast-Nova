using System.Collections.Concurrent;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.Network.Tests;

public sealed class ObsPublishLocalTerminationSafetyTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GracefulCleanupCanEscalateAfterItStarted(bool cancel)
    {
        var entered = Signal(); var release = Signal(); var forced = Signal(); var clock = new SilentClock();
        var counts = new ConcurrentDictionary<ObsPublishTerminationPoint, int>();
        var host = new ObsPublishChannelHost(new() { Ingest = new() { Port = 0 } })
        {
            TerminationTimeProvider = clock,
            TerminationObserver = async point =>
            {
                counts.AddOrUpdate(point, 1, (_, count) => count + 1);
                if (point == ObsPublishTerminationPoint.IngressCleanup) { entered.TrySetResult(); await release.Task; }
                if (point == ObsPublishTerminationPoint.Cancellation) forced.TrySetResult();
            },
        };
        host.Start();
        using var cancellation = new CancellationTokenSource();
        var finish = host.RequestLocalTerminationAsync(Limit, cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(Limit);
            if (cancel)
            {
                cancellation.Cancel();
                await forced.Task.WaitAsync(Limit);
                Assert.False(finish.IsCompleted);
                release.TrySetResult();
            }
            else clock.AdvanceAndFire(Limit + Limit);
            var result = await finish.WaitAsync(Limit);
            await forced.Task.WaitAsync(Limit);
            Assert.Equal(cancel ? ObsPublishLocalTerminationOutcome.Cancelled : ObsPublishLocalTerminationOutcome.TimedOut, result.Outcome);
            Assert.Equal(cancel, result.CleanupCompleted);
            Assert.Equal(cancel, (await host.Completion).ShutdownCompleted);
            release.TrySetResult();
            await host.DisposeAsync().AsTask().WaitAsync(Limit);
            Assert.Equal(1, counts[ObsPublishTerminationPoint.Cancellation]);
            Assert.Equal(1, counts[ObsPublishTerminationPoint.IngressCleanup]);
            Assert.Equal(1, counts[ObsPublishTerminationPoint.HttpListenerCleanup]);
            Assert.Same(result, await host.RequestLocalTerminationAsync(Limit));
            Assert.Equal(cancel, result.CleanupCompleted);
        }
        finally { release.TrySetResult(); try { await host.DisposeAsync(); } catch (InvalidOperationException) { } }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlockedActualOwnerCancelCannotPreventIndependentReleases(bool failLate)
    {
        using var cancelEntered = new ManualResetEventSlim();
        using var cancelRelease = new ManualResetEventSlim();
        var listenerReleased = Signal(); var sendEntered = Signal(); var actualCancelEntered = Signal();
        var host = new ObsPublishChannelHost(new() { Ingest = new() { Port = 0 } })
        {
            TerminationObserver = async point =>
            {
                if (point == ObsPublishTerminationPoint.BeforeResourceRelease)
                    await Task.Run(() => { if (!cancelEntered.Wait(Limit)) throw new TimeoutException(); });
                if (point == ObsPublishTerminationPoint.HttpListenerCleanup) listenerReleased.TrySetResult();
            },
        };
        host.Start();
        using var callback = host.OwnerStopToken.Register(() =>
        {
            cancelEntered.Set(); actualCancelEntered.TrySetResult();
            if (!cancelRelease.Wait(Limit)) throw new TimeoutException();
            if (failLate) throw new IOException("late owner cancellation failure");
        });
        var sink = new PausedSink(sendEntered);
        var registration = host.Fanout.AddChild(sink);
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        await sendEntered.Task.WaitAsync(Limit);
        try
        {
            var result = await host.RequestLocalTerminationAsync(TimeSpan.FromMilliseconds(200)).WaitAsync(Limit);
            await actualCancelEntered.Task.WaitAsync(Limit);
            Assert.True(cancelEntered.IsSet);
            await listenerReleased.Task.WaitAsync(Limit);
            Assert.False(cancelRelease.IsSet);
            Assert.False(result.CleanupCompleted);
            Assert.False((await host.Completion).ShutdownCompleted);
            cancelRelease.Set();
            sink.Release.TrySetResult();
            await registration.Completion.WaitAsync(Limit);
            if (failLate) await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.DisposeAsync().AsTask().WaitAsync(Limit));
            else await host.DisposeAsync().AsTask().WaitAsync(Limit);
            Assert.False(result.CleanupCompleted);
        }
        finally
        {
            cancelRelease.Set(); sink.Release.TrySetResult();
            await registration.DisposeAsync();
            try { await host.DisposeAsync(); } catch (InvalidOperationException) { }
        }
    }

    [Fact]
    public async Task MonotonicExpiryDoesNotRequireTimerNotification()
    {
        var clock = new SilentClock();
        await using var host = new ObsPublishChannelHost(new() { Ingest = new() { Port = 0 } })
        {
            TerminationTimeProvider = clock,
            TerminationObserver = point =>
            {
                if (point == ObsPublishTerminationPoint.BeforeEndSelection) clock.Advance(TimeSpan.FromMinutes(2));
                return Task.CompletedTask;
            },
        };
        host.Start();
        var result = await host.RequestLocalTerminationAsync(TimeSpan.FromMinutes(1)).WaitAsync(Limit);
        Assert.Equal(ObsPublishLocalTerminationOutcome.TimedOut, result.Outcome);
        Assert.True(result.DeadlineExpired);
        Assert.False(host.DeadlineTimerSignaled);
        Assert.False(result.AcceptedCountersMatched);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RejectedOwnerCleanupNeverBecomesWriterNotRequired(int failureKind)
    {
        using var acquireEntered = new ManualResetEventSlim();
        using var acquireRelease = new ManualResetEventSlim();
        var owner = new HeldOwner(failureKind);
        var closed = Signal(); var clock = new SilentClock();
        var host = new ObsPublishChannelHost(new() { Ingest = new() { Port = 0 } }, null, null, null,
            () => { acquireEntered.Set(); if (!acquireRelease.Wait(Limit)) throw new TimeoutException(); return owner; }, null)
        {
            TerminationTimeProvider = clock,
            TerminationObserver = point => { if (point == ObsPublishTerminationPoint.RecordingAdmissionClosed) closed.TrySetResult(); return Task.CompletedTask; },
        };
        host.Start();
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        Assert.True(await host.HeadAvailable.WaitAsync(Limit));
        var request = RecordingSinkStartRequest.Manual(new RecordingCandidate(host.ChannelId, new(name: "fixture", streamExtension: "bin")));
        var acquisition = Task.Run(async () => await host.RecordingSource.AcquireAsync(request, CancellationToken.None));
        Assert.True(acquireEntered.Wait(Limit));
        try
        {
            var finish = host.RequestLocalTerminationAsync(Limit);
            await closed.Task.WaitAsync(Limit);
            Assert.True(host.RecordingSource.Evidence.AdmissionClosed);
            acquireRelease.Set();
            await owner.Entered.Task.WaitAsync(Limit);
            if (failureKind != 0) owner.Release.TrySetResult();
            else clock.AdvanceAndFire(Limit + Limit);
            var result = await finish.WaitAsync(Limit);
            Assert.NotEqual(ObsPublishLocalTerminationOutcome.LocalCompleted, result.Outcome);
            Assert.Equal(ObsPublishWriterEvidence.NotObserved, result.WriterEvidence);
            Assert.NotNull(result.RecordingFinalEvidence);
            if (failureKind == 0) Assert.Equal(1, result.RecordingFinalEvidence.RejectedLeaseCleanupPendingCount);
            else Assert.Equal(1, result.RecordingFinalEvidence.RejectedLeaseCleanupFailureCount);
            owner.Release.TrySetResult();
            if (failureKind == 0) Assert.Null(await acquisition.WaitAsync(Limit));
            else await Assert.ThrowsAnyAsync<Exception>(async () => await acquisition.WaitAsync(Limit));
            Assert.Equal(1, owner.Calls);
        }
        finally
        {
            acquireRelease.Set(); owner.Release.TrySetResult();
            try { await acquisition.WaitAsync(Limit); } catch (Exception) { }
            try { await host.DisposeAsync(); } catch (InvalidOperationException) { }
        }
    }

    [Fact]
    public async Task AcceptedSourceFaultCannotBecomeOwnerSuccess()
    {
        var host = ObsPublishChannelHostTests.Create(); host.Start();
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        Assert.True(await host.HeadAvailable.WaitAsync(Limit));
        await host.RecordingSource.CompleteWithEvidenceAsync(Limit, new IOException("accepted source fault"));
        var result = await host.RequestLocalTerminationAsync(Limit).WaitAsync(Limit);
        Assert.NotEqual(ObsPublishLocalTerminationOutcome.LocalCompleted, result.Outcome);
        Assert.True(result.RecordingSource!.Evidence.AcceptedTerminalFault);
        Assert.Equal(ObsPublishWriterEvidence.NotObserved, result.WriterEvidence);
        Assert.False(result.RecordingSource.WriterFinalizationObserved);
        await host.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordingWorkerFailureCannotBecomeOwnerSuccess(bool taskFault)
    {
        var tokenSeen = Signal(); CancellationTokenRegistration callback = default;
        var observer = new PcpRecordingSourceCleanupObserver
        {
            WorkerTokenObserved = token =>
            {
                if (!taskFault) callback = token.Register(() => throw new IOException("worker cancellation failure"));
                tokenSeen.TrySetResult();
            },
            AfterRegistrationCompletion = taskFault ? () => Task.FromException(new IOException("worker task failed")) : null,
        };
        var host = new ObsPublishChannelHost(new() { Ingest = new() { Port = 0 } }, null, null, null, null, observer);
        host.Start();
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        Assert.True(await host.HeadAvailable.WaitAsync(Limit));
        var lease = await host.RecordingSource.AcquireAsync(RecordingSinkStartRequest.Manual(new RecordingCandidate(host.ChannelId, new())), CancellationToken.None);
        Assert.NotNull(lease);
        await tokenSeen.Task.WaitAsync(Limit);
        var finish = host.RequestLocalTerminationAsync(Limit);
        await lease.ReadStream.CopyToAsync(Stream.Null).WaitAsync(Limit);
        if (taskFault) await Assert.ThrowsAsync<IOException>(async () => await lease.DisposeAsync());
        else await lease.DisposeAsync();
        var result = await finish.WaitAsync(Limit);
        Assert.NotEqual(ObsPublishLocalTerminationOutcome.LocalCompleted, result.Outcome);
        Assert.False((await host.Completion).ShutdownCompleted);
        Assert.Equal(1, result.RecordingFinalEvidence!.CleanupFailedLeaseCount);
        Assert.Equal(1, result.RecordingFinalEvidence.RegistrationCleanupFailedLeaseCount);
        callback.Dispose();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.DisposeAsync());
    }

    [Fact]
    public async Task HttpLateFaultCannotUpgradeFailedOwnerResult()
    {
        var entered = Signal(); var release = Signal(); var clock = new SilentClock();
        var host = new ObsPublishChannelHost(new() { Ingest = new() { Port = 0 } }) { TerminationTimeProvider = clock }; host.Start();
        var source = host.HttpSource!;
        source.AfterSourceCleanup = async () => { entered.TrySetResult(); await release.Task; throw new IOException("late source cleanup"); };
        var finish = host.RequestLocalTerminationAsync(Limit);
        await entered.Task.WaitAsync(Limit);
        clock.AdvanceAndFire(Limit + Limit);
        var result = await finish.WaitAsync(Limit);
        Assert.NotEqual(ObsPublishLocalTerminationOutcome.LocalCompleted, result.Outcome);
        Assert.False(result.CleanupCompleted);
        release.TrySetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.DisposeAsync().AsTask().WaitAsync(Limit));
        Assert.True(source.SourceCleanupFailed);
        Assert.False(result.HttpSourceCleanupFailed); // Immutable before the late fault.
        Assert.Same(result, await host.RequestLocalTerminationAsync(Limit));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EosAndFinishUseActualAdmissionOrder(bool finishFirst)
    {
        var selected = Signal(); var release = Signal();
        await using var host = new ObsPublishChannelHost(new() { Ingest = new() { Port = 0 } })
        {
            TerminationObserver = async point =>
            {
                if (point == ObsPublishTerminationPoint.BeforeEndSelection) { selected.TrySetResult(); await release.Task; }
            },
        };
        host.Start();
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        Task<ObsPublishLocalTerminationResult>? finish = finishFirst ? host.RequestLocalTerminationAsync(Limit) : null;
        if (finishFirst) await selected.Task.WaitAsync(Limit);
        await ObsPublishChannelHostTests.StopAsync(publisher);
        Assert.Equal(ObsRtmpIngestEndReason.PublisherStopped, (await host.IngressCompletion.WaitAsync(Limit)).EndReason);
        await selected.Task.WaitAsync(Limit);
        if (!finishFirst)
        {
            var rejected = await host.RequestLocalTerminationAsync(Limit);
            Assert.Equal(ObsPublishLocalTerminationOutcome.Failed, rejected.Outcome);
        }
        release.TrySetResult();
        var owner = await host.Completion.WaitAsync(Limit);
        if (finishFirst)
        {
            var local = await finish!.WaitAsync(Limit);
            Assert.Equal(ObsPublishLocalTerminationOutcome.LocalCompleted, local.Outcome);
            Assert.Equal(ObsPublishChannelEndReason.LocalTerminated, owner.EndReason);
            Assert.Same(local, owner.LocalTermination);
            Assert.Equal(ObsRtmpIngestEndReason.PublisherStopped, local.Ingress!.FinalResult!.EndReason);
        }
        else
        {
            Assert.Equal(ObsPublishChannelEndReason.PublisherStopped, owner.EndReason);
            Assert.Null(owner.LocalTermination);
        }
    }

    [Fact]
    public async Task DisposeBeforeFinishRejectsAdmission()
    {
        var host = ObsPublishChannelHostTests.Create(); host.Start();
        var disposal = host.DisposeAsync().AsTask();
        var local = await host.RequestLocalTerminationAsync(Limit);
        Assert.Equal(ObsPublishLocalTerminationOutcome.Cancelled, local.Outcome);
        await disposal.WaitAsync(Limit);
        Assert.Null((await host.Completion).LocalTermination);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForceBeforeCommitWinsButDisposeAfterCommitDoesNotRewriteSuccess(bool forceBefore)
    {
        var entered = Signal(); var release = Signal(); var forceAccepted = Signal();
        var counts = new ConcurrentDictionary<ObsPublishTerminationPoint, int>();
        var host = new ObsPublishChannelHost(new() { Ingest = new() { Port = 0 } })
        {
            TerminationObserver = async point =>
            {
                counts.AddOrUpdate(point, 1, (_, count) => count + 1);
                if (point == ObsPublishTerminationPoint.BeforeCommit) { entered.TrySetResult(); await release.Task; }
            },
        };
        host.Start();
        using var stopObservation = host.OwnerStopToken.Register(() => forceAccepted.TrySetResult());
        var finish = host.RequestLocalTerminationAsync(Limit);
        await entered.Task.WaitAsync(Limit);
        Task? disposal = forceBefore ? host.DisposeAsync().AsTask() : null;
        if (forceBefore)
        {
            await forceAccepted.Task.WaitAsync(Limit);
        }
        release.TrySetResult();
        var local = await finish.WaitAsync(Limit);
        var owner = await host.Completion.WaitAsync(Limit);
        if (forceBefore) Assert.Equal(ObsPublishLocalTerminationOutcome.Cancelled, local.Outcome);
        else Assert.Equal(ObsPublishLocalTerminationOutcome.LocalCompleted, local.Outcome);
        Assert.Same(local, owner.LocalTermination);
        Assert.Equal(local.CleanupCompleted, owner.ShutdownCompleted);
        disposal ??= host.DisposeAsync().AsTask();
        Assert.Same(disposal, host.DisposeAsync().AsTask());
        await disposal.WaitAsync(Limit);
        Assert.Equal(forceBefore ? 1 : 0, counts.GetValueOrDefault(ObsPublishTerminationPoint.Cancellation));
        Assert.Equal(1, counts[ObsPublishTerminationPoint.HttpListenerCleanup]);
    }

    [Fact]
    public async Task MultipleCleanupFailuresRemainSeparateAndPreventSuccess()
    {
        var host = new ObsPublishChannelHost(new() { Ingest = new() { Port = 0 } })
        {
            TerminationObserver = point => point is ObsPublishTerminationPoint.IngressCleanup or ObsPublishTerminationPoint.RelayCleanup
                ? Task.FromException(new IOException("cleanup observation failed")) : Task.CompletedTask,
        };
        host.Start();
        var result = await host.RequestLocalTerminationAsync(Limit).WaitAsync(Limit);
        Assert.Equal(ObsPublishLocalTerminationOutcome.CleanupIncomplete, result.Outcome);
        Assert.False(result.Cleanup!.Ingress);
        Assert.False(result.Cleanup.Relay);
        Assert.False((await host.Completion).ShutdownCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.DisposeAsync());
    }

    [Fact]
    public async Task CompletedStageEvidenceFollowsDependencyOrder()
    {
        var observed = new List<ObsPublishTerminationPoint>();
        var expected = new[]
        {
            ObsPublishTerminationPoint.IngressEnded, ObsPublishTerminationPoint.SharedIdle,
            ObsPublishTerminationPoint.RelayDrained, ObsPublishTerminationPoint.HttpCompleted,
            ObsPublishTerminationPoint.RecordingCompleted, ObsPublishTerminationPoint.FanoutCompleted,
        };
        await using var host = new ObsPublishChannelHost(new() { Ingest = new() { Port = 0 } })
        {
            TerminationObserver = point =>
            {
                if (expected.Contains(point)) lock (observed) { observed.Add(point); }
                return Task.CompletedTask;
            },
        };
        host.Start();
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        Assert.True(await host.HeadAvailable.WaitAsync(Limit));
        var local = await host.RequestLocalTerminationAsync(Limit).WaitAsync(Limit);
        Assert.Equal(ObsPublishLocalTerminationOutcome.LocalCompleted, local.Outcome);
        Assert.True(local.AcceptedCountersMatched);
        Assert.Equal(expected, observed);
        var owner = await host.Completion;
        Assert.Same(local, owner.LocalTermination);
        Assert.True(owner.ShutdownCompleted);
    }

    [Fact]
    public async Task KnownHttpCleanupFaultSurvivesOwnerFinalization()
    {
        var host = ObsPublishChannelHostTests.Create(); host.Start();
        host.HttpSource!.AfterSourceCleanup = () => Task.FromException(new IOException("source cleanup failed"));
        var result = await host.RequestLocalTerminationAsync(Limit).WaitAsync(Limit);
        Assert.NotEqual(ObsPublishLocalTerminationOutcome.LocalCompleted, result.Outcome);
        Assert.True(result.HttpSourceCleanupFailed);
        Assert.True(result.Http!.SourceCleanupFailed);
        Assert.False((await host.Completion).ShutdownCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.DisposeAsync());
    }

    private sealed class HeldOwner(int failureKind) : IAsyncDisposable
    {
        public TaskCompletionSource Entered { get; } = Signal();
        public TaskCompletionSource Release { get; } = Signal();
        public int Calls { get; private set; }
        public async ValueTask DisposeAsync()
        {
            Calls++; Entered.TrySetResult(); await Release.Task;
            if (failureKind == 1) throw new OperationCanceledException("rejected owner cleanup");
            if (failureKind == 2) throw new IOException("rejected owner cleanup");
        }
    }

    private sealed class PausedSink(TaskCompletionSource entered) : IPcpRelayChildSink
    {
        public TaskCompletionSource Release { get; } = Signal();
        public async ValueTask SendAsync(PcpRelayPacket packet, CancellationToken cancellationToken)
        {
            entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class SilentClock : TimeProvider
    {
        private long _ticks;
        private TimerCallback? _callback;
        private object? _state;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Volatile.Read(ref _ticks);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
        public void AdvanceAndFire(TimeSpan duration) { Advance(duration); _callback?.Invoke(_state); }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = callback; _state = state;
            return new SilentTimer();
        }
        private sealed class SilentTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
