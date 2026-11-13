using System.Collections.Concurrent;
using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.PortMapping;
using PeerCast.Nova.Network.Tests;

namespace PeerCast.Nova.App.Tests;

public sealed class NovaApplicationPublicRelayTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedWanModeCannotBypassActiveOrCleanupFailedLegacyOwner(bool failCleanup)
    {
        await using var test = new Fixture { FailStop = failCleanup };
        await test.InitializeAsync();
        var lease = (await test.Owner.AcquirePublicRelayAsync(Request)).Lease!;
        var settings = new GenesisGuiNextConnectionPreferences { WanRelayEnabled = true };
        if (failCleanup) Assert.Equal(NovaPublicRelayFailure.CleanupFailed, await lease.ReleaseAsync());
        Assert.Throws<GenesisPublicRelayStartException>(() => test.Owner.GetSharedRelay(settings, (_, _) => null));
        Assert.Null(test.Owner.ReadSnapshot().SharedPublicRelay);
        Assert.Single(test.Drivers);
        if (failCleanup) await Assert.ThrowsAsync<InvalidOperationException>(() => test.Owner.DisposeAsync().AsTask());
        else await lease.ReleaseAsync();
    }

    private static readonly ChannelId Channel = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly ChannelSessionId Session = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    private static NovaPublicRelayRequest Request => new(Channel, Session, $"pcp://example.invalid:7144/channel/{Channel}");

    [Fact]
    public async Task ParallelAcquiresShareOneStartAndOnlyLastReleaseStops()
    {
        await using var test = new Fixture { HoldStart = true };
        await test.InitializeAsync();
        var first = test.Owner.AcquirePublicRelayAsync(Request);
        var second = test.Owner.AcquirePublicRelayAsync(Request);
        await Wait(test.DriverCreated.Task);
        var driver = Assert.Single(test.Drivers);
        await Wait(driver.StartEntered.Task);
        Assert.Equal(2, test.Owner.ReadSnapshot().PublicRelay!.ReferenceCount);
        Assert.Equal(NovaPublicRelayState.Starting, test.Owner.ReadSnapshot().PublicRelay!.State);
        Assert.Empty(test.Runtime!.Manager.ReadState().RegisteredInternalPorts);
        driver.AllowStart.TrySetResult();
        var leases = await Wait(Task.WhenAll(first, second));
        Assert.All(leases, result => Assert.Equal(NovaPublicRelayFailure.None, result.Failure));
        Assert.NotSame(leases[0].Lease, leases[1].Lease);
        Assert.Equal(1, driver.Starts);
        Assert.Single(test.Runtime.Manager.ReadState().RegisteredInternalPorts);
        Assert.Equal(NovaPublicRelayFailure.None, await leases[0].Lease!.ReleaseAsync());
        Assert.Equal(0, driver.Stops);
        Assert.Equal(1, test.Owner.ReadSnapshot().PublicRelay!.ReferenceCount);
        Assert.Single(test.Runtime.Manager.ReadState().RegisteredInternalPorts);
        Assert.Equal(NovaPublicRelayFailure.None, await leases[1].Lease!.ReleaseAsync());
        Assert.Equal(1, driver.Stops);
        Assert.Equal(1, driver.Disposals);
        Assert.Empty(test.Runtime.Manager.ReadState().RegisteredInternalPorts);
        Assert.Equal(0, test.Runtime.Disposals);
        Assert.Equal(NovaPublicRelayState.Stopped, test.Owner.ReadSnapshot().PublicRelay!.State);
    }

    [Fact]
    public async Task RepeatedReleaseJoinsActualStopAndOldLeaseCannotStopReplacement()
    {
        await using var test = new Fixture { HoldStop = true };
        await test.InitializeAsync();
        var first = await test.Owner.AcquirePublicRelayAsync(Request);
        var old = Assert.Single(test.Drivers);
        var release = first.Lease!.ReleaseAsync();
        await Wait(old.StopEntered.Task);
        Assert.Same(release, first.Lease.ReleaseAsync());
        Assert.False(release.IsCompleted);
        var next = test.Owner.AcquirePublicRelayAsync(Request);
        Assert.False(next.IsCompleted);
        Assert.Single(test.Drivers);
        old.AllowStop.TrySetResult();
        await Wait(release);
        var replacement = await Wait(next);
        Assert.Equal(2, test.Drivers.Count);
        Assert.Equal(2, test.Owner.ReadSnapshot().PublicRelay!.Epoch);
        await first.Lease.ReleaseAsync();
        var current = test.Drivers.Last();
        Assert.Equal(0, current.Stops);
        current.AllowStop.TrySetResult();
        await replacement.Lease!.ReleaseAsync();
        Assert.Equal(1, old.Stops);
        Assert.Equal(1, current.Stops);
    }

    [Theory]
    [InlineData("channel")]
    [InlineData("session")]
    [InlineData("host")]
    [InlineData("port")]
    public async Task IncompatibleIdentityIsRejectedWithoutReplacement(string difference)
    {
        await using var test = new Fixture();
        await test.InitializeAsync();
        var lease = (await test.Owner.AcquirePublicRelayAsync(Request)).Lease!;
        var otherChannel = new ChannelId(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var changed = difference switch
        {
            "channel" => Request with { ChannelId = otherChannel, Source = $"pcp://example.invalid:7144/channel/{otherChannel}" },
            "session" => Request with { SessionId = new ChannelSessionId(Guid.NewGuid()) },
            "host" => Request with { Source = $"pcp://other.invalid:7144/channel/{Channel}" },
            _ => Request with { Source = $"pcp://example.invalid:7145/channel/{Channel}" },
        };
        Assert.Equal(NovaPublicRelayFailure.IncompatibleIdentity, (await test.Owner.AcquirePublicRelayAsync(changed)).Failure);
        Assert.Single(test.Drivers);
        Assert.Equal(1, test.Owner.ReadSnapshot().PublicRelay!.ReferenceCount);
        await lease.ReleaseAsync();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("channel")]
    [InlineData("bind")]
    [InlineData("protocol")]
    [InlineData("scope")]
    public async Task InvalidRequestsFailClosed(string difference)
    {
        await using var test = new Fixture();
        await test.InitializeAsync();
        var request = difference switch
        {
            "source" => Request with { Source = "private-malformed-source" },
            "channel" => Request with { ChannelId = new ChannelId(Guid.NewGuid()) },
            "bind" => Request with { BindPolicy = (NovaPublicRelayBindPolicy)42 },
            "protocol" => Request with { Protocol = (NovaPublicRelayProtocol)42 },
            _ => Request with { Scope = (NovaPublicRelayScope)42 },
        };
        Assert.Equal(NovaPublicRelayFailure.InvalidRequest, (await test.Owner.AcquirePublicRelayAsync(request)).Failure);
        Assert.Empty(test.Drivers);
        Assert.Empty(test.Runtime!.Manager.ReadState().RegisteredInternalPorts);
    }

    [Fact]
    public async Task CanonicalHostCaseSharesButSessionAndRuntimeIdentityArePreserved()
    {
        await using var test = new Fixture();
        await test.InitializeAsync();
        var first = await test.Owner.AcquirePublicRelayAsync(Request);
        var second = await test.Owner.AcquirePublicRelayAsync(Request with { Source = Request.Source.Replace("example", "EXAMPLE", StringComparison.Ordinal) });
        var driver = Assert.Single(test.Drivers);
        Assert.Equal(Channel, driver.Identity!.ChannelId);
        Assert.Equal(Session, driver.Identity.SessionId);
        Assert.Equal(driver.Borrowed.OwnerId, driver.Identity.RuntimeOwnerId);
        Assert.Same(test.Runtime!.Manager, driver.Borrowed.Manager);
        Assert.Equal(1, test.RuntimeCreates);
        await first.Lease!.ReleaseAsync();
        await second.Lease!.ReleaseAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NatSettingAndListenerStartAreIndependent(bool enabled)
    {
        await using var test = new Fixture();
        await test.InitializeAsync();
        await test.Owner.SetAutoPortMappingAsync(enabled);
        Assert.Empty(test.Drivers);
        Assert.Equal(0, test.Router.Count);
        var result = await test.Owner.AcquirePublicRelayAsync(Request);
        var driver = Assert.Single(test.Drivers);
        Assert.Contains(driver.Port, test.Runtime!.Manager.ReadState().RegisteredInternalPorts);
        Assert.Equal(NovaApplicationPublicListenerAvailability.Listening, test.Owner.ReadSnapshot().PublicListenerAvailability);
        Assert.Equal(NatDisplayReachability.NotChecked, test.Owner.ReadSnapshot().Nat!.ExternalReachability);
        if (enabled)
        {
            test.Router.AutoComplete = true;
            await test.Runtime.Manager.ReconcileAsync();
            Assert.Contains(test.Router.History, request => request.Operation == NatMappingOperation.Map && request.Identity.InternalPort == driver.Port);
        }
        else
        {
            await test.Runtime.Manager.ReconcileAsync();
            Assert.Equal(0, test.Router.Count);
        }
        await result.Lease!.ReleaseAsync();
        Assert.Empty(test.Runtime.Manager.ReadState().RegisteredInternalPorts);
        Assert.Equal(0, test.Runtime.Disposals);
    }

    [Fact]
    public async Task ToggleNatWhileListenerActiveDoesNotChangeLeaseOrStopDriver()
    {
        await using var test = new Fixture();
        await test.InitializeAsync();
        var lease = (await test.Owner.AcquirePublicRelayAsync(Request)).Lease!;
        test.Router.AutoComplete = true;
        await test.Owner.SetAutoPortMappingAsync(true);
        await test.Runtime!.Manager.ReconcileAsync();
        await test.Owner.SetAutoPortMappingAsync(false);
        await test.Runtime.Manager.ReconcileAsync();
        Assert.Contains(test.Router.History, call => call.Operation == NatMappingOperation.Unmap);
        Assert.Single(test.Runtime.Manager.ReadState().RegisteredInternalPorts);
        Assert.Equal(0, test.Drivers.Single().Stops);
        Assert.Equal(1, test.Owner.ReadSnapshot().PublicRelay!.ReferenceCount);
        await lease.ReleaseAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindFailureOrDriverFailureLeavesNoLeaseOrMapping(bool throws)
    {
        await using var test = new Fixture { FailBind = !throws, ThrowStart = throws };
        await test.InitializeAsync();
        var result = await test.Owner.AcquirePublicRelayAsync(Request);
        Assert.Null(result.Lease);
        Assert.Equal(throws ? NovaPublicRelayFailure.DriverFailed : NovaPublicRelayFailure.ListenFailed, result.Failure);
        Assert.Empty(test.Runtime!.Manager.ReadState().RegisteredInternalPorts);
        Assert.Equal(0, test.Owner.ReadSnapshot().PublicRelay!.ReferenceCount);
        Assert.Equal(1, test.Drivers.Single().Disposals);
        Assert.Equal(0, test.Runtime.Disposals);
    }

    [Fact]
    public async Task FactoryFailureHasSafeOutcomeAndNoMappingOrLease()
    {
        await using var test = new Fixture { ThrowFactory = true };
        await test.InitializeAsync();
        var result = await test.Owner.AcquirePublicRelayAsync(Request);
        Assert.Equal(NovaPublicRelayFailure.DriverFailed, result.Failure);
        Assert.Empty(test.Runtime!.Manager.ReadState().RegisteredInternalPorts);
        Assert.Empty(test.Drivers);
        Assert.DoesNotContain("private", test.Owner.ReadSnapshot().ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelOneStartupIntentRetainsOtherUserAndOnePhysicalStart()
    {
        await using var test = new Fixture { HoldStart = true };
        await test.InitializeAsync();
        using var canceled = new CancellationTokenSource();
        var first = test.Owner.AcquirePublicRelayAsync(Request, canceled.Token);
        var second = test.Owner.AcquirePublicRelayAsync(Request);
        await Wait(test.DriverCreated.Task);
        var driver = test.Drivers.Single();
        await Wait(driver.StartEntered.Task);
        canceled.Cancel();
        Assert.Equal(NovaPublicRelayFailure.Canceled, (await Wait(first)).Failure);
        Assert.Equal(1, test.Owner.ReadSnapshot().PublicRelay!.ReferenceCount);
        Assert.Equal(0, driver.Stops);
        driver.AllowStart.TrySetResult();
        await (await Wait(second)).Lease!.ReleaseAsync();
        Assert.Equal(1, driver.Starts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelFinalStartupIntentJoinsCleanupEvenAfterLateBind(bool ignoreCancellation)
    {
        await using var test = new Fixture { HoldStart = true, IgnoreCancellation = ignoreCancellation, HoldStop = true };
        await test.InitializeAsync();
        using var canceled = new CancellationTokenSource();
        var acquire = test.Owner.AcquirePublicRelayAsync(Request, canceled.Token);
        await Wait(test.DriverCreated.Task);
        var driver = test.Drivers.Single();
        await Wait(driver.StartEntered.Task);
        canceled.Cancel();
        if (ignoreCancellation)
        {
            await Wait(driver.CancellationObserved.Task);
            driver.AllowStart.TrySetResult();
        }
        await Wait(driver.StopEntered.Task);
        Assert.False(acquire.IsCompleted);
        driver.AllowStop.TrySetResult();
        Assert.Equal(NovaPublicRelayFailure.Canceled, (await Wait(acquire)).Failure);
        Assert.Empty(test.Runtime!.Manager.ReadState().RegisteredInternalPorts);
        Assert.Equal(0, test.Owner.ReadSnapshot().PublicRelay!.ReferenceCount);
    }

    [Fact]
    public async Task CancellationOfStopWaitDoesNotCancelActualCleanup()
    {
        await using var test = new Fixture { HoldStop = true };
        await test.InitializeAsync();
        var lease = (await test.Owner.AcquirePublicRelayAsync(Request)).Lease!;
        var release = lease.ReleaseAsync();
        await Wait(test.Drivers.Single().StopEntered.Task);
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => release.WaitAsync(caller.Token));
        Assert.False(release.IsCompleted);
        Assert.Single(test.Runtime!.Manager.ReadState().RegisteredInternalPorts);
        test.Drivers.Single().AllowStop.TrySetResult();
        await Wait(release);
        Assert.Empty(test.Runtime.Manager.ReadState().RegisteredInternalPorts);
    }

    [Fact]
    public async Task AcquireWaitingForStopCanCancelWithoutCreatingNewDriver()
    {
        await using var test = new Fixture { HoldStop = true };
        await test.InitializeAsync();
        var lease = (await test.Owner.AcquirePublicRelayAsync(Request)).Lease!;
        var release = lease.ReleaseAsync();
        await Wait(test.Drivers.Single().StopEntered.Task);
        using var caller = new CancellationTokenSource();
        var next = test.Owner.AcquirePublicRelayAsync(Request, caller.Token);
        caller.Cancel();
        Assert.Equal(NovaPublicRelayFailure.Canceled, (await Wait(next)).Failure);
        Assert.Single(test.Drivers);
        test.Drivers.Single().AllowStop.TrySetResult();
        await Wait(release);
    }

    [Fact]
    public async Task ApplicationDisposeClosesAdmissionAndJoinsActiveLeaseStopBeforeNat()
    {
        await using var test = new Fixture { HoldStop = true };
        await test.InitializeAsync();
        var lease = (await test.Owner.AcquirePublicRelayAsync(Request)).Lease!;
        var disposal = test.Owner.DisposeAsync().AsTask();
        await Wait(test.Drivers.Single().StopEntered.Task);
        Assert.Same(disposal, test.Owner.DisposeAsync().AsTask());
        Assert.False(disposal.IsCompleted);
        Assert.Equal(0, test.Runtime!.Disposals);
        Assert.Equal(NovaPublicRelayFailure.Closing, (await test.Owner.AcquirePublicRelayAsync(Request)).Failure);
        Assert.Equal(NovaApplicationNetworkFailure.Closing, (await test.Owner.SetAutoPortMappingAsync(true)).Failure);
        test.Drivers.Single().AllowStop.TrySetResult();
        await Wait(disposal);
        await lease.ReleaseAsync();
        Assert.Equal(1, test.Runtime.Disposals);
        Assert.True(test.Owner.ReadSnapshot().Disposed);
        Assert.Empty(test.Runtime.Manager.ReadState().RegisteredInternalPorts);
        Assert.Collection(test.Order,
            item => Assert.Equal("listener-stop", item),
            item => Assert.Equal("driver-dispose", item),
            item => Assert.Equal("nat-dispose", item));
    }

    [Fact]
    public async Task ApplicationDisposeDuringLateStartupCannotPublishListening()
    {
        await using var test = new Fixture { HoldStart = true, IgnoreCancellation = true };
        await test.InitializeAsync();
        var acquire = test.Owner.AcquirePublicRelayAsync(Request);
        await Wait(test.DriverCreated.Task);
        var driver = test.Drivers.Single();
        await Wait(driver.StartEntered.Task);
        var disposal = test.Owner.DisposeAsync().AsTask();
        await Wait(driver.CancellationObserved.Task);
        Assert.False(disposal.IsCompleted);
        driver.AllowStart.TrySetResult();
        Assert.Null((await Wait(acquire)).Lease);
        await Wait(disposal);
        Assert.Equal(NovaPublicRelayState.Disposed, test.Owner.ReadSnapshot().PublicRelay!.State);
        Assert.Empty(test.Runtime!.Manager.ReadState().RegisteredInternalPorts);
        Assert.Equal(1, test.Runtime.Disposals);
    }

    [Fact]
    public async Task ApplicationDisposeStopsListenerThenDrainsPendingFieldOnlySave()
    {
        await using var test = new Fixture();
        await test.InitializeAsync();
        _ = await test.Owner.AcquirePublicRelayAsync(Request);
        var entered = Signal();
        var release = Signal();
        var writer = Task.Run(() => test.Store.UpdateAsync(current =>
        {
            entered.TrySetResult();
            release.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            return current with { RecordingOutputDirectory = "isolated-latest", NodeName = "latest-writer" };
        }));
        await Wait(entered.Task);
        var save = test.Owner.SetAutoPortMappingAsync(true);
        var disposal = test.Owner.DisposeAsync().AsTask();
        try
        {
            await Wait(test.Drivers.Single().DisposeEntered.Task);
            Assert.False(disposal.IsCompleted);
            Assert.Equal(0, test.Runtime!.Disposals);
            Assert.Equal(1, test.Owner.ReadSnapshot().PendingSaveCount);
            Assert.Empty(test.Runtime.Manager.ReadState().RegisteredInternalPorts);
        }
        finally { release.TrySetResult(); }
        await Wait(writer);
        await Wait(save);
        await Wait(disposal);
        Assert.Equal("isolated-latest", test.Store.Load().RecordingOutputDirectory);
        Assert.Equal("latest-writer", test.Store.Load().NodeName);
        Assert.True(test.Store.Load().AutoPortMapping);
        Assert.Equal(1, test.Runtime!.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupFailureIsStickyAndApplicationRetainsBorrowedRuntime(bool throws)
    {
        await using var test = new Fixture { FailStop = !throws, ThrowStop = throws };
        await test.InitializeAsync();
        var lease = (await test.Owner.AcquirePublicRelayAsync(Request)).Lease!;
        Assert.Equal(NovaPublicRelayFailure.CleanupFailed, await lease.ReleaseAsync());
        Assert.Equal(NovaPublicRelayFailure.CleanupFailed, (await test.Owner.AcquirePublicRelayAsync(Request)).Failure);
        Assert.Single(test.Drivers);
        var disposal = test.Owner.DisposeAsync().AsTask();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => disposal);
        Assert.DoesNotContain("private", error.ToString(), StringComparison.Ordinal);
        Assert.Same(disposal, test.Owner.DisposeAsync().AsTask());
        Assert.Equal(NovaApplicationNetworkFailure.ShutdownCleanupUnconfirmed, test.Owner.ReadSnapshot().ShutdownFailure);
        Assert.Equal(0, test.Runtime!.Disposals);
        Assert.Null(test.Owner.ReadSnapshot().ShutdownOutcome);
        Assert.Equal(NovaPublicRelayState.Failed, test.Owner.ReadSnapshot().PublicRelay!.State);
    }

    [Fact]
    public async Task UnexpectedDriverEndRevokesReferencesAndCannotAffectNextEpoch()
    {
        await using var test = new Fixture();
        await test.InitializeAsync();
        var oldLease = (await test.Owner.AcquirePublicRelayAsync(Request)).Lease!;
        var old = test.Drivers.Single();
        await Wait(old.CompletionObserved.Task);
        old.End.TrySetResult(new(true));
        await Wait(old.StopEntered.Task);
        await oldLease.ReleaseAsync();
        Assert.Equal(NovaPublicRelayFailure.DriverEnded, test.Owner.ReadSnapshot().PublicRelay!.Failure);
        var next = await test.Owner.AcquirePublicRelayAsync(Request);
        await oldLease.ReleaseAsync();
        Assert.Equal(0, test.Drivers.Last().Stops);
        Assert.Equal(2, test.Owner.ReadSnapshot().PublicRelay!.Epoch);
        await next.Lease!.ReleaseAsync();
    }

    [Fact]
    public async Task RapidLeaseCyclesNeverReuseRetiredDriverOrToken()
    {
        await using var test = new Fixture();
        await test.InitializeAsync();
        for (var index = 0; index < 30; index++)
        {
            var lease = (await test.Owner.AcquirePublicRelayAsync(Request)).Lease!;
            await lease.ReleaseAsync();
            Assert.Empty(test.Runtime!.Manager.ReadState().RegisteredInternalPorts);
            Assert.Equal(index + 1, test.Owner.ReadSnapshot().PublicRelay!.Epoch);
        }
        Assert.Equal(30, test.Drivers.Count);
        Assert.All(test.Drivers, driver => Assert.Equal(1, driver.Stops));
        Assert.All(test.Drivers, driver => Assert.Equal(1, driver.Disposals));
        Assert.Equal(1, test.RuntimeCreates);
    }

    [Fact]
    public async Task PendingMapAfterListenerReleaseIsReconciledByExistingManager()
    {
        await using var nat = new NatPortMappingTestHarness();
        FakeDriver? driver = null;
        await using var owner = new NovaSharedPublicRelayOwner(new(Guid.NewGuid(), nat.Manager),
            borrowed => driver = new FakeDriver(borrowed, 45001, new()));
        var lease = (await owner.AcquireAsync(Request)).Lease!;
        var mapping = nat.Manager.ReconcileAsync();
        var call = await nat.Router.NextAsync();
        await lease.ReleaseAsync();
        Assert.Empty(nat.Manager.ReadState().RegisteredInternalPorts);
        call.Succeed(45001);
        var unmap = await nat.Router.NextAsync();
        Assert.Equal(NatMappingOperation.Unmap, unmap.Request.Operation);
        unmap.Succeed();
        await Wait(mapping);
        nat.Router.AutoComplete = true;
        await nat.Manager.ReconcileAsync();
        Assert.Contains(nat.Router.History, item => item.Operation == NatMappingOperation.Unmap);
        Assert.Equal(1, driver!.Stops);
        Assert.True(nat.Manager.Enabled);
    }

    [Fact]
    public async Task NetworkChangeRetainsAnyListenerAndDoesNotAdoptOldRouteResult()
    {
        await using var nat = new NatPortMappingTestHarness();
        await using var owner = new NovaSharedPublicRelayOwner(new(Guid.NewGuid(), nat.Manager),
            borrowed => new FakeDriver(borrowed, 45002, new()));
        var lease = (await owner.AcquireAsync(Request)).Lease!;
        var mapping = nat.Manager.ReconcileAsync();
        var old = await nat.Router.NextAsync();
        var next = new NatNetworkIdentity(NatPortMappingTestHarness.Home.Device, "192.168.1.3", 2);
        nat.Manager.UpdateNetwork(next);
        old.Succeed(45002);
        var current = await nat.Router.NextAsync();
        Assert.Equal(next, current.Request.Identity.Network);
        current.Succeed(45003);
        await Wait(mapping);
        Assert.Equal(1, owner.ReadSnapshot().ReferenceCount);
        Assert.Single(nat.Manager.ReadState().RegisteredInternalPorts);
        var mapped = Assert.Single(nat.Manager.Snapshot, item => item.Identity.Network == next);
        Assert.Equal(45003, mapped.ReturnedExternalPort);
        Assert.Equal(NatMappingStatus.UnmapUnconfirmed,
            Assert.Single(nat.Manager.Snapshot, item => item.Identity.Network == old.Request.Identity.Network).Status);
        await lease.ReleaseAsync();
        nat.Router.AutoComplete = true;
        await nat.Manager.ReconcileAsync();
        Assert.DoesNotContain(nat.Router.History, item => item.Operation == NatMappingOperation.Unmap && item.Identity.Network == old.Request.Identity.Network);
    }

    [Fact]
    public async Task UninitializedAndDisposedApplicationNeverCreatesDriver()
    {
        await using var test = new Fixture();
        Assert.Equal(NovaPublicRelayFailure.NotInitialized, (await test.Owner.AcquirePublicRelayAsync(Request)).Failure);
        Assert.Empty(test.Drivers);
        await test.Owner.DisposeAsync();
        Assert.Equal(NovaPublicRelayFailure.Closing, (await test.Owner.AcquirePublicRelayAsync(Request)).Failure);
        Assert.Equal(0, test.RuntimeCreates);
        Assert.False(File.Exists(test.SettingsPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionDriverForwardsBorrowedManagerIdentityAndCleanupToSession(bool bindFailure)
    {
        await using var nat = new NatPortMappingTestHarness(enabled: false);
        var borrowed = new NovaBorrowedNatManager(Guid.NewGuid(), nat.Manager);
        var identity = new NovaPublicRelayIdentity(borrowed.OwnerId, Channel, Session, "example.invalid", 7144,
            NovaPublicRelayBindPolicy.AnyIpv4Ephemeral, NovaPublicRelayProtocol.PcpTcp, NovaPublicRelayScope.SingleChannelPublic);
        FakeSession? session = null;
        await using var driver = new NovaBorrowedPublicRelayDriver(borrowed, (owner, request) =>
        {
            Assert.Same(borrowed, owner);
            Assert.Equal(identity, request);
            return session = new FakeSession(nat.Manager, bindFailure);
        });
        Assert.Empty(nat.Manager.ReadState().RegisteredInternalPorts);
        var result = await driver.StartAsync(identity, CancellationToken.None);
        Assert.Equal(bindFailure ? NovaPublicRelayFailure.DriverFailed : NovaPublicRelayFailure.None, result.Failure);
        if (!bindFailure)
        {
            Assert.Equal(46000, result.Endpoint!.Port);
            Assert.Single(nat.Manager.ReadState().RegisteredInternalPorts);
            Assert.False(driver.Completion.IsCompleted);
        }
        Assert.Equal(NovaPublicRelayFailure.None, await driver.StopAsync());
        Assert.Equal(1, session!.Stops);
        Assert.Equal(1, session.Disposals);
        Assert.Empty(nat.Manager.ReadState().RegisteredInternalPorts);
        Assert.False(nat.Manager.Enabled);
        nat.Manager.SetEnabled(true); // Borrower cleanup did not shut the manager down.
    }

    [Fact]
    public async Task ProductionDriverRejectsWrongRuntimeOwnerWithoutSessionCreation()
    {
        await using var nat = new NatPortMappingTestHarness(enabled: false);
        var creates = 0;
        await using var driver = new NovaBorrowedPublicRelayDriver(new(Guid.NewGuid(), nat.Manager), (_, _) =>
        { creates++; return new FakeSession(nat.Manager, false); });
        var identity = new NovaPublicRelayIdentity(Guid.NewGuid(), Channel, Session, "example.invalid", 7144,
            NovaPublicRelayBindPolicy.AnyIpv4Ephemeral, NovaPublicRelayProtocol.PcpTcp, NovaPublicRelayScope.SingleChannelPublic);
        Assert.Equal(NovaPublicRelayFailure.InvalidRequest, (await driver.StartAsync(identity, CancellationToken.None)).Failure);
        Assert.Equal(0, creates);
    }

    [Fact]
    public async Task DriverDisposalFailureIsTypedAndDoesNotPermitReplacement()
    {
        await using var test = new Fixture { ThrowDispose = true };
        await test.InitializeAsync();
        var lease = (await test.Owner.AcquirePublicRelayAsync(Request)).Lease!;
        Assert.Equal(NovaPublicRelayFailure.CleanupFailed, await lease.ReleaseAsync());
        Assert.Equal(NovaPublicRelayFailure.CleanupFailed, (await test.Owner.AcquirePublicRelayAsync(Request)).Failure);
        await Assert.ThrowsAsync<InvalidOperationException>(() => test.Owner.DisposeAsync().AsTask());
        Assert.Equal(0, test.Runtime!.Disposals);
        Assert.Empty(test.Runtime.Manager.ReadState().RegisteredInternalPorts);
    }

    [Fact]
    public async Task UnconfirmedDriverCompletionIsNotPromotedToCleanStop()
    {
        await using var test = new Fixture { ExpectedUnconfirmed = true };
        await test.InitializeAsync();
        var lease = (await test.Owner.AcquirePublicRelayAsync(Request)).Lease!;
        var driver = test.Drivers.Single();
        await Wait(driver.CompletionObserved.Task);
        driver.End.TrySetResult(new(false));
        await Wait(driver.StopEntered.Task);
        Assert.Equal(NovaPublicRelayFailure.CleanupFailed, await lease.ReleaseAsync());
        Assert.Equal(NovaPublicRelayFailure.CleanupFailed, test.Owner.ReadSnapshot().PublicRelay!.Failure);
        await Assert.ThrowsAsync<InvalidOperationException>(() => test.Owner.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task DriverCompletionExceptionIsSafeAndCleanupIsJoined()
    {
        await using var test = new Fixture();
        await test.InitializeAsync();
        var lease = (await test.Owner.AcquirePublicRelayAsync(Request)).Lease!;
        var driver = test.Drivers.Single();
        await Wait(driver.CompletionObserved.Task);
        driver.End.TrySetException(new InvalidOperationException("private-completion-error"));
        await Wait(driver.StopEntered.Task);
        await lease.ReleaseAsync();
        Assert.Equal(NovaPublicRelayFailure.DriverFailed, test.Owner.ReadSnapshot().PublicRelay!.Failure);
        Assert.DoesNotContain("private", test.Owner.ReadSnapshot().ToString(), StringComparison.Ordinal);
        Assert.Empty(test.Runtime!.Manager.ReadState().RegisteredInternalPorts);
        Assert.Equal(1, driver.Disposals);
    }

    [Fact]
    public async Task AlreadyCanceledAcquireDoesNotStartOrRegisterAnything()
    {
        await using var test = new Fixture();
        await test.InitializeAsync();
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        Assert.Equal(NovaPublicRelayFailure.Canceled, (await test.Owner.AcquirePublicRelayAsync(Request, caller.Token)).Failure);
        Assert.Empty(test.Drivers);
        Assert.Equal(0, test.Owner.ReadSnapshot().PublicRelay!.Epoch);
        Assert.Empty(test.Runtime!.Manager.ReadState().RegisteredInternalPorts);
    }

    [Fact]
    public async Task RuntimeOwnerIdentitySeparatesIndependentApplicationOwners()
    {
        await using var first = new Fixture();
        await using var second = new Fixture();
        await first.InitializeAsync();
        await second.InitializeAsync();
        var a = (await first.Owner.AcquirePublicRelayAsync(Request)).Lease!;
        var b = (await second.Owner.AcquirePublicRelayAsync(Request)).Lease!;
        Assert.NotEqual(first.Owner.ReadSnapshot().PublicRelay!.RuntimeOwnerId, second.Owner.ReadSnapshot().PublicRelay!.RuntimeOwnerId);
        await a.ReleaseAsync();
        Assert.Equal(0, second.Drivers.Single().Stops);
        Assert.Single(second.Runtime!.Manager.ReadState().RegisteredInternalPorts);
        await b.ReleaseAsync();
    }

    [Fact]
    public async Task ProductionDriverStopDuringStartupCancelsReadinessAndJoinsCleanup()
    {
        await using var nat = new NatPortMappingTestHarness(enabled: false);
        var borrowed = new NovaBorrowedNatManager(Guid.NewGuid(), nat.Manager);
        var session = new FakeSession(nat.Manager, false) { HoldReadiness = true };
        await using var driver = new NovaBorrowedPublicRelayDriver(borrowed, (_, _) => session);
        var identity = new NovaPublicRelayIdentity(borrowed.OwnerId, Channel, Session, "example.invalid", 7144,
            NovaPublicRelayBindPolicy.AnyIpv4Ephemeral, NovaPublicRelayProtocol.PcpTcp, NovaPublicRelayScope.SingleChannelPublic);
        var start = driver.StartAsync(identity, CancellationToken.None);
        await Wait(session.ReadinessEntered.Task);
        Assert.Single(nat.Manager.ReadState().RegisteredInternalPorts);
        var stop = driver.StopAsync();
        Assert.Same(stop, driver.StopAsync());
        Assert.Equal(NovaPublicRelayFailure.Canceled, (await Wait(start)).Failure);
        Assert.Equal(NovaPublicRelayFailure.None, await Wait(stop));
        Assert.Empty(nat.Manager.ReadState().RegisteredInternalPorts);
        Assert.Equal(1, session.Stops);
        Assert.Equal(1, session.Disposals);
        nat.Manager.SetEnabled(true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionDriverRejectsFailedReadinessOrInvalidEndpoint(bool invalidEndpoint)
    {
        await using var nat = new NatPortMappingTestHarness(enabled: false);
        var borrowed = new NovaBorrowedNatManager(Guid.NewGuid(), nat.Manager);
        var session = new FakeSession(nat.Manager, false)
        { FailedReadiness = !invalidEndpoint, InvalidEndpoint = invalidEndpoint };
        await using var driver = new NovaBorrowedPublicRelayDriver(borrowed, (_, _) => session);
        var identity = new NovaPublicRelayIdentity(borrowed.OwnerId, Channel, Session, "example.invalid", 7144,
            NovaPublicRelayBindPolicy.AnyIpv4Ephemeral, NovaPublicRelayProtocol.PcpTcp, NovaPublicRelayScope.SingleChannelPublic);
        Assert.Equal(NovaPublicRelayFailure.ListenFailed, (await driver.StartAsync(identity, CancellationToken.None)).Failure);
        Assert.Equal(NovaPublicRelayFailure.None, await driver.StopAsync());
        Assert.Empty(nat.Manager.ReadState().RegisteredInternalPorts);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task Wait(Task task) => task.WaitAsync(TimeSpan.FromSeconds(10));
    private static Task<T> Wait<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(10));

    private sealed class Fixture : IAsyncDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "Nova-public-relay-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            SettingsPath = Path.Combine(Root, "settings.json");
            Store = new NovaSettingsStore(SettingsPath);
            Owner = new NovaApplicationNetworkOwner(Store, static () => null, options =>
            {
                RuntimeCreates++;
                return Runtime = new FakeRuntime(new NatPortMappingRuntime(options, Router, new FakeNetwork(), Clock), Order);
            }, createPublicRelayDriver: borrowed =>
            {
                if (ThrowFactory) throw new InvalidOperationException("private-driver-factory");
                var driver = new FakeDriver(borrowed, 45000 + Drivers.Count, Order)
                {
                    HoldStart = HoldStart,
                    HoldStop = HoldStop,
                    IgnoreCancellation = IgnoreCancellation,
                    FailBind = FailBind,
                    ThrowStart = ThrowStart,
                    FailStop = FailStop,
                    ThrowStop = ThrowStop,
                    ThrowDispose = ThrowDispose,
                };
                Drivers.Enqueue(driver);
                DriverCreated.TrySetResult();
                return driver;
            });
        }
        public string Root { get; }
        public string SettingsPath { get; }
        public NovaSettingsStore Store { get; }
        public NovaApplicationNetworkOwner Owner { get; }
        public FakeNatRouter Router { get; } = new();
        public ManualNatTimeProvider Clock { get; } = new();
        public ConcurrentQueue<string> Order { get; } = new();
        public ConcurrentQueue<FakeDriver> Drivers { get; } = new();
        public TaskCompletionSource DriverCreated { get; } = Signal();
        public FakeRuntime? Runtime { get; private set; }
        public int RuntimeCreates { get; private set; }
        public bool HoldStart { get; init; }
        public bool HoldStop { get; init; }
        public bool IgnoreCancellation { get; init; }
        public bool FailBind { get; init; }
        public bool ThrowStart { get; init; }
        public bool ThrowFactory { get; init; }
        public bool FailStop { get; init; }
        public bool ThrowStop { get; init; }
        public bool ThrowDispose { get; init; }
        public bool ExpectedUnconfirmed { get; init; }
        public async Task InitializeAsync() => Assert.True((await Owner.InitializeAsync()).Initialized);
        public async ValueTask DisposeAsync()
        {
            foreach (var driver in Drivers) { driver.AllowStart.TrySetResult(); driver.AllowStop.TrySetResult(); }
            Router.AutoComplete = true;
            Router.CompleteOutstanding();
            try { await Owner.DisposeAsync(); }
            catch (InvalidOperationException) when (FailStop || ThrowStop || ThrowDispose || ExpectedUnconfirmed) { }
            finally
            {
                // Test-only teardown for the deliberately retained runtime in
                // negative cleanup cases; never a production recovery policy.
                if (Runtime is { Disposals: 0 }) await Runtime.DisposeAsync();
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class FakeNetwork : INatNetworkSource
    {
        public NatNetworkIdentity? GetCurrentNetwork() => NatPortMappingTestHarness.Home;
    }

    private sealed class FakeRuntime(NatPortMappingRuntime runtime, ConcurrentQueue<string> order) : INovaApplicationNatRuntime
    {
        public NatPortMappingManager Manager => runtime.Manager;
        public NatShutdownOutcome? ShutdownOutcome => runtime.ShutdownOutcome;
        public int Disposals { get; private set; }
        public async ValueTask DisposeAsync()
        {
            Disposals++;
            order.Enqueue("nat-dispose");
            await runtime.DisposeAsync();
        }
    }

    // Simulates the existing server's bind -> token -> stop -> release contract.
    // This is not physical bind evidence; production server source is audited
    // separately and the driver never registers a token itself.
    private sealed class FakeDriver(NovaBorrowedNatManager borrowed, int port, ConcurrentQueue<string> order) : INovaPublicRelayDriver
    {
        private IDisposable? token;
        public NovaBorrowedNatManager Borrowed => borrowed;
        public int Port => port;
        public NovaPublicRelayIdentity? Identity { get; private set; }
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public int Disposals { get; private set; }
        public bool HoldStart { get; init; }
        public bool HoldStop { get; init; }
        public bool IgnoreCancellation { get; init; }
        public bool FailBind { get; init; }
        public bool ThrowStart { get; init; }
        public bool FailStop { get; init; }
        public bool ThrowStop { get; init; }
        public bool ThrowDispose { get; init; }
        public TaskCompletionSource StartEntered { get; } = Signal();
        public TaskCompletionSource AllowStart { get; } = Signal();
        public TaskCompletionSource CancellationObserved { get; } = Signal();
        public TaskCompletionSource StopEntered { get; } = Signal();
        public TaskCompletionSource AllowStop { get; } = Signal();
        public TaskCompletionSource DisposeEntered { get; } = Signal();
        public TaskCompletionSource CompletionObserved { get; } = Signal();
        public TaskCompletionSource<NovaPublicRelayEnd> End { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<NovaPublicRelayEnd> Completion { get { CompletionObserved.TrySetResult(); return End.Task; } }
        public async Task<NovaPublicRelayStartResult> StartAsync(NovaPublicRelayIdentity identity, CancellationToken cancellationToken)
        {
            Starts++;
            Identity = identity;
            StartEntered.TrySetResult();
            using var registration = cancellationToken.Register(() => CancellationObserved.TrySetResult());
            if (HoldStart)
            {
                if (IgnoreCancellation) await AllowStart.Task;
                else await AllowStart.Task.WaitAsync(cancellationToken);
            }
            if (ThrowStart) throw new InvalidOperationException("private-start-error");
            if (FailBind) return new(null, NovaPublicRelayFailure.ListenFailed);
            token = borrowed.Manager.AcquirePublicTcpListener(new IPEndPoint(IPAddress.Any, port));
            return new(new("0.0.0.0", port), NovaPublicRelayFailure.None);
        }
        public async Task<NovaPublicRelayFailure> StopAsync()
        {
            Stops++;
            StopEntered.TrySetResult();
            if (HoldStop) await AllowStop.Task;
            token?.Dispose();
            token = null;
            order.Enqueue("listener-stop");
            End.TrySetResult(new(true));
            if (ThrowStop) throw new InvalidOperationException("private-stop-error");
            return FailStop ? NovaPublicRelayFailure.CleanupFailed : NovaPublicRelayFailure.None;
        }
        public ValueTask DisposeAsync()
        {
            Disposals++;
            order.Enqueue("driver-dispose");
            DisposeEntered.TrySetResult();
            if (ThrowDispose) throw new InvalidOperationException("private-dispose-error");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSession(NatPortMappingManager manager, bool failBind) : INovaPublicRelaySession
    {
        private IDisposable? token;
        private readonly TaskCompletionSource<bool> end = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IPEndPoint LocalEndPoint => new(InvalidEndpoint ? IPAddress.Loopback : IPAddress.Any, 46000);
        public bool HoldReadiness { get; init; }
        public bool FailedReadiness { get; init; }
        public bool InvalidEndpoint { get; init; }
        public TaskCompletionSource ReadinessEntered { get; } = Signal();
        public TaskCompletionSource<PcpRelayCoordinatorReadinessOutcome> Readiness { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Stops { get; private set; }
        public int Disposals { get; private set; }
        public Task<bool> RunAsync(string host, int port)
        {
            Assert.Equal("example.invalid", host);
            Assert.Equal(7144, port);
            if (failBind) throw new InvalidOperationException("private-bind-failed");
            if (!InvalidEndpoint) token = manager.AcquirePublicTcpListener(LocalEndPoint);
            return end.Task;
        }
        public Task<PcpRelayCoordinatorReadinessOutcome> WaitForReadinessAsync(CancellationToken cancellationToken)
        {
            ReadinessEntered.TrySetResult();
            return HoldReadiness ? Readiness.Task.WaitAsync(cancellationToken) :
                Task.FromResult(FailedReadiness ? PcpRelayCoordinatorReadinessOutcome.Failed : PcpRelayCoordinatorReadinessOutcome.Ready);
        }
        public Task StopAsync()
        {
            Stops++;
            token?.Dispose();
            end.TrySetResult(true);
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
