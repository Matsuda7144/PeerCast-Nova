using System.Net;
using PeerCast.Nova.Network.PortMapping;

namespace PeerCast.Nova.Network.Tests;

public sealed class NatPortMappingManagerTests
{
    [Fact]
    public async Task OffThenOn_StaleMapSuccessIsAdoptedForCurrentOwnersWithoutDelete()
    {
        await using var test = new NatPortMappingTestHarness();
        using var originalOwner = test.Acquire();
        var turn = test.Manager.ReconcileAsync();
        var map = await test.Router.NextAsync();
        test.Manager.SetEnabled(false);
        originalOwner.Dispose();
        using var replacementOwner = test.Acquire();
        test.Manager.SetEnabled(true);
        map.Succeed(42000);
        await turn;

        var snapshot = Assert.Single(test.Manager.Snapshot);
        Assert.Equal(NatMappingStatus.Mapped, snapshot.Status);
        Assert.Equal(42000, snapshot.ReturnedExternalPort);
        Assert.Equal(1, snapshot.OwnerCount);
        Assert.Equal(1, test.Router.Count);
        Assert.Contains(test.Manager.Diagnostics, item => item.Event == "OperationObserved" &&
            item.StaleGeneration && item.CurrentNetwork && item.CurrentOwnerCount == 1);
    }

    [Fact]
    public async Task UnmapTimeout_LateRouterDeleteCompletesBeforeReplacementMap()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        await test.MapAsync();
        test.Manager.SetEnabled(false);
        var offTurn = test.Manager.ReconcileAsync();
        var unmap = await test.Router.NextAsync();
        Assert.Equal(NatMappingOperation.Unmap, unmap.Request.Operation);
        test.Clock.Advance(TimeSpan.FromSeconds(3));
        await offTurn;
        Assert.Equal(NatMappingStatus.UnmapUnconfirmed, Assert.Single(test.Manager.Snapshot).Status);

        test.Manager.SetEnabled(true);
        await test.Manager.ReconcileAsync();
        Assert.Equal(2, test.Router.Count); // No new send while old delete can still finish.
        unmap.Succeed();
        var repair = await test.Router.NextAsync();
        Assert.Equal(NatMappingOperation.Map, repair.Request.Operation);
        Assert.False(test.Router.Contains(repair.Request.Identity));
        repair.Succeed(43000);
        await test.Manager.WaitForObservationAsync(repair.Request.OperationId).WaitAsync(TimeSpan.FromSeconds(10));
        await test.Manager.ReconcileAsync();

        Assert.Equal(1, test.Router.MaximumActive);
        Assert.Equal(43000, test.Router.ExternalPort(repair.Request.Identity));
        Assert.Equal(NatMappingStatus.Mapped, Assert.Single(test.Manager.Snapshot).Status);
        Assert.Contains(test.Manager.Diagnostics, item => item.Event == "OperationObserved" &&
            item.Request?.Operation == NatMappingOperation.Unmap && item.StaleGeneration);
    }

    [Fact]
    public async Task MapTimeout_RouterSideSuccessRemainsUnknownUntilConfirmedRetry()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        var turn = test.Manager.ReconcileAsync();
        var map = await test.Router.NextAsync();
        map.ApplyRouterSide(44000, 30);
        test.Clock.Advance(TimeSpan.FromSeconds(3));
        await turn;
        var unknown = Assert.Single(test.Manager.Snapshot);
        Assert.True(test.Router.Contains(map.Request.Identity));
        Assert.Equal(NatMappingStatus.ResultUnknown, unknown.Status);
        Assert.True(unknown.OperationPending);
        Assert.Equal(120u, unknown.Lease!.RequestedLifetimeSeconds);
        Assert.Null(unknown.Lease.ConfirmedLifetimeSeconds);

        map.Unknown(); // Final backend result still cannot prove whether the send succeeded.
        await test.Manager.WaitForObservationAsync(map.Request.OperationId).WaitAsync(TimeSpan.FromSeconds(10));
        await test.Manager.ReconcileAsync();
        test.Clock.Advance(TimeSpan.FromSeconds(5));
        var retryTurn = test.Manager.ReconcileAsync();
        var retry = await test.Router.NextAsync();
        Assert.Equal(NatMappingOperation.Renew, retry.Request.Operation);
        retry.Succeed(44000, 30);
        await retryTurn;
        var confirmed = Assert.Single(test.Manager.Snapshot);
        Assert.Equal(44000, confirmed.ReturnedExternalPort);
        Assert.Equal(30u, confirmed.Lease!.ConfirmedLifetimeSeconds);
        Assert.Equal(NatExternalReachability.Unknown, confirmed.ExternalReachability);
    }

    [Theory]
    [InlineData("epoch")]
    [InlineData("gateway")]
    [InlineData("interface")]
    [InlineData("address")]
    public async Task OldNetworkResponse_IsRetainedButNeverAdoptedOrDeletedOnNewRoute(string change)
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        var turn = test.Manager.ReconcileAsync();
        var oldMap = await test.Router.NextAsync();
        var home = NatPortMappingTestHarness.Home;
        var next = change switch
        {
            "gateway" => home with { Device = home.Device with { GatewayAddress = "192.168.1.254" } },
            "interface" => home with { Device = home.Device with { InterfaceId = "replacement-interface" } },
            "address" => home with { InternalAddress = "192.168.1.3" },
            _ => home with { NetworkEpoch = 2 },
        };
        test.Manager.UpdateNetwork(next);
        oldMap.Succeed(45000);
        var currentMap = await test.Router.NextAsync();
        Assert.Equal(next, currentMap.Request.Identity.Network);
        Assert.Equal(NatMappingOperation.Map, currentMap.Request.Operation);
        currentMap.Succeed(45001);
        await turn;

        var old = Assert.Single(test.Manager.Snapshot, item => item.Identity.Network == home);
        var current = Assert.Single(test.Manager.Snapshot, item => item.Identity.Network == next);
        Assert.Equal(NatMappingStatus.UnmapUnconfirmed, old.Status);
        Assert.Equal(0, old.OwnerCount);
        Assert.Equal(45000, old.ReturnedExternalPort);
        Assert.Equal(NatMappingStatus.Mapped, current.Status);
        Assert.DoesNotContain(test.Router.History, request => request.Operation == NatMappingOperation.Unmap);
        Assert.Contains(test.Manager.Diagnostics, item => item.Event == "OperationObserved" && !item.CurrentNetwork);
    }

    [Fact]
    public async Task SharedListener_PartialStopKeepsMappingUntilFinalOwnerReleases()
    {
        await using var test = new NatPortMappingTestHarness();
        using var first = test.Acquire();
        using var second = test.Acquire();
        await test.MapAsync();
        Assert.Equal(2, Assert.Single(test.Manager.Snapshot).OwnerCount);
        first.Dispose();
        await test.Manager.ReconcileAsync();
        Assert.Equal(1, test.Router.Count);
        Assert.Equal(1, Assert.Single(test.Manager.Snapshot).OwnerCount);
        Assert.Equal(NatMappingStatus.Mapped, Assert.Single(test.Manager.Snapshot).Status);

        second.Dispose();
        var turn = test.Manager.ReconcileAsync();
        var unmap = await test.Router.NextAsync();
        Assert.Equal(NatMappingOperation.Unmap, unmap.Request.Operation);
        unmap.Succeed();
        await turn;
        Assert.False(Assert.Single(test.Manager.Snapshot).RouterMayContainMapping);
    }

    [Fact]
    public async Task ExternalPortRemap_RenewUsesReturnedPortAndStoresNewRouterResult()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        await test.MapAsync(46000);
        test.Clock.Advance(TimeSpan.FromSeconds(60));
        var turn = test.Manager.ReconcileAsync();
        var renew = await test.Router.NextAsync();
        Assert.Equal(NatMappingOperation.Renew, renew.Request.Operation);
        Assert.Equal(46000, renew.Request.SuggestedExternalPort);
        renew.Succeed(46001);
        await turn;
        Assert.Equal(46001, Assert.Single(test.Manager.Snapshot).ReturnedExternalPort);
    }

    [Fact]
    public async Task ShortenedLifetime_RenewIsScheduledFromConfirmedLifetime()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        await test.MapAsync(lifetime: 10);
        var lease = Assert.Single(test.Manager.Snapshot).Lease!;
        Assert.Equal(120u, lease.RequestedLifetimeSeconds);
        Assert.Equal(10u, lease.ConfirmedLifetimeSeconds);
        Assert.Equal(test.Clock.GetUtcNow().AddSeconds(10), lease.EstimatedExpiresAt);
        test.Clock.Advance(TimeSpan.FromSeconds(4));
        await test.Manager.ReconcileAsync();
        Assert.Equal(1, test.Router.Count);
        test.Clock.Advance(TimeSpan.FromSeconds(1));
        var turn = test.Manager.ReconcileAsync();
        var renew = await test.Router.NextAsync();
        Assert.Equal(NatMappingOperation.Renew, renew.Request.Operation);
        renew.Succeed(lifetime: 6);
        await turn;
        Assert.Equal(6u, Assert.Single(test.Manager.Snapshot).Lease!.ConfirmedLifetimeSeconds);
    }

    [Fact]
    public async Task RenewDuringOff_LateSuccessIsFollowedByCurrentDesiredDelete()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        await test.MapAsync(lifetime: 10);
        test.Clock.Advance(TimeSpan.FromSeconds(5));
        var turn = test.Manager.ReconcileAsync();
        var renew = await test.Router.NextAsync();
        Assert.Equal(NatMappingOperation.Renew, renew.Request.Operation);
        test.Manager.SetEnabled(false);
        renew.Succeed(lifetime: 10);
        var unmap = await test.Router.NextAsync();
        Assert.Equal(NatMappingOperation.Unmap, unmap.Request.Operation);
        unmap.Succeed();
        await turn;
        Assert.Equal(NatMappingStatus.Unmapped, Assert.Single(test.Manager.Snapshot).Status);
        Assert.False(test.Router.Contains(renew.Request.Identity));
        Assert.Equal(3, test.Router.Count);
    }

    [Fact]
    public async Task ShutdownCleanupTimeout_IsBoundedAndRetainsLateDeleteObservation()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        await test.MapAsync();
        var shutdown = test.Manager.ShutdownAsync();
        var unmap = await test.Router.NextAsync();
        test.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(NatShutdownOutcome.CleanupUnconfirmed, await shutdown);
        Assert.Equal(NatMappingStatus.UnmapUnconfirmed, Assert.Single(test.Manager.Snapshot).Status);
        Assert.True(test.Router.Contains(unmap.Request.Identity));
        unmap.Succeed();
        await test.Manager.WaitForObservationAsync(unmap.Request.OperationId).WaitAsync(TimeSpan.FromSeconds(10));
        await test.Manager.ReconcileAsync();
        Assert.Equal(NatMappingStatus.Unmapped, Assert.Single(test.Manager.Snapshot).Status);
        Assert.Equal(2, test.Router.Count);
        Assert.Contains(test.Manager.Diagnostics, item => item.Event == "ShutdownCleanupUnconfirmed");
        Assert.Contains(test.Manager.Diagnostics, item => item.Event == "OperationObserved" &&
            item.Request?.Operation == NatMappingOperation.Unmap && item.Result?.Outcome == NatBackendOutcome.Success);
    }

    [Fact]
    public async Task LostDeleteResponseAndDelayedRouterDelete_PeriodicRenewRepairsUnknownState()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        await test.MapAsync(lifetime: 10);
        test.Manager.SetEnabled(false);
        var off = test.Manager.ReconcileAsync();
        var delete = await test.Router.NextAsync();
        delete.Unknown(); // Backend has ceased sending; router may still apply the datagram.
        await off;
        test.Manager.SetEnabled(true);
        var on = test.Manager.ReconcileAsync();
        var recreate = await test.Router.NextAsync();
        recreate.Succeed(lifetime: 10);
        await on;
        test.Router.DeleteWithoutResponse(recreate.Request.Identity);
        Assert.False(test.Router.Contains(recreate.Request.Identity));
        // NAT-PMP has no read/query or causality token: reachability remains unknown.
        Assert.Equal(NatExternalReachability.Unknown, Assert.Single(test.Manager.Snapshot).ExternalReachability);
        test.Clock.Advance(TimeSpan.FromSeconds(5));
        var repairTurn = test.Manager.ReconcileAsync();
        var repair = await test.Router.NextAsync();
        repair.Succeed(lifetime: 10);
        await repairTurn;
        Assert.True(test.Router.Contains(repair.Request.Identity));
    }

    [Fact]
    public async Task OffSetting_DoesNotDispatchUntilOnAndLoopbackCannotBeRegistered()
    {
        await using var test = new NatPortMappingTestHarness(enabled: false);
        using var owner = test.Acquire();
        await test.Manager.ReconcileAsync();
        Assert.Equal(0, test.Router.Count);
        Assert.Throws<ArgumentException>(() => test.Manager.AcquirePublicTcpListener(new IPEndPoint(IPAddress.Loopback, 7144)));
        test.Manager.SetEnabled(true);
        await test.MapAsync();
    }

    [Fact]
    public async Task TimedOutOldEpochOperation_KeepsDeviceLaneUntilActualCompletion()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        var oldTurn = test.Manager.ReconcileAsync();
        var oldMap = await test.Router.NextAsync();
        test.Clock.Advance(TimeSpan.FromSeconds(3));
        await oldTurn;
        test.Manager.UpdateNetwork(NatPortMappingTestHarness.Home with { NetworkEpoch = 2 });
        var newTurn = test.Manager.ReconcileAsync();
        Assert.Equal(1, test.Router.Count);
        oldMap.Succeed(47000);
        var current = await test.Router.NextAsync();
        Assert.Equal(2, current.Request.Identity.Network.NetworkEpoch);
        current.Succeed(47001);
        await newTurn;
        Assert.Equal(1, test.Router.MaximumActive);
        Assert.Equal(2, test.Router.Count);
        Assert.DoesNotContain(test.Router.History, request => request.Operation == NatMappingOperation.Unmap);
    }

    [Fact]
    public async Task ShutdownWhileMapPending_LateSuccessIsRetainedWithoutSendsAfterCleanupDeadline()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        var mapping = test.Manager.ReconcileAsync();
        var map = await test.Router.NextAsync();
        var shutdown = test.Manager.ShutdownAsync();
        test.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(NatShutdownOutcome.CleanupUnconfirmed, await shutdown);
        map.Succeed(48000);
        await mapping;
        var snapshot = Assert.Single(test.Manager.Snapshot);
        Assert.Equal(NatMappingStatus.UnmapUnconfirmed, snapshot.Status);
        Assert.Equal(48000, snapshot.ReturnedExternalPort);
        Assert.Equal(0, snapshot.OwnerCount);
        Assert.Equal(1, test.Router.Count);
        Assert.True(test.Router.Contains(map.Request.Identity));
    }

    [Fact]
    public async Task WallClockMovingBackward_DoesNotPostponeFiniteLeaseRenewal()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        await test.MapAsync(lifetime: 10);
        test.Clock.ShiftWallClock(TimeSpan.FromHours(-1));
        test.Clock.Advance(TimeSpan.FromSeconds(5));
        var turn = test.Manager.ReconcileAsync();
        var renew = await test.Router.NextAsync();
        Assert.Equal(NatMappingOperation.Renew, renew.Request.Operation);
        renew.Succeed(lifetime: 10);
        await turn;
    }

    [Fact]
    public async Task RouterEpochReset_ReconcilesOtherActiveMappingsBeforeTheirLeaseRenewal()
    {
        await using var test = new NatPortMappingTestHarness();
        using var first = test.Acquire(7144);
        using var second = test.Acquire(7145);
        var mapping = test.Manager.ReconcileAsync();
        var firstMap = await test.Router.NextAsync();
        firstMap.Succeed(7144, 10, routerEpoch: 100);
        var secondMap = await test.Router.NextAsync();
        secondMap.Succeed(7145, 120, routerEpoch: 100);
        await mapping;

        test.Clock.Advance(TimeSpan.FromSeconds(5));
        var renewal = test.Manager.ReconcileAsync();
        var firstRenew = await test.Router.NextAsync();
        firstRenew.Succeed(7144, 10, routerEpoch: 10);
        var recovery = await test.Router.NextAsync();
        Assert.Equal(7145, recovery.Request.Identity.InternalPort);
        Assert.Equal(NatMappingOperation.Renew, recovery.Request.Operation);
        recovery.Succeed(7145, 120, routerEpoch: 11);
        await renewal;
        Assert.Contains(test.Manager.Diagnostics, item => item.Event == "RouterEpochRegressed");
        Assert.All(test.Manager.Snapshot, snapshot => Assert.Equal(NatMappingStatus.Mapped, snapshot.Status));
    }

    [Fact]
    public async Task HundredStartStopCycles_AlwaysEndWithNoRouterMapping()
    {
        await using var test = new NatPortMappingTestHarness();
        test.Router.AutoComplete = true;
        for (var cycle = 0; cycle < 100; cycle++)
        {
            using var owner = test.Acquire();
            await test.Manager.ReconcileAsync();
            Assert.Equal(NatMappingStatus.Mapped, Assert.Single(test.Manager.Snapshot).Status);
            owner.Dispose();
            await test.Manager.ReconcileAsync();
            Assert.Equal(NatMappingStatus.Unmapped, Assert.Single(test.Manager.Snapshot).Status);
        }

        Assert.Equal(200, test.Router.Count);
        Assert.Equal(1, test.Router.MaximumActive);
    }

    [Fact]
    public async Task ReportExternalReachability_UpdatesSnapshotAndResetsOnUnmap()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire(7144);
        await test.MapAsync(7144, lifetime: 120);

        Assert.True(test.Manager.ReportExternalReachability(7144, NatExternalReachability.Reachable, "198.41.0.4"));
        var snapshot = Assert.Single(test.Manager.Snapshot);
        Assert.Equal(NatExternalReachability.Reachable, snapshot.ExternalReachability);
        Assert.Equal("198.41.0.4", snapshot.ExternalAddress);

        Assert.True(test.Manager.ReportExternalReachability(7144, NatExternalReachability.Unreachable));
        snapshot = Assert.Single(test.Manager.Snapshot);
        Assert.Equal(NatExternalReachability.Unreachable, snapshot.ExternalReachability);
        Assert.Equal("198.41.0.4", snapshot.ExternalAddress);

        Assert.False(test.Manager.ReportExternalReachability(9999, NatExternalReachability.Reachable, "198.41.0.5"));

        owner.Dispose();
        var unmapTurn = test.Manager.ReconcileAsync();
        var unmap = await test.Router.NextAsync();
        Assert.Equal(NatMappingOperation.Unmap, unmap.Request.Operation);
        unmap.Succeed(7144, 0);
        await unmapTurn;

        snapshot = Assert.Single(test.Manager.Snapshot);
        Assert.Equal(NatExternalReachability.Unknown, snapshot.ExternalReachability);
        Assert.Null(snapshot.ExternalAddress);
    }
}
