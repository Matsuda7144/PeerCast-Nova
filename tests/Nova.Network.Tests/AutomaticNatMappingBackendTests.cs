using System.Net;
using PeerCast.Nova.Network.PortMapping;

namespace PeerCast.Nova.Network.Tests;

public sealed class AutomaticNatMappingBackendTests
{
    [Fact]
    public async Task UnknownNatPmpMap_PinsProtocolThroughRenewAndNetworkChanges()
    {
        var router = new FakeNatRouter();
        var discoveries = 0;
        var backend = new AutomaticNatMappingBackend((_, _) =>
        {
            discoveries++;
            return Task.FromResult<INatMappingBackend?>(router);
        });
        var request = UpnpIgdMappingBackendTests.Request();
        var pending = backend.ExecuteAsync(request, default);
        (await router.NextAsync()).Unknown();
        Assert.Equal(NatBackendOutcome.ResultUnknown, (await pending).Outcome);
        pending = backend.ExecuteAsync(request with { Operation = NatMappingOperation.Renew }, default);
        (await router.NextAsync()).Unknown();
        await pending;
        var changed = request with { Identity = request.Identity with { Network = request.Identity.Network with { NetworkEpoch = 2 } } };
        Assert.Equal("PreviousNetworkMappingUnconfirmed", (await backend.ExecuteAsync(changed, default)).DetailCode);
        Assert.Equal(1, discoveries);
        Assert.Equal(2, router.Count);
    }

    [Fact]
    public async Task ConfirmedRemoval_ReleasesProtocolSelection()
    {
        var router = new FakeNatRouter { AutoComplete = true };
        var discoveries = 0;
        var backend = new AutomaticNatMappingBackend((_, _) =>
        {
            discoveries++;
            return Task.FromResult<INatMappingBackend?>(router);
        });
        var request = UpnpIgdMappingBackendTests.Request();
        await backend.ExecuteAsync(request, default);
        await backend.ExecuteAsync(UpnpIgdMappingBackendTests.Delete(request), default);
        await backend.ExecuteAsync(request, default);
        Assert.Equal(2, discoveries);
        Assert.Equal(3, router.Count);
    }

    [Fact]
    public async Task NoRouter_IsNotSentAndCanBeRediscovered()
    {
        var backend = new AutomaticNatMappingBackend((_, _) => Task.FromResult<INatMappingBackend?>(null));
        var result = await backend.ExecuteAsync(UpnpIgdMappingBackendTests.Request(), default);
        Assert.Equal(NatBackendOutcome.NotSent, result.Outcome);
        Assert.Equal("RouterNotFound", result.DetailCode);
        var manager = new NatPortMappingManager(backend, new() { Enabled = true });
        manager.UpdateNetwork(NatPortMappingTestHarness.Home);
        using var owner = manager.AcquirePublicTcpListener(new(IPAddress.Any, 7144));
        await manager.ReconcileAsync();
        Assert.Equal(NatDisplayState.RouterNotFound, NatPortMappingStatusProjection.Create(manager.ReadState()).State);
        Assert.Equal(NatShutdownOutcome.Completed, await manager.ShutdownAsync());
    }

    [Fact]
    public async Task PermanentLease_ManagerHasNoExpiryAndVerifiesOnMonotonicSchedule()
    {
        var router = new FakeUpnpGateway { PermanentOnly = true };
        var backend = new UpnpIgdMappingBackend(NatPortMappingTestHarness.Home, router);
        var clock = new ManualNatTimeProvider();
        var manager = Manager(backend, clock);
        using var owner = manager.AcquirePublicTcpListener(new(IPAddress.Any, 7144));
        await manager.ReconcileAsync();
        var lease = Assert.Single(manager.Snapshot).Lease;
        Assert.NotNull(lease);
        Assert.Equal(NatLeaseKind.Permanent, lease.Kind);
        Assert.Null(lease.EstimatedExpiresAt);
        Assert.Equal(NatLeaseEvidenceKind.RouterConfirmedPermanent, NatPortMappingStatusProjection.Create(manager.ReadState()).LeaseEvidence);
        router.Entry = null; // Router lost the mapping, without changing the route.
        clock.ShiftWallClock(TimeSpan.FromDays(-1));
        clock.Advance(TimeSpan.FromSeconds(61));
        await manager.ReconcileAsync();
        Assert.Equal(3, router.Adds);
        Assert.NotNull(router.Entry);
        manager.SetEnabled(false);
        await manager.ReconcileAsync();
        Assert.Null(router.Entry);
        Assert.Equal(NatShutdownOutcome.Completed, await manager.ShutdownAsync());
    }

    [Fact]
    public async Task LatePermanentAdd_OffOnRaceRetainsOwnershipAndFinalListenerReleaseCleansUp()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var router = new FakeUpnpGateway { PermanentOnly = true, AfterAdd = _ => { entered.TrySetResult(); return release.Task; } };
        var backend = new AutomaticNatMappingBackend((_, _) => Task.FromResult<INatMappingBackend?>(
            new UpnpIgdMappingBackend(NatPortMappingTestHarness.Home, router)));
        var clock = new ManualNatTimeProvider();
        var manager = Manager(backend, clock);
        var owner = manager.AcquirePublicTcpListener(new(IPAddress.Any, 7144));
        var turn = manager.ReconcileAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var operation = manager.Diagnostics.Last(item => item.Event == "OperationDispatched").Request!.OperationId;
        manager.SetEnabled(false);
        clock.Advance(TimeSpan.FromSeconds(4));
        await turn.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(NatMappingStatus.UnmapUnconfirmed, Assert.Single(manager.Snapshot).Status);
        Assert.Null(Assert.Single(manager.Snapshot).Lease?.EstimatedExpiresAt);
        manager.SetEnabled(true);
        release.SetResult();
        await manager.WaitForObservationAsync(operation).WaitAsync(TimeSpan.FromSeconds(10));
        await manager.ReconcileAsync();
        Assert.Equal(NatMappingStatus.Mapped, Assert.Single(manager.Snapshot).Status);
        Assert.Contains(manager.Diagnostics, item => item.Event == "OperationObserved" && item.StaleGeneration);
        Assert.Equal(2, router.Adds); // finite rejection + one permanent Add; no second mapping.
        owner.Dispose();
        await manager.ReconcileAsync();
        Assert.Equal(1, router.Deletes);
        Assert.Null(router.Entry);
        Assert.Equal(NatShutdownOutcome.Completed, await manager.ShutdownAsync());
    }

    [Fact]
    public async Task LateAddAfterPortChange_IsCleanedBeforeNewMapping()
    {
        // The existing manager's port transition is exercised with the same
        // selector and exact UPnP receipt, rather than a second control loop.
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var router = new FakeUpnpGateway { AfterAdd = _ => { entered.TrySetResult(); return release.Task; } };
        var backend = new AutomaticNatMappingBackend((_, _) => Task.FromResult<INatMappingBackend?>(new UpnpIgdMappingBackend(NatPortMappingTestHarness.Home, router)));
        var manager = Manager(backend, new ManualNatTimeProvider());
        var owner = manager.AcquirePublicTcpListener(new(IPAddress.Any, 7144));
        var turn = manager.ReconcileAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        owner.Dispose();
        using var next = manager.AcquirePublicTcpListener(new(IPAddress.Any, 7145));
        router.AfterAdd = null;
        release.SetResult();
        await turn.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(router.Entry);
        Assert.Equal(1, router.Deletes);
        Assert.True(router.Entries.ContainsKey(7145));
        Assert.Equal(new[] { ("AddPortMapping", 7144), ("DeletePortMapping", 7144), ("AddPortMapping", 7145) },
            router.History.Where(item => item.Action != "GetSpecificPortMappingEntry"));
        Assert.Equal(NatShutdownOutcome.Completed, await manager.ShutdownAsync());
        Assert.Empty(router.Entries);
    }

    [Fact]
    public async Task MissingEntryAfterUnknownPermanentAdd_DoesNotProveCleanupOrFiniteExpiry()
    {
        var router = new FakeUpnpGateway { PermanentOnly = true, AfterAdd = _ => throw new OperationCanceledException() };
        var backend = new UpnpIgdMappingBackend(NatPortMappingTestHarness.Home, router);
        var clock = new ManualNatTimeProvider();
        var manager = Manager(backend, clock);
        using var owner = manager.AcquirePublicTcpListener(new(IPAddress.Any, 7144));
        await manager.ReconcileAsync();
        var lease = Assert.Single(manager.Snapshot).Lease;
        Assert.NotNull(lease);
        Assert.Equal(NatLeaseKind.Unknown, lease.Kind);
        Assert.Null(lease.EstimatedExpiresAt);
        router.Entry = null;
        clock.Advance(TimeSpan.FromSeconds(6));
        await manager.ReconcileAsync();
        Assert.Equal(2, router.Adds); // No additional Add behind an unresolved request.
        var shutdown = manager.ShutdownAsync();
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(NatShutdownOutcome.CleanupUnconfirmed, await shutdown.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, router.Deletes);
    }

    private static NatPortMappingManager Manager(INatMappingBackend backend, ManualNatTimeProvider clock)
    {
        var manager = new NatPortMappingManager(backend, new() { Enabled = true }, clock, new NatDeviceOperationSerializer());
        manager.UpdateNetwork(NatPortMappingTestHarness.Home);
        return manager;
    }
}
