using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;
using PeerCast.Nova.Network.PortMapping;
using PeerCast.Nova.Network.Tests;

namespace PeerCast.Nova.App.Tests;

public sealed class NovaAcceptanceRelayTests
{
    private static readonly NovaAcceptanceRelayOptions Options = new("acceptance-test", TimeSpan.FromSeconds(30), 0, false);

    [Theory]
    [InlineData("1s", "0", "off", "run.1", true)]
    [InlineData("300s", "65535", "on", "run-2", true)]
    [InlineData("0s", "0", "off", "run", false)]
    [InlineData("301s", "0", "off", "run", false)]
    [InlineData("-1s", "0", "off", "run", false)]
    [InlineData("30", "0", "off", "run", false)]
    [InlineData("30s", "65536", "off", "run", false)]
    [InlineData("30s", "-1", "off", "run", false)]
    [InlineData("30s", "0", "true", "run", false)]
    [InlineData("30s", "0", "off", "", false)]
    [InlineData("30s", "0", "off", "has space", false)]
    public void Inputs_RequireExplicitFiniteValues(string duration, string port, string setting, string runId, bool valid)
    {
        Assert.Equal(valid, NovaAcceptanceRelayOptions.TryParse(
            [NovaAcceptanceRelayOptions.Switch, "--duration", duration, "--port", port,
                "--auto-port-mapping", setting, "--run-id", runId], out var parsed));
        if (valid) { Assert.Equal(setting == "on", parsed!.AutoPortMapping); }
    }

    [Theory]
    [InlineData("--port", "0", "--port", "1", "--auto-port-mapping", "off", "--run-id", "run")]
    [InlineData("--duration", "30s", "--pcrplayer-executable", "player", "--auto-port-mapping", "off", "--run-id", "run")]
    public void Inputs_RejectDuplicateOrMixedModeOptions(params string[] args)
    {
        Assert.False(NovaAcceptanceRelayOptions.TryParse([NovaAcceptanceRelayOptions.Switch, .. args], out _));
    }

    [Fact]
    public void Inputs_NoImplicitUnboundedDefaults()
    {
        Assert.False(NovaAcceptanceRelayOptions.TryParse([NovaAcceptanceRelayOptions.Switch], out _));
    }

    [Fact]
    public async Task Duration_StopsOwnerAndReportsActualEndpointAndDrainedTasks()
    {
        var clock = new AcceptanceClock();
        var session = new FakeSession();
        using var output = new StringWriter();
        var run = NovaAcceptanceRelayHarness.RunAsync(Options, output, CancellationToken.None, _ => session, clock);
        using (var started = Record(output, "ListenerStarted"))
        {
            Assert.Equal("acceptance-test", started.RootElement.GetProperty("RunId").GetString());
            Assert.Equal(45000, started.RootElement.GetProperty("BoundPort").GetInt32());
            Assert.Equal("0.0.0.0", started.RootElement.GetProperty("BoundInternalAddress").GetString());
            Assert.True(started.RootElement.GetProperty("PublicWanEligible").GetBoolean());
            Assert.Equal("NotChecked", started.RootElement.GetProperty("PortMapping").GetProperty("ExternalReachability").GetString());
        }
        clock.Advance(Options.Duration);
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, session.CleanupCount);
        Assert.Equal("Stopped", session.ListenerState);
        Assert.True(session.Completion.IsCompletedSuccessfully);
        using var ended = Record(output, "DurationElapsed");
        Assert.Equal("Completed", ended.RootElement.GetProperty("Summary").GetString());
        Assert.True(ended.RootElement.GetProperty("ApplicationTasksDrained").GetBoolean());
    }

    [Fact]
    public async Task ExplicitCancellation_StopsWithoutWaitingForDuration()
    {
        var session = new FakeSession();
        using var output = new StringWriter();
        using var cancel = new CancellationTokenSource();
        var run = NovaAcceptanceRelayHarness.RunAsync(Options, output, cancel.Token, _ => session, new AcceptanceClock());
        cancel.Cancel();
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, session.CleanupCount);
        using var ended = Record(output, "CallerCanceled");
        Assert.Equal("Stopped", ended.RootElement.GetProperty("ListenerState").GetString());
    }

    [Fact]
    public async Task FactoryFailure_IsDistinctAndCompletesProcess()
    {
        using var output = new StringWriter();
        var result = await NovaAcceptanceRelayHarness.RunAsync(Options, output, CancellationToken.None,
            _ => throw new InvalidOperationException(), new AcceptanceClock());
        Assert.Equal(1, result);
        using var failed = Record(output, "HarnessStartupFailure");
        Assert.False(failed.RootElement.GetProperty("ListenerStarted").GetBoolean());
        using var completed = Record(output, "ProcessCompleted");
        Assert.Equal("1", completed.RootElement.GetProperty("Summary").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupFailure_StillCleansOwner(bool bindFailure)
    {
        var session = new FakeSession { StartFailure = bindFailure ? new SocketException() : new InvalidOperationException() };
        using var output = new StringWriter();
        Assert.Equal(1, await NovaAcceptanceRelayHarness.RunAsync(Options, output, CancellationToken.None,
            _ => session, new AcceptanceClock()));
        Assert.Equal(1, session.CleanupCount);
        Assert.Equal("Stopped", session.ListenerState);
        using var failed = Record(output, bindFailure ? "ListenerBindFailure" : "HarnessStartupFailure");
        Assert.False(failed.RootElement.GetProperty("ListenerStarted").GetBoolean());
    }

    [Fact]
    public async Task StartupTimeout_CancelsAndObservesDelayedStartup()
    {
        var clock = new AcceptanceClock();
        var session = new FakeSession { DelayStartup = true };
        using var output = new StringWriter();
        var run = NovaAcceptanceRelayHarness.RunAsync(Options, output, CancellationToken.None, _ => session, clock);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(session.StartupTask!.IsCanceled);
        using var failed = Record(output, "ListenerStartupTimeout");
        Assert.True(failed.RootElement.GetProperty("ApplicationTasksDrained").GetBoolean());
    }

    [Fact]
    public async Task LifetimeExpiresBeforeStartup_DoesNotReportSuccessfulRun()
    {
        var clock = new AcceptanceClock();
        var session = new FakeSession { DelayStartup = true };
        using var output = new StringWriter();
        var run = NovaAcceptanceRelayHarness.RunAsync(Options with { Duration = TimeSpan.FromSeconds(1) }, output,
            CancellationToken.None, _ => session, clock);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        using var failed = Record(output, "ListenerStartupTimeout");
        Assert.False(failed.RootElement.GetProperty("ListenerStarted").GetBoolean());
        Assert.True(session.StartupTask!.IsCanceled);
    }

    [Fact]
    public async Task CleanupUnconfirmed_DoesNotClaimAllTasksDrained()
    {
        var clock = new AcceptanceClock();
        var session = new FakeSession { Outcome = NatShutdownOutcome.CleanupUnconfirmed };
        using var output = new StringWriter();
        var run = NovaAcceptanceRelayHarness.RunAsync(Options, output, CancellationToken.None, _ => session, clock);
        clock.Advance(Options.Duration);
        Assert.Equal(2, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        using var ended = Record(output, "DurationElapsed");
        Assert.Equal("CleanupUnconfirmed", ended.RootElement.GetProperty("Summary").GetString());
        Assert.False(ended.RootElement.GetProperty("ApplicationTasksDrained").GetBoolean());
    }

    [Fact]
    public async Task ShutdownTimeout_DoesNotClaimApplicationDrain()
    {
        var clock = new AcceptanceClock();
        var session = new FakeSession { DelayCleanup = true };
        using var output = new StringWriter();
        var run = NovaAcceptanceRelayHarness.RunAsync(Options, output, CancellationToken.None, _ => session, clock);
        clock.Advance(Options.Duration);
        await clock.WaitForTimerAsync(TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(2, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        using var failed = Record(output, "ApplicationShutdownTimeout");
        Assert.False(failed.RootElement.GetProperty("ApplicationTasksDrained").GetBoolean());
        Assert.Equal("CleanupUnconfirmed", failed.RootElement.GetProperty("Summary").GetString());
        session.FinishCleanup();
        await session.CleanupAsync();
    }

    [Fact]
    public async Task ListenerEndsEarly_TerminatesAndCleansInsteadOfWaitingForDuration()
    {
        var session = new FakeSession();
        using var output = new StringWriter();
        var run = NovaAcceptanceRelayHarness.RunAsync(Options, output, CancellationToken.None, _ => session, new AcceptanceClock());
        session.EndListener();
        Assert.Equal(1, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        using var ended = Record(output, "ListenerEndedUnexpectedly");
        Assert.Equal(1, session.CleanupCount);
    }

    [Theory]
    [InlineData(false, "Discovering")]
    [InlineData(true, "ResultUnknown")]
    public async Task Status_DistinguishesDiscoveryFromUnknownMapping(bool unknown, string expected)
    {
        var clock = new AcceptanceClock();
        var session = new FakeSession();
        session.Mapping = session.Mapping with
        {
            Enabled = true,
            Network = unknown ? NatPortMappingTestHarness.Home : null,
            Mappings = unknown ? [new(new(NatPortMappingTestHarness.Home, NatMappingProtocol.Tcp, 45000),
                1, NatMappingStatus.ResultUnknown, NatExternalReachability.Unknown, 46000, null, false, true)] : []
        };
        using var output = new StringWriter();
        var run = NovaAcceptanceRelayHarness.RunAsync(Options with { AutoPortMapping = true }, output,
            CancellationToken.None, _ => session, clock);
        using var started = Record(output, "ListenerStarted");
        Assert.Equal(expected, started.RootElement.GetProperty("PortMapping").GetProperty("State").GetString());
        clock.Advance(Options.Duration);
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Entry_SelectsAcceptanceWithoutInvokingGui()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var calls = 0;
        Assert.Equal(17, await NovaApplicationEntrypoint.RunDailyAsync(
            [NovaAcceptanceRelayOptions.Switch, "--duration", "30s", "--port", "0", "--auto-port-mapping", "off", "--run-id", "run"],
            output, error, (_, _) => throw new InvalidOperationException("GUI must not launch"), CancellationToken.None,
            (options, _, _) => { calls++; Assert.False(options.AutoPortMapping); return Task.FromResult(17); }));
        Assert.Equal(1, calls);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Entry_InvalidAcceptanceFailsBeforeAnyLaunch()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(2, await NovaApplicationEntrypoint.RunDailyAsync([NovaAcceptanceRelayOptions.Switch], output, error,
            (_, _) => throw new InvalidOperationException(), CancellationToken.None,
            (_, _, _) => throw new InvalidOperationException()));
        Assert.Contains("--duration", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Entry_DefaultStillUsesProductionGui()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var guiCalls = 0;
        Assert.Equal(0, await NovaApplicationEntrypoint.RunDailyAsync([], output, error,
            (path, _) => { guiCalls++; Assert.Null(path); return ValueTask.FromResult(NovaWindowsStartupResult.Completed); },
            CancellationToken.None, (_, _, _) => throw new InvalidOperationException("Acceptance must not launch")));
        Assert.Equal(1, guiCalls);
    }

    [Fact]
    public async Task HundredBoundedLifetimes_LeaveNoOwnerTasks()
    {
        for (var index = 0; index < 100; index++)
        {
            var session = new FakeSession();
            var clock = new AcceptanceClock();
            using var output = new StringWriter();
            var run = NovaAcceptanceRelayHarness.RunAsync(Options, output, CancellationToken.None, _ => session, clock);
            clock.Advance(Options.Duration);
            Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(session.Completion.IsCompletedSuccessfully);
            Assert.Equal(1, session.CleanupCount);
        }
    }

    [Fact]
    public async Task ProductionSession_Off_BindsPublicEndpointReleasesDemandAndSocket()
    {
        var router = new FakeNatRouter();
        var clock = new AcceptanceClock();
        await using var session = new NovaAcceptanceRelaySession(Options, router, new FixedNetwork(), clock);
        var endpoint = await session.StartAsync(CancellationToken.None);
        Assert.Equal(IPAddress.Any, endpoint.Address);
        Assert.NotEqual(0, endpoint.Port);
        Assert.Equal([endpoint.Port], session.ReadMappingState().RegisteredInternalPorts);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, router.Count);
        Assert.Equal(NatShutdownOutcome.Completed, await session.CleanupAsync());
        Assert.Empty(session.ReadMappingState().RegisteredInternalPorts);
        Assert.True(session.Completion.IsCompletedSuccessfully);
        Assert.Equal("Stopped", session.ListenerState);
        var probe = new TcpListener(IPAddress.Any, endpoint.Port);
        probe.Start();
        probe.Stop();
        Assert.Equal(0, router.Count);
    }

    [Fact]
    public async Task ProductionSession_BindFailure_NeverCreatesPublicDemandOrMapping()
    {
        var occupied = new TcpListener(IPAddress.Any, 0);
        occupied.Start();
        try
        {
            var router = new FakeNatRouter();
            var clock = new AcceptanceClock();
            await using var session = new NovaAcceptanceRelaySession(Options with
            {
                Port = ((IPEndPoint)occupied.LocalEndpoint).Port,
                AutoPortMapping = true
            }, router, new FixedNetwork(), clock);
            await Assert.ThrowsAsync<SocketException>(() => session.StartAsync(CancellationToken.None));
            Assert.Empty(session.ReadMappingState().RegisteredInternalPorts);
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(0, router.Count);
            Assert.Equal(NatShutdownOutcome.Completed, await session.CleanupAsync());
            Assert.Equal("Stopped", session.ListenerState);
        }
        finally { occupied.Stop(); }
    }

    [Fact]
    public async Task ProductionSession_On_UsesPumpAndReturnsRouterPortAndLeaseEvidence()
    {
        var router = new FakeNatRouter();
        var clock = new AcceptanceClock();
        await using var session = new NovaAcceptanceRelaySession(Options with { AutoPortMapping = true }, router, new FixedNetwork(), clock);
        await clock.WaitForTimerAsync(TimeSpan.FromMilliseconds(250));
        var endpoint = await session.StartAsync(CancellationToken.None);
        Assert.Equal(0, router.Count);
        clock.Advance(TimeSpan.FromMilliseconds(250));
        var map = await router.NextAsync();
        Assert.Equal(NatMappingOperation.Map, map.Request.Operation);
        Assert.Equal(endpoint.Port, map.Request.Identity.InternalPort);
        map.Succeed(46000, 20);
        // The next pump delay is registered only after the reconciler has adopted the result.
        await clock.WaitForTimerAsync(TimeSpan.FromMilliseconds(250));
        var display = NatPortMappingStatusProjection.Create(session.ReadMappingState());
        Assert.Equal(46000, display.ExternalPort);
        Assert.Equal(120U, display.Lease!.RequestedLifetimeSeconds);
        Assert.Equal(20U, display.Lease.ConfirmedLifetimeSeconds);
        Assert.Equal(NatDisplayReachability.NotChecked, display.ExternalReachability);
        var cleanup = session.CleanupAsync();
        var unmap = await router.NextAsync();
        Assert.Equal(NatMappingOperation.Unmap, unmap.Request.Operation);
        Assert.Empty(session.ReadMappingState().RegisteredInternalPorts);
        unmap.Succeed();
        Assert.Equal(NatShutdownOutcome.Completed, await cleanup.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(router.Contains(map.Request.Identity));
        Assert.True(session.Completion.IsCompletedSuccessfully);
        Assert.All(session.ReadMappingState().Mappings, item => Assert.False(item.OperationPending));
    }

    [Fact]
    public async Task ProductionSession_UnansweredUnmap_UsesExistingFiniteCleanupSemantics()
    {
        var router = new FakeNatRouter();
        var clock = new AcceptanceClock();
        await using var session = new NovaAcceptanceRelaySession(Options with { AutoPortMapping = true }, router, new FixedNetwork(), clock);
        await clock.WaitForTimerAsync(TimeSpan.FromMilliseconds(250));
        await session.StartAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(250));
        var map = await router.NextAsync();
        map.Succeed();
        await clock.WaitForTimerAsync(TimeSpan.FromMilliseconds(250));
        var cleanup = session.CleanupAsync();
        var unmap = await router.NextAsync();
        Assert.Equal(NatMappingOperation.Unmap, unmap.Request.Operation);
        await clock.WaitForTimerAsync(TimeSpan.FromSeconds(2));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(NatShutdownOutcome.CleanupUnconfirmed, await cleanup.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Empty(session.ReadMappingState().RegisteredInternalPorts);
        var retained = Assert.Single(session.ReadMappingState().Mappings);
        Assert.Equal(NatMappingStatus.UnmapUnconfirmed, retained.Status);
        Assert.True(retained.RouterMayContainMapping);
        Assert.True(session.Completion.IsCompletedSuccessfully);
        // Finish the controlled late operation after the budget, so the test leaves
        // no deliberately pending fake backend. It must not cause another send.
        unmap.Succeed();
        var count = router.Count;
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(count, router.Count);
    }

    [Fact]
    public async Task ProductionHarness_DurationDrainsRealListenerWithMappingOff()
    {
        var clock = new AcceptanceClock();
        var router = new FakeNatRouter();
        NovaAcceptanceRelaySession? session = null;
        using var output = new StringWriter();
        var run = NovaAcceptanceRelayHarness.RunAsync(Options, output, CancellationToken.None,
            options => session = new NovaAcceptanceRelaySession(options, router, new FixedNetwork(), clock), clock);
        using var started = Record(output, "ListenerStarted");
        var port = started.RootElement.GetProperty("BoundPort").GetInt32();
        Assert.NotEqual(0, port);
        clock.Advance(Options.Duration);
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("Stopped", session!.ListenerState);
        Assert.True(session.Completion.IsCompletedSuccessfully);
        Assert.Empty(session.ReadMappingState().RegisteredInternalPorts);
        Assert.Equal(0, router.Count);
        var probe = new TcpListener(IPAddress.Any, port);
        probe.Start();
        probe.Stop();
    }

    private sealed class FixedNetwork : INatNetworkSource
    {
        public NatNetworkIdentity? GetCurrentNetwork() => NatPortMappingTestHarness.Home;
    }

    private static JsonDocument Record(StringWriter output, string code) =>
        JsonDocument.Parse(output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Last(line => line.Contains("\"Code\":\"" + code + "\"", StringComparison.Ordinal)));

    private sealed class FakeSession : INovaAcceptanceRelaySession
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource cleanupDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task<NatShutdownOutcome>? cleanup;
        public NatPortMappingReadState Mapping { get; set; } = new(false, 1, NatPortMappingTestHarness.Home,
            120, DateTimeOffset.UtcNow, [], [], []);
        public Exception? StartFailure { get; init; }
        public bool DelayStartup { get; init; }
        public bool DelayCleanup { get; init; }
        public Task<IPEndPoint>? StartupTask { get; private set; }
        public NatShutdownOutcome Outcome { get; init; }
        public int CleanupCount { get; private set; }
        public Task Completion => completion.Task;
        public string ListenerState { get; private set; } = "Created";
        public NatPortMappingReadState ReadMappingState() => Mapping;

        public Task<IPEndPoint> StartAsync(CancellationToken cancellationToken)
        {
            if (StartFailure is not null) { throw StartFailure; }
            if (DelayStartup)
            {
                var pending = new TaskCompletionSource<IPEndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
                return StartupTask = pending.Task;
            }
            ListenerState = "Running";
            return Task.FromResult(new IPEndPoint(IPAddress.Any, 45000));
        }

        public void EndListener() => completion.TrySetResult();
        public void FinishCleanup() => cleanupDone.TrySetResult();
        public Task<NatShutdownOutcome> CleanupAsync() => cleanup ??= CleanupCoreAsync();
        private async Task<NatShutdownOutcome> CleanupCoreAsync()
        {
            CleanupCount++;
            if (DelayCleanup) { await cleanupDone.Task; }
            ListenerState = "Stopped";
            completion.TrySetResult();
            return Outcome;
        }
    }

    private sealed class AcceptanceClock : TimeProvider
    {
        private readonly ManualNatTimeProvider inner = new();
        private readonly Channel<TimeSpan> created = Channel.CreateUnbounded<TimeSpan>();
        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();
        public override long GetTimestamp() => inner.GetTimestamp();
        public override long TimestampFrequency => inner.TimestampFrequency;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = inner.CreateTimer(callback, state, dueTime, period);
            created.Writer.TryWrite(dueTime);
            return timer;
        }
        public void Advance(TimeSpan duration) => inner.Advance(duration);
        public async Task WaitForTimerAsync(TimeSpan duration)
        {
            while (await created.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)) != duration) { }
        }
    }
}
