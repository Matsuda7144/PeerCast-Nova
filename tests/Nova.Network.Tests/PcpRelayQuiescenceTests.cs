using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Policies;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Tests;

public sealed partial class PcpDownstreamRelayServerTests
{
    [Fact]
    public async Task Quiescence_preserves_existing_send_and_excludes_unrelated_fanout_children()
    {
        await using var fanout = new PcpRelayFanout();
        var writer = new GatedQuiescenceWriter();
        var unrelated = new QuiescenceSink();
        await using var other = fanout.AddChild(unrelated);
        await using var server = CreateQuiescenceServer(fanout, writer);
        using var deadline = new CancellationTokenSource(TestTimeout);
        var run = server.RunAsync(deadline.Token);
        await using var peer = await ConnectPeerAsync(server.LocalEndPoint, CreateTransportOptions(), deadline.Token);
        var endpoint = server.LocalEndPoint;
        await server.StopAcceptingAsync();
        Assert.False(run.IsCompleted);
        using (var extra = new TcpClient(AddressFamily.InterNetwork))
            await Assert.ThrowsAnyAsync<SocketException>(() => extra.ConnectAsync(endpoint.Address, endpoint.Port));
        // Closing admission must not prevent an existing child receiving data.
        await fanout.ConsumeAsync(CreatePacket([1, 2, 3]), deadline.Token);
        await writer.Entered.Task.WaitAsync(deadline.Token);
        await unrelated.Entered.Task.WaitAsync(deadline.Token);
        var receive = peer.ReadChannelPayloadAsync(deadline.Token);
        var drain = server.DrainAfterUpstreamEndAsync(TestTimeout);
        Assert.Same(drain, server.DrainAfterUpstreamEndAsync(TestTimeout));
        Assert.False(drain.IsCompleted);
        try
        {
            writer.Release.TrySetResult();
            var result = await drain.WaitAsync(deadline.Token);
            await receive;
            Assert.Equal(new byte[] { 1, 2, 3 }, peer.LastPayload);
            Assert.Equal(PcpRelayQuiescenceOutcome.Drained, result.Outcome);
            Assert.True(result.SendsDrained);
            Assert.True(result.CleanupCompleted);
            Assert.Equal(1, result.Snapshot.DeliveredPacketCount);
            Assert.Equal(3, result.Snapshot.DeliveredByteCount);
            Assert.Equal(0, result.Snapshot.DiscardedPacketCount);
            Assert.Equal(0, result.Snapshot.ActiveConnectionCount);
            Assert.Equal(PcpDownstreamRelayServerEndReason.Quiesced, (await run).EndReason);
            Assert.Equal(1, other.Snapshot.PendingPacketCount);
            Assert.False(unrelated.Token.IsCancellationRequested);
            // A borrowed fan-out is still open to another owner.
            await using var later = fanout.AddChild(new QuiescenceSink(released: true));
        }
        finally { writer.Release.TrySetResult(); unrelated.Release.TrySetResult(); }
    }

    [Fact]
    public async Task Quiescence_rejects_a_late_HTTP_handshake_from_an_already_accepted_connection()
    {
        await using var fanout = new PcpRelayFanout();
        await using var server = CreateServer(fanout, CreateTransportOptions());
        using var deadline = new CancellationTokenSource(TestTimeout);
        _ = server.RunAsync(deadline.Token);
        await using var connection = await new TcpTransportConnector(CreateTransportOptions())
            .ConnectAsync(server.LocalEndPoint.Address.ToString(), server.LocalEndPoint.Port, deadline.Token);
        await WaitForAcceptedCountAsync(server, 1, deadline.Token);
        await server.StopAcceptingAsync();
        await connection.WriteAsync(new PcpRelayRequest { ChannelId = ChannelId.Value }.Encode(), deadline.Token);
        var reader = PipeReader.Create(connection.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            var response = await PcpHttpHeaderBlockReader.ReadResponseAsync(reader, 4096, deadline.Token);
            Assert.Equal(503, response.StatusCode);
        }
        finally { await reader.CompleteAsync(); }
        await WaitForCompletedCountAsync(server, 1, deadline.Token);
        Assert.Equal(0, fanout.ActiveChildCount);
        Assert.Equal(1, server.Snapshot.AdmissionClosedCount);
        Assert.Equal(0, server.Snapshot.InternalFailureCount);
        var result = await server.DrainAfterUpstreamEndAsync(TestTimeout);
        Assert.Equal(PcpRelayQuiescenceOutcome.Drained, result.Outcome);
        Assert.True(result.CleanupCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Quiescence_timeout_or_wait_cancel_does_not_abort_a_blocked_send(bool cancel)
    {
        await using var fanout = new PcpRelayFanout();
        var writer = new GatedQuiescenceWriter();
        await using var server = CreateQuiescenceServer(fanout, writer);
        using var deadline = new CancellationTokenSource(TestTimeout);
        _ = server.RunAsync(deadline.Token);
        await using var peer = await ConnectPeerAsync(server.LocalEndPoint, CreateTransportOptions(), deadline.Token);
        await fanout.ConsumeAsync(CreatePacket([1, 2, 3]), deadline.Token);
        await writer.Entered.Task.WaitAsync(deadline.Token);
        await server.StopAcceptingAsync();
        using var waitCancel = new CancellationTokenSource();
        var task = server.DrainAfterUpstreamEndAsync(cancel ? TestTimeout : TimeSpan.FromMilliseconds(30), waitCancel.Token);
        if (cancel) waitCancel.Cancel();
        try
        {
            var result = await task.WaitAsync(deadline.Token);
            Assert.Equal(cancel ? PcpRelayQuiescenceOutcome.Cancelled : PcpRelayQuiescenceOutcome.TimedOut, result.Outcome);
            Assert.False(result.SendsDrained);
            Assert.False(result.CleanupCompleted);
            Assert.False(writer.Token.IsCancellationRequested);
            Assert.Equal(1, fanout.ActiveChildCount);
            writer.Release.TrySetResult();
            Assert.True(await fanout.WaitForIdleAsync(TestTimeout, deadline.Token));
            Assert.Same(task, server.DrainAfterUpstreamEndAsync(TestTimeout));
            await server.StopAsync(deadline.Token);
        }
        finally { writer.Release.TrySetResult(); }
    }

    [Fact]
    public async Task Quiescence_reports_send_failure_even_when_its_child_has_already_completed()
    {
        await using var fanout = new PcpRelayFanout();
        var writer = new GatedQuiescenceWriter(fail: true);
        await using var server = CreateQuiescenceServer(fanout, writer);
        using var deadline = new CancellationTokenSource(TestTimeout);
        _ = server.RunAsync(deadline.Token);
        await using var peer = await ConnectPeerAsync(server.LocalEndPoint, CreateTransportOptions(), deadline.Token);
        await fanout.ConsumeAsync(CreatePacket([1]), deadline.Token);
        await writer.Entered.Task.WaitAsync(deadline.Token);
        writer.Release.TrySetResult();
        await WaitForCompletedCountAsync(server, 1, deadline.Token);
        var result = await server.DrainAfterUpstreamEndAsync(TestTimeout);
        Assert.Equal(PcpRelayQuiescenceOutcome.DeliveryFailed, result.Outcome);
        Assert.False(result.SendsDrained);
        Assert.True(result.CleanupCompleted);
        Assert.Equal(1, result.Snapshot.WriteFailedCount);
        Assert.Equal(1, result.Snapshot.DiscardedPacketCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Quiescence_shutdown_race_preserves_cancellation_and_shares_disposal(bool dispose)
    {
        await using var fanout = new PcpRelayFanout();
        var writer = new GatedQuiescenceWriter();
        await using var server = CreateQuiescenceServer(fanout, writer);
        using var deadline = new CancellationTokenSource(TestTimeout);
        _ = server.RunAsync(deadline.Token);
        await using var peer = await ConnectPeerAsync(server.LocalEndPoint, CreateTransportOptions(), deadline.Token);
        await fanout.ConsumeAsync(CreatePacket([1]), deadline.Token);
        await writer.Entered.Task.WaitAsync(deadline.Token);
        await server.StopAcceptingAsync();
        var drain = server.DrainAfterUpstreamEndAsync(TestTimeout);
        if (dispose)
        {
            var first = server.DisposeAsync().AsTask();
            var second = server.DisposeAsync().AsTask();
            Assert.Same(first, second);
            await first.WaitAsync(deadline.Token);
        }
        else await server.StopAsync(deadline.Token);
        var result = await drain.WaitAsync(deadline.Token);
        Assert.Equal(PcpRelayQuiescenceOutcome.Cancelled, result.Outcome);
        Assert.False(result.SendsDrained);
        Assert.True(writer.Token.IsCancellationRequested);
        Assert.Equal(1, server.Snapshot.DiscardedPacketCount);
    }

    [Fact]
    public async Task Quiescence_concurrent_connections_do_not_register_after_close()
    {
        for (var i = 0; i < 12; i++)
        {
            await using var fanout = new PcpRelayFanout();
            await using var server = CreateServer(fanout, CreateTransportOptions());
            using var deadline = new CancellationTokenSource(TestTimeout);
            _ = server.RunAsync(deadline.Token);
            var endpoint = server.LocalEndPoint;
            var clients = Enumerable.Range(0, 4).Select(_ => new TcpClient(AddressFamily.InterNetwork)).ToArray();
            try
            {
                var connections = clients.Select(async client =>
                {
                    try { await client.ConnectAsync(endpoint, deadline.Token); }
                    catch (SocketException) { }
                }).ToArray();
                await server.StopAcceptingAsync();
                await Task.WhenAll(connections);
                var result = await server.DrainAfterUpstreamEndAsync(TestTimeout);
                Assert.Equal(PcpRelayQuiescenceOutcome.Drained, result.Outcome);
                Assert.True(result.CleanupCompleted);
                Assert.Equal(0, fanout.ActiveChildCount);
            }
            finally { foreach (var client in clients) client.Dispose(); }
        }
    }

    [Theory]
    [InlineData(false, false, true, PcpRelayQuiescenceOutcome.CleanupFailed)]
    [InlineData(false, true, false, PcpRelayQuiescenceOutcome.CleanupFailed)]
    [InlineData(true, true, false, PcpRelayQuiescenceOutcome.Cancelled)]
    [InlineData(true, false, true, PcpRelayQuiescenceOutcome.DeliveryFailed)]
    [InlineData(true, false, false, PcpRelayQuiescenceOutcome.Drained)]
    public void Quiescence_result_never_hides_unproven_cleanup(bool clean, bool cancel, bool failed, PcpRelayQuiescenceOutcome expected)
        => Assert.Equal(expected, PcpDownstreamRelayServer.ClassifyQuiescence(clean, cancel, failed));

    [Fact]
    public async Task Quiescence_invalid_and_pre_cancelled_requests_do_not_close_admission()
    {
        await using var fanout = new PcpRelayFanout();
        await using var server = CreateServer(fanout, CreateTransportOptions());
        Assert.Throws<InvalidOperationException>(() => { _ = server.StopAcceptingAsync(); });
        _ = server.RunAsync();
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = server.DrainAfterUpstreamEndAsync(TimeSpan.Zero); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = server.DrainAfterUpstreamEndAsync(TimeSpan.FromMinutes(2)); });
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Equal(PcpRelayQuiescenceOutcome.Cancelled, (await server.DrainAfterUpstreamEndAsync(TestTimeout, cancelled.Token)).Outcome);
        Assert.Equal(PcpDownstreamRelayServerState.Running, server.Snapshot.State);
        Assert.Equal(PcpRelayQuiescenceOutcome.Drained, (await server.DrainAfterUpstreamEndAsync(TestTimeout)).Outcome);
    }

    private static PcpDownstreamRelayServer CreateQuiescenceServer(PcpRelayFanout fanout, GatedQuiescenceWriter writer)
        => new(new IPEndPoint(IPAddress.Loopback, 0), fanout, ChannelId, LocalSessionId, CreateTransportOptions())
        { PacketWriterFactory = connection => { writer.Connection = connection; return writer; } };

    [Fact]
    public async Task Quiescence_preserves_cleanup_failure_evidence_from_an_uncooperative_send()
    {
        await using var fanout = new PcpRelayFanout(new PcpRelayFanoutOptions { ChildStopTimeout = TimeSpan.FromMilliseconds(30) });
        var writer = new GatedQuiescenceWriter(ignoreCancellation: true);
        await using var server = CreateQuiescenceServer(fanout, writer);
        using var deadline = new CancellationTokenSource(TestTimeout);
        var run = server.RunAsync(deadline.Token);
        await using var peer = await ConnectPeerAsync(server.LocalEndPoint, CreateTransportOptions(), deadline.Token);
        await fanout.ConsumeAsync(CreatePacket([1]), deadline.Token);
        await writer.Entered.Task.WaitAsync(deadline.Token);
        try
        {
            await server.StopAsync(deadline.Token);
            Assert.Equal(1, (await run).Snapshot.CleanupIncompleteCount);
            var result = await server.DrainAfterUpstreamEndAsync(TestTimeout);
            Assert.Equal(PcpRelayQuiescenceOutcome.CleanupFailed, result.Outcome);
            Assert.False(result.SendsDrained);
            Assert.False(result.CleanupCompleted);
            Assert.Equal(1, result.Snapshot.CleanupIncompleteCount);
        }
        finally { writer.Release.TrySetResult(); }
    }

    [Fact]
    public async Task Quiescence_registration_and_close_are_serialized_and_failed_children_are_retained_as_evidence()
    {
        for (var i = 0; i < 32; i++)
        {
            await using var fanout = new PcpRelayFanout();
            var admission = new PcpRelayChildAdmission();
            var add = Task.Run(() => admission.TryAddChild(fanout, new QuiescenceSink(released: true), out _));
            admission.Close();
            var accepted = await add;
            var scope = admission.CaptureClosed();
            Assert.Equal(accepted ? 1 : 0, scope.Children.Length);
            Assert.False(admission.TryAddChild(fanout, new QuiescenceSink(), out var rejected));
            Assert.Null(rejected);
        }
        await using var failedFanout = new PcpRelayFanout();
        var failedAdmission = new PcpRelayChildAdmission();
        Assert.True(failedAdmission.TryAddChild(failedFanout, new FailingQuiescenceSink(), out var child));
        await failedFanout.ConsumeAsync(CreatePacket([1]), CancellationToken.None);
        await child!.Completion.WaitAsync(TestTimeout);
        failedAdmission.Close();
        var failedScope = failedAdmission.CaptureClosed();
        Assert.Empty(failedScope.Children);
        Assert.True(failedScope.DeliveryFailed);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Quiescence_close_before_or_after_policy_reservation_prevents_registration(bool afterReserve)
    {
        await using var fanout = new PcpRelayFanout();
        using var policy = new RelayAdmissionPolicy(new(MaxRelaysTotal: 1));
        using var owner = policy.OpenChannel(ChannelId, UpstreamSessionId, RelayChannelKind.Broadcast);
        var admission = new PolicyAdmission(owner);
        var entered = Barrier();
        var release = Barrier();
        var observer = new PcpRelayQuiescenceObserver(async point =>
        {
            if (afterReserve && point == PcpRelayQuiescencePoint.AfterReserve)
            {
                entered.TrySetResult();
                await release.Task;
            }
        });
        await using var server = PolicyServer(fanout, admission, observer);
        using var deadline = new CancellationTokenSource(TestTimeout);
        _ = server.RunAsync(deadline.Token);
        await using var connection = await new TcpTransportConnector(CreateTransportOptions())
            .ConnectAsync(server.LocalEndPoint.Address.ToString(), server.LocalEndPoint.Port, deadline.Token);
        await WaitForAcceptedCountAsync(server, 1, deadline.Token);
        try
        {
            if (!afterReserve) await server.StopAcceptingAsync().WaitAsync(deadline.Token);
            await connection.WriteAsync(new PcpRelayRequest { ChannelId = ChannelId.Value }.Encode(), deadline.Token);
            if (afterReserve)
            {
                await entered.Task.WaitAsync(deadline.Token);
                Assert.Equal(1, policy.ReservedCount);
                await server.StopAcceptingAsync().WaitAsync(deadline.Token);
                Assert.Equal(1, policy.ReservedCount);
                release.TrySetResult();
            }
            var reader = PipeReader.Create(connection.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
            try
            {
                var response = await PcpHttpHeaderBlockReader.ReadResponseAsync(reader, 4096, deadline.Token);
                Assert.Equal(503, response.StatusCode);
            }
            finally { await reader.CompleteAsync(); }
            await WaitForCompletedCountAsync(server, 1, deadline.Token);
            var result = await server.DrainAfterUpstreamEndAsync(TestTimeout).WaitAsync(deadline.Token);
            Assert.Equal(PcpRelayQuiescenceOutcome.Drained, result.Outcome);
            Assert.Equal(afterReserve ? 1 : 0, admission.Attempts);
            Assert.Equal(afterReserve ? 1 : 0, admission.ReleaseCalls);
            Assert.Equal(0, policy.ReservedCount);
            Assert.Equal(0, fanout.ActiveChildCount);
            Assert.Equal(0, result.Snapshot.AdmissionCleanupPendingCount);
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Quiescence_policy_limit_and_retired_owner_cannot_be_bypassed(bool retired)
    {
        await using var fanout = new PcpRelayFanout();
        using var policy = new RelayAdmissionPolicy(new(MaxRelaysTotal: retired ? 1 : 0));
        using var owner = policy.OpenChannel(ChannelId, UpstreamSessionId, RelayChannelKind.Broadcast);
        if (retired) owner.Dispose();
        var admission = new PolicyAdmission(owner);
        await using var server = PolicyServer(fanout, admission);
        using var deadline = new CancellationTokenSource(TestTimeout);
        _ = server.RunAsync(deadline.Token);
        await using var peer = await ConnectPeerAsync(server.LocalEndPoint, CreateTransportOptions(), deadline.Token);
        Assert.Equal(PcpCompatibilityOutcome.RelayFull, peer.Handshake.Outcome);
        await WaitForCompletedCountAsync(server, 1, deadline.Token);
        Assert.Equal(0, fanout.ActiveChildCount);
        Assert.Equal(0, policy.ReservedCount);
        Assert.Equal(1, admission.Attempts);
        Assert.Equal(PcpRelayQuiescenceOutcome.Drained, (await server.DrainAfterUpstreamEndAsync(TestTimeout)).Outcome);
    }

    [Fact]
    public async Task Quiescence_successful_policy_reservation_is_released_once_after_cleanup()
    {
        await using var fanout = new PcpRelayFanout();
        using var policy = new RelayAdmissionPolicy(new(MaxRelaysTotal: 1));
        using var owner = policy.OpenChannel(ChannelId, UpstreamSessionId, RelayChannelKind.Broadcast);
        var admission = new PolicyAdmission(owner);
        await using var server = PolicyServer(fanout, admission);
        using var deadline = new CancellationTokenSource(TestTimeout);
        _ = server.RunAsync(deadline.Token);
        await using var peer = await ConnectPeerAsync(server.LocalEndPoint, CreateTransportOptions(), deadline.Token);
        Assert.Equal(1, policy.ReservedCount);
        await server.StopAcceptingAsync();
        Assert.Equal(1, policy.ReservedCount);
        var result = await server.DrainAfterUpstreamEndAsync(TestTimeout).WaitAsync(deadline.Token);
        await server.DisposeAsync();
        await server.DisposeAsync();
        Assert.Equal(PcpRelayQuiescenceOutcome.Drained, result.Outcome);
        Assert.True(result.CleanupCompleted);
        Assert.Equal(0, policy.ReservedCount);
        Assert.Equal(1, admission.ReleaseCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Quiescence_reservation_waits_for_both_actual_transport_and_worker_barriers(bool transportFirst)
    {
        var transport = Barrier();
        var worker = Barrier();
        var lease = new CountingLease();
        var cleanup = PcpDownstreamChildSession.ReleaseAdmissionAfterCleanupAsync(lease, transport.Task, worker.Task);
        Assert.False(cleanup.IsCompleted);
        (transportFirst ? transport : worker).TrySetResult();
        Assert.False(cleanup.IsCompleted);
        Assert.Equal(0, lease.Calls);
        (transportFirst ? worker : transport).TrySetResult();
        Assert.True(await cleanup.WaitAsync(TestTimeout));
        Assert.Equal(1, lease.Calls);
    }

    [Fact]
    public async Task Quiescence_failed_transport_proof_retains_reservation()
    {
        var lease = new CountingLease();
        var cleanup = PcpDownstreamChildSession.ReleaseAdmissionAfterCleanupAsync(lease,
            Task.FromException(new IOException("private transport detail")), Task.CompletedTask);
        Assert.False(await cleanup);
        Assert.Equal(0, lease.Calls);
    }

    [Fact]
    public async Task Quiescence_terminal_worker_fault_still_proves_callback_returned()
    {
        var lease = new CountingLease();
        Assert.True(await PcpDownstreamChildSession.ReleaseAdmissionAfterCleanupAsync(lease,
            Task.CompletedTask, Task.FromException(new IOException("private worker detail"))));
        Assert.Equal(1, lease.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Quiescence_tracks_success_false_and_fault_after_connection_history_is_pruned(int outcome)
    {
        await using var fanout = new PcpRelayFanout();
        await using var server = CreateServer(fanout, CreateTransportOptions());
        using var deadline = new CancellationTokenSource(TestTimeout);
        _ = server.RunAsync(deadline.Token);
        var proof = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.TrackAdmissionCleanup(proof.Task);
        Assert.Equal(1, server.Snapshot.AdmissionCleanupPendingCount);
        if (outcome == 2) proof.SetException(new IOException("private release detail"));
        else proof.SetResult(outcome == 0);
        var result = await server.DrainAfterUpstreamEndAsync(TestTimeout).WaitAsync(deadline.Token);
        Assert.Equal(outcome == 0 ? PcpRelayQuiescenceOutcome.Drained : PcpRelayQuiescenceOutcome.CleanupFailed, result.Outcome);
        Assert.Equal(0, result.Snapshot.AdmissionCleanupPendingCount);
        Assert.Equal(outcome == 0 ? 0 : 1, result.Snapshot.AdmissionCleanupFailedCount);
        Assert.Equal(outcome == 0, result.CleanupCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Quiescence_idle_is_not_reservation_cleanup_proof(bool cancel)
    {
        await using var fanout = new PcpRelayFanout();
        await using var server = CreateServer(fanout, CreateTransportOptions());
        using var deadline = new CancellationTokenSource(TestTimeout);
        var run = server.RunAsync(deadline.Token);
        var proof = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.TrackAdmissionCleanup(proof.Task);
        using var waitCancel = new CancellationTokenSource();
        try
        {
            var drain = server.DrainAfterUpstreamEndAsync(cancel ? TestTimeout : TimeSpan.FromMilliseconds(200), waitCancel.Token);
            await run.WaitAsync(deadline.Token); // Admission + sends + sessions settled; reservation still pending.
            Assert.Equal(0, server.Snapshot.ActiveConnectionCount);
            Assert.False(drain.IsCompleted);
            if (cancel) waitCancel.Cancel();
            var result = await drain.WaitAsync(deadline.Token);
            Assert.Equal(cancel ? PcpRelayQuiescenceOutcome.Cancelled : PcpRelayQuiescenceOutcome.TimedOut, result.Outcome);
            Assert.True(result.SendsDrained);
            Assert.False(result.CleanupCompleted);
            Assert.Equal(1, result.Snapshot.AdmissionCleanupPendingCount);
            Assert.False(proof.Task.IsCompleted);
            proof.SetResult(true);
            Assert.Equal(0, server.Snapshot.AdmissionCleanupPendingCount);
            Assert.Same(drain, server.DrainAfterUpstreamEndAsync(TestTimeout));
        }
        finally { proof.TrySetResult(true); }
    }

    [Fact]
    public async Task Quiescence_completed_connection_release_fault_is_preserved()
    {
        await using var fanout = new PcpRelayFanout();
        using var policy = new RelayAdmissionPolicy();
        using var owner = policy.OpenChannel(ChannelId, UpstreamSessionId, RelayChannelKind.Broadcast);
        var admission = new PolicyAdmission(owner, failRelease: true);
        await using var server = PolicyServer(fanout, admission);
        using var deadline = new CancellationTokenSource(TestTimeout);
        _ = server.RunAsync(deadline.Token);
        await using var peer = await ConnectPeerAsync(server.LocalEndPoint, CreateTransportOptions(), deadline.Token);
        await peer.SendQuitAsync(deadline.Token);
        await WaitForCompletedCountAsync(server, 1, deadline.Token);
        var result = await server.DrainAfterUpstreamEndAsync(TestTimeout);
        Assert.Equal(PcpRelayQuiescenceOutcome.CleanupFailed, result.Outcome);
        Assert.False(result.CleanupCompleted);
        Assert.Equal(1, result.Snapshot.AdmissionCleanupFailedCount);
        Assert.Equal(1, policy.ReservedCount);
        admission.LastReservation!.Dispose(); // Explicit fixture-only recovery of the throwing lease.
    }

    [Fact]
    public async Task Quiescence_final_check_observes_fanout_stop_after_initial_success()
    {
        await using var fanout = new PcpRelayFanout();
        var entered = Barrier();
        var release = Barrier();
        var observer = new PcpRelayQuiescenceObserver(async point =>
        {
            if (point == PcpRelayQuiescencePoint.AfterInitialFailureEvaluation)
            {
                entered.TrySetResult();
                await release.Task;
            }
        });
        await using var server = PolicyServer(fanout, null, observer);
        using var deadline = new CancellationTokenSource(TestTimeout);
        _ = server.RunAsync(deadline.Token);
        await using var peer = await ConnectPeerAsync(server.LocalEndPoint, CreateTransportOptions(), deadline.Token);
        try
        {
            var drain = server.DrainAfterUpstreamEndAsync(TestTimeout);
            await entered.Task.WaitAsync(deadline.Token);
            await fanout.DisposeAsync();
            await WaitForCompletedCountAsync(server, 1, deadline.Token);
            release.TrySetResult();
            var result = await drain.WaitAsync(deadline.Token);
            Assert.Equal(PcpRelayQuiescenceOutcome.DeliveryFailed, result.Outcome);
            Assert.False(result.SendsDrained);
            Assert.True(result.CleanupCompleted);
            Assert.Equal(1, result.Snapshot.FanoutStoppedCount);
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Quiescence_accept_and_listener_failure_settle_closure_barrier(int failureKind)
    {
        var failure = failureKind == 0 ? PcpRelayQuiescencePoint.BeforeAccept : PcpRelayQuiescencePoint.BeforeListenerStop;
        await using var fanout = new PcpRelayFanout();
        var observer = new PcpRelayQuiescenceObserver(point => point == failure
            ? ValueTask.FromException(new IOException("private listener detail")) : ValueTask.CompletedTask);
        await using var server = PolicyServer(fanout, null, observer);
        using var deadline = new CancellationTokenSource(TestTimeout);
        _ = server.RunAsync(deadline.Token);
        await Assert.ThrowsAsync<IOException>(() => server.StopAcceptingAsync().WaitAsync(deadline.Token));
        var result = await server.DrainAfterUpstreamEndAsync(TestTimeout).WaitAsync(deadline.Token);
        Assert.Equal(PcpRelayQuiescenceOutcome.Failed, result.Outcome);
        Assert.False(result.CleanupCompleted);
        Assert.Equal(1, result.Snapshot.AdmissionCloseFailedCount);
    }

    [Fact]
    public async Task Quiescence_failed_late_503_write_does_not_register_or_leak_connection()
    {
        await using var fanout = new PcpRelayFanout();
        var observer = new PcpRelayQuiescenceObserver(point => point == PcpRelayQuiescencePoint.BeforeClosedResponse
            ? ValueTask.FromException(new IOException("private write detail")) : ValueTask.CompletedTask);
        await using var server = PolicyServer(fanout, null, observer);
        using var deadline = new CancellationTokenSource(TestTimeout);
        _ = server.RunAsync(deadline.Token);
        await using var connection = await new TcpTransportConnector(CreateTransportOptions())
            .ConnectAsync(server.LocalEndPoint.Address.ToString(), server.LocalEndPoint.Port, deadline.Token);
        await WaitForAcceptedCountAsync(server, 1, deadline.Token);
        await server.StopAcceptingAsync();
        await connection.WriteAsync(new PcpRelayRequest { ChannelId = ChannelId.Value }.Encode(), deadline.Token);
        await WaitForCompletedCountAsync(server, 1, deadline.Token);
        Assert.Equal(0, fanout.ActiveChildCount);
        Assert.Equal(0, server.Snapshot.ActiveConnectionCount);
        Assert.Equal(0, server.Snapshot.InternalFailureCount);
        Assert.Equal(PcpRelayQuiescenceOutcome.Drained, (await server.DrainAfterUpstreamEndAsync(TestTimeout)).Outcome);
    }

    [Fact]
    public async Task Quiescence_duplicate_close_and_first_cancelled_wait_share_barriers()
    {
        await using var fanout = new PcpRelayFanout();
        var writer = new GatedQuiescenceWriter();
        await using var server = CreateQuiescenceServer(fanout, writer);
        using var deadline = new CancellationTokenSource(TestTimeout);
        _ = server.RunAsync(deadline.Token);
        await using var peer = await ConnectPeerAsync(server.LocalEndPoint, CreateTransportOptions(), deadline.Token);
        await fanout.ConsumeAsync(CreatePacket([1]), deadline.Token);
        await writer.Entered.Task.WaitAsync(deadline.Token);
        var firstClose = server.StopAcceptingAsync();
        Assert.Same(firstClose, server.StopAcceptingAsync());
        await firstClose;
        using var firstCancel = new CancellationTokenSource();
        try
        {
            var first = server.DrainAfterUpstreamEndAsync(TestTimeout, firstCancel.Token);
            var second = server.DrainAfterUpstreamEndAsync(TimeSpan.FromMilliseconds(1));
            Assert.Same(first, second);
            firstCancel.Cancel();
            Assert.Equal(PcpRelayQuiescenceOutcome.Cancelled, (await second.WaitAsync(deadline.Token)).Outcome);
            Assert.False(writer.Token.IsCancellationRequested);
        }
        finally { writer.Release.TrySetResult(); }
    }

    [Fact]
    public async Task Quiescence_forced_stop_during_reservation_wait_never_succeeds()
    {
        await using var fanout = new PcpRelayFanout();
        await using var server = CreateServer(fanout, CreateTransportOptions());
        using var deadline = new CancellationTokenSource(TestTimeout);
        var run = server.RunAsync(deadline.Token);
        var proof = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.TrackAdmissionCleanup(proof.Task);
        try
        {
            var drain = server.DrainAfterUpstreamEndAsync(TimeSpan.FromMilliseconds(50));
            await run.WaitAsync(deadline.Token);
            await server.StopAsync(deadline.Token);
            var result = await drain.WaitAsync(deadline.Token);
            Assert.Equal(PcpRelayQuiescenceOutcome.Cancelled, result.Outcome);
            Assert.False(result.CleanupCompleted);
            Assert.False(result.SendsDrained);
            Assert.False(proof.Task.IsCompleted);
        }
        finally { proof.TrySetResult(true); }
    }

    [Fact]
    public async Task Quiescence_wrong_channel_does_not_invoke_policy()
    {
        await using var fanout = new PcpRelayFanout();
        using var policy = new RelayAdmissionPolicy();
        using var owner = policy.OpenChannel(ChannelId, UpstreamSessionId, RelayChannelKind.Broadcast);
        var admission = new PolicyAdmission(owner);
        await using var server = PolicyServer(fanout, admission);
        using var deadline = new CancellationTokenSource(TestTimeout);
        _ = server.RunAsync(deadline.Token);
        await using var connection = await new TcpTransportConnector(CreateTransportOptions())
            .ConnectAsync(server.LocalEndPoint.Address.ToString(), server.LocalEndPoint.Port, deadline.Token);
        await connection.WriteAsync(new PcpRelayRequest { ChannelId = Guid.NewGuid() }.Encode(), deadline.Token);
        var reader = PipeReader.Create(connection.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        try { Assert.Equal(404, (await PcpHttpHeaderBlockReader.ReadResponseAsync(reader, 4096, deadline.Token)).StatusCode); }
        finally { await reader.CompleteAsync(); }
        await WaitForCompletedCountAsync(server, 1, deadline.Token);
        Assert.Equal(0, admission.Attempts);
        Assert.Equal(0, policy.ReservedCount);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Quiescence_forced_stop_after_initial_idle_check_remains_cancelled(bool dispose)
    {
        await using var fanout = new PcpRelayFanout();
        var entered = Barrier();
        var release = Barrier();
        var observer = new PcpRelayQuiescenceObserver(async point =>
        {
            if (point == PcpRelayQuiescencePoint.AfterInitialFailureEvaluation)
            {
                entered.TrySetResult();
                await release.Task;
            }
        });
        await using var server = PolicyServer(fanout, null, observer);
        using var deadline = new CancellationTokenSource(TestTimeout);
        _ = server.RunAsync(deadline.Token);
        try
        {
            var drain = server.DrainAfterUpstreamEndAsync(TestTimeout);
            await entered.Task.WaitAsync(deadline.Token);
            if (dispose) await server.DisposeAsync();
            else await server.StopAsync(deadline.Token);
            release.TrySetResult();
            var result = await drain.WaitAsync(deadline.Token);
            Assert.Equal(PcpRelayQuiescenceOutcome.Cancelled, result.Outcome);
            Assert.False(result.SendsDrained);
            Assert.False(result.CleanupCompleted);
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Quiescence_policy_total_and_per_channel_limits_keep_existing_reservation(bool total)
    {
        await using var fanout = new PcpRelayFanout();
        using var policy = new RelayAdmissionPolicy(total ? new(MaxRelaysTotal: 1) : new(MaxRelaysPerBroadcastChannel: 1));
        using var owner = policy.OpenChannel(ChannelId, UpstreamSessionId, RelayChannelKind.Broadcast);
        var admission = new PolicyAdmission(owner);
        await using var server = PolicyServer(fanout, admission);
        using var deadline = new CancellationTokenSource(TestTimeout);
        _ = server.RunAsync(deadline.Token);
        await using var first = await ConnectPeerAsync(server.LocalEndPoint, CreateTransportOptions(), deadline.Token);
        await using var denied = await ConnectPeerAsync(server.LocalEndPoint, CreateTransportOptions(), deadline.Token);
        Assert.Equal(PcpCompatibilityOutcome.Accepted, first.Handshake.Outcome);
        Assert.Equal(PcpCompatibilityOutcome.RelayFull, denied.Handshake.Outcome);
        await WaitForCompletedCountAsync(server, 1, deadline.Token);
        Assert.Equal(1, policy.ReservedCount);
        Assert.Equal(1, fanout.ActiveChildCount);
        Assert.Equal(0, admission.ReleaseCalls);
        Assert.Equal(PcpRelayQuiescenceOutcome.Drained, (await server.DrainAfterUpstreamEndAsync(TestTimeout)).Outcome);
        Assert.Equal(0, policy.ReservedCount);
        Assert.Equal(1, admission.ReleaseCalls);
    }

    [Fact]
    public async Task Quiescence_fanout_registration_failure_releases_policy_after_transport_cleanup()
    {
        await using var fanout = new PcpRelayFanout(new PcpRelayFanoutOptions { MaxChildren = 1 });
        var unrelated = new QuiescenceSink(released: true);
        await using var borrowed = fanout.AddChild(unrelated);
        using var policy = new RelayAdmissionPolicy();
        using var owner = policy.OpenChannel(ChannelId, UpstreamSessionId, RelayChannelKind.Broadcast);
        var admission = new PolicyAdmission(owner);
        await using var server = PolicyServer(fanout, admission);
        using var deadline = new CancellationTokenSource(TestTimeout);
        _ = server.RunAsync(deadline.Token);
        await using var denied = await ConnectPeerAsync(server.LocalEndPoint, CreateTransportOptions(), deadline.Token);
        Assert.Equal(PcpCompatibilityOutcome.RelayFull, denied.Handshake.Outcome);
        await WaitForCompletedCountAsync(server, 1, deadline.Token);
        Assert.Equal(PcpRelayQuiescenceOutcome.Drained, (await server.DrainAfterUpstreamEndAsync(TestTimeout)).Outcome);
        Assert.Equal(1, admission.ReleaseCalls);
        Assert.Equal(0, policy.ReservedCount);
        Assert.Equal(1, fanout.ActiveChildCount);
        Assert.False(borrowed.Completion.IsCompleted);
    }

    [Fact]
    public async Task Quiescence_prunes_successful_cleanup_history_and_preserves_failure_count()
    {
        await using var fanout = new PcpRelayFanout();
        await using var server = CreateServer(fanout, CreateTransportOptions());
        for (var i = 0; i < 128; i++) server.TrackAdmissionCleanup(Task.FromResult(true));
        server.TrackAdmissionCleanup(Task.FromResult(false));
        server.TrackAdmissionCleanup(Task.FromException<bool>(new IOException("private")));
        Assert.Equal(0, server.Snapshot.AdmissionCleanupPendingCount);
        Assert.Equal(2, server.Snapshot.AdmissionCleanupFailedCount);
        _ = server.RunAsync();
        var result = await server.DrainAfterUpstreamEndAsync(TestTimeout);
        Assert.Equal(PcpRelayQuiescenceOutcome.CleanupFailed, result.Outcome);
        Assert.Equal(2, result.Snapshot.AdmissionCleanupFailedCount);
    }

    [Fact]
    public async Task Quiescence_loopback_admission_close_keeps_mapping_demand_empty()
    {
        await using var mapping = new NatPortMappingTestHarness();
        await using var fanout = new PcpRelayFanout();
        await using var server = new PcpDownstreamRelayServer(new IPEndPoint(IPAddress.Loopback, 0),
            fanout, ChannelId, LocalSessionId, CreateTransportOptions(), portMapping: mapping.Manager);
        _ = server.RunAsync();
        await server.StopAcceptingAsync();
        await mapping.Manager.ReconcileAsync();
        Assert.Equal(0, mapping.Router.Count);
        Assert.Empty(mapping.Manager.Snapshot);
        Assert.Equal(PcpRelayQuiescenceOutcome.Drained, (await server.DrainAfterUpstreamEndAsync(TestTimeout)).Outcome);
    }

    private static TaskCompletionSource Barrier() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static PcpDownstreamRelayServer PolicyServer(PcpRelayFanout fanout,
        IPcpDownstreamRelayAdmission? admission, PcpRelayQuiescenceObserver? observer = null)
        => new(new IPEndPoint(IPAddress.Loopback, 0), fanout, ChannelId, LocalSessionId,
            CreateTransportOptions(), admission: admission)
        { QuiescenceObserver = observer };

    private sealed class CountingLease : IDisposable
    {
        public int Calls;
        public void Dispose() => Interlocked.Increment(ref Calls);
    }

    private sealed class PolicyAdmission(RelayAdmissionPolicy.Channel owner, bool failRelease = false) : IPcpDownstreamRelayAdmission
    {
        public ChannelId ChannelId => owner.ChannelId;
        public int Attempts;
        public int ReleaseCalls;
        public RelayAdmissionPolicy.Reservation? LastReservation;
        public bool TryReserve(long connectionId, [NotNullWhen(true)] out IDisposable? reservation,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Attempts);
            LastReservation = owner.TryReserve(connectionId, cancellationToken).Reservation;
            reservation = LastReservation is null ? null : new ObservedLease(this, LastReservation, failRelease);
            return reservation is not null;
        }

        private sealed class ObservedLease(PolicyAdmission admission, IDisposable inner, bool fail) : IDisposable
        {
            public void Dispose()
            {
                Interlocked.Increment(ref admission.ReleaseCalls);
                if (fail) throw new IOException("private reservation release detail");
                inner.Dispose();
            }
        }
    }

    private sealed class FailingQuiescenceSink : IPcpRelayChildSink
    {
        public ValueTask SendAsync(PcpRelayPacket packet, CancellationToken cancellationToken)
            => ValueTask.FromException(new IOException("Private sink detail."));
    }

    private sealed class GatedQuiescenceWriter(bool fail = false, bool ignoreCancellation = false) : IPcpDownstreamPacketWriter
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TcpTransportConnection Connection { get; set; } = null!;
        public CancellationToken Token { get; private set; }

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ignoreCancellation ? CancellationToken.None : cancellationToken);
            if (fail) throw new IOException("Private writer detail.");
            await Connection.WriteAsync(buffer, cancellationToken);
        }
    }

    private sealed class QuiescenceSink(bool released = false) : IPcpRelayChildSink
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }

        public async ValueTask SendAsync(PcpRelayPacket packet, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            Entered.TrySetResult();
            if (!released) await Release.Task.WaitAsync(cancellationToken);
        }
    }
}
