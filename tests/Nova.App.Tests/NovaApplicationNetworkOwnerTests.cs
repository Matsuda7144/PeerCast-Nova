using System.Collections.Concurrent;
using PeerCast.Nova.Foundation.Configuration;
using PeerCast.Nova.Network.PortMapping;
using PeerCast.Nova.Network.Tests;

namespace PeerCast.Nova.App.Tests;

public sealed class NovaApplicationNetworkOwnerTests
{
    [Fact]
    public async Task SharedPublicListenerBorrowsOnlyInitializedManagerAndJoinsNetworkShutdown()
    {
        await using var test = new Fixture();
        using var port = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        port.Start();
        var settings = new GenesisGuiNextConnectionPreferences
        { WanRelayEnabled = true, ListenPort = ((System.Net.IPEndPoint)port.LocalEndpoint).Port };
        port.Stop();
        Assert.Throws<GenesisPublicRelayStartException>(() => test.Owner.GetSharedRelay(settings, (_, _) => null));
        await test.Owner.InitializeAsync();
        var host = test.Owner.GetSharedRelay(settings, (_, _) => null);
        Assert.Empty(test.Runtime!.Manager.ReadState().RegisteredInternalPorts);
        Assert.Equal(1, test.RuntimeCreates);
        var id = new PeerCast.Nova.Core.Channels.ChannelId(Guid.NewGuid());
        await using var fanout = new PeerCast.Nova.Network.Pcp.PcpRelayFanout();
        await using var lease = await host.AcquireAsync(routes => routes.Register(id, fanout), CancellationToken.None);
        Assert.Equal(settings.ListenPort, Assert.Single(test.Runtime.Manager.ReadState().RegisteredInternalPorts));
        Assert.Equal(NovaApplicationPublicListenerAvailability.Listening, test.Owner.ReadSnapshot().PublicListenerAvailability);
        Assert.Contains("TCP待受中", GuiNextNetworkSettingsPresentation.Create(test.Owner.ReadSnapshot()).IntegratedSettings);
        var request = new NovaPublicRelayRequest(id, new(Guid.NewGuid()), $"pcp://example.invalid:7144/channel/{id}");
        Assert.Equal(NovaPublicRelayFailure.IncompatibleIdentity, (await test.Owner.AcquirePublicRelayAsync(request)).Failure);
        var disposal = test.Owner.DisposeAsync().AsTask();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => host.AcquireAsync(r => r.Register(new(Guid.NewGuid()), fanout), CancellationToken.None));
        await disposal;
        Assert.Equal(1, test.Runtime.Disposals);
        Assert.Empty(test.Runtime.Manager.ReadState().RegisteredInternalPorts);
        Assert.Equal(GenesisSharedPublicRelayState.Disposed, host.Snapshot.State);
        Assert.True(test.Owner.ReadSnapshot().Disposed);
        Assert.Equal(0, test.Router.Count);
    }

    [Fact]
    public async Task ConstructorAndUninitializedReadsAreInert()
    {
        await using var test = new Fixture();
        var first = test.Owner.ReadSnapshot();
        Assert.Equal(first, test.Owner.ReadSnapshot());
        Assert.False(first.Initialized);
        Assert.False(first.Initializing);
        Assert.False(first.Disposing);
        Assert.False(first.Disposed);
        Assert.Equal(0, first.Revision);
        Assert.Null(first.RuntimeDesiredEnabled);
        Assert.Null(first.SavedAutoPortMapping);
        Assert.Null(first.Nat);
        Assert.Null(first.LastPersistenceResult);
        Assert.Equal(NovaApplicationPublicListenerAvailability.Unavailable, first.PublicListenerAvailability);
        Assert.Equal(0, test.EnvironmentReads);
        Assert.Equal(0, test.RuntimeCreates);
        Assert.Equal(0, test.ControlCreates);
        Assert.Equal(0, test.Network.Reads);
        Assert.Equal(0, test.Router.Count);
        Assert.Empty(Directory.GetFiles(test.Root));
    }

    [Theory]
    [InlineData(null, false, false, false)]
    [InlineData(null, true, true, false)]
    [InlineData("", true, true, false)]
    [InlineData("  ", true, true, false)]
    [InlineData("on", false, true, true)]
    [InlineData("off", true, false, true)]
    [InlineData(" ON ", false, true, true)]
    [InlineData(" OFF ", true, false, true)]
    public async Task StartupUsesSavedSettingAndExistingEnvironmentParser(string? environment, bool saved, bool effective, bool overridden)
    {
        await using var test = new Fixture { EnvironmentValue = environment };
        await test.Store.SaveAsync(new NovaOptions { AutoPortMapping = saved });
        var bytes = await File.ReadAllBytesAsync(test.SettingsPath);
        Assert.True((await test.Owner.InitializeAsync()).Initialized);
        var view = test.Owner.ReadSnapshot();
        Assert.True(view.Initialized);
        Assert.False(view.Initializing);
        Assert.Equal(effective, view.RuntimeDesiredEnabled);
        Assert.Equal(saved, view.SavedAutoPortMapping);
        Assert.Equal(overridden, view.StartupEnvironmentOverride);
        Assert.Equal(overridden ? effective : (bool?)null, view.StartupEnvironmentEnabled);
        Assert.Equal(effective, view.NextStartEffectiveEnabled);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(test.SettingsPath));
        Assert.Equal(1, test.RuntimeCreates);
        Assert.Equal(1, test.ControlCreates);
        Assert.Equal(1, test.EnvironmentReads);
        Assert.Equal(0, test.Router.Count);
    }

    [Fact]
    public async Task MissingSettingsDefaultOffAndInitializationDoesNotWrite()
    {
        await using var test = new Fixture();
        await test.Owner.InitializeAsync();
        Assert.False(test.Owner.ReadSnapshot().RuntimeDesiredEnabled);
        Assert.False(test.Owner.ReadSnapshot().SavedAutoPortMapping);
        Assert.Equal(NatDisplayState.Disabled, test.Owner.ReadSnapshot().Nat!.State);
        Assert.False(File.Exists(test.SettingsPath));
        Assert.Equal(0, test.Router.Count);
    }

    [Fact]
    public async Task InitializeLoadsLatestDocumentRatherThanConstructionSnapshot()
    {
        await using var test = new Fixture();
        await test.Store.SaveAsync(new NovaOptions { AutoPortMapping = true });
        await test.Owner.InitializeAsync();
        Assert.True(test.Owner.ReadSnapshot().RuntimeDesiredEnabled);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("unexpected-private-value")]
    public async Task InvalidEnvironmentFailsClosedWithoutRuntimeOrWrites(string environment)
    {
        await using var test = new Fixture { EnvironmentValue = environment };
        var task = test.Owner.InitializeAsync();
        Assert.Same(task, test.Owner.InitializeAsync());
        var result = await task;
        Assert.False(result.Initialized);
        Assert.Equal(NovaApplicationNetworkFailure.InvalidEnvironment, result.Failure);
        Assert.Equal(result.Failure, test.Owner.ReadSnapshot().SafeFailure);
        Assert.Equal(0, test.RuntimeCreates);
        Assert.Equal(0, test.ControlCreates);
        Assert.False(File.Exists(test.SettingsPath));
        Assert.DoesNotContain(environment, test.Owner.ReadSnapshot().ToString(), StringComparison.Ordinal);
        Assert.Equal(NovaApplicationNetworkPersistenceStatus.Rejected, (await test.Owner.SetAutoPortMappingAsync(true)).Status);
    }

    [Theory]
    [InlineData("invalid-json")]
    [InlineData("null")]
    public async Task CorruptSettingsFailWithoutOverwritingOrCreatingRuntime(string payload)
    {
        await using var test = new Fixture();
        await File.WriteAllTextAsync(test.SettingsPath, payload);
        var result = await test.Owner.InitializeAsync();
        Assert.Equal(NovaApplicationNetworkFailure.SettingsLoadFailed, result.Failure);
        Assert.Equal(payload, await File.ReadAllTextAsync(test.SettingsPath));
        Assert.Equal(0, test.EnvironmentReads);
        Assert.Equal(0, test.RuntimeCreates);
    }

    [Fact]
    public async Task ConcurrentInitializeJoinsSingleFactoryAndControl()
    {
        await using var test = new Fixture { HoldRuntimeFactory = true };
        var first = test.Owner.InitializeAsync();
        try
        {
            await Await(test.FactoryEntered.Task);
            Assert.Same(first, test.Owner.InitializeAsync());
            Assert.True(test.Owner.ReadSnapshot().Initializing);
            Assert.False(test.Owner.ReadSnapshot().Initialized);
            Assert.Equal(NovaApplicationNetworkFailure.NotInitialized, (await test.Owner.SetAutoPortMappingAsync(true)).Failure);
            Assert.Equal(0, test.Owner.ReadSnapshot().LastAcceptedRequestId);
        }
        finally { test.FactoryRelease.TrySetResult(); }
        Assert.True((await Await(first)).Initialized);
        Assert.Same(first, test.Owner.InitializeAsync());
        Assert.Equal(1, test.RuntimeCreates);
        Assert.Equal(1, test.ControlCreates);
    }

    [Fact]
    public async Task OnWithoutPublicListenerNeverCreatesMappingDemand()
    {
        await using var test = new Fixture();
        await test.Owner.InitializeAsync();
        var before = test.Owner.ReadSnapshot();
        await test.Owner.SetAutoPortMappingAsync(true);
        test.Clock.Advance(TimeSpan.FromSeconds(30));
        var view = test.Owner.ReadSnapshot();
        Assert.True(view.RuntimeDesiredEnabled);
        Assert.True(view.SavedAutoPortMapping);
        Assert.True(view.Revision > before.Revision);
        Assert.Equal(NatDisplayState.Idle, view.Nat!.State);
        Assert.Empty(view.Nat.RegisteredInternalPorts);
        Assert.Null(view.Nat.ExternalPort);
        Assert.Null(view.Nat.ExternalAddress);
        Assert.Equal(NatDisplayReachability.NotChecked, view.Nat.ExternalReachability);
        Assert.Equal(NovaApplicationPublicListenerAvailability.Unavailable, view.PublicListenerAvailability);
        Assert.Equal(0, test.Router.Count);
    }

    [Fact]
    public async Task ReadDoesNotChangeRevisionOrAllowCollectionMutation()
    {
        await using var test = new Fixture();
        await test.Owner.InitializeAsync();
        var view = test.Owner.ReadSnapshot();
        Assert.Equal(view.Revision, test.Owner.ReadSnapshot().Revision);
        Assert.Equal(view.Nat!.Generation, test.Owner.ReadSnapshot().Nat!.Generation);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)view.Nat.RegisteredInternalPorts).Add(7144));
        Assert.Throws<NotSupportedException>(() => ((IList<NatDisplayDiagnostic>)view.Nat.Diagnostics).Clear());
        Assert.False(File.Exists(test.SettingsPath));
        Assert.Equal(0, test.Router.Count);
    }

    [Fact]
    public async Task FieldOnlySavePreservesLatestRecordingDirectoryAndAllOtherKnownFields()
    {
        await using var test = new Fixture();
        await test.Owner.InitializeAsync();
        var latest = new NovaOptions
        {
            NodeName = "latest-node",
            ListenPort = 24888,
            EnableDetailedDiagnostics = true,
            RecordingOutputDirectory = Path.Combine(test.Root, "recordings"),
        };
        await new NovaSettingsStore(test.SettingsPath).SaveAsync(latest);
        var result = await test.Owner.SetAutoPortMappingAsync(true);
        Assert.Equal(NovaApplicationNetworkPersistenceStatus.Saved, result.Status);
        Assert.Equal(latest with { AutoPortMapping = true }, test.Store.Load());
        Assert.Single(Directory.GetFiles(test.Root));
        Assert.False(Directory.Exists(latest.RecordingOutputDirectory));
    }

    [Fact]
    public async Task RapidChangesPublishDesiredInAdmissionOrderAndExposePendingSaves()
    {
        await using var test = new Fixture { HoldSaveCompletions = true };
        await test.Owner.InitializeAsync();
        var off = test.Owner.SetAutoPortMappingAsync(false);
        Assert.False(test.Owner.ReadSnapshot().RuntimeDesiredEnabled);
        var on = test.Owner.SetAutoPortMappingAsync(true);
        var view = test.Owner.ReadSnapshot();
        Assert.True(view.RuntimeDesiredEnabled);
        Assert.False(view.SavedAutoPortMapping);
        Assert.Equal(2, view.PendingSaveCount);
        Assert.Equal(2, view.LastAcceptedRequestId);
        Assert.Equal(NovaApplicationNetworkPersistenceStatus.Pending, view.LastPersistenceResult!.Status);
        var calls = test.Control!.Calls;
        try
        {
            await Await(Task.WhenAll(calls.Select(call => call.Persisted.Task)));
            Assert.True(test.Store.Load().AutoPortMapping);
            Assert.False(off.IsCompleted);
            Assert.False(on.IsCompleted);
        }
        finally { foreach (var call in calls) call.Release.TrySetResult(); }
        await Await(Task.WhenAll(off, on));
        Assert.Equal(0, test.Owner.ReadSnapshot().PendingSaveCount);
        Assert.Equal(2, test.Owner.ReadSnapshot().LastPersistedRequestId);
    }

    [Fact]
    public async Task LateOlderCompletionCannotReplaceLatestSavedValueOrResult()
    {
        await using var test = new Fixture { HoldSaveCompletions = true };
        await test.Owner.InitializeAsync();
        var older = test.Owner.SetAutoPortMappingAsync(false);
        var newer = test.Owner.SetAutoPortMappingAsync(true);
        var calls = test.Control!.Calls;
        try
        {
            await Await(Task.WhenAll(calls.Select(call => call.Persisted.Task)));
            calls[1].Release.TrySetResult();
            await Await(newer);
            var beforeOlder = test.Owner.ReadSnapshot();
            Assert.Equal(1, beforeOlder.PendingSaveCount);
            Assert.True(beforeOlder.SavedAutoPortMapping);
            calls[0].Release.TrySetResult();
            await Await(older);
            var final = test.Owner.ReadSnapshot();
            Assert.True(final.Revision > beforeOlder.Revision);
            Assert.True(final.RuntimeDesiredEnabled);
            Assert.True(final.SavedAutoPortMapping);
            Assert.Equal(2, final.LastPersistedRequestId);
            Assert.Equal(2, final.LastPersistenceResult!.RequestId);
            Assert.Equal(0, final.PendingSaveCount);
        }
        finally { foreach (var call in calls) call.Release.TrySetResult(); }
    }

    [Theory]
    [InlineData("on", true, false)]
    [InlineData("off", false, true)]
    public async Task RuntimeChangesDoNotOverwriteNextStartupEnvironmentOverride(string environment, bool startup, bool requested)
    {
        await using var test = new Fixture { EnvironmentValue = environment };
        await test.Owner.InitializeAsync();
        Assert.Equal(startup, test.Owner.ReadSnapshot().RuntimeDesiredEnabled);
        await test.Owner.SetAutoPortMappingAsync(requested);
        var view = test.Owner.ReadSnapshot();
        Assert.Equal(requested, view.RuntimeDesiredEnabled);
        Assert.Equal(requested, view.SavedAutoPortMapping);
        Assert.Equal(startup, view.NextStartEffectiveEnabled);
        Assert.Equal(startup, view.StartupEnvironmentEnabled);
        Assert.Equal(1, test.EnvironmentReads);
    }

    [Fact]
    public async Task SaveFailureLeavesDesiredAppliedAndDoesNotExposeRawData()
    {
        await using var test = new Fixture();
        await test.Owner.InitializeAsync();
        await File.WriteAllTextAsync(test.SettingsPath, "private-corrupt-document");
        var result = await test.Owner.SetAutoPortMappingAsync(true);
        var view = test.Owner.ReadSnapshot();
        Assert.Equal(NovaApplicationNetworkPersistenceStatus.Failed, result.Status);
        Assert.Equal(NovaApplicationNetworkFailure.PersistenceFailed, result.Failure);
        Assert.True(view.RuntimeDesiredEnabled);
        Assert.False(view.SavedAutoPortMapping);
        Assert.False(view.NextStartEffectiveEnabled);
        Assert.Equal(0, view.PendingSaveCount);
        Assert.Equal(0, view.LastPersistedRequestId);
        Assert.Equal(result, view.LastPersistenceResult);
        Assert.Equal(result.Failure, view.SafeFailure);
        Assert.Equal("private-corrupt-document", await File.ReadAllTextAsync(test.SettingsPath));
        Assert.DoesNotContain(test.Root, view.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-corrupt-document", view.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, test.Router.Count);
    }

    [Fact]
    public async Task SaveCanRecoverAfterFailureWithoutRetryingInitialization()
    {
        await using var test = new Fixture();
        await test.Owner.InitializeAsync();
        Directory.CreateDirectory(test.SettingsPath);
        Assert.Equal(NovaApplicationNetworkPersistenceStatus.Failed, (await test.Owner.SetAutoPortMappingAsync(true)).Status);
        Directory.Delete(test.SettingsPath);
        Assert.Equal(NovaApplicationNetworkPersistenceStatus.Saved, (await test.Owner.SetAutoPortMappingAsync(false)).Status);
        Assert.False(test.Owner.ReadSnapshot().SavedAutoPortMapping);
        Assert.Equal(NovaApplicationNetworkFailure.None, test.Owner.ReadSnapshot().SafeFailure);
        Assert.Equal(1, test.RuntimeCreates);
    }

    [Fact]
    public async Task PendingSavePreservesConcurrentOtherWriterLatestDocument()
    {
        await using var test = new Fixture();
        await test.Owner.InitializeAsync();
        var entered = Signal();
        var release = Signal();
        var path = Path.Combine(test.Root, "new-recordings");
        var other = Task.Run(() => new NovaSettingsStore(test.SettingsPath).UpdateAsync(current =>
        {
            entered.TrySetResult();
            release.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            return current with { RecordingOutputDirectory = path, NodeName = "other-writer" };
        }));
        Task<NovaApplicationNetworkPersistenceResult>? save = null;
        try
        {
            await Await(entered.Task);
            save = test.Owner.SetAutoPortMappingAsync(true);
            Assert.Equal(1, test.Owner.ReadSnapshot().PendingSaveCount);
            Assert.True(test.Owner.ReadSnapshot().RuntimeDesiredEnabled);
            Assert.False(save.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await Await(other);
            if (save is not null) await Await(save);
        }
        Assert.Equal(path, test.Store.Load().RecordingOutputDirectory);
        Assert.Equal("other-writer", test.Store.Load().NodeName);
        Assert.True(test.Store.Load().AutoPortMapping);
    }

    [Fact]
    public async Task DisposeClosesAdmissionAndDrainsSaveBeforeRuntimeThenControl()
    {
        await using var test = new Fixture { HoldSaveCompletions = true };
        await test.Owner.InitializeAsync();
        var save = test.Owner.SetAutoPortMappingAsync(true);
        var call = Assert.Single(test.Control!.Calls);
        var disposal = test.Owner.DisposeAsync().AsTask();
        try
        {
            Assert.True(test.Owner.ReadSnapshot().Disposing);
            Assert.Equal(NovaApplicationNetworkFailure.Closing, (await test.Owner.SetAutoPortMappingAsync(false)).Failure);
            Assert.Same(disposal, test.Owner.DisposeAsync().AsTask());
            Assert.Equal(0, test.Runtime!.Disposals);
            Assert.Equal(0, test.Control.Disposals);
            Assert.False(disposal.IsCompleted);
        }
        finally { call.Release.TrySetResult(); }
        await Await(save);
        await Await(disposal);
        Assert.Equal(1, test.Runtime!.Disposals);
        Assert.Equal(1, test.Control.Disposals);
        Assert.Collection(test.CleanupOrder,
            value => Assert.Equal("runtime", value),
            value => Assert.Equal("control", value));
        var final = test.Owner.ReadSnapshot();
        Assert.True(final.Disposed);
        Assert.False(final.Disposing);
        Assert.Equal(0, final.PendingSaveCount);
        Assert.Equal(NatShutdownOutcome.Completed, final.ShutdownOutcome);
        Assert.False(final.RuntimeDesiredEnabled);
        Assert.True(final.SavedAutoPortMapping);
        await test.Owner.DisposeAsync();
        Assert.Equal(1, test.Runtime.Disposals);
    }

    [Fact]
    public async Task AllDisposeCallersJoinActualRuntimeCleanup()
    {
        await using var test = new Fixture { HoldRuntimeDisposal = true };
        await test.Owner.InitializeAsync();
        var first = test.Owner.DisposeAsync().AsTask();
        try
        {
            await Await(test.Runtime!.DisposalEntered.Task);
            var second = test.Owner.DisposeAsync().AsTask();
            Assert.Same(first, second);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.False(test.Owner.ReadSnapshot().Disposed);
            Assert.Equal(0, test.Control!.Disposals);
        }
        finally { test.Runtime!.DisposalRelease.TrySetResult(); }
        await Await(first);
        Assert.Equal(1, test.Runtime!.Disposals);
    }

    [Fact]
    public async Task DisposeBeforeInitializeDoesNotCreateOrLoadAnything()
    {
        await using var test = new Fixture();
        await test.Owner.DisposeAsync();
        Assert.Equal(NovaApplicationNetworkFailure.Closing, (await test.Owner.InitializeAsync()).Failure);
        Assert.Equal(0, test.RuntimeCreates);
        Assert.Equal(0, test.ControlCreates);
        Assert.Equal(0, test.EnvironmentReads);
        Assert.Null(test.Owner.ReadSnapshot().Nat);
        Assert.Empty(Directory.GetFiles(test.Root));
    }

    [Fact]
    public async Task InitializeAfterSuccessfulShutdownRejectsRatherThanReusingOldSuccess()
    {
        await using var test = new Fixture();
        Assert.True((await test.Owner.InitializeAsync()).Initialized);
        await test.Owner.DisposeAsync();
        var result = await test.Owner.InitializeAsync();
        Assert.False(result.Initialized);
        Assert.Equal(NovaApplicationNetworkFailure.Closing, result.Failure);
        Assert.Equal(1, test.RuntimeCreates);
        Assert.Equal(1, test.Runtime!.Disposals);
    }

    [Fact]
    public async Task DisposeDuringFactoryCleansUnpublishedResourcesAndJoinsInitialization()
    {
        await using var test = new Fixture { HoldRuntimeFactory = true };
        var initialization = test.Owner.InitializeAsync();
        Task? disposal = null;
        try
        {
            await Await(test.FactoryEntered.Task);
            disposal = test.Owner.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
        }
        finally { test.FactoryRelease.TrySetResult(); }
        Assert.Equal(NovaApplicationNetworkFailure.Closing, (await Await(initialization)).Failure);
        await Await(disposal!);
        Assert.False(test.Owner.ReadSnapshot().Initialized);
        Assert.Null(test.Owner.ReadSnapshot().Nat);
        Assert.Equal(1, test.Runtime!.Disposals);
        Assert.Equal(1, test.Control!.Disposals);
    }

    [Fact]
    public async Task DisposeDuringConfigurationReadPreventsRuntimeCreation()
    {
        await using var test = new Fixture { HoldEnvironmentRead = true };
        var initialization = test.Owner.InitializeAsync();
        Task? disposal = null;
        try
        {
            await Await(test.EnvironmentEntered.Task);
            disposal = test.Owner.DisposeAsync().AsTask();
        }
        finally { test.EnvironmentRelease.TrySetResult(); }
        Assert.Equal(NovaApplicationNetworkFailure.Closing, (await Await(initialization)).Failure);
        await Await(disposal!);
        Assert.Equal(0, test.RuntimeCreates);
        Assert.Equal(NovaApplicationNetworkFailure.None, test.Owner.ReadSnapshot().InitializationFailure);
    }

    [Fact]
    public async Task PartialInitializationFailureDisposesRuntimeAndDoesNotRetry()
    {
        await using var test = new Fixture { FailControlFactory = true };
        var task = test.Owner.InitializeAsync();
        var result = await task;
        Assert.Equal(NovaApplicationNetworkFailure.ControlInitializationFailed, result.Failure);
        Assert.Equal(1, test.Runtime!.Disposals);
        Assert.Null(test.Owner.ReadSnapshot().Nat);
        Assert.Same(task, test.Owner.InitializeAsync());
        await test.Owner.DisposeAsync();
        Assert.Equal(1, test.Runtime.Disposals);
        Assert.Equal(0, test.Router.Count);
    }

    [Fact]
    public async Task RuntimeFactoryFailureDoesNotConstructControl()
    {
        await using var test = new Fixture { FailRuntimeFactory = true };
        Assert.Equal(NovaApplicationNetworkFailure.RuntimeInitializationFailed, (await test.Owner.InitializeAsync()).Failure);
        Assert.Equal(0, test.ControlCreates);
        Assert.False(test.Owner.ReadSnapshot().Initialized);
        await test.Owner.DisposeAsync();
    }

    [Fact]
    public async Task ShutdownFailureIsTypedInSnapshotAndFaultsSharedDisposeTask()
    {
        await using var test = new Fixture { FailRuntimeDisposal = true };
        await test.Owner.InitializeAsync();
        var first = test.Owner.DisposeAsync().AsTask();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        Assert.Same(first, test.Owner.DisposeAsync().AsTask());
        Assert.Equal(NovaApplicationNetworkFailure.ShutdownFailed, test.Owner.ReadSnapshot().SafeFailure);
        Assert.True(test.Owner.ReadSnapshot().Disposed);
        Assert.Equal(1, test.Control!.Disposals);
        Assert.DoesNotContain("private-runtime-error", failure.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(test.Root, failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializationCleanupFailureIsNotReportedAsSuccessfulDisposal()
    {
        await using var test = new Fixture { FailRuntimeDisposal = true, FailControlFactory = true };
        Assert.Equal(NovaApplicationNetworkFailure.InitializationCleanupFailed, (await test.Owner.InitializeAsync()).Failure);
        await Assert.ThrowsAsync<InvalidOperationException>(() => test.Owner.DisposeAsync().AsTask());
        Assert.Equal(1, test.Runtime!.Disposals);
        Assert.Equal(NovaApplicationNetworkFailure.ShutdownFailed, test.Owner.ReadSnapshot().ShutdownFailure);
    }

    [Fact]
    public async Task SynchronousControlFailureDoesNotLeakPendingSaveOrBlockShutdown()
    {
        await using var test = new Fixture { FailControlSynchronously = true };
        await test.Owner.InitializeAsync();
        var result = await test.Owner.SetAutoPortMappingAsync(true);
        Assert.Equal(NovaApplicationNetworkPersistenceStatus.Failed, result.Status);
        Assert.Equal(0, test.Owner.ReadSnapshot().PendingSaveCount);
        await Await(test.Owner.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task ProductionForwardingAdaptersUseExistingRuntimeAndFieldSave()
    {
        await using var test = new Fixture(useProductionAdapters: true);
        Assert.True((await test.Owner.InitializeAsync()).Initialized);
        Assert.Equal(NovaApplicationNetworkPersistenceStatus.Saved, (await test.Owner.SetAutoPortMappingAsync(true)).Status);
        Assert.True(test.Store.Load().AutoPortMapping);
        await test.Owner.DisposeAsync();
        Assert.Equal(NatShutdownOutcome.Completed, test.Owner.ReadSnapshot().ShutdownOutcome);
        Assert.True(test.Network.Reads > 0);
        Assert.Equal(0, test.Router.Count);
    }

    [Fact]
    public async Task FailedAcceptedSaveStillDrainsAndAllowsRuntimeCleanup()
    {
        await using var test = new Fixture();
        await test.Owner.InitializeAsync();
        await File.WriteAllTextAsync(test.SettingsPath, "corrupt-after-initialization");
        var save = test.Owner.SetAutoPortMappingAsync(true);
        var disposal = test.Owner.DisposeAsync().AsTask();
        Assert.Equal(NovaApplicationNetworkPersistenceStatus.Failed, (await Await(save)).Status);
        await Await(disposal);
        Assert.Equal(1, test.Runtime!.Disposals);
        Assert.Equal(1, test.Control!.Disposals);
        Assert.Equal(NovaApplicationNetworkFailure.PersistenceFailed, test.Owner.ReadSnapshot().SafeFailure);
        Assert.Equal(NovaApplicationNetworkFailure.None, test.Owner.ReadSnapshot().ShutdownFailure);
    }

    [Fact]
    public async Task UnconfirmedRuntimeCleanupIsNotReportedAsSuccessfulShutdown()
    {
        await using var test = new Fixture { ReportUnconfirmedCleanup = true };
        await test.Owner.InitializeAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => test.Owner.DisposeAsync().AsTask());
        Assert.Equal(NatShutdownOutcome.CleanupUnconfirmed, test.Owner.ReadSnapshot().ShutdownOutcome);
        Assert.Equal(NovaApplicationNetworkFailure.ShutdownCleanupUnconfirmed, test.Owner.ReadSnapshot().SafeFailure);
        Assert.Equal(0, test.Router.Count);
    }

    [Fact]
    public async Task ControlDisposalFailureStillFinalizesStateAndDoesNotLeakRawException()
    {
        await using var test = new Fixture { FailControlDisposal = true };
        await test.Owner.InitializeAsync();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => test.Owner.DisposeAsync().AsTask());
        Assert.Equal(NovaApplicationNetworkFailure.ShutdownFailed, test.Owner.ReadSnapshot().SafeFailure);
        Assert.True(test.Owner.ReadSnapshot().Disposed);
        Assert.False(test.Owner.ReadSnapshot().Disposing);
        Assert.Equal(1, test.Runtime!.Disposals);
        Assert.Equal(1, test.Control!.Disposals);
        Assert.DoesNotContain("private-control-dispose-error", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnpublishedControlCleanupFailureIsTypedAndFinalized()
    {
        await using var test = new Fixture { HoldRuntimeFactory = true, FailControlDisposal = true };
        var initialization = test.Owner.InitializeAsync();
        Task? disposal = null;
        try
        {
            await Await(test.FactoryEntered.Task);
            disposal = test.Owner.DisposeAsync().AsTask();
        }
        finally { test.FactoryRelease.TrySetResult(); }
        Assert.Equal(NovaApplicationNetworkFailure.InitializationCleanupFailed, (await Await(initialization)).Failure);
        await Assert.ThrowsAsync<InvalidOperationException>(() => disposal!);
        Assert.Equal(1, test.Runtime!.Disposals);
        Assert.Equal(1, test.Control!.Disposals);
        Assert.True(test.Owner.ReadSnapshot().Disposed);
        Assert.Equal(NovaApplicationNetworkFailure.InitializationCleanupFailed, test.Owner.ReadSnapshot().InitializationFailure);
    }

    [Fact]
    public async Task LateOlderFailureCannotOverwriteNewerSuccessfulPersistence()
    {
        await using var test = new Fixture { HoldSaveCompletions = true };
        await test.Owner.InitializeAsync();
        await File.WriteAllTextAsync(test.SettingsPath, "old-corrupt-document");
        var older = test.Owner.SetAutoPortMappingAsync(true);
        var first = Assert.Single(test.Control!.Calls);
        Task<NovaApplicationNetworkPersistenceResult>? newer = null;
        try
        {
            await Await(first.Persisted.Task);
            await test.Store.SaveAsync(new NovaOptions());
            newer = test.Owner.SetAutoPortMappingAsync(false);
            var second = test.Control.Calls[1];
            await Await(second.Persisted.Task);
            second.Release.TrySetResult();
            Assert.Equal(NovaApplicationNetworkPersistenceStatus.Saved, (await Await(newer)).Status);
            first.Release.TrySetResult();
            Assert.Equal(NovaApplicationNetworkPersistenceStatus.Failed, (await Await(older)).Status);
            var final = test.Owner.ReadSnapshot();
            Assert.Equal(2, final.LastPersistenceResult!.RequestId);
            Assert.Equal(NovaApplicationNetworkPersistenceStatus.Saved, final.LastPersistenceResult.Status);
            Assert.Equal(NovaApplicationNetworkFailure.None, final.SafeFailure);
            Assert.Equal(2, final.LastPersistedRequestId);
            Assert.False(final.SavedAutoPortMapping);
            Assert.False(final.RuntimeDesiredEnabled);
        }
        finally
        {
            foreach (var call in test.Control.Calls) call.Release.TrySetResult();
            await Await(older);
            if (newer is not null) await Await(newer);
        }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task Await(Task task) => task.WaitAsync(TimeSpan.FromSeconds(10));
    private static Task<T> Await<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(10));

    private sealed class Fixture : IAsyncDisposable
    {
        public Fixture(bool useProductionAdapters = false)
        {
            Root = Path.Combine(Path.GetTempPath(), "Nova-application-network-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            SettingsPath = Path.Combine(Root, "settings.json");
            Store = new NovaSettingsStore(SettingsPath);
            Owner = new NovaApplicationNetworkOwner(Store, ReadEnvironment,
                useProductionAdapters ? options => new NovaApplicationNatRuntime(new NatPortMappingRuntime(options, Router, Network, Clock)) : CreateRuntime,
                useProductionAdapters ? null : CreateControl);
        }

        public string Root { get; }
        public string SettingsPath { get; }
        public NovaSettingsStore Store { get; }
        public NovaApplicationNetworkOwner Owner { get; }
        public FakeNatRouter Router { get; } = new();
        public ManualNatTimeProvider Clock { get; } = new();
        public FakeNetworkSource Network { get; } = new();
        public ConcurrentQueue<string> CleanupOrder { get; } = new();
        public string? EnvironmentValue { get; init; }
        public bool HoldRuntimeFactory { get; init; }
        public bool HoldEnvironmentRead { get; init; }
        public bool HoldSaveCompletions { get; init; }
        public bool HoldRuntimeDisposal { get; init; }
        public bool FailRuntimeDisposal { get; init; }
        public bool FailRuntimeFactory { get; init; }
        public bool FailControlFactory { get; init; }
        public bool FailControlSynchronously { get; init; }
        public bool FailControlDisposal { get; init; }
        public bool ReportUnconfirmedCleanup { get; init; }
        public int EnvironmentReads { get; private set; }
        public int RuntimeCreates { get; private set; }
        public int ControlCreates { get; private set; }
        public OwnedRuntime? Runtime { get; private set; }
        public ControlledControl? Control { get; private set; }
        public TaskCompletionSource FactoryEntered { get; } = Signal();
        public TaskCompletionSource FactoryRelease { get; } = Signal();
        public TaskCompletionSource EnvironmentEntered { get; } = Signal();
        public TaskCompletionSource EnvironmentRelease { get; } = Signal();

        private string? ReadEnvironment()
        {
            EnvironmentReads++;
            EnvironmentEntered.TrySetResult();
            if (HoldEnvironmentRead) EnvironmentRelease.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            return EnvironmentValue;
        }

        private INovaApplicationNatRuntime CreateRuntime(NatPortMappingOptions options)
        {
            RuntimeCreates++;
            FactoryEntered.TrySetResult();
            if (HoldRuntimeFactory) FactoryRelease.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            if (FailRuntimeFactory) throw new InvalidOperationException("private-factory-error");
            return Runtime = new OwnedRuntime(new NatPortMappingRuntime(options, Router, Network, Clock), CleanupOrder,
                HoldRuntimeDisposal, FailRuntimeDisposal, ReportUnconfirmedCleanup);
        }

        private INovaApplicationPortMappingControl CreateControl(NatPortMappingManager manager, NovaSettingsStore store,
            NovaOptions saved, bool overridden)
        {
            ControlCreates++;
            if (FailControlFactory) throw new InvalidOperationException("private-control-error");
            return Control = new ControlledControl(new NovaPortMappingControl(manager, store, saved, overridden),
                CleanupOrder, HoldSaveCompletions, FailControlSynchronously, FailControlDisposal);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                FactoryRelease.TrySetResult();
                EnvironmentRelease.TrySetResult();
                if (Control is not null) foreach (var call in Control.Calls) call.Release.TrySetResult();
                Runtime?.DisposalRelease.TrySetResult();
                if (!Owner.ReadSnapshot().Disposed) await Owner.DisposeAsync();
            }
            finally { Directory.Delete(Root, recursive: true); }
        }
    }

    private sealed class FakeNetworkSource : INatNetworkSource
    {
        private int reads;
        public int Reads => Volatile.Read(ref reads);
        public NatNetworkIdentity? GetCurrentNetwork()
        {
            Interlocked.Increment(ref reads);
            return NatPortMappingTestHarness.Home;
        }
    }

    private sealed class OwnedRuntime(NatPortMappingRuntime runtime, ConcurrentQueue<string> order,
        bool hold, bool fail, bool reportUnconfirmed) : INovaApplicationNatRuntime
    {
        public NatPortMappingManager Manager => runtime.Manager;
        public NatShutdownOutcome? ShutdownOutcome => runtime.ShutdownOutcome is null ? null :
            reportUnconfirmed ? NatShutdownOutcome.CleanupUnconfirmed : runtime.ShutdownOutcome;
        public int Disposals { get; private set; }
        public TaskCompletionSource DisposalEntered { get; } = Signal();
        public TaskCompletionSource DisposalRelease { get; } = Signal();
        public async ValueTask DisposeAsync()
        {
            Disposals++;
            DisposalEntered.TrySetResult();
            if (hold) await DisposalRelease.Task;
            await runtime.DisposeAsync();
            order.Enqueue("runtime");
            if (fail) throw new InvalidOperationException("private-runtime-error");
        }
    }

    private sealed class ControlledControl(NovaPortMappingControl control, ConcurrentQueue<string> order,
        bool hold, bool failSynchronously, bool failDisposal) : INovaApplicationPortMappingControl
    {
        private readonly ConcurrentQueue<SaveCall> calls = new();
        public SaveCall[] Calls => calls.ToArray();
        public int Disposals { get; private set; }
        public Task SetEnabledAsync(bool enabled)
        {
            if (failSynchronously) throw new InvalidOperationException("private-sync-error");
            var call = new SaveCall(control.SetEnabledAsync(enabled));
            calls.Enqueue(call);
            return CompleteAsync(call);
        }

        private async Task CompleteAsync(SaveCall call)
        {
            Exception? failure = null;
            try { await call.Persistence; }
            catch (Exception exception) { failure = exception; }
            call.Persisted.TrySetResult();
            if (hold) await call.Release.Task;
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        public void Dispose()
        {
            Disposals++;
            order.Enqueue("control");
            control.Dispose();
            if (failDisposal) throw new InvalidOperationException("private-control-dispose-error");
        }
    }

    private sealed class SaveCall(Task persistence)
    {
        public Task Persistence { get; } = persistence;
        public TaskCompletionSource Persisted { get; } = Signal();
        public TaskCompletionSource Release { get; } = Signal();
    }
}
