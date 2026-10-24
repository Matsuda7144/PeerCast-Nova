using System.Net.Sockets;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.Network.Tests;

public sealed class ObsRtmpQuiescenceTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Close_during_a_sliced_message_finishes_it_once_and_rejects_the_next_message()
    {
        var entered = Signal();
        var release = Signal();
        var consumer = new ObsRtmpTestConsumer
        {
            BeforeAccept = async (packet, token) =>
            {
                if (packet.Sequence != 2) return;
                entered.SetResult();
                await release.Task.WaitAsync(Deadline, CancellationToken.None);
                Assert.False(token.IsCancellationRequested);
            },
        };
        await using var host = Create(consumer);
        host.Start();
        using var client = await ConfigureAsync(host, consumer);
        var body = new byte[] { 0xAF, 1 }.Concat(new byte[300]).ToArray();
        await client.SendAsync(8, 1, 0, body);
        await entered.Task.WaitAsync(Deadline);
        // Buffered on the wire while processing is blocked; it is NOT admitted.
        await client.SendAsync(8, 1, 0, ObsFlvPublishingSessionTests.Audio);
        var port = host.LocalEndPoint.Port;
        var first = host.RequestQuiescenceAsync(Deadline);
        var second = host.RequestQuiescenceAsync(Deadline);
        Assert.False(first.IsCompleted);
        try
        {
            using var extra = new TcpClient(AddressFamily.InterNetwork);
            await Assert.ThrowsAnyAsync<SocketException>(() => extra.ConnectAsync(
                System.Net.IPAddress.Loopback, port));
        }
        finally { release.TrySetResult(); }
        var result = await first;
        Assert.Equal(ObsRtmpQuiescenceOutcome.Completed, result.Outcome);
        Assert.Equal(result, await second);
        AssertFinalCounts(result.FinalResult!, consumer);
        Assert.Equal(5, result.FinalResult!.AcceptedPacketCount);
        Assert.Equal(ObsRtmpIngestEndReason.Quiesced, result.FinalResult.EndReason);
        Assert.Equal(ObsFlvPublishingSession.CreateTag(8, 0, body),
            consumer.Packets.Skip(2).SelectMany(packet => packet.Payload).ToArray());
        await host.DisposeAsync();
        await host.DisposeAsync();
        Assert.Equal(result.FinalResult, await host.Completion);
        Assert.Equal(result, await host.RequestQuiescenceAsync(Deadline));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Blocked_accept_or_handshake_is_quiesced_without_admitting_media(bool handshake)
    {
        var probe = new TransportProbe();
        await using var host = Create(new(), probe.Observation);
        host.Start();
        await probe.AcceptWaiting.Task.WaitAsync(Deadline);
        using var client = new TcpClient(AddressFamily.InterNetwork);
        if (handshake)
        {
            await client.ConnectAsync(host.LocalEndPoint);
            await (await probe.Stream.Task.WaitAsync(Deadline)).WaitForPendingAfterAsync(0);
        }
        var result = await host.RequestQuiescenceAsync(Deadline);
        Assert.Equal(ObsRtmpQuiescenceOutcome.Completed, result.Outcome);
        Assert.Equal(0, result.FinalResult!.AcceptedPacketCount);
        Assert.Equal(0, result.FinalResult.AcceptedByteCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Blocked_read_or_incomplete_wire_message_is_not_admitted(bool partial)
    {
        var consumer = new ObsRtmpTestConsumer();
        var probe = new TransportProbe();
        await using var host = Create(consumer, probe.Observation);
        host.Start();
        using var client = await ConfigureAsync(host, consumer);
        var stream = await probe.Stream.Task.WaitAsync(Deadline);
        await stream.WaitForPendingAfterAsync(0);
        if (partial)
        {
            var before = stream.ReadBytes;
            var wire = ObsRtmpTestClient.WireMessage(8, 1, 0,
                new byte[] { 0xAF, 1 }.Concat(new byte[300]).ToArray());
            await client.SendWireAsync(wire[..20]);
            await stream.WaitForPendingAfterAsync(before + 20);
        }
        var result = await host.RequestQuiescenceAsync(Deadline);
        Assert.Equal(ObsRtmpQuiescenceOutcome.Completed, result.Outcome);
        Assert.Equal(2, result.FinalResult!.AcceptedPacketCount);
        AssertFinalCounts(result.FinalResult, consumer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bounded_wait_timeout_or_cancellation_does_not_abort_admitted_consumer(bool cancel)
    {
        var entered = Signal();
        var release = Signal();
        var consumer = new ObsRtmpTestConsumer
        {
            BeforeAccept = async (packet, token) =>
            {
                if (packet.Sequence != 2) return;
                entered.SetResult();
                await release.Task.WaitAsync(Deadline, CancellationToken.None);
                Assert.False(token.IsCancellationRequested);
            },
        };
        await using var host = Create(consumer);
        host.Start();
        using var client = await ConfigureAsync(host, consumer);
        await client.SendAsync(8, 1, 0, ObsFlvPublishingSessionTests.Audio);
        await entered.Task.WaitAsync(Deadline);
        using var cancelled = new CancellationTokenSource();
        var wait = host.RequestQuiescenceAsync(cancel ? Deadline : TimeSpan.FromMilliseconds(30), cancelled.Token);
        if (cancel) cancelled.Cancel();
        try
        {
            var result = await wait.WaitAsync(Deadline);
            Assert.Equal(cancel ? ObsRtmpQuiescenceOutcome.Cancelled : ObsRtmpQuiescenceOutcome.TimedOut, result.Outcome);
            Assert.Null(result.FinalResult); // Unsettled counters are never presented as final.
            Assert.False(host.Completion.IsCompleted);
        }
        finally { release.TrySetResult(); }
        var final = await host.Completion.WaitAsync(Deadline);
        Assert.Equal(ObsRtmpIngestEndReason.Quiesced, final.EndReason);
        Assert.Equal(3, final.AcceptedPacketCount);
        AssertFinalCounts(final, consumer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Processing_failure_or_forced_cancel_is_not_quiescence_success(bool cancel)
    {
        var entered = Signal();
        var release = Signal();
        var consumer = new ObsRtmpTestConsumer
        {
            BeforeAccept = async (packet, token) =>
            {
                if (packet.Sequence != 2) return;
                entered.SetResult();
                if (cancel) await Task.Delay(Timeout.InfiniteTimeSpan, token);
                else
                {
                    await release.Task.WaitAsync(Deadline, CancellationToken.None);
                    throw new IOException("Private consumer failure text must not be returned.");
                }
            },
        };
        await using var host = Create(consumer);
        host.Start();
        using var client = await ConfigureAsync(host, consumer);
        await client.SendAsync(8, 1, 0, ObsFlvPublishingSessionTests.Audio);
        await entered.Task.WaitAsync(Deadline);
        var wait = host.RequestQuiescenceAsync(Deadline);
        if (cancel) await host.DisposeAsync();
        else release.SetResult();
        var result = await wait;
        Assert.Equal(cancel ? ObsRtmpQuiescenceOutcome.Cancelled : ObsRtmpQuiescenceOutcome.Failed, result.Outcome);
        Assert.Equal(cancel ? ObsRtmpIngestEndReason.Cancelled : ObsRtmpIngestEndReason.ConsumerRejected, result.FinalResult!.EndReason);
        Assert.Equal(2, result.FinalResult.AcceptedPacketCount);
        AssertFinalCounts(result.FinalResult, consumer);
    }

    [Fact]
    public async Task Already_observed_OBS_EOS_remains_a_separate_fact()
    {
        var consumer = new ObsRtmpTestConsumer();
        await using var host = Create(consumer);
        host.Start();
        using var client = await ConfigureAsync(host, consumer);
        await client.SendCommandAsync(0, "deleteStream", 5.0, null, 1.0);
        var final = await host.Completion.WaitAsync(Deadline);
        Assert.Equal(ObsRtmpIngestEndReason.PublisherStopped, final.EndReason);
        var result = await host.RequestQuiescenceAsync(Deadline);
        Assert.Equal(ObsRtmpQuiescenceOutcome.AlreadyEnded, result.Outcome);
        Assert.Equal(final, result.FinalResult);
    }

    [Fact]
    public async Task Admission_and_close_have_one_ordering_and_cannot_reopen()
    {
        using var ordered = new ObsRtmpIngressAdmission();
        Assert.True(ordered.TryAdmit());
        ordered.Close();
        Assert.False(ordered.TryAdmit());
        for (var i = 0; i < 100; i++)
        {
            using var admission = new ObsRtmpIngressAdmission();
            var race = Task.Run(admission.TryAdmit);
            admission.Close();
            await race;
            Assert.False(admission.TryAdmit());
            Assert.True(admission.ReadStop.IsCancellationRequested);
            admission.Close();
        }
    }

    [Fact]
    public async Task Message_admission_racing_close_delivers_all_slices_or_none()
    {
        for (var i = 0; i < 12; i++)
        {
            var consumer = new ObsRtmpTestConsumer();
            await using var host = Create(consumer);
            host.Start();
            using var client = await ConfigureAsync(host, consumer);
            var go = Signal();
            var body = new byte[] { 0xAF, 1 }.Concat(new byte[300]).ToArray();
            var send = Task.Run(async () =>
            {
                await go.Task;
                try { await client.SendAsync(8, 1, 0, body); }
                catch (IOException) { } // Close may win before the wire write.
                catch (SocketException) { }
            });
            var close = Task.Run(async () =>
            {
                await go.Task;
                return await host.RequestQuiescenceAsync(Deadline);
            });
            go.SetResult();
            await send.WaitAsync(Deadline);
            var result = await close.WaitAsync(Deadline);
            Assert.Equal(ObsRtmpQuiescenceOutcome.Completed, result.Outcome);
            Assert.Contains(result.FinalResult!.AcceptedPacketCount, new long[] { 2, 5 });
            AssertFinalCounts(result.FinalResult, consumer);
            if (result.FinalResult.AcceptedPacketCount == 5)
                Assert.Equal(ObsFlvPublishingSession.CreateTag(8, 0, body),
                    consumer.Packets.Skip(2).SelectMany(packet => packet.Payload).ToArray());
        }
    }

    [Fact]
    public async Task Invalid_requests_and_pre_cancelled_wait_do_not_close_admission()
    {
        await using var host = Create(new());
        Assert.Throws<InvalidOperationException>(() => { _ = host.RequestQuiescenceAsync(Deadline); });
        host.Start();
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = host.RequestQuiescenceAsync(TimeSpan.Zero); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = host.RequestQuiescenceAsync(TimeSpan.FromMinutes(2)); });
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Equal(ObsRtmpQuiescenceOutcome.Cancelled,
            (await host.RequestQuiescenceAsync(Deadline, cancelled.Token)).Outcome);
        Assert.False(host.Completion.IsCompleted);
        Assert.True(host.ReadStatus().Listening);
        Assert.Equal(ObsRtmpQuiescenceOutcome.Completed, (await host.RequestQuiescenceAsync(Deadline)).Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Complete_message_admission_has_deterministic_close_before_and_after_orders(bool admitted)
    {
        var entered = Signal();
        var release = Signal();
        var consumer = new ObsRtmpTestConsumer();
        void Pause(RtmpMessage message)
        {
            if (message.Type != 8 || message.Payload.Length != 302) return;
            entered.TrySetResult();
            release.Task.WaitAsync(Deadline).GetAwaiter().GetResult();
        }
        var observation = new ObsRtmpIngressObservation
        {
            MessageDecoded = admitted ? null : Pause,
            MessageAdmitted = admitted ? Pause : null,
        };
        await using var host = Create(consumer, observation);
        host.Start();
        using var client = await ConfigureAsync(host, consumer);
        var body = new byte[] { 0xAF, 1 }.Concat(new byte[300]).ToArray();
        await client.SendAsync(8, 1, 0, body);
        await entered.Task.WaitAsync(Deadline);
        ObsRtmpQuiescenceResult result;
        try
        {
            var close = host.RequestQuiescenceAsync(Deadline);
            Assert.False(close.IsCompleted);
            release.TrySetResult();
            result = await close.WaitAsync(Deadline);
        }
        finally { release.TrySetResult(); }
        Assert.Equal(ObsRtmpQuiescenceOutcome.Completed, result.Outcome);
        Assert.Equal(admitted ? 5 : 2, result.FinalResult!.AcceptedPacketCount);
        AssertFinalCounts(result.FinalResult, consumer);
        if (admitted)
            Assert.Equal(ObsFlvPublishingSession.CreateTag(8, 0, body),
                consumer.Packets.Skip(2).SelectMany(packet => packet.Payload).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Forced_stop_during_quiescence_keeps_only_completed_slices(bool startLifetime)
    {
        var entered = Signal();
        var consumer = new ObsRtmpTestConsumer
        {
            BeforeAccept = async (packet, token) =>
            {
                if (packet.Sequence != 3) return;
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
        };
        using var lifetime = new CancellationTokenSource();
        await using var host = Create(consumer);
        host.Start(lifetime.Token);
        using var client = await ConfigureAsync(host, consumer);
        await client.SendAsync(8, 1, 0, new byte[] { 0xAF, 1 }.Concat(new byte[300]).ToArray());
        await entered.Task.WaitAsync(Deadline);
        var close = host.RequestQuiescenceAsync(Deadline);
        var duplicate = host.RequestQuiescenceAsync(Deadline);
        Assert.False(close.IsCompleted);
        if (startLifetime) await lifetime.CancelAsync();
        else await Task.WhenAll(host.DisposeAsync().AsTask(), host.DisposeAsync().AsTask()).WaitAsync(Deadline);
        var result = await close.WaitAsync(Deadline);
        Assert.Equal(ObsRtmpQuiescenceOutcome.Cancelled, result.Outcome);
        Assert.Equal(ObsRtmpIngestEndReason.Cancelled, result.FinalResult!.EndReason);
        Assert.Equal(3, result.FinalResult.AcceptedPacketCount);
        AssertFinalCounts(result.FinalResult, consumer);
        Assert.Equal(result, await duplicate.WaitAsync(Deadline));
        await host.DisposeAsync();
        Assert.Equal(result.FinalResult, await host.Completion);
        AssertFinalCounts(result.FinalResult, consumer);
    }

    [Fact]
    public async Task Failure_on_second_slice_retains_first_slice_without_success_or_counter_inflation()
    {
        var entered = Signal();
        var release = Signal();
        var consumer = new ObsRtmpTestConsumer
        {
            BeforeAccept = async (packet, _) =>
            {
                if (packet.Sequence != 3) return;
                entered.TrySetResult();
                await release.Task.WaitAsync(Deadline, CancellationToken.None);
                throw new IOException("SECRET consumer failure");
            },
        };
        await using var host = Create(consumer);
        host.Start();
        using var client = await ConfigureAsync(host, consumer);
        await client.SendAsync(8, 1, 0, new byte[] { 0xAF, 1 }.Concat(new byte[300]).ToArray());
        await entered.Task.WaitAsync(Deadline);
        ObsRtmpQuiescenceResult result;
        try
        {
            var close = host.RequestQuiescenceAsync(Deadline);
            release.TrySetResult();
            result = await close.WaitAsync(Deadline);
        }
        finally { release.TrySetResult(); }
        Assert.Equal(ObsRtmpQuiescenceOutcome.Failed, result.Outcome);
        Assert.Equal(ObsRtmpIngestEndReason.ConsumerRejected, result.FinalResult!.EndReason);
        Assert.Equal(3, result.FinalResult.AcceptedPacketCount);
        AssertFinalCounts(result.FinalResult, consumer);
        Assert.Equal(128, consumer.Packets.ElementAt(2).Payload.Length);
        Assert.DoesNotContain("SECRET", System.Text.Json.JsonSerializer.Serialize(result));
        await host.DisposeAsync();
        Assert.Equal(result, await host.RequestQuiescenceAsync(Deadline));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Quiescence_and_duplicate_dispose_join_actual_pending_accept_handshake_or_read(int phase)
    {
        var consumer = new ObsRtmpTestConsumer();
        var probe = new TransportProbe(holdCancelledReads: phase != 0);
        await using var host = Create(consumer, probe.Observation);
        host.Start();
        await probe.AcceptWaiting.Task.WaitAsync(Deadline);
        using var raw = new TcpClient(AddressFamily.InterNetwork);
        using var publisher = phase == 2 ? await ConfigureAsync(host, consumer) : null;
        ObservedStream? stream = null;
        if (phase == 1) await raw.ConnectAsync(host.LocalEndPoint);
        if (phase != 0)
        {
            stream = await probe.Stream.Task.WaitAsync(Deadline);
            await stream.WaitForPendingAfterAsync(0);
        }
        var close = host.RequestQuiescenceAsync(Deadline);
        var duplicate = host.RequestQuiescenceAsync(Deadline);
        Task firstDispose;
        Task secondDispose;
        try
        {
            if (stream is not null) await stream.CancelledReadEntered.Task.WaitAsync(Deadline);
            firstDispose = host.DisposeAsync().AsTask();
            secondDispose = host.DisposeAsync().AsTask();
            if (stream is not null)
            {
                await probe.ForcedStopRequested.Task.WaitAsync(Deadline);
                Assert.False(host.Completion.IsCompleted);
                Assert.False(firstDispose.IsCompleted);
            }
        }
        finally { stream?.ReleaseCancelledRead.TrySetResult(); }
        await Task.WhenAll(firstDispose, secondDispose).WaitAsync(Deadline);
        var result = await close.WaitAsync(Deadline);
        Assert.Contains(result.Outcome, new[] { ObsRtmpQuiescenceOutcome.Completed, ObsRtmpQuiescenceOutcome.Cancelled });
        if (stream is not null) Assert.Equal(ObsRtmpQuiescenceOutcome.Cancelled, result.Outcome);
        Assert.Equal(result, await duplicate.WaitAsync(Deadline));
        Assert.Equal(result.FinalResult, await host.Completion);
        AssertFinalCounts(result.FinalResult!, consumer);
        Assert.False(host.ReadStatus().Listening);
        await host.DisposeAsync();
        Assert.Equal(result, await host.RequestQuiescenceAsync(Deadline));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(600000001L)]
    public async Task Invalid_wait_boundaries_do_not_close_started_host(long ticks)
    {
        await using var host = Create(new());
        host.Start();
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = host.RequestQuiescenceAsync(TimeSpan.FromTicks(ticks)); });
        Assert.True(host.ReadStatus().Listening);
        Assert.False(host.Completion.IsCompleted);
        var result = await host.RequestQuiescenceAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(ObsRtmpQuiescenceOutcome.Completed, result.Outcome);
    }

    [Fact]
    public async Task Incomplete_bootstrap_is_not_flushed_into_an_implicit_head()
    {
        var configured = Signal();
        var consumer = new ObsRtmpTestConsumer();
        var observation = new ObsRtmpIngressObservation
        {
            MessageAdmitted = message =>
            {
                if (message.Type == 8) configured.TrySetResult();
            },
        };
        await using var host = Create(consumer, observation);
        host.Start();
        using var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.LocalEndPoint);
        await client.PublishAsync();
        await client.SendAsync(8, 1, 0, ObsFlvPublishingSessionTests.AudioConfiguration);
        await configured.Task.WaitAsync(Deadline);
        var result = await host.RequestQuiescenceAsync(Deadline);
        Assert.Equal(ObsRtmpQuiescenceOutcome.Completed, result.Outcome);
        Assert.Empty(consumer.Packets);
        Assert.Equal(0, result.FinalResult!.AcceptedPacketCount);
        Assert.Equal(0, result.FinalResult.AcceptedByteCount);
    }

    [Fact]
    public async Task Constructor_does_not_bind_and_publisher_wait_outlives_connected_io_timeout()
    {
        var reservation = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((System.Net.IPEndPoint)reservation.LocalEndpoint).Port;
        try
        {
            await using var unused = new ObsRtmpIngestHost(
                ObsFlvPublishingSessionTests.Channel, ObsFlvPublishingSessionTests.Session, new ObsRtmpTestConsumer(),
                new ObsRtmpIngestOptions { Port = port });
            Assert.Throws<InvalidOperationException>(() => unused.LocalEndPoint);
            Assert.Throws<InvalidOperationException>(() => { _ = unused.RequestQuiescenceAsync(Deadline); });
        }
        finally { reservation.Stop(); }
        var probe = new TransportProbe();
        await using var host = new ObsRtmpIngestHost(
            ObsFlvPublishingSessionTests.Channel, ObsFlvPublishingSessionTests.Session, new ObsRtmpTestConsumer(),
            new ObsRtmpIngestOptions { Port = 0, IoTimeout = TimeSpan.FromMilliseconds(30) }, probe.Observation);
        host.Start();
        await probe.AcceptWaiting.Task.WaitAsync(Deadline);
        // This tests a deadline contract, not race ordering or sleep-based success.
        await Assert.ThrowsAsync<TimeoutException>(() => host.Completion.WaitAsync(TimeSpan.FromMilliseconds(100)));
        Assert.True(host.ReadStatus().Listening);
        Assert.False(host.Completion.IsCompleted);
        Assert.Equal(ObsRtmpQuiescenceOutcome.Completed, (await host.RequestQuiescenceAsync(Deadline)).Outcome);
    }

    [Fact]
    public async Task Connected_handshake_io_timeout_remains_a_terminal_timeout_with_final_counts()
    {
        var probe = new TransportProbe();
        await using var host = new ObsRtmpIngestHost(
            ObsFlvPublishingSessionTests.Channel, ObsFlvPublishingSessionTests.Session, new ObsRtmpTestConsumer(),
            new ObsRtmpIngestOptions { Port = 0, IoTimeout = TimeSpan.FromMilliseconds(100) }, probe.Observation);
        host.Start();
        using var raw = new TcpClient(AddressFamily.InterNetwork);
        await raw.ConnectAsync(host.LocalEndPoint);
        await (await probe.Stream.Task.WaitAsync(Deadline)).WaitForPendingAfterAsync(0);
        var final = await host.Completion.WaitAsync(Deadline);
        var result = await host.RequestQuiescenceAsync(Deadline);
        Assert.Equal(ObsRtmpQuiescenceOutcome.TimedOut, result.Outcome);
        Assert.Equal(final, result.FinalResult);
        Assert.Equal(0, final.AcceptedPacketCount);
    }

    [Fact]
    public async Task Additional_client_is_rejected_and_reject_worker_joins_quiescence()
    {
        var consumer = new ObsRtmpTestConsumer();
        await using var host = Create(consumer);
        host.Start();
        using var publisher = await ConfigureAsync(host, consumer);
        using var extra = new TcpClient(AddressFamily.InterNetwork);
        await extra.ConnectAsync(host.LocalEndPoint);
        using var deadline = new CancellationTokenSource(Deadline);
        Assert.Equal(0, await extra.GetStream().ReadAsync(new byte[1], deadline.Token));
        var result = await host.RequestQuiescenceAsync(Deadline);
        Assert.Equal(ObsRtmpQuiescenceOutcome.Completed, result.Outcome);
        AssertFinalCounts(result.FinalResult!, consumer);
        Assert.Equal(2, result.FinalResult!.AcceptedPacketCount);
    }

    [Fact]
    public async Task Unexpected_ingress_exception_is_sanitized_internal_failure_not_success()
    {
        var observation = new ObsRtmpIngressObservation { ObserveStream = _ => throw new InvalidOperationException("SECRET observation") };
        await using var host = Create(new(), observation);
        host.Start();
        using var raw = new TcpClient(AddressFamily.InterNetwork);
        await raw.ConnectAsync(host.LocalEndPoint);
        var final = await host.Completion.WaitAsync(Deadline);
        Assert.Equal(ObsRtmpIngestEndReason.InternalFailure, final.EndReason);
        var result = await host.RequestQuiescenceAsync(Deadline);
        Assert.Equal(ObsRtmpQuiescenceOutcome.Failed, result.Outcome);
        Assert.Equal(final, result.FinalResult);
        Assert.DoesNotContain("SECRET", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task Concurrent_callers_have_independent_waits_and_share_eventual_final_counters()
    {
        var entered = Signal();
        var release = Signal();
        var consumer = new ObsRtmpTestConsumer
        {
            BeforeAccept = async (packet, token) =>
            {
                if (packet.Sequence != 2) return;
                entered.TrySetResult();
                await release.Task.WaitAsync(Deadline, CancellationToken.None);
                Assert.False(token.IsCancellationRequested);
            },
        };
        await using var host = Create(consumer);
        host.Start();
        using var client = await ConfigureAsync(host, consumer);
        await client.SendAsync(8, 1, 0, ObsFlvPublishingSessionTests.Audio);
        await entered.Task.WaitAsync(Deadline);
        using var caller = new CancellationTokenSource();
        var shortWait = host.RequestQuiescenceAsync(TimeSpan.FromMilliseconds(30));
        var cancelledWait = host.RequestQuiescenceAsync(Deadline, caller.Token);
        var eventual = host.RequestQuiescenceAsync(Deadline);
        try
        {
            await caller.CancelAsync();
            var cancelled = await cancelledWait.WaitAsync(Deadline);
            var timedOut = await shortWait.WaitAsync(Deadline);
            Assert.Equal(ObsRtmpQuiescenceOutcome.Cancelled, cancelled.Outcome);
            Assert.Equal(ObsRtmpQuiescenceOutcome.TimedOut, timedOut.Outcome);
            Assert.Null(cancelled.FinalResult);
            Assert.Null(timedOut.FinalResult);
            Assert.False(eventual.IsCompleted);
            Assert.False(host.Completion.IsCompleted);
            Assert.False(host.ReadStatus().Listening);
        }
        finally { release.TrySetResult(); }
        var result = await eventual.WaitAsync(Deadline);
        Assert.Equal(ObsRtmpQuiescenceOutcome.Completed, result.Outcome);
        Assert.Equal(result.FinalResult, await host.Completion);
        AssertFinalCounts(result.FinalResult!, consumer);
        await host.DisposeAsync();
        Assert.Equal(result, await host.RequestQuiescenceAsync(Deadline));
        AssertFinalCounts(result.FinalResult!, consumer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Forced_stop_before_message_admission_is_not_reported_as_successful_quiescence(bool startLifetime)
    {
        var entered = Signal();
        var release = Signal();
        var forced = Signal();
        var consumer = new ObsRtmpTestConsumer();
        var observation = new ObsRtmpIngressObservation
        {
            ForcedStopRequested = () => forced.TrySetResult(),
            MessageDecoded = message =>
            {
                if (message.Type != 8 || message.Payload.Length != 302) return;
                entered.TrySetResult();
                release.Task.WaitAsync(Deadline).GetAwaiter().GetResult();
            },
        };
        using var lifetime = new CancellationTokenSource();
        await using var host = Create(consumer, observation);
        host.Start(lifetime.Token);
        using var client = await ConfigureAsync(host, consumer);
        await client.SendAsync(8, 1, 0, new byte[] { 0xAF, 1 }.Concat(new byte[300]).ToArray());
        await entered.Task.WaitAsync(Deadline);
        var close = host.RequestQuiescenceAsync(Deadline);
        Task? dispose = null;
        try
        {
            if (startLifetime) await lifetime.CancelAsync();
            else
            {
                dispose = host.DisposeAsync().AsTask();
                await forced.Task.WaitAsync(Deadline);
            }
        }
        finally { release.TrySetResult(); }
        if (dispose is not null) await dispose.WaitAsync(Deadline);
        var result = await close.WaitAsync(Deadline);
        Assert.Equal(ObsRtmpQuiescenceOutcome.Cancelled, result.Outcome);
        Assert.Equal(ObsRtmpIngestEndReason.Cancelled, result.FinalResult!.EndReason);
        Assert.Equal(2, result.FinalResult.AcceptedPacketCount);
        AssertFinalCounts(result.FinalResult, consumer);
    }

    private sealed class TransportProbe(bool holdCancelledReads = false)
    {
        internal TaskCompletionSource AcceptWaiting { get; } = Signal();
        internal TaskCompletionSource ForcedStopRequested { get; } = Signal();
        internal TaskCompletionSource<ObservedStream> Stream { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ObsRtmpIngressObservation Observation => new()
        {
            AcceptWaiting = () => AcceptWaiting.TrySetResult(),
            ForcedStopRequested = () => ForcedStopRequested.TrySetResult(),
            ObserveStream = stream =>
            {
                var observed = new ObservedStream(stream, holdCancelledReads);
                Stream.TrySetResult(observed);
                return observed;
            },
        };
    }

    private sealed class ObservedStream(Stream inner, bool holdCancelledReads) : Stream
    {
        private readonly object _gate = new();
        private TaskCompletionSource _changed = Signal();
        private bool _pending;
        private long _readBytes;
        internal long ReadBytes { get { lock (_gate) return _readBytes; } }
        internal TaskCompletionSource CancelledReadEntered { get; } = Signal();
        internal TaskCompletionSource ReleaseCancelledRead { get; } = Signal();
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = inner.ReadAsync(buffer, cancellationToken);
            lock (_gate) { _pending = !read.IsCompleted; Pulse(); }
            try
            {
                var count = await read.ConfigureAwait(false);
                lock (_gate) { _readBytes += count; }
                return count;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && holdCancelledReads)
            {
                CancelledReadEntered.TrySetResult();
                await ReleaseCancelledRead.Task.WaitAsync(Deadline, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            finally { lock (_gate) { _pending = false; Pulse(); } }
        }
        internal async Task WaitForPendingAfterAsync(long bytes)
        {
            while (true)
            {
                Task changed;
                lock (_gate)
                {
                    if (_pending && _readBytes >= bytes) return;
                    changed = _changed.Task;
                }
                await changed.WaitAsync(Deadline);
            }
        }
        private void Pulse()
        {
            var changed = _changed;
            _changed = Signal();
            changed.TrySetResult();
        }
    }

    private static async Task<ObsRtmpTestClient> ConfigureAsync(ObsRtmpIngestHost host, ObsRtmpTestConsumer consumer)
    {
        var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.LocalEndPoint);
        await client.PublishAsync();
        await client.SendAsync(9, 1, 0, ObsFlvPublishingSessionTests.VideoConfiguration);
        await client.SendAsync(8, 1, 0, ObsFlvPublishingSessionTests.AudioConfiguration);
        await client.SendAsync(9, 1, 0, ObsFlvPublishingSessionTests.Idr);
        await consumer.WaitForCountAsync(2);
        return client;
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static ObsRtmpIngestHost Create(ObsRtmpTestConsumer consumer, ObsRtmpIngressObservation? observation = null) => new(
        ObsFlvPublishingSessionTests.Channel, ObsFlvPublishingSessionTests.Session, consumer,
        new ObsRtmpIngestOptions { Port = 0, MaximumPacketBytes = 128, MaximumBootstrapBytes = 128 }, observation);

    private static void AssertFinalCounts(ObsRtmpIngestResult result, ObsRtmpTestConsumer consumer)
    {
        Assert.Equal(consumer.Packets.Count, result.AcceptedPacketCount);
        Assert.Equal(consumer.Packets.Sum(packet => (long)packet.Payload.Length), result.AcceptedByteCount);
        Assert.Equal(Enumerable.Range(0, consumer.Packets.Count).Select(value => (long)value),
            consumer.Packets.Select(packet => packet.Sequence));
        long position = 0;
        foreach (var packet in consumer.Packets)
        {
            Assert.Equal((uint)position, packet.WirePosition);
            Assert.Equal(position, packet.StreamPosition);
            Assert.Equal(ObsFlvPublishingSessionTests.Channel, packet.ChannelId);
            Assert.Equal(ObsFlvPublishingSessionTests.Session, packet.SessionId);
            position += packet.Payload.Length;
        }
    }
}
