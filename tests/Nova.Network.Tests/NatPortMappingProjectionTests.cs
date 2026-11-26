using PeerCast.Nova.Network.PortMapping;

namespace PeerCast.Nova.Network.Tests;

public sealed class NatPortMappingProjectionTests
{
    [Fact]
    public async Task OffWithListener_HasNoMappingAndDefaultDisabled()
    {
        await using var test = new NatPortMappingTestHarness(enabled: false);
        using var owner = test.Acquire();
        await test.Manager.ReconcileAsync();
        var display = Read(test);
        Assert.False(display.Enabled);
        Assert.Equal(NatDisplayState.Disabled, display.State);
        Assert.Equal([7144], display.RegisteredInternalPorts);
        Assert.Equal(0, test.Router.Count);
    }

    [Fact]
    public async Task RapidOffOn_LateMapRemainsObservationAndSatisfiesCurrentDemand()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        var turn = test.Manager.ReconcileAsync();
        var map = await test.Router.NextAsync();
        Assert.Equal(NatDisplayState.Mapping, Read(test).State);
        test.Manager.SetEnabled(false);
        test.Manager.SetEnabled(true);
        map.Succeed(42000);
        await turn;
        var display = Read(test);
        Assert.True(display.Enabled);
        Assert.Equal(NatDisplayState.Mapped, display.State);
        Assert.Equal(42000, display.ExternalPort);
        Assert.Contains(display.Diagnostics, item => item.Code == "StaleObservation");
        Assert.Equal(1, test.Router.Count);
    }

    [Fact]
    public async Task Mapped_DoesNotClaimReachabilityOrExternalAddress()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        await test.MapAsync();
        var display = Read(test);
        Assert.Equal("NAT-PMP", display.Backend);
        Assert.Equal(7144, display.InternalPort);
        Assert.Equal(NatDisplayState.Mapped, display.State);
        Assert.Equal(NatDisplayReachability.NotChecked, display.ExternalReachability);
        Assert.Null(display.ExternalAddress);
        Assert.Contains(display.Diagnostics, item => item.Code == "MappingEstablished");
    }

    [Fact]
    public async Task Timeout_DoesNotClaimFailureOrConfirmedLease()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        var turn = test.Manager.ReconcileAsync();
        var map = await test.Router.NextAsync();
        map.ApplyRouterSide(43000);
        test.Clock.Advance(TimeSpan.FromSeconds(3));
        await turn;
        var display = Read(test);
        Assert.Equal(NatDisplayState.ResultUnknown, display.State);
        Assert.Equal(NatDisplayReachability.NotChecked, display.ExternalReachability);
        Assert.Null(display.ExternalPort);
        Assert.Null(display.Lease!.ConfirmedLifetimeSeconds);
        Assert.Equal(NatLeaseEvidenceKind.RequestedEstimate, display.LeaseEvidence);
        Assert.Contains(display.Diagnostics, item => item.Code == "MapResultUnknown");
        Assert.Contains(display.Diagnostics, item => item.Code == "ResponseTimeout");
    }

    [Fact]
    public async Task ReturnedRemappedPortAndShortLease_AreDisplayedAsRouterEvidence()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        await test.MapAsync(44000, 10);
        var display = Read(test);
        Assert.Equal(44000, display.ExternalPort);
        Assert.Equal(120u, display.Lease!.RequestedLifetimeSeconds);
        Assert.Equal(10u, display.Lease.ConfirmedLifetimeSeconds);
        Assert.Equal(test.Clock.GetUtcNow().AddSeconds(10), display.Lease.EstimatedExpiresAt);
        Assert.Equal(NatLeaseEvidenceKind.RouterConfirmedWithEstimatedExpiry, display.LeaseEvidence);
        Assert.Contains(display.Diagnostics, item => item.Code == "ExternalPortRemapped");
        Assert.Contains(display.Diagnostics, item => item.Code == "LifetimeShortened");
    }

    [Fact]
    public async Task NewEpochImmediately_HidesOldMappedPortAndLeaseEvenBeforeReconcile()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        await test.MapAsync(45000);
        test.Manager.UpdateNetwork(NatPortMappingTestHarness.Home with { NetworkEpoch = 2 });
        var display = Read(test);
        Assert.NotEqual(NatDisplayState.Mapped, display.State);
        Assert.Null(display.ExternalPort);
        Assert.Null(display.Lease);
        Assert.DoesNotContain(display.Diagnostics, item => item.Code == "MappingEstablished");
        Assert.Contains(display.Diagnostics, item => item.Code == "StaleObservation");
        Assert.Contains(display.Diagnostics, item => item.Code == "NetworkChanged");
        Assert.All(display.Diagnostics.Where(item => item.Code == "MappingObserved"),
            item => Assert.True(item.Historical));
    }

    [Fact]
    public async Task OffUnmapTimeout_RemainsVisibleAsUnconfirmedCleanup()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        await test.MapAsync();
        test.Manager.SetEnabled(false);
        var turn = test.Manager.ReconcileAsync();
        var unmap = await test.Router.NextAsync();
        Assert.Equal(NatDisplayState.Unmapping, Read(test).State);
        test.Clock.Advance(TimeSpan.FromSeconds(3));
        await turn;
        var display = Read(test);
        Assert.False(display.Enabled);
        Assert.Equal(NatDisplayState.UnmapUnconfirmed, display.State);
        Assert.Equal(NatDisplayReachability.NotChecked, display.ExternalReachability);
        Assert.Contains(display.Diagnostics, item => item.Code == "UnmapUnconfirmed");
        unmap.Succeed();
        await test.Manager.WaitForObservationAsync(unmap.Request.OperationId);
        Assert.Equal(NatDisplayState.Disabled, Read(test).State);
        Assert.Null(Read(test).ExternalPort);
    }

    [Fact]
    public async Task ReadProjection_DoesNotChangeGenerationSendOperationsOrMutateSnapshot()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        await test.MapAsync();
        var before = test.Manager.ReadState();
        for (var iteration = 0; iteration < 100; iteration++)
        {
            _ = Read(test);
        }
        var after = test.Manager.ReadState();
        Assert.Equal(before.Generation, after.Generation);
        Assert.Equal(before.Mappings, after.Mappings);
        Assert.Equal(before.Diagnostics, after.Diagnostics);
        Assert.Equal(1, test.Router.Count);
    }

    [Fact]
    public async Task HeadlessFoundation_RenewProgressAndUnknownAreProjectedWithoutGui()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        await test.MapAsync();
        test.Clock.Advance(TimeSpan.FromSeconds(60));
        var turn = test.Manager.ReconcileAsync();
        _ = await test.Router.NextAsync();
        Assert.Equal(NatDisplayState.Renewing, Read(test).State);
        Assert.Contains(Read(test).Diagnostics, item => item.Code == "RenewInProgress");
        test.Clock.Advance(TimeSpan.FromSeconds(3));
        await turn;
        Assert.Equal(NatDisplayState.ResultUnknown, Read(test).State);
        Assert.Contains(Read(test).Diagnostics, item => item.Code == "RenewResultUnknown");
    }

    [Fact]
    public async Task MissingRoute_IsDiscoveringNotMappingFailure()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        test.Manager.UpdateNetwork(null);
        var display = Read(test);
        Assert.Equal(NatDisplayState.Discovering, display.State);
        Assert.Contains(display.Diagnostics, item => item.Code == "RouterNotFound");
        Assert.DoesNotContain(display.Diagnostics, item => item.Code == "DiscoveryTimeout");
    }

    [Fact]
    public async Task RouterRejection_IsSeparateFromUnknown()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        var turn = test.Manager.ReconcileAsync();
        var map = await test.Router.NextAsync();
        map.Completion.SetResult(new NatBackendResult(NatBackendOutcome.Rejected, DetailCode: "NatPmpResult2"));
        await turn;
        Assert.Equal(NatDisplayState.Rejected, Read(test).State);
        Assert.Contains(Read(test).Diagnostics, item => item.Code == "RouterRejected");
        Assert.Equal(NatDisplayReachability.NotChecked, Read(test).ExternalReachability);
    }

    private static NatPortMappingDisplay Read(NatPortMappingTestHarness test) =>
        NatPortMappingStatusProjection.Create(test.Manager.ReadState());

    [Fact]
    public async Task DiscoveryTimeoutObservation_IsDistinctFromMappingTimeoutAndRouterRefusal()
    {
        await using var test = new NatPortMappingTestHarness();
        test.Manager.UpdateNetwork(null);
        var source = test.Manager.ReadState();
        var diagnostic = new NatMappingDiagnostic(source.CapturedAt, "DiscoveryTimeout",
            source.Generation, null, null, false, false, 0, null);
        var display = NatPortMappingStatusProjection.Create(source with { Diagnostics = [diagnostic] });
        Assert.Equal(NatDisplayState.Discovering, display.State);
        Assert.Contains(display.Diagnostics, item => item.Code == "DiscoveryTimeout");
        Assert.DoesNotContain(display.Diagnostics, item => item.Code == "ResponseTimeout");
        Assert.DoesNotContain(display.Diagnostics, item => item.Code == "RouterRejected");
    }

    [Fact]
    public async Task ExternalReachabilityAndPublicAddress_ProjectToNatDisplay()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire(7144);
        await test.MapAsync(7144, lifetime: 120);

        var initial = Read(test);
        Assert.Equal(NatDisplayReachability.NotChecked, initial.ExternalReachability);
        Assert.Null(initial.ExternalAddress);

        Assert.True(test.Manager.ReportExternalReachability(7144, NatExternalReachability.Reachable, "198.41.0.4"));
        var reachable = Read(test);
        Assert.Equal(NatDisplayReachability.Reachable, reachable.ExternalReachability);
        Assert.Equal("198.41.0.4", reachable.ExternalAddress);

        Assert.True(test.Manager.ReportExternalReachability(7144, NatExternalReachability.Unreachable));
        var unreachable = Read(test);
        Assert.Equal(NatDisplayReachability.Unreachable, unreachable.ExternalReachability);
        Assert.Equal("198.41.0.4", unreachable.ExternalAddress);
    }
}
