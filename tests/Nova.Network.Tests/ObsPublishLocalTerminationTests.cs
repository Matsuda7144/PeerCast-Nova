using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.Network.Tests;

public sealed class ObsPublishLocalTerminationTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ListeningWithoutPublisher_CanFinishLocallyWithoutObsEosOrRecordingRequirement()
    {
        await using var host = ObsPublishChannelHostTests.Create();
        host.Start();
        Assert.False(host.Completion.IsCompleted);
        var terminal = await host.RequestLocalTerminationAsync(Limit).WaitAsync(Limit);
        AssertCompleted(host, terminal);
        Assert.Equal(0, terminal.Ingress!.FinalResult!.AcceptedPacketCount);
        Assert.Equal(ObsRtmpIngestEndReason.Quiesced, terminal.Ingress.FinalResult.EndReason);
        Assert.Equal(ObsPublishWriterEvidence.NotRequired, terminal.WriterEvidence);
        Assert.True(terminal.Cleanup!.HttpListenerOwned);
        Assert.Equal(ObsPublishChannelEndReason.LocalTerminated, (await host.Completion).EndReason);
        Assert.Same(terminal, (await host.Completion).LocalTermination);
    }

    [Fact]
    public async Task AcceptedPackets_AreDeliveredBeforeTermination_BorrowedHttpRemainsUsable()
    {
        await using var http = new PcpHttpFlvPlaybackHost();
        http.Start();
        var registry = new RecordingStreamSourceLeaseRegistry();
        await using var host = ObsPublishChannelHostTests.Create(http, registry);
        host.Start();
        var sink = new CopySink();
        await using var registration = host.Fanout.AddChild(sink);
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        Assert.True(await host.HeadAvailable.WaitAsync(Limit));
        await publisher.SendAsync(9, 1, 37, ObsFlvPublishingSessionTests.Idr);
        await WaitAsync(() => sink.Count >= 2);
        var terminal = await host.RequestLocalTerminationAsync(Limit).WaitAsync(Limit);
        AssertCompleted(host, terminal);
        Assert.Equal(2, terminal.Ingress!.FinalResult!.AcceptedPacketCount);
        Assert.Equal(terminal.Ingress.FinalResult.AcceptedByteCount, sink.Bytes);
        Assert.Equal(2, sink.Count);
        Assert.False(terminal.Cleanup!.HttpListenerOwned);
        Assert.Equal(0, http.ActiveSourceCount);
        Assert.Equal(0, registry.Count);
        Assert.NotNull(http.LocalEndPoint);
        await using var unrelated = http.Create(new ChannelId(Guid.NewGuid()), new ChannelSessionId(Guid.NewGuid()));
    }

    [Fact]
    public async Task DuplicateFinish_JoinsFirstRequest_NoSecondGenerationOrDeadline()
    {
        await using var host = ObsPublishChannelHostTests.Create();
        host.Start();
        var first = host.RequestLocalTerminationAsync(Limit);
        var duplicate = host.RequestLocalTerminationAsync(TimeSpan.FromMilliseconds(1));
        Assert.Same(first, duplicate);
        var result = await first;
        Assert.Same(result, await host.RequestLocalTerminationAsync(Limit));
        AssertCompleted(host, result);
        Assert.Throws<InvalidOperationException>(() => host.Start());
    }

    [Fact]
    public async Task FinishWhileAdmittedConsumerIsBlocked_PreservesPacketUntilConsumerCompletes()
    {
        await using var http = new PcpHttpFlvPlaybackHost();
        http.Start();
        var gate = new PacketGate();
        await using var host = ObsPublishChannelHostTests.Create(http);
        host.Start();
        await using var registration = host.Fanout.AddChild(gate);
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        await gate.Entered.Task.WaitAsync(Limit);
        var finish = host.RequestLocalTerminationAsync(Limit);
        await WaitAsync(() => host.RelaySnapshot.State == PcpDownstreamRelayServerState.Quiescent);
        Assert.False(finish.IsCompleted);
        Assert.False(registration.Completion.IsCompleted);
        Assert.False(gate.CancellationObserved);
        gate.Release.TrySetResult();
        var terminal = await finish.WaitAsync(Limit);
        AssertCompleted(host, terminal);
        Assert.Equal(1, gate.Delivered);
        Assert.Equal(0, terminal.Fanout!.Evidence.DiscardedPacketCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlockedSend_TimeoutOrCancellationIsNotLocalCompletion(bool cancel)
    {
        await using var host = ObsPublishChannelHostTests.Create();
        host.Start();
        var gate = new PacketGate();
        await using var registration = host.Fanout.AddChild(gate);
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        await gate.Entered.Task.WaitAsync(Limit);
        using var cancellation = new CancellationTokenSource();
        var finish = host.RequestLocalTerminationAsync(cancel ? Limit : TimeSpan.FromMilliseconds(100), cancellation.Token);
        await WaitAsync(() => host.Snapshot.State == ChannelSessionState.Stopping);
        if (cancel) cancellation.Cancel();
        var result = await finish.WaitAsync(Limit);
        Assert.Equal(cancel ? ObsPublishLocalTerminationOutcome.Cancelled : ObsPublishLocalTerminationOutcome.TimedOut, result.Outcome);
        Assert.Equal(!cancel, result.DeadlineExpired);
        Assert.True(result.UpstreamEnded);
        Assert.Null(result.Fanout);
        Assert.False((await host.Completion).HttpDrained);
        Assert.NotEqual(ObsPublishChannelEndReason.PublisherStopped, (await host.Completion).EndReason);
        gate.Release.TrySetResult();
    }

    [Fact]
    public async Task SendFailureDuringFinish_IsRetained_NotSuccessfulPartialDrain()
    {
        await using var host = ObsPublishChannelHostTests.Create();
        host.Start();
        var gate = new PacketGate(fail: true);
        await using var registration = host.Fanout.AddChild(gate);
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        await gate.Entered.Task.WaitAsync(Limit);
        var finish = host.RequestLocalTerminationAsync(Limit);
        await WaitAsync(() => !host.IsRegistered);
        gate.Release.TrySetResult();
        var result = await finish.WaitAsync(Limit);
        Assert.Equal(ObsPublishLocalTerminationOutcome.DeliveryFailed, result.Outcome);
        Assert.NotNull(result.Fanout);
        Assert.Equal(1, result.Fanout.Evidence.SinkFailedChildCount);
        Assert.Equal(1, result.Fanout.Evidence.DiscardedPacketCount);
        Assert.True(result.CleanupCompleted);
        Assert.NotEqual(ObsPublishChannelEndReason.PublisherStopped, (await host.Completion).EndReason);
    }

    [Fact]
    public async Task FinishAndDispose_JoinCleanup_AndRemainCancellationNotObsEos()
    {
        await using var host = ObsPublishChannelHostTests.Create();
        host.Start();
        var gate = new PacketGate();
        await using var registration = host.Fanout.AddChild(gate);
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        await gate.Entered.Task.WaitAsync(Limit);
        var finish = host.RequestLocalTerminationAsync(Limit);
        await WaitAsync(() => host.Snapshot.State == ChannelSessionState.Stopping);
        var disposal = host.DisposeAsync().AsTask();
        Assert.Same(disposal, host.DisposeAsync().AsTask());
        var result = await finish.WaitAsync(Limit);
        Assert.Equal(ObsPublishLocalTerminationOutcome.Cancelled, result.Outcome);
        gate.Release.TrySetResult();
        await disposal.WaitAsync(Limit);
        Assert.True(result.CleanupCompleted);
        Assert.Equal(ObsPublishChannelEndReason.HostCancelled, (await host.Completion).EndReason);
    }

    [Fact]
    public async Task IgnoreCancellationCleanup_IsUnprovenAndCannotBeUpgradedByLateRelease()
    {
        var host = new ObsPublishChannelHost(new()
        {
            Ingest = new() { Port = 0 },
            Fanout = new() { ChildStopTimeout = TimeSpan.FromMilliseconds(75) },
        });
        host.Start();
        var gate = new PacketGate(ignoreCancellation: true);
        var registration = host.Fanout.AddChild(gate);
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        await gate.Entered.Task.WaitAsync(Limit);
        try
        {
            var result = await host.RequestLocalTerminationAsync(TimeSpan.FromMilliseconds(100)).WaitAsync(Limit);
            Assert.Equal(ObsPublishLocalTerminationOutcome.TimedOut, result.Outcome);
            Assert.False(result.CleanupCompleted);
            Assert.False(registration.Completion.IsCompleted);
            Assert.False((await host.Completion).ShutdownCompleted);
            Assert.Throws<InvalidOperationException>(() => host.Start());
            gate.Release.TrySetResult();
            await registration.Completion.WaitAsync(Limit);
            Assert.Same(result, await host.RequestLocalTerminationAsync(Limit));
            Assert.False(result.CleanupCompleted);
        }
        finally
        {
            gate.Release.TrySetResult();
            await registration.DisposeAsync();
            // Actual late cleanup may finish before its own bounded child wait, but
            // neither the earlier finish result nor owner Completion is upgraded.
            try { await host.DisposeAsync(); } catch (InvalidOperationException) { }
        }
    }

    [Fact]
    public async Task ActiveRecordingLease_IsNotDisposedToManufactureCleanupOrWriterEvidence()
    {
        var host = ObsPublishChannelHostTests.Create();
        host.Start();
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        Assert.True(await host.HeadAvailable.WaitAsync(Limit));
        var lease = await host.RecordingRegistry.AcquireAsync(RecordingSinkStartRequest.Manual(
            new RecordingCandidate(host.ChannelId, new ChannelMetadataSnapshot(name: "fixture", streamExtension: "flv"))), CancellationToken.None);
        Assert.NotNull(lease);
        try
        {
            var result = await host.RequestLocalTerminationAsync(TimeSpan.FromMilliseconds(200)).WaitAsync(Limit);
            Assert.Equal(ObsPublishLocalTerminationOutcome.CleanupIncomplete, result.Outcome);
            Assert.Equal(ObsPublishLocalTerminationPhase.RecordingSource, result.Phase);
            Assert.True(result.DeadlineExpired);
            Assert.Equal(ObsPublishWriterEvidence.NotObserved, result.WriterEvidence);
            Assert.NotNull(result.RecordingSource);
            Assert.True(result.RecordingSource.Evidence.EofNotified);
            Assert.False(result.RecordingSource.Evidence.CleanupCompleted);
            Assert.False((await host.Completion).ShutdownCompleted);
            await lease.ReadStream.CopyToAsync(Stream.Null).WaitAsync(Limit);
        }
        finally
        {
            await lease.DisposeAsync();
            try { await host.DisposeAsync(); } catch (InvalidOperationException) { }
        }
    }

    [Fact]
    public async Task NaturalRecordingLeaseRelease_DoesNotInferWriterSuccessOrMatchPublishingSessionId()
    {
        await using var host = ObsPublishChannelHostTests.Create();
        host.Start();
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        Assert.True(await host.HeadAvailable.WaitAsync(Limit));
        var lease = await host.RecordingRegistry.AcquireAsync(RecordingSinkStartRequest.Manual(
            new RecordingCandidate(host.ChannelId, new ChannelMetadataSnapshot(name: "fixture", streamExtension: "flv"))), CancellationToken.None);
        Assert.NotNull(lease);
        var finish = host.RequestLocalTerminationAsync(Limit);
        await lease.ReadStream.CopyToAsync(Stream.Null).WaitAsync(Limit);
        await lease.DisposeAsync();
        var result = await finish.WaitAsync(Limit);
        AssertCompleted(host, result);
        Assert.Equal(ObsPublishWriterEvidence.NotObserved, result.WriterEvidence);
        Assert.Equal(1, result.RecordingSource!.Evidence.AcquiredLeaseCount);
        Assert.False(result.RecordingSource.WriterFinalizationObserved);
    }

    [Fact]
    public async Task AdmittedMandatoryConsumer_FinishesBeforeUpstreamCountersAndRecordingAdmissionClose()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        await using var http = new PcpHttpFlvPlaybackHost(streamUrlReady: _ =>
        {
            entered.Set();
            if (!release.Wait(Limit)) throw new TimeoutException();
        });
        http.Start();
        await using var host = ObsPublishChannelHostTests.Create(http);
        host.Start();
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        Assert.True(entered.Wait(Limit));
        try
        {
            var finish = host.RequestLocalTerminationAsync(Limit);
            await WaitAsync(() => !host.IsRegistered);
            Assert.False(finish.IsCompleted);
            Assert.False(host.HeadAvailable.IsCompleted);
            Assert.Equal(ChannelSessionState.Negotiating, host.Snapshot.State);
            release.Set();
            var result = await finish.WaitAsync(Limit);
            AssertCompleted(host, result);
            Assert.Equal(1, result.Ingress!.FinalResult!.AcceptedPacketCount);
            Assert.True(result.RecordingSource!.Evidence.AdmissionClosed);
            Assert.Equal(0, host.RecordingRegistry.Count);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task DeadlineWithBlockedMandatoryConsumer_DoesNotInventUpstreamOrCleanupEvidence()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        await using var http = new PcpHttpFlvPlaybackHost(streamUrlReady: _ =>
        {
            entered.Set();
            if (!release.Wait(Limit)) throw new TimeoutException();
        });
        http.Start();
        var host = ObsPublishChannelHostTests.Create(http);
        host.Start();
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        Assert.True(entered.Wait(Limit));
        try
        {
            var result = await host.RequestLocalTerminationAsync(TimeSpan.FromMilliseconds(100)).WaitAsync(Limit);
            Assert.Equal(ObsPublishLocalTerminationOutcome.TimedOut, result.Outcome);
            // The one shared deadline also covers scheduling and relay admission closure.
            // Under parallel load it may expire before the ingress API is reached.
            Assert.Contains(result.Phase, new[] { ObsPublishLocalTerminationPhase.Admission, ObsPublishLocalTerminationPhase.Ingress });
            Assert.False(result.UpstreamEnded);
            Assert.False(result.AcceptedCountersMatched);
            Assert.False(result.CleanupCompleted);
            if (result.Phase == ObsPublishLocalTerminationPhase.Admission) Assert.Null(result.Ingress);
            else Assert.Null(result.Ingress?.FinalResult);
            Assert.Null(result.Http);
        }
        finally
        {
            release.Set();
            try { await host.DisposeAsync(); } catch (InvalidOperationException) { }
        }
    }

    [Fact]
    public async Task ChildCancellationCleanupFailure_IsNotPromotedToSuccessfulFinish()
    {
        var host = ObsPublishChannelHostTests.Create();
        host.Start();
        using var sink = new FailingCleanupSink();
        var registration = host.Fanout.AddChild(sink);
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        await sink.Delivered.Task.WaitAsync(Limit);
        try
        {
            var result = await host.RequestLocalTerminationAsync(Limit).WaitAsync(Limit);
            Assert.Equal(ObsPublishLocalTerminationOutcome.CleanupIncomplete, result.Outcome);
            Assert.Equal(ObsPublishLocalTerminationPhase.Fanout, result.Phase);
            Assert.NotNull(result.Fanout);
            Assert.Equal(1, result.Fanout.Evidence.CleanupFailedChildCount);
            Assert.False(result.CleanupCompleted);
            Assert.False((await host.Completion).ShutdownCompleted);
        }
        finally
        {
            await registration.DisposeAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.DisposeAsync());
        }
    }

    [Fact]
    public async Task UnadmittedRequests_DoNotStartOrCloseAnOwner()
    {
        await using var host = ObsPublishChannelHostTests.Create();
        var initial = await host.RequestLocalTerminationAsync(Limit);
        Assert.Equal(ObsPublishLocalTerminationOutcome.Failed, initial.Outcome);
        Assert.Equal(ChannelSessionState.Created, host.Snapshot.State);
        host.Start();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await host.RequestLocalTerminationAsync(Limit, cancellation.Token);
        Assert.Equal(ObsPublishLocalTerminationOutcome.Cancelled, cancelled.Outcome);
        Assert.True(host.IsRegistered);
        Assert.False(host.Completion.IsCompleted);
        AssertCompleted(host, await host.RequestLocalTerminationAsync(Limit));
    }

    [Fact]
    public async Task AlreadyCompletedObsEos_IsNotRetroactivelyLocalTermination()
    {
        await using var host = ObsPublishChannelHostTests.Create();
        host.Start();
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        await ObsPublishChannelHostTests.StopAsync(publisher);
        var original = await host.Completion.WaitAsync(Limit);
        Assert.Equal(ObsPublishChannelEndReason.PublisherStopped, original.EndReason);
        var local = await host.RequestLocalTerminationAsync(Limit);
        Assert.Equal(ObsPublishLocalTerminationOutcome.Failed, local.Outcome);
        Assert.Null(original.LocalTermination);
    }

    [Fact]
    public async Task DistinctOwners_HaveNoStaleCompletionOrCleanupInNewGeneration()
    {
        await using var http = new PcpHttpFlvPlaybackHost();
        http.Start();
        var registry = new RecordingStreamSourceLeaseRegistry();
        var id = new ChannelId(Guid.NewGuid());
        var first = ObsPublishChannelHostTests.Create(http, registry, id);
        first.Start();
        var a = await first.RequestLocalTerminationAsync(Limit);
        AssertCompleted(first, a);
        await using var second = ObsPublishChannelHostTests.Create(http, registry, id);
        second.Start();
        await first.DisposeAsync();
        Assert.True(second.IsRegistered);
        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.Equal(1, registry.Count);
        var b = await second.RequestLocalTerminationAsync(Limit);
        AssertCompleted(second, b);
        Assert.NotEqual(a.SessionId, b.SessionId);
    }

    [Fact]
    public async Task InvalidDeadline_DoesNotCloseAdmissionOrStartNewWork()
    {
        await using var host = ObsPublishChannelHostTests.Create();
        host.Start();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await host.RequestLocalTerminationAsync(TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await host.RequestLocalTerminationAsync(TimeSpan.FromMinutes(2)));
        Assert.True(host.IsRegistered);
    }

    private static void AssertCompleted(ObsPublishChannelHost host, ObsPublishLocalTerminationResult result)
    {
        Assert.Equal(ObsPublishLocalTerminationOutcome.LocalCompleted, result.Outcome);
        Assert.Equal(ObsPublishLocalTerminationPhase.Finished, result.Phase);
        Assert.True(result.UpstreamEnded);
        Assert.True(result.AcceptedCountersMatched);
        Assert.True(result.RelayAdmissionClosed);
        Assert.True(result.CleanupCompleted);
        Assert.Equal(host.ChannelId, result.ChannelId);
        Assert.Equal(host.SessionId, result.SessionId);
        Assert.False(host.IsRegistered);
        Assert.NotNull(result.Relay);
        Assert.True(result.Relay.CleanupCompleted);
        Assert.NotNull(result.Fanout);
        Assert.True(result.Fanout.SendsCompleted);
        Assert.True(result.Fanout.CleanupCompleted);
        Assert.NotNull(result.Http);
        Assert.True(result.Http.CleanupCompleted);
        Assert.NotNull(result.RecordingSource);
        Assert.True(result.RecordingSource.Evidence.CleanupCompleted);
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        using var guard = new CancellationTokenSource(Limit);
        while (!condition()) await Task.Delay(5, guard.Token);
    }

    private sealed class PacketGate(bool fail = false, bool ignoreCancellation = false) : IPcpRelayChildSink
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }
        public int Delivered { get; private set; }
        public async ValueTask SendAsync(PcpRelayPacket packet, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            try
            {
                if (ignoreCancellation) await Release.Task;
                else await Release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) { CancellationObserved = true; throw; }
            if (fail) throw new IOException("fixed fixture failure");
            Delivered++;
        }
    }

    private sealed class CopySink : IPcpRelayChildSink
    {
        private long _bytes;
        private int _count;
        public long Bytes => Interlocked.Read(ref _bytes);
        public int Count => Volatile.Read(ref _count);
        public ValueTask SendAsync(PcpRelayPacket packet, CancellationToken cancellationToken)
        {
            Interlocked.Add(ref _bytes, packet.Payload.Length);
            Interlocked.Increment(ref _count);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingCleanupSink : IPcpRelayChildSink, IDisposable
    {
        private CancellationTokenRegistration _callback;
        public TaskCompletionSource Delivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask SendAsync(PcpRelayPacket packet, CancellationToken cancellationToken)
        {
            _callback = cancellationToken.Register(() => throw new IOException("fixed cancellation cleanup failure"));
            Delivered.TrySetResult();
            return ValueTask.CompletedTask;
        }
        public void Dispose() => _callback.Dispose();
    }
}
