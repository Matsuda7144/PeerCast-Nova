using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpRecordingSourceCompletionEvidenceTests
{
    private static readonly ChannelId Channel = new(Guid.ParseExact("531DC8DFC7FB42928AC2C0A626517A87", "N"));
    private static readonly ChannelSessionId Session = new(Guid.ParseExact("3F2504E04F8911D39A0C0305E82C3301", "N"));
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ShortLimit = TimeSpan.FromMilliseconds(600);

    [Fact]
    public async Task BufferedPackets_EofAndNaturalLeaseCleanup_AreProvenSeparatelyFromWriter()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = Create(fanout);
        var lease = await Acquire(factory);
        await fanout.ConsumeAsync(Packet(0, [1, 2, 3]), CancellationToken.None);
        var completion = factory.CompleteWithEvidenceAsync(Limit);
        using var payload = new MemoryStream();
        await lease.ReadStream.CopyToAsync(payload).WaitAsync(Limit);
        Assert.Equal(new byte[] { 1, 2, 3 }, payload.ToArray());
        await lease.DisposeAsync();

        var result = await completion.WaitAsync(Limit);
        Assert.Equal(PcpRecordingSourceCompletionOutcome.NormalEof, result.Outcome);
        Assert.True(result.Evidence.EofNotified);
        Assert.True(result.Evidence.ChildrenStopped);
        Assert.True(result.Evidence.CleanupCompleted);
        Assert.True(result.Evidence.PacketsSettled);
        Assert.Equal(1, result.Evidence.DeliveredPacketCount);
        Assert.Equal(3, result.Evidence.DeliveredByteCount);
        Assert.Equal(1, result.Evidence.RetiredLeaseCount);
        Assert.False(result.WriterFinalizationObserved);
        Assert.Null(await factory.AcquireAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task EmptyFactory_ClosesAdmissionWithoutCreatingResources()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = Create(fanout);
        var result = await factory.CompleteWithEvidenceAsync(Limit);
        Assert.Equal(PcpRecordingSourceCompletionOutcome.NormalEof, result.Outcome);
        Assert.True(result.Evidence.AdmissionClosed);
        Assert.True(result.Evidence.CleanupCompleted);
        Assert.Equal(0, result.Evidence.AcquiredLeaseCount);
    }

    [Fact]
    public async Task OwnerFaultWithoutLiveLease_IsNotPromotedToNormalEof()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = Create(fanout);
        var result = await factory.CompleteWithEvidenceAsync(Limit, new IOException("private source fault"));
        Assert.Equal(PcpRecordingSourceCompletionOutcome.SourceFault, result.Outcome);
        Assert.True(result.Evidence.OwnerFaultRequested);
        Assert.False(result.Evidence.EofNotified);
        Assert.True(result.Evidence.CleanupCompleted);
    }

    [Fact]
    public async Task ActiveReader_EofNotificationDoesNotManufactureCleanupOrConsumeBufferedBytes()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = Create(fanout);
        var lease = await Acquire(factory);
        await fanout.ConsumeAsync(Packet(0, [10]), CancellationToken.None);
        var result = await factory.CompleteWithEvidenceAsync(ShortLimit);
        Assert.Equal(PcpRecordingSourceCompletionOutcome.CleanupIncomplete, result.Outcome);
        Assert.Equal(PcpRecordingSourceCompletionPhase.LeaseCleanup, result.Phase);
        Assert.True(result.DeadlineExpired);
        Assert.True(result.Evidence.EofNotified);
        Assert.True(result.Evidence.ChildrenStopped);
        Assert.Equal(1, result.Evidence.ActiveLeaseCount);
        Assert.Equal(1, result.Evidence.CleanupPendingLeaseCount);
        Assert.Equal(10, lease.ReadStream.ReadByte());
        Assert.Equal(-1, lease.ReadStream.ReadByte());
        await lease.DisposeAsync();
        Assert.True(factory.Evidence.CleanupCompleted);
        Assert.False(result.Evidence.CleanupCompleted);
        Assert.Same(result, await factory.CompleteWithEvidenceAsync(Limit));
    }

    [Fact]
    public async Task BlockedReader_ReceivesEofAndCanReleaseLease()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = Create(fanout);
        var lease = await Acquire(factory);
        var read = lease.ReadStream.ReadAsync(new byte[1]).AsTask();
        Assert.False(read.IsCompleted);
        var completion = factory.CompleteWithEvidenceAsync(Limit);
        Assert.Equal(0, await read.WaitAsync(Limit));
        await lease.DisposeAsync();
        Assert.Equal(PcpRecordingSourceCompletionOutcome.NormalEof, (await completion).Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlockedDelivery_DeadlineOrCancellationDoesNotPretendEofOrDiscardPendingPacket(bool cancel)
    {
        await using var fanout = new PcpRelayFanout();
        var factory = Create(fanout, new()
        {
            PauseWriterThresholdBytes = 8,
            ResumeWriterThresholdBytes = 4,
            MinimumSegmentSizeBytes = 8,
        });
        var lease = await Acquire(factory);
        await fanout.ConsumeAsync(Packet(0, new byte[16]), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var completion = factory.CompleteWithEvidenceAsync(cancel ? Limit : ShortLimit, cancellationToken: cancellation.Token);
        if (cancel) cancellation.Cancel();
        var result = await completion.WaitAsync(Limit);
        Assert.Equal(cancel ? PcpRecordingSourceCompletionOutcome.Cancelled : PcpRecordingSourceCompletionOutcome.TimedOut, result.Outcome);
        Assert.False(result.Evidence.EofNotified);
        Assert.Equal(1, result.Evidence.PendingPacketCount);
        Assert.Equal(0, result.Evidence.DiscardedPacketCount);
        Assert.Equal(1, fanout.ActiveChildCount);
        await lease.ReadStream.ReadExactlyAsync(new byte[16]);
        Assert.True(await fanout.WaitForIdleAsync(Limit));
        await factory.CompleteAsync();
        await lease.DisposeAsync();
        Assert.Equal(1, factory.Evidence.DeliveredPacketCount);
        Assert.False(result.Evidence.CleanupCompleted);
    }

    [Fact]
    public async Task DuplicateCompletionAndLegacyCompletion_JoinFirstNotificationAndResult()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = Create(fanout);
        var lease = await Acquire(factory);
        await factory.CompleteAsync();
        var first = factory.CompleteWithEvidenceAsync(Limit);
        var second = factory.CompleteWithEvidenceAsync(ShortLimit, new IOException("must not replace EOF"));
        Assert.Same(first, second);
        Assert.Equal(-1, lease.ReadStream.ReadByte());
        await Task.WhenAll(lease.DisposeAsync().AsTask(), lease.DisposeAsync().AsTask());
        var result = await first;
        Assert.Equal(PcpRecordingSourceCompletionOutcome.NormalEof, result.Outcome);
        Assert.Equal(1, result.Evidence.CleanupCompletedLeaseCount);
        Assert.Equal(0, result.Evidence.SourceFaultLeaseCount);
    }

    [Fact]
    public async Task PriorOwnerFault_RetiredLeaseDoesNotLoseFaultEvidence()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = Create(fanout);
        var lease = await Acquire(factory);
        await factory.CompleteAsync(new InvalidDataException("private diagnostic"));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await lease.ReadStream.CopyToAsync(Stream.Null));
        await lease.DisposeAsync();
        var result = await factory.CompleteWithEvidenceAsync(Limit);
        Assert.Equal(PcpRecordingSourceCompletionOutcome.SourceFault, result.Outcome);
        Assert.Equal(1, result.Evidence.SourceFaultLeaseCount);
        Assert.Equal(1, result.Evidence.FaultNotificationCount);
        Assert.Equal(1, result.Evidence.RetiredLeaseCount);
        Assert.False(result.Evidence.EofNotified);
        Assert.DoesNotContain("private diagnostic", result.ToString());
    }

    [Fact]
    public async Task ExplicitFault_UsesExistingReaderFaultBoundary()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = Create(fanout);
        var lease = await Acquire(factory);
        var completion = factory.CompleteWithEvidenceAsync(Limit, new IOException("source fault"));
        await Assert.ThrowsAsync<IOException>(async () =>
            await lease.ReadStream.CopyToAsync(Stream.Null).WaitAsync(Limit));
        await lease.DisposeAsync();
        Assert.Equal(PcpRecordingSourceCompletionOutcome.SourceFault, (await completion).Outcome);
    }

    [Fact]
    public async Task PastDeliveryFailureAndDiscard_RemainAfterLeaseRetirement()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = Create(fanout);
        var lease = await Acquire(factory);
        await fanout.ConsumeAsync(Packet(0, [1]), CancellationToken.None);
        Assert.True(await fanout.WaitForIdleAsync(Limit));
        await lease.ReadStream.DisposeAsync();
        await fanout.ConsumeAsync(Packet(1, [2]), CancellationToken.None);
        Assert.True(await fanout.WaitForIdleAsync(Limit));
        await lease.DisposeAsync();
        var result = await factory.CompleteWithEvidenceAsync(Limit);
        Assert.Equal(PcpRecordingSourceCompletionOutcome.DeliveryFailed, result.Outcome);
        Assert.True(result.Evidence.DeliveryFailed);
        Assert.Equal(1, result.Evidence.DeliveredPacketCount);
        Assert.Equal(1, result.Evidence.DiscardedPacketCount);
        Assert.Equal(1, result.Evidence.RetiredLeaseCount);
    }

    [Fact]
    public async Task UnexpectedChildEnd_IsNotNormalEof()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = Create(fanout);
        var lease = await Acquire(factory);
        await fanout.DisposeAsync();
        await Assert.ThrowsAsync<IOException>(async () =>
            await lease.ReadStream.CopyToAsync(Stream.Null).WaitAsync(Limit));
        await lease.DisposeAsync();
        var result = await factory.CompleteWithEvidenceAsync(Limit);
        Assert.Equal(PcpRecordingSourceCompletionOutcome.DeliveryFailed, result.Outcome);
        Assert.False(result.Evidence.EofNotified);
    }

    [Fact]
    public async Task DisposeBeforeTerminal_IsRetainedAsAbort_NotNormalEof()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = Create(fanout);
        var lease = await Acquire(factory);
        await lease.DisposeAsync();
        var result = await factory.CompleteWithEvidenceAsync(Limit);
        Assert.Equal(PcpRecordingSourceCompletionOutcome.Cancelled, result.Outcome);
        Assert.Equal(1, result.Evidence.AbortedLeaseCount);
        Assert.True(result.Evidence.CleanupCompleted);
        Assert.False(result.Evidence.EofNotified);
    }

    [Fact]
    public async Task BlockedOwnerCleanup_ConcurrentDisposeJoinsAndDeadlineDoesNotRetireEarly()
    {
        await using var fanout = new PcpRelayFanout();
        var ownerLease = new GatedOwnerLease();
        var factory = Create(fanout, owner: () => ownerLease);
        var lease = await Acquire(factory);
        await factory.CompleteAsync();
        var completion = factory.CompleteWithEvidenceAsync(ShortLimit);
        var disposal = lease.DisposeAsync().AsTask();
        Assert.Same(disposal, lease.DisposeAsync().AsTask());
        await ownerLease.Entered.Task.WaitAsync(Limit);
        var result = await completion;
        Assert.Equal(PcpRecordingSourceCompletionOutcome.CleanupIncomplete, result.Outcome);
        Assert.Equal(0, result.Evidence.ActiveLeaseCount);
        Assert.Equal(1, result.Evidence.CleanupPendingLeaseCount);
        Assert.Equal(0, result.Evidence.RetiredLeaseCount);
        ownerLease.Release.TrySetResult();
        await disposal.WaitAsync(Limit);
        Assert.True(factory.Evidence.CleanupCompleted);
        Assert.Equal(1, factory.Evidence.RetiredLeaseCount);
        Assert.False(result.Evidence.CleanupCompleted);
        Assert.Equal(1, ownerLease.Calls);
    }

    [Fact]
    public async Task OwnerCleanupFailure_IsRetainedAndNeverUpgraded()
    {
        await using var fanout = new PcpRelayFanout();
        var ownerLease = new GatedOwnerLease(fail: true);
        ownerLease.Release.TrySetResult();
        var factory = Create(fanout, owner: () => ownerLease);
        var lease = await Acquire(factory);
        await factory.CompleteAsync();
        await Assert.ThrowsAsync<IOException>(async () => await lease.DisposeAsync());
        var result = await factory.CompleteWithEvidenceAsync(Limit);
        Assert.Equal(PcpRecordingSourceCompletionOutcome.CleanupIncomplete, result.Outcome);
        Assert.Equal(1, result.Evidence.CleanupFailedLeaseCount);
        Assert.Equal(1, result.Evidence.RetiredLeaseCount);
        Assert.True(result.Evidence.EofNotified);
        Assert.False(result.Evidence.CleanupCompleted);
    }

    [Fact]
    public async Task PendingAcquisition_IsInsideDeadline_AndRejectedLeaseMustReleaseOwner()
    {
        await using var fanout = new PcpRelayFanout();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var ownerLease = new GatedOwnerLease();
        var factory = Create(fanout, owner: () =>
        {
            entered.Set();
            if (!release.Wait(Limit)) throw new TimeoutException();
            return ownerLease;
        });
        var acquisition = Task.Run(async () => await factory.AcquireAsync(Request(), CancellationToken.None));
        Assert.True(entered.Wait(Limit));
        try
        {
            var result = await factory.CompleteWithEvidenceAsync(ShortLimit);
            Assert.Equal(PcpRecordingSourceCompletionOutcome.TimedOut, result.Outcome);
            Assert.Equal(PcpRecordingSourceCompletionPhase.Acquisition, result.Phase);
            Assert.Equal(1, result.Evidence.PendingAcquisitionCount);
            Assert.False(result.Evidence.CleanupCompleted);
            release.Set();
            await ownerLease.Entered.Task.WaitAsync(Limit);
            Assert.Equal(1, factory.Evidence.PendingAcquisitionCount);
            ownerLease.Release.TrySetResult();
            Assert.Null(await acquisition.WaitAsync(Limit));
            Assert.Equal(0, factory.Evidence.AcquiredLeaseCount);
            Assert.True(factory.Evidence.CleanupCompleted);
        }
        finally
        {
            release.Set();
            ownerLease.Release.TrySetResult();
            await acquisition.WaitAsync(Limit);
        }
    }

    [Fact]
    public async Task AcquisitionFailure_IsNotCleanEmptySuccess()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = Create(fanout, owner: () => throw new IOException("private owner failure"));
        await Assert.ThrowsAsync<IOException>(async () => await factory.AcquireAsync(Request(), CancellationToken.None));
        var result = await factory.CompleteWithEvidenceAsync(Limit);
        Assert.Equal(PcpRecordingSourceCompletionOutcome.CleanupIncomplete, result.Outcome);
        Assert.Equal(1, result.Evidence.AcquisitionFailureCount);
    }

    [Fact]
    public async Task Completion_DoesNotStopOrDrainUnrelatedBorrowedFanoutChild()
    {
        await using var fanout = new PcpRelayFanout();
        var unrelated = new GatedSink();
        var registration = fanout.AddChild(unrelated);
        var factory = Create(fanout);
        var lease = await Acquire(factory);
        await fanout.ConsumeAsync(Packet(0, [1]), CancellationToken.None);
        await unrelated.Entered.Task.WaitAsync(Limit);
        var completion = factory.CompleteWithEvidenceAsync(Limit);
        await lease.ReadStream.CopyToAsync(Stream.Null).WaitAsync(Limit);
        await lease.DisposeAsync();
        Assert.Equal(PcpRecordingSourceCompletionOutcome.NormalEof, (await completion).Outcome);
        Assert.False(registration.Completion.IsCompleted);
        Assert.Equal(1, fanout.ActiveChildCount);
        unrelated.Release.TrySetResult();
        Assert.True(await fanout.WaitForIdleAsync(Limit));
    }

    [Fact]
    public async Task PreCancelledAndInvalidDeadline_DoNotCloseAdmission()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = Create(fanout);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await factory.CompleteWithEvidenceAsync(Limit, cancellationToken: cancellation.Token);
        Assert.Equal(PcpRecordingSourceCompletionOutcome.Cancelled, result.Outcome);
        Assert.False(result.Evidence.AdmissionClosed);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await factory.CompleteWithEvidenceAsync(TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await factory.CompleteWithEvidenceAsync(TimeSpan.FromMinutes(2)));
        var lease = await Acquire(factory);
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task RuntimeTerminalObserver_ProvidesIndependentWriterEvidence_SourceCannotClaimIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Nova-SourceCompletionTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "opaque.bin");
        try
        {
            await using var fanout = new PcpRelayFanout();
            var factory = Create(fanout);
            var observer = new TerminalObserver();
            await using var runtime = new RecordingRuntime(new LeasedRecordingStreamSourceProvider(factory),
                new FixedPathProvider(path), terminalObserver: observer);
            var started = await runtime.StartManualAsync(new RecordingCandidate(Channel,
                new ChannelMetadataSnapshot(name: "fixture", streamExtension: "bin")));
            Assert.Equal(RecordingManualStartResultKind.Started, started.Kind);
            await fanout.ConsumeAsync(Packet(0, [1, 2, 3]), CancellationToken.None);
            var result = await factory.CompleteWithEvidenceAsync(Limit);
            var fact = await observer.Terminal.Task.WaitAsync(Limit);
            Assert.Equal(PcpRecordingSourceCompletionOutcome.NormalEof, result.Outcome);
            Assert.False(result.WriterFinalizationObserved);
            Assert.Equal(RecordingSinkSessionLifecycleKind.Completed, fact.Kind);
            Assert.NotNull(started.State.Session);
            Assert.Equal(started.State.Session.SessionId, fact.Session.SessionId);
            Assert.NotNull(fact.WriteResult);
            Assert.Equal(3, fact.WriteResult.BytesWritten);
            Assert.Equal(path, fact.WriteResult.Path);
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.GetFiles(directory, "*.partial"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CancellationDuringNaturalCleanup_IsNotReportedAsTimeoutOrCleanCompletion()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = Create(fanout);
        var lease = await Acquire(factory);
        using var cancellation = new CancellationTokenSource();
        var completion = factory.CompleteWithEvidenceAsync(Limit, cancellationToken: cancellation.Token);
        Assert.Equal(0, await lease.ReadStream.ReadAsync(new byte[1]).AsTask().WaitAsync(Limit));
        using var guard = new CancellationTokenSource(Limit);
        while (!factory.Evidence.ChildrenStopped) await Task.Delay(1, guard.Token);
        cancellation.Cancel();
        var result = await completion;
        Assert.Equal(PcpRecordingSourceCompletionOutcome.Cancelled, result.Outcome);
        Assert.False(result.DeadlineExpired);
        Assert.False(result.Evidence.CleanupCompleted);
        await lease.DisposeAsync();
    }

    [Fact]
    public void Evidence_ContainsOnlyFixedCountersAndFlags_NotSensitiveDataOrUnboundedHistory()
    {
        Assert.All(typeof(PcpRecordingSourceEvidence).GetProperties(), property =>
            Assert.True(property.PropertyType == typeof(bool) || property.PropertyType == typeof(int) || property.PropertyType == typeof(long)));
        Assert.Null(typeof(PcpRecordingSourceCompletionResult).GetProperty(nameof(PcpRecordingSourceCompletionResult.WriterFinalizationObserved))!.SetMethod);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RejectedCleanupFailure_IsIndependentOfAcquireCancellation(bool cleanupCancelled, bool acquireCancelled)
    {
        await using var fanout = new PcpRelayFanout();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var failure = cleanupCancelled ? (Exception)new OperationCanceledException("cleanup cancellation") : new IOException("cleanup fault");
        var owner = new ScriptedOwnerLease(failure);
        var factory = Create(fanout, owner: () =>
        {
            entered.Set();
            if (!release.Wait(Limit)) throw new TimeoutException();
            return owner;
        });
        using var cancellation = new CancellationTokenSource();
        var acquisition = Task.Run(async () => await factory.AcquireAsync(Request(), cancellation.Token));
        Assert.True(entered.Wait(Limit));
        var completion = factory.CompleteWithEvidenceAsync(Limit);
        try
        {
            Assert.True(factory.Evidence.AdmissionClosed);
            release.Set();
            await owner.Entered.Task.WaitAsync(Limit);
            if (acquireCancelled) cancellation.Cancel();
            Assert.Equal(1, factory.Evidence.PendingAcquisitionCount);
            Assert.Equal(1, factory.Evidence.RejectedLeaseCleanupPendingCount);
            Assert.False(factory.Evidence.CleanupCompleted);
            owner.Release.TrySetResult();
            var observed = await Assert.ThrowsAnyAsync<Exception>(async () => await acquisition.WaitAsync(Limit));
            Assert.Same(failure, observed);
            var result = await completion.WaitAsync(Limit);
            Assert.Equal(PcpRecordingSourceCompletionOutcome.CleanupIncomplete, result.Outcome);
            Assert.Equal(0, result.Evidence.AcquiredLeaseCount);
            Assert.Equal(0, result.Evidence.AcquisitionFailureCount);
            Assert.Equal(1, result.Evidence.RejectedLeaseCleanupFailureCount);
            Assert.Equal(0, result.Evidence.RejectedLeaseCleanupPendingCount);
            Assert.False(result.Evidence.CleanupCompleted);
            Assert.Equal(1, owner.Calls);
            Assert.Same(result, await factory.CompleteWithEvidenceAsync(Limit));
            Assert.Equal(1, factory.Evidence.RejectedLeaseCleanupFailureCount);
        }
        finally
        {
            release.Set();
            owner.Release.TrySetResult();
            try { await acquisition.WaitAsync(Limit); } catch (Exception) { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedFaultSurvivesAcquisitionDeadlineOrCallerCancellation(bool cancel)
    {
        await using var fanout = new PcpRelayFanout();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var factory = Create(fanout, owner: () =>
        {
            entered.Set();
            if (!release.Wait(Limit)) throw new TimeoutException();
            return null;
        });
        var acquisition = Task.Run(async () => await factory.AcquireAsync(Request(), CancellationToken.None));
        Assert.True(entered.Wait(Limit));
        using var cancellation = new CancellationTokenSource();
        try
        {
            var completion = factory.CompleteWithEvidenceAsync(cancel ? Limit : ShortLimit,
                new IOException("private accepted fault"), cancellation.Token);
            if (cancel) cancellation.Cancel();
            var result = await completion.WaitAsync(Limit);
            Assert.Equal(cancel ? PcpRecordingSourceCompletionOutcome.Cancelled : PcpRecordingSourceCompletionOutcome.TimedOut, result.Outcome);
            Assert.Equal(PcpRecordingSourceCompletionPhase.Acquisition, result.Phase);
            Assert.True(result.Evidence.AcceptedTerminalFault);
            Assert.False(result.Evidence.OwnerFaultRequested);
            Assert.False(result.Evidence.TerminalNotificationSelected);
            Assert.False(result.Evidence.TerminalNotificationCompleted);
            Assert.DoesNotContain("private accepted fault", result.ToString());
            release.Set();
            Assert.Null(await acquisition.WaitAsync(Limit));
            await factory.CompleteAsync();
            Assert.True(factory.Evidence.TerminalNotificationCompleted);
            Assert.False(result.Evidence.TerminalNotificationCompleted);
            Assert.Same(result, await factory.CompleteWithEvidenceAsync(Limit));
        }
        finally { release.Set(); await acquisition.WaitAsync(Limit); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyEofWinner_IsSeparateFromAcceptedTypedFault(bool legacyFirst)
    {
        await using var fanout = new PcpRelayFanout();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var factory = Create(fanout, owner: () =>
        {
            if (Interlocked.Increment(ref calls) == 1) return new ScriptedOwnerLease(null, released: true);
            entered.Set();
            if (!release.Wait(Limit)) throw new TimeoutException();
            return null;
        });
        var lease = await Acquire(factory);
        var acquisition = Task.Run(async () => await factory.AcquireAsync(Request(), CancellationToken.None));
        Assert.True(entered.Wait(Limit));
        try
        {
            if (legacyFirst) await factory.CompleteAsync();
            var completion = factory.CompleteWithEvidenceAsync(Limit, new IOException("accepted fault"));
            if (!legacyFirst) await factory.CompleteAsync();
            Assert.Equal(-1, lease.ReadStream.ReadByte());
            release.Set();
            Assert.Null(await acquisition.WaitAsync(Limit));
            await lease.DisposeAsync();
            var result = await completion.WaitAsync(Limit);
            Assert.Equal(PcpRecordingSourceCompletionOutcome.SourceFault, result.Outcome);
            Assert.True(result.Evidence.AcceptedTerminalFault);
            Assert.False(result.Evidence.OwnerFaultRequested);
            Assert.True(result.Evidence.TerminalNotificationCompleted);
            Assert.Equal(1, result.Evidence.EofNotificationCount);
            Assert.Equal(0, result.Evidence.FaultNotificationCount);
        }
        finally { release.Set(); await acquisition.WaitAsync(Limit); await lease.DisposeAsync(); }
    }

    [Fact]
    public async Task TypedFaultNotificationWinsBeforeLaterLegacyEof()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = Create(fanout);
        var lease = await Acquire(factory);
        var completion = factory.CompleteWithEvidenceAsync(Limit, new IOException("source failure"));
        await Assert.ThrowsAsync<IOException>(async () => await lease.ReadStream.CopyToAsync(Stream.Null).WaitAsync(Limit));
        await factory.CompleteAsync();
        await lease.DisposeAsync();
        var result = await completion.WaitAsync(Limit);
        Assert.Equal(PcpRecordingSourceCompletionOutcome.SourceFault, result.Outcome);
        Assert.True(result.Evidence.AcceptedTerminalFault);
        Assert.True(result.Evidence.OwnerFaultRequested);
        Assert.Equal(1, result.Evidence.FaultNotificationCount);
        Assert.Equal(0, result.Evidence.EofNotificationCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateTypedRequestKeepsFirstFaultAndCancellationToken(bool firstFault)
    {
        await using var fanout = new PcpRelayFanout();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var factory = Create(fanout, owner: () =>
        {
            entered.Set();
            if (!release.Wait(Limit)) throw new TimeoutException();
            return null;
        });
        var acquisition = Task.Run(async () => await factory.AcquireAsync(Request(), CancellationToken.None));
        Assert.True(entered.Wait(Limit));
        using var firstToken = new CancellationTokenSource();
        using var secondToken = new CancellationTokenSource();
        try
        {
            var first = factory.CompleteWithEvidenceAsync(Limit, firstFault ? new IOException("first") : null, firstToken.Token);
            var second = factory.CompleteWithEvidenceAsync(ShortLimit, firstFault ? null : new IOException("second"), secondToken.Token);
            Assert.Same(first, second);
            secondToken.Cancel();
            Assert.False(first.IsCompleted);
            Assert.Equal(firstFault, factory.Evidence.AcceptedTerminalFault);
            firstToken.Cancel();
            var result = await first.WaitAsync(Limit);
            Assert.Equal(PcpRecordingSourceCompletionOutcome.Cancelled, result.Outcome);
            Assert.Equal(firstFault, result.Evidence.AcceptedTerminalFault);
        }
        finally { release.Set(); await acquisition.WaitAsync(Limit); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RealRegistrationCompletionFaultStillCleansBridgeAndOwner(bool bridgeFails, bool ownerFails)
    {
        var fanout = new PcpRelayFanout();
        var workerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workerRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bridgeDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registrationFailure = new IOException("registration completion failure");
        var bridgeFailure = new IOException("bridge cleanup observation failure");
        var ownerFailure = new IOException("owner cleanup failure");
        var owner = new ScriptedOwnerLease(ownerFails ? ownerFailure : null);
        var factory = new PcpRelayFanoutRecordingStreamSourceLeaseFactory(Channel, Session, fanout, acquireOwnerLease: () => owner)
        {
            CleanupObserver = new()
            {
                AfterRegistrationCompletion = async () =>
                {
                    workerEntered.TrySetResult();
                    await workerRelease.Task;
                    throw registrationFailure;
                },
                AfterBridgeCleanup = () =>
                {
                    bridgeDisposed.TrySetResult();
                    return bridgeFails ? Task.FromException(bridgeFailure) : Task.CompletedTask;
                },
            },
        };
        factory.StartAccepting();
        var lease = await Acquire(factory);
        await factory.CompleteAsync();
        var disposal = lease.DisposeAsync().AsTask();
        await workerEntered.Task.WaitAsync(Limit);
        Assert.False(bridgeDisposed.Task.IsCompleted);
        workerRelease.TrySetResult();
        await bridgeDisposed.Task.WaitAsync(Limit);
        await owner.Entered.Task.WaitAsync(Limit);
        Assert.False(disposal.IsCompleted);
        owner.Release.TrySetResult();
        var failure = await Assert.ThrowsAnyAsync<Exception>(async () => await disposal.WaitAsync(Limit));
        var failures = failure is AggregateException aggregate ? aggregate.Flatten().InnerExceptions.ToArray() : [failure];
        Assert.Contains(registrationFailure, failures);
        Assert.Equal(1 + (bridgeFails ? 1 : 0) + (ownerFails ? 1 : 0), failures.Length);
        if (bridgeFails) Assert.Contains(bridgeFailure, failures);
        if (ownerFails) Assert.Contains(ownerFailure, failures);
        var result = await factory.CompleteWithEvidenceAsync(Limit);
        Assert.Equal(PcpRecordingSourceCompletionOutcome.CleanupIncomplete, result.Outcome);
        Assert.Equal(1, result.Evidence.RegistrationCleanupFailedLeaseCount);
        Assert.Equal(bridgeFails ? 1 : 0, result.Evidence.BridgeCleanupFailedLeaseCount);
        Assert.Equal(ownerFails ? 1 : 0, result.Evidence.OwnerCleanupFailedLeaseCount);
        Assert.Equal(1, result.Evidence.CleanupFailedLeaseCount);
        Assert.Equal(1, result.Evidence.RetiredLeaseCount);
        Assert.Equal(1, owner.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fanout.DisposeAsync());
    }

    [Fact]
    public async Task RegistrationCompletionFaultDuringChildStopReportsCleanupIncomplete()
    {
        var fanout = new PcpRelayFanout();
        var registrationFailure = new IOException("registration completion failure");
        var factory = new PcpRelayFanoutRecordingStreamSourceLeaseFactory(Channel, Session, fanout)
        {
            CleanupObserver = new()
            {
                AfterRegistrationCompletion = () => Task.FromException(registrationFailure),
            },
        };
        factory.StartAccepting();
        var lease = await Acquire(factory);
        await factory.CompleteAsync();

        var result = await factory.CompleteWithEvidenceAsync(Limit);
        Assert.Equal(PcpRecordingSourceCompletionOutcome.CleanupIncomplete, result.Outcome);
        Assert.Equal(1, result.Evidence.RegistrationCleanupFailedLeaseCount);

        var failure = await Assert.ThrowsAsync<IOException>(async () => await lease.DisposeAsync());
        Assert.Same(registrationFailure, failure);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fanout.DisposeAsync());
    }

    [Fact]
    public async Task WorkerCleanupFailedPreventsNormalEofAfterSuccessfulLeaseDisposal()
    {
        var fanout = new PcpRelayFanout();
        CancellationTokenRegistration callback = default;
        var tokenObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new PcpRelayFanoutRecordingStreamSourceLeaseFactory(Channel, Session, fanout)
        {
            CleanupObserver = new()
            {
                WorkerTokenObserved = token =>
                {
                    callback = token.Register(() => throw new IOException("worker callback failure"));
                    tokenObserved.TrySetResult();
                },
            },
        };
        factory.StartAccepting();
        var lease = await Acquire(factory);
        try
        {
            await fanout.ConsumeAsync(Packet(0, [1]), CancellationToken.None);
            await tokenObserved.Task.WaitAsync(Limit);
            Assert.True(await fanout.WaitForIdleAsync(Limit));
            await factory.CompleteAsync();
            await lease.DisposeAsync();
            var result = await factory.CompleteWithEvidenceAsync(Limit);
            Assert.Equal(PcpRecordingSourceCompletionOutcome.CleanupIncomplete, result.Outcome);
            Assert.Equal(1, result.Evidence.CleanupFailedLeaseCount);
            Assert.Equal(1, result.Evidence.RegistrationCleanupFailedLeaseCount);
            Assert.Equal(1, fanout.Evidence.CleanupFailedChildCount);
        }
        finally { callback.Dispose(); await lease.DisposeAsync(); }
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fanout.DisposeAsync());
    }

    [Fact]
    public async Task LateOwnerFailureRetiresOnceWithoutChangingDeadlineSnapshot()
    {
        await using var fanout = new PcpRelayFanout();
        var owner = new ScriptedOwnerLease(new IOException("late failure"));
        var factory = Create(fanout, owner: () => owner);
        var lease = await Acquire(factory);
        await factory.CompleteAsync();
        var completion = factory.CompleteWithEvidenceAsync(ShortLimit);
        var disposal = lease.DisposeAsync().AsTask();
        await owner.Entered.Task.WaitAsync(Limit);
        var result = await completion.WaitAsync(Limit);
        Assert.Equal(PcpRecordingSourceCompletionOutcome.CleanupIncomplete, result.Outcome);
        Assert.Equal(0, result.Evidence.OwnerCleanupFailedLeaseCount);
        Assert.Equal(0, result.Evidence.RetiredLeaseCount);
        owner.Release.TrySetResult();
        await Assert.ThrowsAsync<IOException>(async () => await disposal.WaitAsync(Limit));
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(1, factory.Evidence.OwnerCleanupFailedLeaseCount);
            Assert.Equal(1, factory.Evidence.RetiredLeaseCount);
        }
        Assert.Equal(0, result.Evidence.OwnerCleanupFailedLeaseCount);
        Assert.Same(result, await factory.CompleteWithEvidenceAsync(Limit));
    }

    [Fact]
    public async Task ForcedLeaseDisposeDuringPendingDeliveryCannotBecomeNormalEof()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = Create(fanout, new() { PauseWriterThresholdBytes = 8, ResumeWriterThresholdBytes = 4, MinimumSegmentSizeBytes = 8 });
        var lease = await Acquire(factory);
        await fanout.ConsumeAsync(Packet(0, new byte[16]), CancellationToken.None);
        var completion = factory.CompleteWithEvidenceAsync(Limit);
        Assert.Equal(1, factory.Evidence.PendingPacketCount);
        await lease.DisposeAsync();
        var result = await completion.WaitAsync(Limit);
        Assert.NotEqual(PcpRecordingSourceCompletionOutcome.NormalEof, result.Outcome);
        Assert.Equal(1, result.Evidence.AbortedLeaseCount);
        Assert.True(result.Evidence.DeliveryFailed);
        Assert.True(result.Evidence.CleanupCompleted);
    }

    [Fact]
    public async Task WrongChannelDoesNotAcquireOwnerOrChangeAdmissionCounters()
    {
        await using var fanout = new PcpRelayFanout();
        var calls = 0;
        var factory = Create(fanout, owner: () => { calls++; return null; });
        var wrong = new RecordingSinkStartRequest(new RecordingCandidate(new ChannelId(Guid.NewGuid()), new ChannelMetadataSnapshot()), Guid.NewGuid());
        await Assert.ThrowsAsync<ArgumentException>(async () => await factory.AcquireAsync(wrong, CancellationToken.None));
        Assert.Equal(0, calls);
        Assert.Equal(0, factory.Evidence.PendingAcquisitionCount);
        Assert.Equal(0, factory.Evidence.AcquisitionFailureCount);
        Assert.False(factory.Evidence.AdmissionClosed);
        Assert.Equal(0, fanout.ActiveChildCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedCleanupLateFaultRemainsVisibleAfterAcquisitionTimeout(bool cancelAcquire)
    {
        await using var fanout = new PcpRelayFanout();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var owner = new ScriptedOwnerLease(new OperationCanceledException("late rejected cleanup"));
        var factory = Create(fanout, owner: () =>
        {
            entered.Set();
            if (!release.Wait(Limit)) throw new TimeoutException();
            return owner;
        });
        using var cancellation = new CancellationTokenSource();
        var acquisition = Task.Run(async () => await factory.AcquireAsync(Request(), cancellation.Token));
        Assert.True(entered.Wait(Limit));
        var completion = factory.CompleteWithEvidenceAsync(ShortLimit);
        try
        {
            release.Set();
            await owner.Entered.Task.WaitAsync(Limit);
            if (cancelAcquire) cancellation.Cancel();
            var result = await completion.WaitAsync(Limit);
            Assert.Equal(PcpRecordingSourceCompletionOutcome.TimedOut, result.Outcome);
            Assert.Equal(1, result.Evidence.PendingAcquisitionCount);
            Assert.Equal(1, result.Evidence.RejectedLeaseCleanupPendingCount);
            Assert.Equal(0, result.Evidence.RejectedLeaseCleanupFailureCount);
            owner.Release.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await acquisition.WaitAsync(Limit));
            Assert.Equal(0, factory.Evidence.PendingAcquisitionCount);
            Assert.Equal(0, factory.Evidence.RejectedLeaseCleanupPendingCount);
            Assert.Equal(1, factory.Evidence.RejectedLeaseCleanupFailureCount);
            Assert.False(factory.Evidence.CleanupCompleted);
            Assert.Equal(0, result.Evidence.RejectedLeaseCleanupFailureCount);
            Assert.Same(result, await factory.CompleteWithEvidenceAsync(Limit));
        }
        finally
        {
            release.Set();
            owner.Release.TrySetResult();
            try { await acquisition.WaitAsync(Limit); } catch (Exception) { }
        }
    }

    [Fact]
    public async Task AcquisitionCancellationBeforeRegistrationStillRecordsIndependentCleanupFailure()
    {
        await using var fanout = new PcpRelayFanout();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var cleanupFailure = new IOException("owner cleanup failed after acquisition cancellation");
        var owner = new ScriptedOwnerLease(cleanupFailure);
        var factory = Create(fanout, owner: () =>
        {
            entered.Set();
            if (!release.Wait(Limit)) throw new TimeoutException();
            return owner;
        });
        using var cancellation = new CancellationTokenSource();
        var acquisition = Task.Run(async () => await factory.AcquireAsync(Request(), cancellation.Token));
        Assert.True(entered.Wait(Limit));
        var completion = factory.CompleteWithEvidenceAsync(Limit);
        try
        {
            cancellation.Cancel();
            release.Set();
            await owner.Entered.Task.WaitAsync(Limit);
            owner.Release.TrySetResult();
            var failure = await Assert.ThrowsAsync<AggregateException>(async () => await acquisition.WaitAsync(Limit));
            Assert.Contains(cleanupFailure, failure.InnerExceptions);
            Assert.Contains(failure.InnerExceptions, exception => exception is OperationCanceledException);
            var result = await completion.WaitAsync(Limit);
            Assert.Equal(PcpRecordingSourceCompletionOutcome.CleanupIncomplete, result.Outcome);
            Assert.Equal(1, result.Evidence.RejectedLeaseCleanupFailureCount);
            Assert.Equal(0, result.Evidence.AcquisitionFailureCount);
            Assert.Equal(1, owner.Calls);
        }
        finally
        {
            release.Set();
            owner.Release.TrySetResult();
            try { await acquisition.WaitAsync(Limit); } catch (Exception) { }
        }
    }

    private sealed class ScriptedOwnerLease(Exception? failure, bool released = false) : IAsyncDisposable
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = CreateRelease(released);
        public int Calls { get; private set; }
        public async ValueTask DisposeAsync()
        {
            Calls++;
            Entered.TrySetResult();
            await Release.Task;
            if (failure is not null) throw failure;
        }
        private static TaskCompletionSource CreateRelease(bool released)
        {
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (released) source.TrySetResult();
            return source;
        }
    }

    private static PcpRelayFanoutRecordingStreamSourceLeaseFactory Create(
        PcpRelayFanout fanout, PcpRecordingPacketStreamBridgeOptions? options = null, Func<IAsyncDisposable?>? owner = null)
    {
        var factory = new PcpRelayFanoutRecordingStreamSourceLeaseFactory(Channel, Session, fanout, options, owner);
        factory.StartAccepting();
        return factory;
    }

    private static async Task<IRecordingStreamSourceLease> Acquire(PcpRelayFanoutRecordingStreamSourceLeaseFactory factory)
    {
        var lease = await factory.AcquireAsync(Request(), CancellationToken.None);
        Assert.NotNull(lease);
        return lease;
    }

    private static RecordingSinkStartRequest Request() => new(
        new RecordingCandidate(Channel, new ChannelMetadataSnapshot(name: "fixture", streamExtension: "bin")), Guid.NewGuid());

    private static PcpChannelPacket Packet(long sequence, byte[] payload) =>
        new(Channel, Session, sequence, 1, checked((uint)sequence), sequence, payload, null);

    private sealed class GatedOwnerLease(bool fail = false) : IAsyncDisposable
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public async ValueTask DisposeAsync()
        {
            Calls++;
            Entered.TrySetResult();
            await Release.Task;
            if (fail) throw new IOException("private cleanup failure");
        }
    }

    private sealed class GatedSink : IPcpRelayChildSink
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask SendAsync(PcpRelayPacket packet, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class FixedPathProvider(string path) : IRecordingFilePathProvider
    {
        public string GetPath(RecordingSinkStartRequest request, RecordingSessionId sessionId) => path;
    }

    private sealed class TerminalObserver : IRecordingSessionTerminalObserver
    {
        public TaskCompletionSource<RecordingSessionTerminalFact> Terminal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ObserveTerminal(RecordingSessionTerminalFact fact) => Terminal.TrySetResult(fact);
        public void RecordPublicationFailure() => Terminal.TrySetException(new InvalidOperationException("publication failed"));
    }
}
