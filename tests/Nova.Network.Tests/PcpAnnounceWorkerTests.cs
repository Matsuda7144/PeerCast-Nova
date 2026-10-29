using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Protocol.Pcp;
using static PeerCast.Nova.Network.Tests.AnnounceTestData;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpAnnounceWorkerTests
{
    [Fact]
    public async Task SilentTrackerContinuesAtThirtySixtyNinetySecondsAndCoalescesMetadata()
    {
        using var clock = new AnnounceManualTimeProvider();
        var connector = new SilentProductionConnector();
        await using var owner = new PcpAnnounceOwner(connector, timeProvider: clock);
        var advertised = new IPEndPoint(IPAddress.Parse("198.41.0.4"), 17144);
        var tracker = new IPEndPoint(IPAddress.Parse("198.41.0.5"), 7144);
        var sid = Guid.NewGuid();
        var identity = new PcpAnnounceIdentity(1, Guid.NewGuid(), sid, sid, owner.BroadcastId);
        var capability = new PublishingAnnounceCapability(identity, Guid.NewGuid(),
            new(IPAddress.Parse("192.168.1.2"), 7144), advertised, tracker, () => true);
        PcpAnnounceDocument Snapshot(string name) => new(
            new PeerCast.Nova.Core.Channels.ChannelMetadataSnapshot(name, "", "", "https://example.com/", "FLV", 128), advertised);
        var oleh = PcpHeloCodec.CreateOleh("silent tracker", Guid.NewGuid());
        connector.Connection.ReplaceHandshake(PcpAtom.Parent(oleh.Name, oleh.Children.Concat(new[]
        {
            PcpAtom.Leaf(PcpKnownAtomNames.HeloPort, PcpWireValueCodec.EncodeUInt16(17144)),
            PcpAtom.Leaf(PcpKnownAtomNames.HeloRemoteIp, PcpWireValueCodec.EncodeLegacyIpv4(advertised.Address)),
        }).ToArray()));
        var worker = owner.Start(1, identity.ChannelId, sid, sid, PcpAnnounceEndpoint.Parse("pcp://198.41.0.5:7144"),
            Snapshot("Motion + tone"), PcpAnnounceAccess.ForProduction(tracker), capability: capability);
        await connector.Connection.NextWriteAsync();
        Assert.Equal("Motion + tone", Name(Decode(await connector.Connection.NextWriteAsync())));
        await clock.TimerCreated.Task.WaitAsync(Deadline);
        await connector.Connection.IdleReadStarted.Task.WaitAsync(Deadline);
        foreach (var seconds in new[] { 30, 60, 90 })
        {
            clock.Advance(TimeSpan.FromSeconds(30));
            Assert.Equal("Motion + tone", Name(Decode(await connector.Connection.NextWriteAsync())));
            Assert.Equal(seconds, clock.GetUtcNow().ToUnixTimeSeconds());
            Assert.False(worker.Completion.IsCompleted);
            Assert.True(worker.TrackerReport?.CanAnnounce(capability));
            Assert.False(connector.Connection.Closed);
            Assert.Equal(1, connector.Connection.PendingReads);
        }
        connector.Connection.BlockWriteAt = 6;
        Assert.True(worker.Update(worker.Identity, Snapshot("in-flight")));
        await connector.Connection.Blocked.Task.WaitAsync(Deadline);
        for (var i = 0; i < 10; i++) Assert.True(worker.Update(worker.Identity, Snapshot($"latest{i}")));
        connector.Connection.Release.TrySetResult();
        Assert.Equal("in-flight", Name(Decode(await connector.Connection.NextWriteAsync())));
        Assert.Equal("latest9", Name(Decode(await connector.Connection.NextWriteAsync())));
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal("latest9", Name(Decode(await connector.Connection.NextWriteAsync())));
        Assert.Equal(1, connector.Calls);
        Assert.Equal(1, connector.Connection.MaxActiveWrites);
        var result = await worker.StopAsync(PcpAnnounceStopReason.Finish).WaitAsync(Deadline);
        Assert.True(result.StopNoticeSent && result.QuitSent && result.CleanupProven);
        Assert.Equal(PcpAnnounceFailure.None, result.Failure);
        Assert.Equal(0, connector.Connection.PendingReads);
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task SilentReadIsJoinedOnFinishEosDisposeOrCancellation(int stop)
    {
        var connector = new AnnounceFakeConnector();
        using var cancel = new CancellationTokenSource();
        await using var owner = new PcpAnnounceOwner(connector);
        var worker = Start(owner, cancellationToken: cancel.Token);
        await connector.Connection.NextWriteAsync(); await connector.Connection.NextWriteAsync();
        await connector.Connection.IdleReadStarted.Task.WaitAsync(Deadline);
        if (stop == 3) cancel.Cancel();
        else if (stop == 2) await worker.DisposeAsync().AsTask().WaitAsync(Deadline);
        else await worker.StopAsync((PcpAnnounceStopReason)stop).WaitAsync(Deadline);
        var result = await worker.Completion.WaitAsync(Deadline);
        Assert.Equal(stop == 3 ? PcpAnnounceStopReason.Cancelled : (PcpAnnounceStopReason)stop, result.StopReason);
        Assert.True(result.StopNoticeSent && result.QuitSent && result.CleanupProven);
        Assert.Equal(0, connector.Connection.PendingReads);
        Assert.True(connector.Connection.Closed);
        Assert.False(worker.Update(worker.Identity, Document("late")));
    }

    [Fact]
    public async Task NonCooperativeSilentReadCannotProveCleanupOrPermitNextGeneration()
    {
        var connector = new AnnounceFakeConnector();
        connector.Connection.IgnoreIdleReadCancellation = true;
        await using var owner = new PcpAnnounceOwner(connector, new() { ShutdownTimeout = TimeSpan.FromMilliseconds(150) });
        var worker = Start(owner);
        await connector.Connection.NextWriteAsync(); await connector.Connection.NextWriteAsync();
        await connector.Connection.IdleReadStarted.Task.WaitAsync(Deadline);
        try
        {
            var result = await worker.StopAsync(PcpAnnounceStopReason.Finish).WaitAsync(Deadline);
            Assert.False(result.CleanupProven);
            Assert.Equal(1, connector.Connection.PendingReads);
            Assert.Equal(PcpAnnounceState.CleanupIncomplete, worker.State);
            Assert.True(connector.Connection.Aborted);
            Assert.Throws<InvalidOperationException>(() => Start(owner, 2));
        }
        finally { connector.Connection.ReadRelease.TrySetResult(PcpAnnounceCodec.CreateQuit()); }
        await connector.Connection.IdleReadFinished.Task.WaitAsync(Deadline);
        await worker.Completion.WaitAsync(Deadline);
        Assert.Equal(0, connector.Connection.PendingReads);
        Assert.False(worker.CleanupProven);
        Assert.Throws<InvalidOperationException>(() => Start(owner, 2));
    }
    [Fact]
    public void IdleReadMustNotInstallATimeoutOnEitherClock()
    {
        // Fake time alone cannot catch the original system-clock WaitAsync(60s).
        // Inspect this narrow read path as well, so the untouched original fails immediately.
        var method = typeof(PcpAnnounceWorker).GetMethod("ReadControlAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var body = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            .GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? method;
        var il = body.GetMethodBody()!.GetILAsByteArray()!;
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(code => unchecked((ushort)code.Value));
        for (var offset = 0; offset < il.Length;)
        {
            ushort value = il[offset++];
            if (value == 0xfe) value = (ushort)(0xfe00 | il[offset++]);
            var code = opcodes[value];
            if (code.OperandType == OperandType.InlineMethod)
            {
                var called = body.Module.ResolveMethod(BitConverter.ToInt32(il, offset),
                    body.DeclaringType!.GetGenericArguments(), null)!;
                Assert.DoesNotContain(called.GetParameters(), parameter => parameter.ParameterType == typeof(TimeSpan));
            }
            offset += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => 4,
            };
        }
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task NormalStopSendsReceivingFalseThenOneQuitAndCloses(int stopReason)
    {
        var reason = (PcpAnnounceStopReason)stopReason;
        var connector = new AnnounceFakeConnector();
        await using var owner = new PcpAnnounceOwner(connector);
        var worker = Start(owner);
        var heloBytes = await connector.Connection.NextWriteAsync();
        var helo = Decode(heloBytes[12..]);
        var initial = Decode(await connector.Connection.NextWriteAsync());
        Assert.Equal(worker.Identity.SenderSessionId, PcpHeloCodec.ParseHelo(helo).SessionId);
        Assert.Equal(worker.Identity.PublishingSessionId, worker.Identity.SenderSessionId);
        Assert.Equal(owner.BroadcastId, PcpWireValueCodec.DecodeGuid(Field(helo, "bcid").Content.Span));
        Assert.Equal((byte)0, PcpWireValueCodec.EncodeGuid(owner.BroadcastId)[0]);
        Assert.NotEqual(owner.BroadcastId, worker.Identity.SenderSessionId);
        Assert.Equal("Motion + tone", Name(initial));
        var stop = worker.StopAsync(reason);
        Assert.Same(stop, worker.StopAsync(PcpAnnounceStopReason.Dispose));
        var result = await stop.WaitAsync(Deadline);
        Assert.True(result.AnnounceSent && result.StopNoticeSent && result.QuitSent && result.CleanupProven);
        Assert.Equal(reason, result.StopReason);
        var stopped = Decode(await connector.Connection.NextWriteAsync());
        Assert.Equal(0, Field(Field(stopped, "host"), "flg1").Content.Span[0] & 0x10);
        Assert.Equal(PcpKnownAtomNames.Quit, Decode(await connector.Connection.NextWriteAsync()).Name);
        Assert.False(connector.Connection.TryWrite(out _));
        Assert.False(worker.Update(worker.Identity, Document("late")));
        Assert.Equal(PcpAnnounceState.Stopped, worker.State);
        Assert.True(connector.Connection.Closed);
    }

    [Fact]
    public async Task PeriodicUpdateUsesThirtySecondsAndLatestMetadata()
    {
        using var clock = new AnnounceManualTimeProvider();
        var connector = new AnnounceFakeConnector();
        await using var owner = new PcpAnnounceOwner(connector, timeProvider: clock);
        var worker = Start(owner);
        await connector.Connection.NextWriteAsync();
        await connector.Connection.NextWriteAsync();
        await clock.TimerCreated.Task.WaitAsync(Deadline);
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.False(connector.Connection.TryWrite(out _));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("Motion + tone", Name(Decode(await connector.Connection.NextWriteAsync())));
        Assert.True(worker.Update(worker.Identity, Document("最新Metadata")));
        Assert.Equal("最新Metadata", Name(Decode(await connector.Connection.NextWriteAsync())));
        Assert.Equal(1, connector.Connection.MaxActiveWrites);
        Assert.Equal(PcpAnnounceFailure.None, (await worker.StopAsync(PcpAnnounceStopReason.Finish)).Failure);
    }

    [Fact]
    public async Task UpdatesAreCoalescedWhileAWriteIsInFlight()
    {
        var connector = new AnnounceFakeConnector();
        connector.Connection.BlockWriteAt = 2;
        await using var owner = new PcpAnnounceOwner(connector);
        var worker = Start(owner);
        await connector.Connection.Blocked.Task.WaitAsync(Deadline);
        for (var i = 0; i < 100; i++) Assert.True(worker.Update(worker.Identity, Document($"update{i}")));
        connector.Connection.Release.TrySetResult();
        await connector.Connection.NextWriteAsync(); // HELO
        await connector.Connection.NextWriteAsync(); // Initial snapshot, already in flight.
        Assert.Equal("update99", Name(Decode(await connector.Connection.NextWriteAsync())));
        await worker.StopAsync(PcpAnnounceStopReason.Finish);
        Assert.Equal(1, connector.Connection.MaxActiveWrites);
    }

    [Fact]
    public async Task OnlyExplicitNewGenerationCanStartAfterActualCleanupAndBcidIsStable()
    {
        var connector = new AnnounceFakeConnector();
        await using var owner = new PcpAnnounceOwner(connector);
        var first = Start(owner);
        await connector.Connection.NextWriteAsync(); await connector.Connection.NextWriteAsync();
        Assert.Throws<InvalidOperationException>(() => Start(owner, 1));
        Assert.Throws<InvalidOperationException>(() => Start(owner, 2));
        await first.StopAsync(PcpAnnounceStopReason.ObsNormalEos);
        connector.Connect = _ => Task.FromResult<IPcpAnnounceConnection>(new AnnounceFakeConnection());
        var second = Start(owner, 2);
        Assert.Equal(first.Identity.BroadcastId, second.Identity.BroadcastId);
        Assert.NotEqual(first.Identity.PublishingSessionId, second.Identity.PublishingSessionId);
        Assert.NotEqual(first.Identity.ChannelId, second.Identity.ChannelId);
        Assert.False(first.Update(second.Identity, Document("wrong generation")));
        Assert.False(second.Update(first.Identity, Document("old generation")));
        await first.StopAsync(PcpAnnounceStopReason.Dispose);
        Assert.False(second.Completion.IsCompleted);
        Assert.True((await second.StopAsync(PcpAnnounceStopReason.Dispose)).CleanupProven);
    }

    [Fact]
    public async Task LateWritePastDeadlineCannotAuthorizeNextGenerationOrOverwriteTerminalState()
    {
        var connector = new AnnounceFakeConnector();
        connector.Connection.BlockWriteAt = 2;
        var options = new PcpAnnounceOptions { ShutdownTimeout = TimeSpan.FromMilliseconds(150) };
        await using var owner = new PcpAnnounceOwner(connector, options);
        var worker = Start(owner);
        try
        {
            await connector.Connection.Blocked.Task.WaitAsync(Deadline);
            var stopped = await worker.StopAsync(PcpAnnounceStopReason.Finish).WaitAsync(Deadline);
            Assert.False(stopped.CleanupProven);
            Assert.True(connector.Connection.Aborted);
            Assert.Equal(PcpAnnounceState.CleanupIncomplete, worker.State);
            Assert.Throws<InvalidOperationException>(() => Start(owner, 2));
        }
        finally { connector.Connection.Release.TrySetResult(); }
        await worker.Completion.WaitAsync(Deadline);
        Assert.False(worker.CleanupProven);
        Assert.Equal(PcpAnnounceState.CleanupIncomplete, worker.State);
        Assert.Throws<InvalidOperationException>(() => Start(owner, 2));
    }

    [Fact]
    public async Task CloseDeadlineIsNotCleanupProofAndLateCloseDoesNotReopenOwner()
    {
        var connector = new AnnounceFakeConnector();
        connector.Connection.DelayClose = true;
        await using var owner = new PcpAnnounceOwner(connector, new() { ShutdownTimeout = TimeSpan.FromMilliseconds(150) });
        var worker = Start(owner);
        await connector.Connection.NextWriteAsync(); await connector.Connection.NextWriteAsync();
        var result = await worker.StopAsync(PcpAnnounceStopReason.Dispose).WaitAsync(Deadline);
        Assert.False(result.CleanupProven);
        Assert.Throws<InvalidOperationException>(() => Start(owner, 2));
        connector.Connection.CloseRelease.TrySetResult();
        await worker.Completion.WaitAsync(Deadline);
        Assert.Equal(PcpAnnounceState.CleanupIncomplete, worker.State);
    }

    [Fact]
    public async Task TrackerFailureDoesNotReconnectAndStopDeliveryRemainsUnknown()
    {
        var connector = new AnnounceFakeConnector();
        await using var owner = new PcpAnnounceOwner(connector);
        var worker = Start(owner);
        await connector.Connection.NextWriteAsync(); await connector.Connection.NextWriteAsync();
        connector.Connection.Disconnect();
        var result = await worker.Completion.WaitAsync(Deadline);
        Assert.Equal(PcpAnnounceState.Failed, worker.State);
        Assert.True(result.CleanupProven);
        Assert.False(result.StopNoticeSent || result.QuitSent);
        Assert.Equal(1, connector.Calls);
    }

    [Fact]
    public async Task ExplicitCancellationStopsTheGenerationAndLeavesNoWorker()
    {
        var connector = new AnnounceFakeConnector();
        using var cancel = new CancellationTokenSource();
        await using var owner = new PcpAnnounceOwner(connector);
        var worker = Start(owner, cancellationToken: cancel.Token);
        await connector.Connection.NextWriteAsync(); await connector.Connection.NextWriteAsync();
        cancel.Cancel();
        var result = await worker.Completion.WaitAsync(Deadline);
        Assert.Equal(PcpAnnounceStopReason.Cancelled, result.StopReason);
        Assert.True(result.CleanupProven);
    }

    [Fact]
    public async Task FailedStopNoticeAndQuitAreNotReportedAsSuccessfulDelivery()
    {
        var connector = new AnnounceFakeConnector();
        await using var owner = new PcpAnnounceOwner(connector);
        var worker = Start(owner);
        await connector.Connection.NextWriteAsync(); await connector.Connection.NextWriteAsync();
        connector.Connection.ThrowOnWrite = true;
        var result = await worker.StopAsync(PcpAnnounceStopReason.Finish);
        Assert.False(result.StopNoticeSent || result.QuitSent);
        Assert.True(result.CleanupProven);
        Assert.Equal(PcpAnnounceFailure.Transport, result.Failure);
    }

    [Fact]
    public async Task ConnectorThatReturnsAfterItsDeadlineIsClosedAndNeverUsed()
    {
        var connector = new AnnounceFakeConnector();
        var late = new TaskCompletionSource<IPcpAnnounceConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connector.Connect = _ => { entered.TrySetResult(); return late.Task; };
        await using var owner = new PcpAnnounceOwner(connector, new() { ConnectTimeout = TimeSpan.FromMilliseconds(100), ShutdownTimeout = TimeSpan.FromMilliseconds(150) });
        var worker = Start(owner);
        await entered.Task.WaitAsync(Deadline);
        var result = await worker.Completion.WaitAsync(Deadline);
        Assert.False(result.CleanupProven);
        Assert.Equal(PcpAnnounceFailure.Timeout, result.Failure);
        late.TrySetResult(connector.Connection);
        // Completion of the late owned connection must include Close; an explicit barrier observes it.
        await connector.Connection.CloseObserved.Task.WaitAsync(Deadline);
        Assert.False(connector.Connection.TryWrite(out _));
        Assert.Throws<InvalidOperationException>(() => Start(owner, 2));
    }

    [Fact]
    public async Task DisabledAndNotReadyNeverCallConnectorEvenWithAnExternalTracker()
    {
        foreach (var access in new[] { PcpAnnounceAccess.Disabled, PcpAnnounceAccess.NotReady, Access })
        {
            var connector = new AnnounceFakeConnector { Connect = _ => throw new InvalidOperationException("must not connect") };
            await using var owner = new PcpAnnounceOwner(connector);
            var worker = Start(owner, access: access, endpoint: PcpAnnounceEndpoint.Parse("pcp://tracker.example:7144"));
            var result = await worker.Completion.WaitAsync(Deadline);
            Assert.Equal(access.Enabled ? PcpAnnounceState.NotReady : PcpAnnounceState.Disabled, worker.State);
            Assert.Equal(0, connector.Calls);
            Assert.True(result.CleanupProven);
        }
    }

    [Theory]
    [InlineData("http://example.com/index.txt")]
    [InlineData("pcp://user:pass@example.com:7144")]
    [InlineData("pcp://example.com/channel")]
    [InlineData("pcp://example.com/ignored/..")]
    [InlineData(" pcp://example.com")]
    [InlineData("pcp://example.com/?x=1")]
    [InlineData("pcp://example.com/#x")]
    [InlineData("pcp://example.com:0")]
    [InlineData("pcp://[::1]:7144")]
    [InlineData("pcp://")]
    public void InvalidTrackerEndpointsAreRejected(string value) => Assert.Throws<ArgumentException>(() => PcpAnnounceEndpoint.Parse(value));

    [Fact]
    public void PublicMetadataIsBoundedAndCredentialUrlsAreRejected()
    {
        var endpoint = new IPEndPoint(IPAddress.Parse("192.0.2.42"), 7144);
        Assert.Throws<ArgumentException>(() => new PcpAnnounceDocument(new(name: "test", comment: new string('x', 4096)), endpoint));
        Assert.Throws<ArgumentException>(() => new PcpAnnounceDocument(new(name: "test", contactUrl: "https://user:secret@example.com/"), endpoint));
        Assert.Throws<ArgumentException>(() => new PcpAnnounceDocument(new(name: "test", track: new(ContactUrl: "file:///C:/private")), endpoint));
    }

    [Fact]
    public async Task ProductionDefaultIsDisabledAndMismatchedRelayIdentityIsRejected()
    {
        var connector = new AnnounceFakeConnector();
        await using var owner = new PcpAnnounceOwner(connector);
        var sid = Guid.NewGuid();
        var worker = owner.Start(1, Guid.NewGuid(), sid, sid, Endpoint, Document());
        await worker.Completion;
        Assert.Equal(0, connector.Calls);
        Assert.Equal(PcpAnnounceState.Disabled, worker.State);
        Assert.Throws<ArgumentException>(() => owner.Start(2, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Endpoint, Document()));
        Assert.Throws<ArgumentException>(() => PcpAnnounceAccess.ForLoopbackFixture(new(IPAddress.Parse("192.0.2.42"), 7144)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PcpAnnounceLoopbackConnector().ConnectAsync(new(IPAddress.Parse("192.0.2.42"), 7144), default));
    }

    [Fact]
    public async Task AnnounceFailureCannotStopTheIndependentPublishingHost()
    {
        await using var host = new ObsPublishChannelHost(new() { Ingest = new() { Port = 0 }, Http = new() { Port = 0 } });
        host.Start();
        var connector = new AnnounceFakeConnector();
        await using var owner = new PcpAnnounceOwner(connector);
        var worker = owner.Start(1, host.ChannelId.Value, host.SessionId.Value, host.SessionId.Value, Endpoint, Document(), Access);
        await connector.Connection.NextWriteAsync(); await connector.Connection.NextWriteAsync();
        connector.Connection.Receive(PcpAnnounceCodec.CreateQuit());
        var result = await worker.Completion.WaitAsync(Deadline);
        Assert.Equal(PcpAnnounceFailure.TrackerQuit, result.Failure);
        Assert.False(host.Completion.IsCompleted);
        Assert.True(host.IsRegistered);
        Assert.Equal(PeerCast.Nova.Core.Channels.ChannelSessionState.Negotiating, host.Snapshot.State);
    }

    [Fact]
    public async Task TrackerControlFloodHasABoundedProcessingBudget()
    {
        var connector = new AnnounceFakeConnector();
        await using var owner = new PcpAnnounceOwner(connector);
        var worker = Start(owner);
        await connector.Connection.NextWriteAsync(); await connector.Connection.NextWriteAsync();
        for (var i = 0; i < 100; i++) connector.Connection.Receive(PcpAtom.Parent(PcpAtomName.FromAscii("root")));
        var result = await worker.Completion.WaitAsync(Deadline);
        Assert.Equal(PcpAnnounceFailure.Protocol, result.Failure);
        Assert.True(result.CleanupProven);
        Assert.Equal(1, connector.Calls);
    }

    [Fact]
    public async Task WriteTimeoutDoesNotAbandonTheOutstandingWriteOrStartASecondGeneration()
    {
        var connector = new AnnounceFakeConnector();
        connector.Connection.BlockWriteAt = 2;
        await using var owner = new PcpAnnounceOwner(connector,
            new() { WriteTimeout = TimeSpan.FromMilliseconds(100), ShutdownTimeout = TimeSpan.FromMilliseconds(150) });
        var worker = Start(owner);
        try
        {
            await connector.Connection.Blocked.Task.WaitAsync(Deadline);
            var result = await worker.Completion.WaitAsync(Deadline);
            Assert.Equal(PcpAnnounceFailure.Timeout, result.Failure);
            Assert.False(result.CleanupProven);
            Assert.Throws<InvalidOperationException>(() => Start(owner, 2));
        }
        finally { connector.Connection.Release.TrySetResult(); }
    }
    private sealed class SilentProductionConnector : IPcpAnnounceConnector
    {
        internal AnnounceFakeConnection Connection { get; } = new();
        internal int Calls { get; private set; }
        public Task<IPcpAnnounceConnection> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
        {
            Assert.Equal("198.41.0.5", endpoint.Address.ToString());
            Calls++;
            return Task.FromResult<IPcpAnnounceConnection>(Connection);
        }
    }
}
