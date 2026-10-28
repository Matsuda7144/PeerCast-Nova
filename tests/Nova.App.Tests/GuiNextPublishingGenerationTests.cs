using System.Text.Json;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.App.Tests;

public sealed partial class GuiNextPublishingGenerationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task SameGuiBindingSupportsThreeExplicitCyclesWithFreshOwnersSessionsAndAdapters()
    {
        var factory = new Factory(new Owner(), new Owner(), new Owner());
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await using var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        var status = composition.Status;
        var commands = composition.Commands;
        tabs.SetPublishingDispatcher(action => action());
        tabs.BindPublishingStatus(status, commands);
        await tabs.RefreshPublishingStatusAsync();
        tabs.SelectedTab = GenesisGuiNextTab.Publishing;
        var sessions = new HashSet<Guid>();
        for (var i = 0; i < 3; i++)
        {
            Assert.True(tabs.PublishingCommands.CanStart);
            Assert.Equal(Math.Max(1, i), factory.Creates);
            await tabs.RequestPublishingStartAsync();
            await tabs.RefreshPublishingStatusAsync();
            var row = Assert.Single(tabs.PublishingRows);
            Assert.Equal(i + 1, row.Generation);
            Assert.True(sessions.Add(row.SessionId!.Value));
            Assert.Equal("OBS待機中", row.StateText);
            Assert.True(tabs.PublishingCommands.CanInterrupt);
            await tabs.RequestPublishingInterruptAsync();
            await tabs.RefreshPublishingStatusAsync();
            Assert.Equal("停止（キャンセル）", Assert.Single(tabs.PublishingRows).StateText);
            Assert.True(tabs.PublishingCommands.CanStart);
            Assert.False(tabs.PublishingCommands.CanInterrupt);
            tabs.SelectedTab = GenesisGuiNextTab.Channels;
            tabs.SelectedTab = GenesisGuiNextTab.Publishing;
            await tabs.RefreshPublishingStatusAsync();
            Assert.Equal(i + 1, factory.Creates); // Reads/navigation never create the next generation.
        }
        Assert.Same(status, composition.Status);
        Assert.Same(commands, composition.Commands);
        await composition.DisposeAsync();
        Assert.All(factory.Owners, owner => { Assert.Equal(1, owner.Starts); Assert.Equal(1, owner.Disposals); });
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(tabs.PublishingRows));
        Assert.Contains("中断完了後に再開始可能", GenesisGuiNextPublishingPresentation.ScopeText);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RestartIsRejectedUntilCleanupAndTerminalCompletionAreBothProven(bool blockCleanup)
    {
        var old = new Owner { BlockDispose = blockCleanup, HoldCompletion = !blockCleanup };
        var factory = new Factory(old, new Owner());
        var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        var stop = composition.Commands.RequestInterruptAsync();
        await old.DisposeEntered.Task.WaitAsync(Timeout);
        if (blockCleanup)
        {
            Assert.False(stop.IsCompleted);
            Assert.False(composition.Commands.ReadCommands().CanStart);
            await composition.Commands.RequestStartAsync();
            Assert.Equal(1, factory.Creates);
            old.ReleaseDispose.TrySetResult();
            await stop.WaitAsync(Timeout);
            Assert.True(composition.Commands.ReadCommands().CanStart);
            await composition.Commands.RequestStartAsync();
            Assert.Equal(2, factory.Creates);
            Assert.Equal(2, composition.Status.Read().Generation);
            await composition.DisposeAsync();
        }
        else
        {
            await stop.WaitAsync(Timeout);
            var terminal = await composition.Lifecycle.Completion;
            Assert.False(terminal.CleanupCompleted);
            Assert.Equal(NovaObsPublishingLifecycleFailure.CompletionFailed, terminal.Failure);
            old.End(); // Late evidence cannot repair a disposed, failed generation.
            Assert.Same(terminal, await composition.Lifecycle.Completion);
            Assert.False(composition.Commands.ReadCommands().CanStart);
            await composition.Commands.RequestStartAsync();
            Assert.Equal(1, factory.Creates);
            await Assert.ThrowsAsync<InvalidOperationException>(() => composition.DisposeAsync().AsTask());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CleanupFailureOrUnprovenFoundationShutdownBlocksNewOwner(bool throwCleanup)
    {
        var old = new Owner { FailDispose = throwCleanup, ShutdownProven = throwCleanup };
        var factory = new Factory(old, new Owner());
        var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        await composition.Commands.RequestInterruptAsync();
        Assert.False(composition.Commands.ReadCommands().CanStart);
        Assert.Contains("cleanup", composition.Commands.ReadCommands().DiagnosticText);
        await composition.Commands.RequestStartAsync();
        Assert.Equal(1, factory.Creates);
        Assert.Equal(NovaObsPublishingLifecycleFailure.CleanupFailed, composition.Status.Read().LifecycleFailure);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(composition.Status.Read()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => composition.DisposeAsync().AsTask());
        Assert.Equal(1, old.Disposals);
    }

    [Fact]
    public async Task RawStoppedSnapshotIsNotTerminalProofAndOldCompletionFailureCannotBeRetried()
    {
        var old = new Owner();
        var factory = new Factory(old, new Owner());
        var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        old.Current = old.Current with { Revision = 3, State = ChannelSessionState.Stopped, EndReason = ObsPublishChannelEndReason.HostCancelled };
        Assert.False(composition.Commands.ReadCommands().CanStart);
        Assert.Contains("確認待ち", composition.Commands.ReadCommands().DiagnosticText);
        await composition.Commands.RequestStartAsync();
        Assert.Equal(1, factory.Creates);
        old.Completed.TrySetException(new InvalidOperationException("SECRET completion"));
        await composition.Lifecycle.Completion.WaitAsync(Timeout);
        Assert.Equal(NovaObsPublishingLifecycleFailure.CompletionFailed, composition.Status.Read().LifecycleFailure);
        await composition.Commands.RequestStartAsync();
        Assert.Equal(1, factory.Creates);
        Assert.False(composition.Commands.ReadCommands().CanStart);
        await Assert.ThrowsAsync<InvalidOperationException>(() => composition.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task FactoryFailureLeavesOldTerminalVisibleAndFailsClosedWithoutExceptionBody()
    {
        var factory = new Factory(new Owner()) { FailReplacement = true };
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        await composition.Commands.RequestInterruptAsync();
        var terminal = composition.Status.Read();
        await composition.Commands.RequestStartAsync();
        Assert.Equal(terminal, composition.Status.Read());
        var command = composition.Commands.ReadCommands();
        Assert.False(command.CanStart);
        Assert.False(command.CanInterrupt);
        Assert.Contains("ownerを作成できません", command.DiagnosticText);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(command));
        await composition.Commands.RequestStartAsync();
        Assert.Equal(2, factory.Creates); // No automatic factory retry.
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidOrPreviouslyUsedSessionIsRejectedAndCandidateIsCleanedOnce(bool reusedSession)
    {
        var old = new Owner();
        var candidate = new Owner();
        candidate.Current = candidate.Current with { SessionId = reusedSession ? old.Current.SessionId : null! };
        var factory = new Factory(old, candidate);
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        await composition.Commands.RequestInterruptAsync();
        var terminal = composition.Status.Read();
        await composition.Commands.RequestStartAsync();
        Assert.Equal(terminal, composition.Status.Read());
        Assert.Contains("新SessionId", composition.Commands.ReadCommands().DiagnosticText);
        Assert.False(composition.Commands.ReadCommands().CanStart);
        Assert.Equal(0, candidate.Starts);
        Assert.Equal(1, candidate.Disposals);
        await composition.DisposeAsync();
        Assert.Equal(1, candidate.Disposals);
        Assert.Equal(1, old.Disposals);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task FreshLifecycleStateCannotHideNonIdleCandidateRuntime(int scenario)
    {
        var old = new Owner();
        var candidate = new Owner();
        candidate.Current = scenario switch
        {
            0 => candidate.Current with { State = ChannelSessionState.Negotiating, ListenerReady = true },
            1 => candidate.Current with { State = ChannelSessionState.Stopped, EndReason = ObsPublishChannelEndReason.HostCancelled },
            2 => candidate.Current with { HeadAvailable = true },
            _ => candidate.Current with { PublisherConnected = true },
        };
        var factory = new Factory(old, candidate);
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        await composition.Commands.RequestInterruptAsync();
        var terminal = composition.Status.Read();
        await composition.Commands.RequestStartAsync();
        Assert.Equal(terminal, composition.Status.Read());
        Assert.Contains("未起動状態", composition.Commands.ReadCommands().DiagnosticText);
        Assert.False(composition.Commands.ReadCommands().CanStart);
        Assert.Equal(0, candidate.Starts);
        Assert.Equal(1, candidate.Disposals);
    }

    [Fact]
    public async Task AlreadyOwnedOlderRuntimeAndOlderSessionCannotSelectThemselvesAgain()
    {
        var first = new Owner();
        var second = new Owner();
        var factory = new Factory(first, second, first);
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        for (var i = 0; i < 2; i++)
        {
            await composition.Commands.RequestStartAsync();
            await composition.Commands.RequestInterruptAsync();
        }
        await composition.Commands.RequestStartAsync();
        Assert.Equal(2, composition.Status.Read().Generation);
        Assert.Contains("既存owner", composition.Commands.ReadCommands().DiagnosticText);
        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, second.Disposals);
        Assert.Equal(1, first.Starts);
    }

    [Fact]
    public async Task FreshOwnerCannotReuseSessionIdFromAnOlderRetiredGeneration()
    {
        var first = new Owner();
        var second = new Owner();
        var candidate = new Owner();
        candidate.Current = candidate.Current with { SessionId = first.Current.SessionId };
        var factory = new Factory(first, second, candidate);
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        for (var i = 0; i < 2; i++)
        {
            await composition.Commands.RequestStartAsync();
            await composition.Commands.RequestInterruptAsync();
        }
        await composition.Commands.RequestStartAsync();
        Assert.Equal(2, composition.Status.Read().Generation);
        Assert.Contains("新SessionId", composition.Commands.ReadCommands().DiagnosticText);
        Assert.Equal(0, candidate.Starts);
        Assert.Equal(1, candidate.Disposals);
        Assert.False(composition.Commands.ReadCommands().CanStart);
    }

    [Fact]
    public async Task NullReplacementFactoryResultFailsClosedWithoutLosingRetiredOwnership()
    {
        var old = new Owner();
        var factory = new Factory(old, null!);
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        await composition.Commands.RequestInterruptAsync();
        var terminal = composition.Status.Read();
        await composition.Commands.RequestStartAsync();
        Assert.Equal(terminal, composition.Status.Read());
        Assert.False(composition.Commands.ReadCommands().CanStart);
        Assert.False(composition.Commands.ReadCommands().CanInterrupt);
        Assert.Equal(1, old.Disposals);
    }

    [Fact]
    public async Task LateRetiredSnapshotAndCompletionCannotOverwriteNewGenerationOrTabRows()
    {
        var old = new Owner();
        var factory = new Factory(old, new Owner());
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await using var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        tabs.SetPublishingDispatcher(action => action());
        tabs.BindPublishingStatus(composition.Status, composition.Commands);
        await tabs.RefreshPublishingStatusAsync();
        tabs.SelectedTab = GenesisGuiNextTab.Publishing;
        var retiredLifecycle = composition.Lifecycle;
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        await tabs.RequestPublishingInterruptAsync();
        await tabs.RefreshPublishingStatusAsync();
        var oldSession = Assert.Single(tabs.PublishingRows).SessionId;
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        var rows = tabs.PublishingRows;
        var current = Assert.Single(rows);
        Assert.NotEqual(oldSession, current.SessionId);
        old.Current = old.Current with { Revision = 999, State = ChannelSessionState.Faulted, EndReason = ObsPublishChannelEndReason.StartupFailed };
        Assert.False(old.Completed.TrySetException(new InvalidOperationException("SECRET old completion")));
        await retiredLifecycle.RequestStopAsync();
        await tabs.RefreshPublishingStatusAsync();
        Assert.Same(rows, tabs.PublishingRows);
        Assert.Equal(current, composition.Status.Read());
        Assert.Equal(2, current.Generation);
        Assert.Equal(NovaObsPublishingLifecycleFailure.None, current.LifecycleFailure);
        Assert.Equal("OBS待機中", current.StateText);
        Assert.NotSame(retiredLifecycle, composition.Lifecycle);
    }

    [Fact]
    public async Task DuplicateRestartClicksCreateAndStartOnlyOneNewOwner()
    {
        var factory = new Factory(new Owner(), new Owner()) { BlockReplacement = true };
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        await composition.Commands.RequestInterruptAsync();
        var restart = composition.Commands.RequestStartAsync();
        await factory.CreateEntered.Task.WaitAsync(Timeout);
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => composition.Commands.RequestStartAsync()));
            Assert.False(composition.Commands.ReadCommands().CanStart);
            Assert.True(composition.Commands.ReadCommands().IsPending);
            Assert.Equal(2, factory.Creates);
            Assert.Equal(0, factory.Owners[1].Starts);
        }
        finally { factory.ReleaseCreate.TrySetResult(); }
        await restart.WaitAsync(Timeout);
        Assert.Equal(1, factory.Owners[1].Starts);
    }

    [Fact]
    public async Task ShutdownDuringFactoryCreationDisposesCandidateWithoutStartingOrPublishingIt()
    {
        var factory = new Factory(new Owner(), new Owner()) { BlockReplacement = true };
        var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        await composition.Commands.RequestInterruptAsync();
        var restart = composition.Commands.RequestStartAsync();
        await factory.CreateEntered.Task.WaitAsync(Timeout);
        var shutdown = composition.DisposeAsync().AsTask();
        try
        {
            Assert.False(shutdown.IsCompleted);
            await composition.Commands.RequestStartAsync();
            Assert.False(composition.Commands.ReadCommands().CanStart);
        }
        finally { factory.ReleaseCreate.TrySetResult(); }
        await Task.WhenAll(restart, shutdown).WaitAsync(Timeout);
        Assert.Equal(1, composition.Status.Read().Generation);
        Assert.Equal(0, factory.Owners[1].Starts);
        Assert.All(factory.Owners, owner => Assert.Equal(1, owner.Disposals));
    }

    [Fact]
    public async Task ShutdownAfterReplacementAdmissionDrainsBlockedStartAndCleansNewOwnerOnce()
    {
        var next = new Owner { BlockStart = true };
        var factory = new Factory(new Owner(), next);
        var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        await composition.Commands.RequestInterruptAsync();
        var restart = composition.Commands.RequestStartAsync();
        await next.StartEntered.Task.WaitAsync(Timeout);
        var shutdown = composition.DisposeAsync().AsTask();
        try
        {
            Assert.Equal(2, composition.Status.Read().Generation);
            Assert.False(shutdown.IsCompleted);
            await composition.Commands.RequestStartAsync();
            await composition.Commands.RequestInterruptAsync();
            Assert.False(composition.Commands.ReadCommands().CanStart);
            Assert.Equal(0, next.Disposals);
        }
        finally { next.ReleaseStart.TrySetResult(); }
        await Task.WhenAll(restart, shutdown).WaitAsync(Timeout);
        Assert.All(factory.Owners, owner => { Assert.Equal(1, owner.Starts); Assert.Equal(1, owner.Disposals); });
    }

    [Fact]
    public async Task InterruptDuringNewStartIsOrderedAndNextExplicitCycleRemainsAvailable()
    {
        var next = new Owner { BlockStart = true };
        var factory = new Factory(new Owner(), next, new Owner());
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        await composition.Commands.RequestInterruptAsync();
        var restart = composition.Commands.RequestStartAsync();
        await next.StartEntered.Task.WaitAsync(Timeout);
        Task stop;
        try
        {
            Assert.True(composition.Commands.ReadCommands().CanInterrupt);
            stop = composition.Commands.RequestInterruptAsync();
            await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => composition.Commands.RequestStartAsync()));
            Assert.Equal(2, factory.Creates);
            Assert.Equal(0, next.Disposals);
        }
        finally { next.ReleaseStart.TrySetResult(); }
        await Task.WhenAll(restart, stop).WaitAsync(Timeout);
        Assert.True(composition.Commands.ReadCommands().CanStart);
        await composition.Commands.RequestStartAsync();
        Assert.Equal(3, composition.Status.Read().Generation);
        Assert.Equal(3, factory.Creates);
    }

    [Fact]
    public async Task StartInterruptRestartAndCloseRacesNeverStartOrDisposeAnOwnerTwice()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var factory = new Factory(Enumerable.Range(0, 30).Select(_ => new Owner()).ToArray());
            var composition = NovaGuiNextPublishingComposition.Create(factory);
            await composition.Commands.RequestStartAsync();
            await composition.Commands.RequestInterruptAsync();
            await Task.WhenAll(Enumerable.Range(0, 30).Select(i => Task.Run(async () =>
            {
                if (i % 3 == 0) await composition.Commands.RequestStartAsync();
                else if (i % 3 == 1) await composition.Commands.RequestInterruptAsync();
                else await composition.DisposeAsync();
            }))).WaitAsync(Timeout);
            await composition.DisposeAsync();
            Assert.All(factory.Owners.Take(factory.Creates), owner =>
            {
                Assert.InRange(owner.Starts, 0, 1);
                Assert.Equal(1, owner.Disposals);
            });
            Assert.False(composition.Commands.ReadCommands().CanStart);
            Assert.False(composition.Commands.ReadCommands().CanInterrupt);
        }
    }

    [Fact]
    public async Task RejectedCandidateCleanupFailureRemainsOwnedAndShutdownReportsIt()
    {
        var candidate = new Owner { FailDispose = true };
        var old = new Owner();
        candidate.Current = candidate.Current with { SessionId = old.Current.SessionId };
        var factory = new Factory(old, candidate);
        var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        await composition.Commands.RequestInterruptAsync();
        await composition.Commands.RequestStartAsync();
        Assert.Contains("候補generationのcleanup", composition.Commands.ReadCommands().DiagnosticText);
        Assert.False(composition.Commands.ReadCommands().CanStart);
        await Assert.ThrowsAsync<InvalidOperationException>(() => composition.DisposeAsync().AsTask());
        Assert.Equal(1, candidate.Disposals);
        Assert.Equal(0, candidate.Starts);
        Assert.Equal(1, old.Disposals);
    }

    [Fact]
    public async Task ReplacementStartFailureIsProjectedOnlyToItsOwnGenerationWithoutAutomaticRetry()
    {
        var next = new Owner { FailStart = true };
        var factory = new Factory(new Owner(), next, new Owner());
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        await composition.Commands.RequestInterruptAsync();
        var oldSession = composition.Status.Read().SessionId;
        await composition.Commands.RequestStartAsync();
        await composition.Lifecycle.Completion.WaitAsync(Timeout);
        var failed = composition.Status.Read();
        Assert.Equal(2, failed.Generation);
        Assert.NotEqual(oldSession, failed.SessionId);
        Assert.Equal(NovaObsPublishingLifecycleFailure.StartFailed, failed.LifecycleFailure);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(failed));
        Assert.False(composition.Commands.ReadCommands().CanStart);
        await composition.Commands.RequestStartAsync();
        Assert.Equal(2, factory.Creates);
        Assert.Equal(1, next.Disposals);
    }

    [Fact]
    public async Task CloseAdmissionBeforeRestartPreventsFactoryAndIsIdempotent()
    {
        var factory = new Factory(new Owner(), new Owner());
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        await composition.Commands.RequestInterruptAsync();
        Assert.True(composition.Commands.ReadCommands().CanStart);
        composition.CloseAdmission();
        composition.CloseAdmission();
        await composition.Commands.RequestStartAsync();
        await composition.Commands.RequestInterruptAsync();
        Assert.Equal(1, factory.Creates);
        Assert.Equal(0, factory.Owners[1].Disposals);
        Assert.False(composition.Commands.ReadCommands().CanStart);
    }

    [Fact]
    public async Task CloseDuringAdmittedFactoryPreventsCandidatePublicationAndInterruptIsNoOp()
    {
        var factory = new Factory(new Owner(), new Owner()) { BlockReplacement = true };
        var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        await composition.Commands.RequestInterruptAsync();
        var restart = composition.Commands.RequestStartAsync();
        await factory.CreateEntered.Task.WaitAsync(Timeout);
        Assert.False(composition.Commands.ReadCommands().CanInterrupt);
        await composition.Commands.RequestInterruptAsync();
        composition.CloseAdmission(); // Must return while Factory.Create remains blocked.
        var shutdown = composition.DisposeAsync().AsTask();
        Assert.False(shutdown.IsCompleted);
        factory.ReleaseCreate.TrySetResult();
        await Task.WhenAll(restart, shutdown).WaitAsync(Timeout);
        Assert.Equal(1, composition.Status.Read().Generation);
        Assert.Equal(0, factory.Owners[1].Starts);
        Assert.Equal(1, factory.Owners[1].Disposals);
    }

    [Fact]
    public async Task CloseAtFactoryReturnSkipsCandidateReadAndStartButCleansIt()
    {
        var reads = 0;
        var candidate = new Owner { ReadSnapshot = () => { Interlocked.Increment(ref reads); throw new InvalidOperationException("Must not read after close"); } };
        var factory = new Factory(new Owner(), candidate);
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        await composition.Commands.RequestInterruptAsync();
        factory.BeforeReplacementReturn = composition.CloseAdmission;
        await composition.Commands.RequestStartAsync().WaitAsync(Timeout);
        Assert.Equal(1, composition.Status.Read().Generation);
        Assert.Equal(0, reads);
        Assert.Equal(0, candidate.Starts);
        Assert.Equal(1, candidate.Disposals);
    }

    [Fact]
    public async Task CloseWhileCandidateSnapshotIsBeingValidatedWinsPublicationGate()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var candidate = new Owner { ReadSnapshot = () => { entered.TrySetResult(); release.Task.GetAwaiter().GetResult(); } };
        var factory = new Factory(new Owner(), candidate);
        var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        await composition.Commands.RequestInterruptAsync();
        var restart = composition.Commands.RequestStartAsync();
        await entered.Task.WaitAsync(Timeout);
        composition.CloseAdmission(); // Snapshot validation is outside the composition gate.
        var shutdown = composition.DisposeAsync().AsTask();
        Assert.False(shutdown.IsCompleted);
        release.TrySetResult();
        await Task.WhenAll(restart, shutdown).WaitAsync(Timeout);
        Assert.Equal(1, composition.Status.Read().Generation);
        Assert.Equal(0, candidate.Starts);
        Assert.Equal(1, candidate.Disposals);
    }

    [Fact]
    public async Task CloseFromLastCandidateSnapshotReadPreventsPublishAndStart()
    {
        var candidate = new Owner();
        var factory = new Factory(new Owner(), candidate);
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        await composition.Commands.RequestInterruptAsync();
        candidate.ReadSnapshot = composition.CloseAdmission;
        await composition.Commands.RequestStartAsync().WaitAsync(Timeout);
        Assert.Equal(1, composition.Status.Read().Generation);
        Assert.Equal(0, candidate.Starts);
        Assert.Equal(1, candidate.Disposals);
    }

    [Theory]
    [InlineData(0)] // Dispose returned with pending Completion
    [InlineData(1)] // Fault
    [InlineData(2)] // Null result
    [InlineData(3)] // ShutdownCompleted=false
    [InlineData(4)] // Null task
    [InlineData(5)] // Getter throws
    [InlineData(6)] // Cancelled
    public async Task RejectedCandidateWithUnprovenCleanupRemainsOwnedAndCannotBeRepaired(int mode)
    {
        var old = new Owner();
        var candidate = new Owner
        {
            HoldCompletion = mode == 0,
            FaultCompletion = mode == 1,
            NullResult = mode == 2,
            ShutdownProven = mode != 3,
            NullCompletion = mode == 4,
            ThrowCompletion = mode == 5,
            CancelCompletion = mode == 6,
        };
        candidate.Current = candidate.Current with { SessionId = old.Current.SessionId };
        var factory = new Factory(old, candidate);
        var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        await composition.Commands.RequestInterruptAsync();
        await composition.Commands.RequestStartAsync().WaitAsync(Timeout);
        Assert.Contains("候補generationのcleanup", composition.Commands.ReadCommands().DiagnosticText);
        candidate.End();
        await composition.Commands.RequestStartAsync();
        Assert.Equal(2, factory.Creates);
        Assert.Equal(1, composition.Status.Read().Generation);
        await Assert.ThrowsAsync<InvalidOperationException>(() => composition.DisposeAsync().AsTask());
        Assert.Equal(0, candidate.Starts);
        Assert.Equal(1, candidate.Disposals);
    }

    [Fact]
    public async Task PendingRejectedCandidateDisposeIsRetainedUntilRealCleanupSettles()
    {
        var old = new Owner();
        var candidate = new Owner { BlockDispose = true };
        candidate.Current = candidate.Current with { SessionId = old.Current.SessionId };
        var factory = new Factory(old, candidate);
        var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        await composition.Commands.RequestInterruptAsync();
        var restart = composition.Commands.RequestStartAsync();
        await candidate.DisposeEntered.Task.WaitAsync(Timeout);
        var shutdown = composition.DisposeAsync().AsTask();
        Assert.False(restart.IsCompleted);
        Assert.False(shutdown.IsCompleted);
        Assert.False(composition.Commands.ReadCommands().CanStart);
        candidate.ReleaseDispose.TrySetResult();
        await Task.WhenAll(restart, shutdown).WaitAsync(Timeout);
        Assert.Equal(0, candidate.Starts);
        Assert.Equal(1, candidate.Disposals);
        Assert.Equal(1, composition.Status.Read().Generation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task CandidateIdentityMustBeValidBeforePublication(int mode)
    {
        var candidate = new Owner();
        candidate.Current = mode switch
        {
            0 => candidate.Current with { ChannelId = null! },
            1 => candidate.Current with { SessionId = null! },
            2 => candidate.Current with { State = (ChannelSessionState)int.MaxValue },
            _ => candidate.Current with { Revision = -1 },
        };
        var factory = new Factory(new Owner(), candidate);
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        await composition.Commands.RequestInterruptAsync();
        await composition.Commands.RequestStartAsync();
        Assert.Equal(1, composition.Status.Read().Generation);
        Assert.Equal(0, candidate.Starts);
        Assert.Equal(1, candidate.Disposals);
        Assert.False(composition.Commands.ReadCommands().CanStart);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task OldGenerationWithoutCompletionProofCannotRestart(int mode)
    {
        var old = new Owner();
        var factory = new Factory(old, new Owner());
        var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        old.FaultCompletion = mode == 0;
        old.CancelCompletion = mode == 1;
        old.NullResult = mode == 2;
        old.NullCompletion = mode == 3;
        old.ThrowCompletion = mode == 4;
        await composition.Commands.RequestInterruptAsync().WaitAsync(Timeout);
        var terminal = await composition.Lifecycle.Completion;
        Assert.False(terminal.CleanupCompleted);
        old.End();
        await composition.Commands.RequestStartAsync();
        Assert.Equal(1, factory.Creates);
        Assert.False(composition.Commands.ReadCommands().CanStart);
        Assert.Same(terminal, await composition.Lifecycle.Completion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => composition.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task DisposedTabsIgnoreReplacementCompletionButApplicationStillOwnsIt()
    {
        var factory = new Factory(new Owner(), new Owner()) { BlockReplacement = true };
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        tabs.SetPublishingDispatcher(action => action());
        tabs.BindPublishingStatus(composition.Status, composition.Commands);
        await tabs.RefreshPublishingStatusAsync();
        tabs.SelectedTab = GenesisGuiNextTab.Publishing;
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        await tabs.RequestPublishingInterruptAsync();
        await tabs.RefreshPublishingStatusAsync();
        var restart = tabs.RequestPublishingStartAsync();
        await factory.CreateEntered.Task.WaitAsync(Timeout);
        await tabs.DisposeAsync();
        var rows = tabs.PublishingRows;
        var notifications = 0;
        tabs.PropertyChanged += (_, _) => Interlocked.Increment(ref notifications);
        factory.ReleaseCreate.TrySetResult();
        await restart.WaitAsync(Timeout);
        Assert.Same(rows, tabs.PublishingRows);
        Assert.Equal(0, notifications);
        Assert.Equal(2, composition.Status.Read().Generation);
        Assert.Equal(1, factory.Owners[1].Starts);
    }

    [Fact]
    public async Task UnsupportedCandidateCannotBePublishedOrCertifyCleanup()
    {
        var old = new Owner();
        var unsupported = new UnsupportedCandidate();
        var composition = NovaGuiNextPublishingComposition.Create(new RuntimeFactory(old, unsupported));
        await composition.Commands.RequestStartAsync();
        await composition.Commands.RequestInterruptAsync();
        await composition.Commands.RequestStartAsync();
        Assert.Equal(1, composition.Status.Read().Generation);
        Assert.False(composition.Commands.ReadCommands().CanStart);
        Assert.Equal(0, unsupported.Starts);
        Assert.Equal(1, unsupported.Disposals);
        await Assert.ThrowsAsync<InvalidOperationException>(() => composition.DisposeAsync().AsTask());
        Assert.Equal(1, unsupported.Disposals);
    }

    private sealed class RuntimeFactory(params INovaObsPublishingRuntime[] owners) : INovaObsPublishingRuntimeFactory
    {
        private int _index;
        public INovaObsPublishingRuntime Create() => owners[_index++];
    }

    private sealed class UnsupportedCandidate : INovaObsPublishingRuntime
    {
        internal int Starts;
        internal int Disposals;
        public ObsPublishStatusSnapshot Snapshot { get; } = new(new ChannelId(Guid.NewGuid()), new ChannelSessionId(Guid.NewGuid()),
            1, ChannelSessionState.Created, false, false, false, null, string.Empty, null, null, null, null);
        public void Start(CancellationToken token) => Starts++;
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private sealed class Factory(params Owner[] owners) : INovaObsPublishingRuntimeFactory
    {
        internal List<PublishingSettingsSnapshot> SettingsSnapshots { get; } = [];
        public INovaObsPublishingRuntime Create(PublishingSettingsSnapshot settings)
        {
            SettingsSnapshots.Add(settings);
            return Create();
        }
        internal Owner[] Owners { get; } = owners;
        internal int Creates;
        internal bool BlockReplacement;
        internal bool FailReplacement;
        internal Action? BeforeReplacementReturn;
        internal TaskCompletionSource CreateEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseCreate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public INovaObsPublishingRuntime Create()
        {
            var index = Interlocked.Increment(ref Creates) - 1;
            if (index > 0)
            {
                CreateEntered.TrySetResult();
                if (BlockReplacement) ReleaseCreate.Task.GetAwaiter().GetResult();
                if (FailReplacement) throw new InvalidOperationException("SECRET factory");
            }
            if (index > 0) BeforeReplacementReturn?.Invoke();
            return Owners[index];
        }
    }

    private sealed class Owner : INovaObsPublishingLifecycleRuntime
    {
        internal ObsPublishStatusSnapshot Current = new(new ChannelId(Guid.NewGuid()), new ChannelSessionId(Guid.NewGuid()),
            1, ChannelSessionState.Created, false, false, false, "rtmp://SECRET", "SECRET-key", "http://SECRET", null, null, null);
        internal int Starts;
        internal int Disposals;
        internal bool BlockStart;
        internal bool BlockDispose;
        internal bool HoldCompletion;
        internal bool FailDispose;
        internal bool FailStart;
        internal bool ShutdownProven = true;
        internal bool FaultCompletion;
        internal bool CancelCompletion;
        internal bool NullResult;
        internal bool NullCompletion;
        internal bool ThrowCompletion;
        internal Action? ReadSnapshot;
        internal TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource DisposeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseDispose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<ObsPublishChannelResult> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ObsPublishChannelResult> Completion => ThrowCompletion ? throw new InvalidOperationException("SECRET getter") : NullCompletion ? null! : Completed.Task;
        public ObsPublishStatusSnapshot Snapshot { get { ReadSnapshot?.Invoke(); return Current; } }
        public void Start(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Starts);
            StartEntered.TrySetResult();
            if (BlockStart) ReleaseStart.Task.GetAwaiter().GetResult();
            if (FailStart) throw new InvalidOperationException("SECRET start");
            Current = Current with { Revision = 2, State = ChannelSessionState.Negotiating, ListenerReady = true };
        }
        internal void End()
        {
            Current = Current with { Revision = 3, State = ChannelSessionState.Stopped, EndReason = ObsPublishChannelEndReason.HostCancelled };
            Completed.TrySetResult(new(ObsPublishChannelEndReason.HostCancelled, 0, 0, 0, 0,
                PcpRelayDrainOutcome.Completed, true, ShutdownProven, new(ObsRtmpIngestEndReason.Cancelled, 0, 0), null));
        }
        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref Disposals);
            DisposeEntered.TrySetResult();
            if (BlockDispose) await ReleaseDispose.Task;
            if (FailDispose) throw new InvalidOperationException("SECRET cleanup");
            if (FaultCompletion) Completed.TrySetException(new InvalidOperationException("SECRET result"));
            else if (CancelCompletion) Completed.TrySetCanceled();
            else if (NullResult) Completed.TrySetResult(null!);
            else if (!HoldCompletion && !Completed.Task.IsCompleted) End();
        }
    }
}
