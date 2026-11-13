using System.Buffers;
using System.Globalization;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed partial class PcpRelayCoordinatorTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private static readonly Guid ChannelGuid = Guid.ParseExact(
        "531dc8dfc7fb42928ac2c0a626517a87",
        "N");

    private static readonly ChannelSessionId LocalSessionId = new(
        Guid.ParseExact("3f2504e04f8911d39a0c0305e82c3301", "N"));

    private static readonly Guid RemoteSessionId = Guid.ParseExact(
        "102132435465768798a9babbdcddedef",
        "N");

    [Fact]
    public async Task Coordinator_RelaysContinuousWirePacketsAndDrainsBeforeStop()
    {
        var transportOptions = CreateTransportOptions();
        await using var upstreamListener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            transportOptions);
        upstreamListener.Start();
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var releasePackets = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var upstreamTask = RunAcceptedUpstreamAttemptAsync(
            upstreamListener,
            expectedPosition: 0,
            releasePackets.Task,
            [
                CreatePacketAtom(position: 100, [0x10, 0x20]),
                CreatePacketAtom(position: 102, [0x30]),
                CreateQuitAtom(),
            ],
            timeoutSource.Token);
        var tracker = CreateTracker();
        await using var runtime = new PcpRelayRuntimeComposition();
        var coordinator = CreateCoordinator(
            runtime,
            transportOptions,
            tracker,
            maxReconnectAttempts: 0);
        Assert.Same(
            runtime.RecordingStreamSourceLeaseRegistry,
            coordinator.RecordingSourceLeaseRegistry);
        Assert.Same(
            runtime.RecordingStreamSourceLeaseRegistry,
            runtime.RecordingSourceProvider.LeaseFactory);
        var runTask = coordinator.RunAsync(
            upstreamListener.LocalEndPoint.Address.ToString(),
            upstreamListener.LocalEndPoint.Port,
            cancellationToken: timeoutSource.Token);
        var readiness = await coordinator.WaitForReadinessAsync(
            timeoutSource.Token);
        Assert.True(runtime.RecordingStreamSourceLeaseRegistry.TryGet(
            tracker.Snapshot.ChannelId,
            out _));
        await using var recordingReader =
            await runtime.RecordingSourceProvider.OpenReadAsync(
                CreateRecordingRequest(tracker.Snapshot.ChannelId),
                timeoutSource.Token);

        await using var downstream = await ConnectDownstreamAsync(
            coordinator.LocalEndPoint,
            transportOptions,
            timeoutSource.Token);
        releasePackets.TrySetResult();
        var payloads = await downstream.ReadPayloadsAsync(
            count: 2,
            timeoutSource.Token);
        var result = await runTask.WaitAsync(timeoutSource.Token);
        await upstreamTask;

        Assert.False(runtime.RecordingStreamSourceLeaseRegistry.TryGet(
            tracker.Snapshot.ChannelId,
            out _));
        Assert.Equal(0, runtime.RecordingStreamSourceLeaseRegistry.Count);

        using var recordedPayload = new MemoryStream();
        await Assert.ThrowsAsync<IOException>(
            async () => await recordingReader.CopyToAsync(
                recordedPayload,
                timeoutSource.Token));

        Assert.Equal(
            new byte[][] { [0x10, 0x20], [0x30] },
            payloads);
        Assert.Equal(
            PcpRelayCoordinatorReadinessOutcome.Ready,
            readiness);
        Assert.Equal(
            PcpRelayCoordinatorEndReason.UpstreamEnded,
            result.EndReason);
        Assert.Equal(PcpRelayDrainOutcome.Completed, result.DrainOutcome);
        Assert.True(result.ShutdownCompleted);
        Assert.Equal(2, result.Ingress.AcceptedPacketCount);
        Assert.Equal(3, result.Ingress.AcceptedPayloadByteCount);
        Assert.Equal(PcpChannelSessionEndReason.QuitReceived, result.Upstream?.EndReason);
        Assert.Equal(2, result.Upstream?.PacketCount);
        Assert.Equal(3, result.Upstream?.PayloadByteCount);
        Assert.Equal(1, result.Downstream?.Snapshot.AcceptedHandshakeCount);
        Assert.Equal(2, result.Downstream?.Snapshot.DeliveredPacketCount);
        Assert.Equal(3, result.Downstream?.Snapshot.DeliveredByteCount);
        Assert.Equal(0, result.Downstream?.Snapshot.DiscardedPacketCount);
        Assert.Equal(0, result.Downstream?.Snapshot.CleanupIncompleteCount);
        Assert.Equal(PcpRelayCoordinatorState.Stopped, coordinator.State);
        Assert.Equal(102, tracker.Snapshot.StreamPosition);
    }

    [Fact]
    public async Task RuntimeComposition_ShutdownRemovesOwnersAndIsIdempotent()
    {
        var transportOptions = CreateTransportOptions();
        await using var upstreamListener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            transportOptions);
        upstreamListener.Start();
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var accepted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var upstreamTask = RunHeldUpstreamAsync(
            upstreamListener,
            accepted,
            timeoutSource.Token);
        var tracker = CreateTracker();
        await using var runtime = new PcpRelayRuntimeComposition();
        var coordinator = CreateCoordinator(
            runtime,
            transportOptions,
            tracker,
            maxReconnectAttempts: 0);
        var runTask = coordinator.RunAsync(
            upstreamListener.LocalEndPoint.Address.ToString(),
            upstreamListener.LocalEndPoint.Port,
            cancellationToken: timeoutSource.Token);
        Assert.Equal(
            PcpRelayCoordinatorReadinessOutcome.Ready,
            await coordinator.WaitForReadinessAsync(timeoutSource.Token));
        await accepted.Task.WaitAsync(timeoutSource.Token);

        await using var recordingReader =
            await runtime.RecordingSourceProvider.OpenReadAsync(
                CreateRecordingRequest(tracker.Snapshot.ChannelId),
                timeoutSource.Token);
        Assert.Equal(1, runtime.RecordingStreamSourceLeaseRegistry.Count);

        await runtime.DisposeAsync();
        var result = await runTask.WaitAsync(timeoutSource.Token);
        await upstreamTask;

        Assert.Equal(PcpRelayCoordinatorEndReason.Stopped, result.EndReason);
        Assert.Equal(0, runtime.RecordingStreamSourceLeaseRegistry.Count);
        Assert.Equal(-1, recordingReader.ReadByte());
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await runtime.RecordingSourceProvider.OpenReadAsync(
                CreateRecordingRequest(tracker.Snapshot.ChannelId),
                timeoutSource.Token));

        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task RecordingRuntime_UsesExistingRelayFanoutAndFinalizesFile()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "PeerCast-Nova-PcpRecordingRuntimeTests",
            Guid.NewGuid().ToString("N"));
        var firstPayload = Enumerable.Range(0, 64 * 1024)
            .Select(static index => (byte)((index + 11) % 251))
            .ToArray();
        var secondPayload = Enumerable.Range(0, 64 * 1024)
            .Select(static index => (byte)((index + 17) % 251))
            .ToArray();
        var releaseFirstPacket = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecondPacket = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closeUpstream = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstPacketWritten = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondPacketWritten = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task? upstreamTask = null;

        try
        {
            var transportOptions = CreateTransportOptions();
            await using var upstreamListener = new TcpTransportListener(
                new IPEndPoint(IPAddress.Loopback, 0),
                transportOptions);
            upstreamListener.Start();
            using var timeoutSource = new CancellationTokenSource(TestTimeout);
            upstreamTask = RunHeldRecordingPacketsAsync(
                upstreamListener,
                firstPayload,
                secondPayload,
                accepted,
                releaseFirstPacket.Task,
                firstPacketWritten,
                releaseSecondPacket.Task,
                secondPacketWritten,
                closeUpstream.Task,
                timeoutSource.Token);

            await using (var relayRuntime = new PcpRelayRuntimeComposition())
            await using (var recordingRuntime = new RecordingRuntime(
                relayRuntime.RecordingSourceProvider,
                new DefaultRecordingFilePathProvider(directory)))
            {
                try
                {
                    var tracker = CreateTracker();
                    var coordinator = relayRuntime.CreateCoordinator(
                        new TcpTransportConnector(transportOptions),
                        tracker,
                        new IPEndPoint(IPAddress.Loopback, 0));
                    var runTask = coordinator.RunAsync(
                        upstreamListener.LocalEndPoint.Address.ToString(),
                        upstreamListener.LocalEndPoint.Port,
                        cancellationToken: timeoutSource.Token);

                    Assert.Equal(
                        PcpRelayCoordinatorReadinessOutcome.Ready,
                        await coordinator.WaitForReadinessAsync(timeoutSource.Token));
                    await accepted.Task.WaitAsync(timeoutSource.Token);
                    await WaitForConditionAsync(
                        () => relayRuntime.RecordingStreamSourceLeaseRegistry.Count == 1,
                        timeoutSource.Token);

                    var started = await recordingRuntime.StartManualAsync(
                        new RecordingCandidate(
                            tracker.Snapshot.ChannelId,
                            // These are opaque PCP fixture bytes, not an FLV file.
                            new ChannelMetadataSnapshot(
                                name: "Channel",
                                streamExtension: "bin")),
                      timeoutSource.Token);
                    Assert.Equal(RecordingManualStartResultKind.Started, started.Kind);

                    releaseFirstPacket.TrySetResult();
                    await firstPacketWritten.Task.WaitAsync(timeoutSource.Token);
                    await WaitForPartialSizeAsync(
                        directory,
                        expectedSize: firstPayload.Length,
                        timeoutSource.Token);

                    releaseSecondPacket.TrySetResult();
                    await secondPacketWritten.Task.WaitAsync(timeoutSource.Token);
                    await WaitForPartialSizeAsync(
                        directory,
                        expectedSize: firstPayload.Length + secondPayload.Length,
                        timeoutSource.Token);

                    var stopped = await recordingRuntime.StopAsync(
                        tracker.Snapshot.ChannelId,
                        timeoutSource.Token);
                    Assert.Equal(RecordingAutomationStopResultKind.Stopped, stopped.Kind);
                    var finalFiles = Directory.GetFiles(directory)
                        .Where(static path => !path.EndsWith(
                            ".partial",
                            StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    var finalPath = Assert.Single(finalFiles);
                    Assert.Equal(
                        firstPayload.Concat(secondPayload).ToArray(),
                        await File.ReadAllBytesAsync(finalPath, timeoutSource.Token));

                    closeUpstream.TrySetResult();
                    var result = await runTask.WaitAsync(timeoutSource.Token);
                    await upstreamTask.WaitAsync(timeoutSource.Token);
                    Assert.Equal(PcpRelayCoordinatorEndReason.UpstreamEnded, result.EndReason);
                }
                finally
                {
                    releaseFirstPacket.TrySetResult();
                    releaseSecondPacket.TrySetResult();
                    closeUpstream.TrySetResult();

                    if (upstreamTask is not null)
                    {
                        try
                        {
                            await upstreamTask.WaitAsync(TimeSpan.FromSeconds(2));
                        }
                        catch
                        {
                            // Preserve the primary assertion or runtime failure.
                        }
                    }
                }
            }
        }
        finally
        {
            releaseFirstPacket.TrySetResult();
            releaseSecondPacket.TrySetResult();
            closeUpstream.TrySetResult();

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Coordinator_RelaysHeadBeforeFollowingDataToDownstreamPeer()
    {
        const uint headType = 0x64616568;
        const uint dataType = 0x61746164;
        var transportOptions = CreateTransportOptions();
        await using var upstreamListener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            transportOptions);
        upstreamListener.Start();
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var releasePackets = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDataAfterHead = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var upstreamTask = RunAcceptedUpstreamAttemptAsync(
            upstreamListener,
            expectedPosition: 0,
            releasePackets.Task,
            [
                CreatePacketAtom(position: 100, [0x48], packetType: headType),
                CreatePacketAtom(position: 101, [0x44], packetType: dataType),
                CreateQuitAtom(),
            ],
            timeoutSource.Token,
            releaseRemainingPackets: releaseDataAfterHead.Task);
        await using var coordinator = CreateCoordinator(
            transportOptions,
            CreateTracker(),
            maxReconnectAttempts: 0);
        var runTask = coordinator.RunAsync(
            upstreamListener.LocalEndPoint.Address.ToString(),
            upstreamListener.LocalEndPoint.Port,
            cancellationToken: timeoutSource.Token);
        var readiness = await coordinator.WaitForReadinessAsync(timeoutSource.Token);
        releasePackets.TrySetResult();
        await coordinator.FirstPacketAccepted.WaitAsync(timeoutSource.Token);
        await using var downstream = await ConnectDownstreamAsync(
            coordinator.LocalEndPoint,
            transportOptions,
            timeoutSource.Token);
        releaseDataAfterHead.TrySetResult();
        var packets = await downstream.ReadChannelPacketsAsync(2, timeoutSource.Token);
        var result = await runTask.WaitAsync(timeoutSource.Token);
        await upstreamTask;

        Assert.Equal(PcpRelayCoordinatorReadinessOutcome.Ready, readiness);
        Assert.Collection(
            packets,
            head =>
            {
                Assert.Equal(headType, head.Type);
                Assert.Equal([0x48], head.Payload);
            },
            data =>
            {
                Assert.Equal(dataType, data.Type);
                Assert.Equal([0x44], data.Payload);
            });
        Assert.Equal(PcpRelayCoordinatorEndReason.UpstreamEnded, result.EndReason);
        Assert.True(result.ShutdownCompleted);
    }

    [Fact]
    public async Task SharedOwner_PlaybackAndRecordingShareOneUpstreamAndStopIndependently()
    {
        const uint headType = 0x64616568;
        const uint dataType = 0x61746164;
        var transportOptions = CreateTransportOptions();
        await using var upstreamListener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            transportOptions);
        upstreamListener.Start();
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var accepted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHead = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var headWritten = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstData = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstDataWritten = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecondData = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondDataWritten = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseThirdData = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdDataWritten = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var finishUpstream = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var upstreamAcceptCount = 0;
        var upstreamTask = RunSharedOwnerUpstreamAsync(
            upstreamListener,
            accepted,
            () => Interlocked.Increment(ref upstreamAcceptCount),
            releaseHead.Task,
            headWritten,
            releaseFirstData.Task,
            firstDataWritten,
            releaseSecondData.Task,
            secondDataWritten,
            releaseThirdData.Task,
            thirdDataWritten,
            finishUpstream.Task,
            timeoutSource.Token);

        var tracker = CreateTracker();
        await using var runtime = new PcpRelayRuntimeComposition();
        var options = CreateCoordinatorOptions(
            transportOptions,
            maxReconnectAttempts: 0) with
        {
            UpstreamSession = new PcpChannelSessionOptions
            {
                HandshakeTimeout = TimeSpan.FromSeconds(2),
                MaxReconnectAttempts = 0,
                Stream = new PcpChannelStreamOptions
                {
                    ReadIdleTimeout = TestTimeout,
                },
            },
        };
        var coordinator = runtime.CreateCoordinator(
            new TcpTransportConnector(transportOptions),
            tracker,
            new IPEndPoint(IPAddress.Loopback, 0),
            options,
            stopWhenNoConsumers: true);
        var earlyPlayback = new CollectingChannelPacketConsumer();
        var earlyAttachment = await coordinator.AttachPlaybackConsumerAsync(
            earlyPlayback);
        Assert.NotNull(earlyAttachment);
        Assert.Equal(1, coordinator.ActiveConsumerCount);

        var runTask = coordinator.RunAsync(
            upstreamListener.LocalEndPoint.Address.ToString(),
            upstreamListener.LocalEndPoint.Port,
            cancellationToken: CancellationToken.None);
        Assert.Equal(
            PcpRelayCoordinatorReadinessOutcome.Ready,
            await coordinator.WaitForReadinessAsync(timeoutSource.Token));
        await accepted.Task.WaitAsync(timeoutSource.Token);
        await WaitForConditionAsync(
            () => runtime.RecordingStreamSourceLeaseRegistry.TryGet(
                tracker.Snapshot.ChannelId,
                out _),
            timeoutSource.Token);

        var recordingReader = await runtime.RecordingSourceProvider.OpenReadAsync(
            CreateRecordingRequest(tracker.Snapshot.ChannelId),
            timeoutSource.Token);
        Assert.Equal(2, coordinator.ActiveConsumerCount);

        releaseHead.TrySetResult();
        var earlyHead = await earlyPlayback.ReceiveAsync(timeoutSource.Token);
        await headWritten.Task.WaitAsync(timeoutSource.Token);

        var failingPlayback = await coordinator.AttachPlaybackConsumerAsync(
            new FailingChannelPacketConsumer());
        Assert.NotNull(failingPlayback);
        await failingPlayback!.Completion.WaitAsync(timeoutSource.Token);
        await failingPlayback.DisposeAsync();
        Assert.Equal(2, coordinator.ActiveConsumerCount);

        var latePlayback = new CollectingChannelPacketConsumer();
        var lateAttachment = await coordinator.AttachPlaybackConsumerAsync(
            latePlayback);
        Assert.NotNull(lateAttachment);
        var lateHead = await latePlayback.ReceiveAsync(timeoutSource.Token);
        var recordedHead = new byte[1];
        await recordingReader.ReadExactlyAsync(
            recordedHead,
            timeoutSource.Token);

        Assert.Equal(headType, earlyHead.Type);
        Assert.Equal(0, earlyHead.Sequence);
        Assert.Equal(ChannelGuid, earlyHead.ChannelId.Value);
        Assert.Equal(LocalSessionId, earlyHead.SessionId);
        Assert.Equal([0x48], earlyHead.Payload);
        Assert.Equal(headType, lateHead.Type);
        Assert.Equal(0, lateHead.Sequence);
        Assert.Equal(LocalSessionId, lateHead.SessionId);
        Assert.Equal([0x48], lateHead.Payload);
        Assert.Equal([0x48], recordedHead);
        Assert.Equal(3, coordinator.ActiveConsumerCount);

        releaseFirstData.TrySetResult();
        await headWritten.Task.WaitAsync(timeoutSource.Token);
        var earlyData = await earlyPlayback.ReceiveAsync(timeoutSource.Token);
        var lateData = await latePlayback.ReceiveAsync(timeoutSource.Token);
        await firstDataWritten.Task.WaitAsync(timeoutSource.Token);
        var recordedFirstData = new byte[1];
        await recordingReader.ReadExactlyAsync(
            recordedFirstData,
            timeoutSource.Token);
        Assert.Equal(dataType, earlyData.Type);
        Assert.Equal(1, earlyData.Sequence);
        Assert.Equal([0x44], earlyData.Payload);
        Assert.Equal(dataType, lateData.Type);
        Assert.Equal(1, lateData.Sequence);
        Assert.Equal([0x44], lateData.Payload);
        Assert.Equal([0x44], recordedFirstData);

        await recordingReader.DisposeAsync();
        Assert.Equal(2, coordinator.ActiveConsumerCount);
        Assert.Equal(PcpRelayCoordinatorState.Running, coordinator.State);

        releaseSecondData.TrySetResult();
        await secondDataWritten.Task.WaitAsync(timeoutSource.Token);
        var earlySecondData = await earlyPlayback.ReceiveAsync(timeoutSource.Token);
        var lateSecondData = await latePlayback.ReceiveAsync(timeoutSource.Token);
        Assert.Equal([0x45], earlySecondData.Payload);
        Assert.Equal([0x45], lateSecondData.Payload);

        var recordingReaderAfterPlayback =
            await runtime.RecordingSourceProvider.OpenReadAsync(
                CreateRecordingRequest(tracker.Snapshot.ChannelId),
                timeoutSource.Token);
        var replayedRecordedHead = new byte[1];
        await recordingReaderAfterPlayback.ReadExactlyAsync(
            replayedRecordedHead,
            timeoutSource.Token);
        Assert.Equal([0x48], replayedRecordedHead);

        await earlyAttachment!.DisposeAsync();
        await lateAttachment!.DisposeAsync();
        Assert.Equal(1, coordinator.ActiveConsumerCount);
        Assert.Equal(PcpRelayCoordinatorState.Running, coordinator.State);

        releaseThirdData.TrySetResult();
        await thirdDataWritten.Task.WaitAsync(timeoutSource.Token);
        var recordedThirdData = new byte[1];
        await recordingReaderAfterPlayback.ReadExactlyAsync(
            recordedThirdData,
            timeoutSource.Token);
        Assert.Equal([0x46], recordedThirdData);

        await recordingReaderAfterPlayback.DisposeAsync();
        var result = await runTask.WaitAsync(timeoutSource.Token);
        finishUpstream.TrySetResult();
        await upstreamTask.WaitAsync(timeoutSource.Token);

        Assert.Equal(1, Volatile.Read(ref upstreamAcceptCount));
        Assert.Equal(PcpRelayCoordinatorEndReason.Stopped, result.EndReason);
        Assert.Equal(PcpRelayCoordinatorState.Stopped, coordinator.State);
        Assert.Equal(0, coordinator.ActiveConsumerCount);
        Assert.False(runtime.RecordingStreamSourceLeaseRegistry.TryGet(
            tracker.Snapshot.ChannelId,
            out _));
        Assert.True(result.ShutdownCompleted);
    }

    [Fact]
    public async Task Coordinator_PreservesDownstreamChildAcrossUpstreamReconnect()
    {
        var transportOptions = CreateTransportOptions();
        await using var upstreamListener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            transportOptions);
        upstreamListener.Start();
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var releasePackets = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var upstreamTask = RunTwoUpstreamAttemptsAsync(
            upstreamListener,
            releasePackets.Task,
            timeoutSource.Token);
        var tracker = CreateTracker();
        await using var coordinator = CreateCoordinator(
            transportOptions,
            tracker,
            maxReconnectAttempts: 1);
        var runTask = coordinator.RunAsync(
            upstreamListener.LocalEndPoint.Address.ToString(),
            upstreamListener.LocalEndPoint.Port,
            cancellationToken: timeoutSource.Token);

        await using var downstream = await ConnectDownstreamAsync(
            coordinator.LocalEndPoint,
            transportOptions,
            timeoutSource.Token);
        releasePackets.TrySetResult();
        var payloads = await downstream.ReadPayloadsAsync(
            count: 2,
            timeoutSource.Token);
        var result = await runTask.WaitAsync(timeoutSource.Token);
        await upstreamTask;

        Assert.Equal(new byte[][] { [0x41], [0x42] }, payloads);
        Assert.Equal(
            PcpRelayCoordinatorEndReason.UpstreamEnded,
            result.EndReason);
        Assert.Equal(PcpRelayDrainOutcome.Completed, result.DrainOutcome);
        Assert.Equal(2, result.Ingress.AcceptedPacketCount);
        Assert.Equal(2, result.Ingress.AcceptedPayloadByteCount);
        Assert.Equal(2, result.Upstream?.AttemptCount);
        Assert.Equal(1, result.Upstream?.ReconnectCount);
        Assert.Equal(2, result.Upstream?.PacketCount);
        Assert.Equal(1, result.Downstream?.Snapshot.AcceptedConnectionCount);
        Assert.Equal(2, result.Downstream?.Snapshot.DeliveredPacketCount);
        Assert.Equal(200, tracker.Snapshot.StreamPosition);
    }

    [Fact]
    public async Task Coordinator_CallerCancellationStopsOwnedBoundaries()
    {
        var transportOptions = CreateTransportOptions();
        await using var upstreamListener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            transportOptions);
        upstreamListener.Start();
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        using var callerSource = new CancellationTokenSource();
        var accepted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var upstreamTask = RunHeldUpstreamAsync(
            upstreamListener,
            accepted,
            timeoutSource.Token);
        var tracker = CreateTracker();
        var recordingSources = new RecordingStreamSourceLeaseRegistry();
        await using var coordinator = CreateCoordinator(
            transportOptions,
            tracker,
            maxReconnectAttempts: 0,
            recordingSourceLeaseRegistry: recordingSources);
        var runTask = coordinator.RunAsync(
            upstreamListener.LocalEndPoint.Address.ToString(),
            upstreamListener.LocalEndPoint.Port,
            cancellationToken: callerSource.Token);
        var recordingLease = await recordingSources.AcquireAsync(
            CreateRecordingRequest(tracker.Snapshot.ChannelId),
            timeoutSource.Token);
        Assert.NotNull(recordingLease);
        Assert.Throws<InvalidOperationException>(
            () =>
            {
                _ = coordinator.RunAsync(
                    upstreamListener.LocalEndPoint.Address.ToString(),
                    upstreamListener.LocalEndPoint.Port);
            });

        await accepted.Task.WaitAsync(timeoutSource.Token);
        callerSource.Cancel();
        Assert.False(recordingSources.TryGet(
            tracker.Snapshot.ChannelId,
            out _));
        Assert.Null(await recordingSources.AcquireAsync(
            CreateRecordingRequest(tracker.Snapshot.ChannelId),
            timeoutSource.Token));
        var result = await runTask.WaitAsync(timeoutSource.Token);
        await upstreamTask;

        Assert.Equal(-1, recordingLease.ReadStream.ReadByte());
        await recordingLease.DisposeAsync();

        Assert.Equal(
            PcpRelayCoordinatorEndReason.CallerCanceled,
            result.EndReason);
        Assert.Equal(PcpRelayDrainOutcome.NotRequired, result.DrainOutcome);
        Assert.True(result.ShutdownCompleted);
        Assert.Equal(0, result.Ingress.AcceptedPacketCount);
        Assert.Equal(0, result.Ingress.AcceptedPayloadByteCount);
        Assert.Equal(PcpChannelSessionEndReason.Stopped, result.Upstream?.EndReason);
        Assert.Equal(
            PcpDownstreamRelayServerEndReason.Stopped,
            result.Downstream?.EndReason);
        Assert.Equal(PcpRelayCoordinatorState.Stopped, coordinator.State);
    }

    [Fact]
    public async Task Coordinator_StartupFailureLeavesNoRecordingRegistration()
    {
        var transportOptions = CreateTransportOptions();
        await using var occupiedListener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            transportOptions);
        occupiedListener.Start();
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var tracker = CreateTracker();
        var recordingSources = new RecordingStreamSourceLeaseRegistry();
        await using var coordinator = CreateCoordinator(
            transportOptions,
            tracker,
            maxReconnectAttempts: 0,
            downstreamEndPoint: occupiedListener.LocalEndPoint,
            recordingSourceLeaseRegistry: recordingSources);

        var runTask = coordinator.RunAsync(
            IPAddress.Loopback.ToString(),
            upstreamPort: 1,
            cancellationToken: timeoutSource.Token);
        var readiness = await coordinator.WaitForReadinessAsync(
            timeoutSource.Token);
        var result = await runTask.WaitAsync(timeoutSource.Token);

        Assert.Equal(
            PcpRelayCoordinatorReadinessOutcome.Failed,
            readiness);
        Assert.Equal(PcpRelayCoordinatorEndReason.DownstreamFailed, result.EndReason);
        Assert.Equal(0, recordingSources.Count);
        Assert.False(recordingSources.TryGet(
            tracker.Snapshot.ChannelId,
            out _));
    }

    [Fact]
    public async Task Coordinator_FullRecordingRegistryDoesNotStopRelay()
    {
        var transportOptions = CreateTransportOptions();
        await using var upstreamListener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            transportOptions);
        upstreamListener.Start();
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var accepted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var upstreamTask = RunHeldUpstreamAsync(
            upstreamListener,
            accepted,
            timeoutSource.Token);
        var otherChannelId = new ChannelId(
            Guid.ParseExact("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "N"));
        await using var otherFanout = new PcpRelayFanout();
        var recordingSources = new RecordingStreamSourceLeaseRegistry(
            maximumFactoryCount: 1);
        recordingSources.Upsert(
            otherChannelId,
            new PcpRelayFanoutRecordingStreamSourceLeaseFactory(
                otherChannelId,
                new ChannelSessionId(Guid.NewGuid()),
                otherFanout));
        var tracker = CreateTracker();
        await using var coordinator = CreateCoordinator(
            transportOptions,
            tracker,
            maxReconnectAttempts: 0,
            recordingSourceLeaseRegistry: recordingSources);

        var runTask = coordinator.RunAsync(
            upstreamListener.LocalEndPoint.Address.ToString(),
            upstreamListener.LocalEndPoint.Port,
            cancellationToken: timeoutSource.Token);
        var readiness = await coordinator.WaitForReadinessAsync(
            timeoutSource.Token);
        await accepted.Task.WaitAsync(timeoutSource.Token);

        Assert.Equal(PcpRelayCoordinatorReadinessOutcome.Ready, readiness);
        Assert.Equal(PcpRelayCoordinatorState.Running, coordinator.State);
        Assert.Equal(1, recordingSources.Count);
        Assert.False(recordingSources.TryGet(
            tracker.Snapshot.ChannelId,
            out _));

        coordinator.RequestStop();
        var result = await runTask.WaitAsync(timeoutSource.Token);
        await upstreamTask;

        Assert.Equal(PcpRelayCoordinatorEndReason.Stopped, result.EndReason);
        Assert.Equal(1, recordingSources.Count);
    }

    [Fact]
    public async Task Coordinator_ConvertsUpstreamFailureToFixedResult()
    {
        var transportOptions = CreateTransportOptions();
        await using var upstreamListener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            transportOptions);
        upstreamListener.Start();
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var releasePacket = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var upstreamTask = RunPacketThenTruncatedUpstreamAsync(
            upstreamListener,
            releasePacket.Task,
            timeoutSource.Token);
        var tracker = CreateTracker();
        var recordingSources = new RecordingStreamSourceLeaseRegistry();
        await using var coordinator = CreateCoordinator(
            transportOptions,
            tracker,
            maxReconnectAttempts: 0,
            recordingSourceLeaseRegistry: recordingSources);

        var runTask = coordinator.RunAsync(
            upstreamListener.LocalEndPoint.Address.ToString(),
            upstreamListener.LocalEndPoint.Port,
            cancellationToken: timeoutSource.Token);
        Assert.Equal(
            PcpRelayCoordinatorReadinessOutcome.Ready,
            await coordinator.WaitForReadinessAsync(timeoutSource.Token));
        var recordingLease = await recordingSources.AcquireAsync(
            CreateRecordingRequest(tracker.Snapshot.ChannelId),
            timeoutSource.Token);
        Assert.NotNull(recordingLease);

        await using var downstream = await ConnectDownstreamAsync(
            coordinator.LocalEndPoint,
            transportOptions,
            timeoutSource.Token);
        releasePacket.TrySetResult();
        var payloads = await downstream.ReadPayloadsAsync(
            count: 1,
            timeoutSource.Token);
        var result = await runTask.WaitAsync(timeoutSource.Token);
        await upstreamTask;

        Assert.False(recordingSources.TryGet(
            tracker.Snapshot.ChannelId,
            out _));
        Assert.Null(await recordingSources.AcquireAsync(
            CreateRecordingRequest(tracker.Snapshot.ChannelId),
            timeoutSource.Token));
        using var recordedPayload = new MemoryStream();
        await Assert.ThrowsAsync<IOException>(
            async () => await recordingLease.ReadStream.CopyToAsync(
                recordedPayload,
                timeoutSource.Token));
        await recordingLease.DisposeAsync();

        Assert.Equal(new byte[][] { [0x51, 0x52] }, payloads);
        Assert.Equal(
            PcpRelayCoordinatorEndReason.UpstreamFailed,
            result.EndReason);
        Assert.Equal(PcpRelayDrainOutcome.Completed, result.DrainOutcome);
        Assert.Null(result.Upstream);
        Assert.Equal(1, result.Ingress.AcceptedPacketCount);
        Assert.Equal(2, result.Ingress.AcceptedPayloadByteCount);
        Assert.Equal(1, coordinator.IngressSnapshot.AcceptedPacketCount);
        Assert.Equal(1, result.Downstream?.Snapshot.DeliveredPacketCount);
        Assert.Equal(2, result.Downstream?.Snapshot.DeliveredByteCount);
        Assert.True(result.ShutdownCompleted);
        Assert.Equal(
            ChannelFaultCategory.ConnectionFailed,
            tracker.Snapshot.Fault?.Category);

        var properties = typeof(PcpRelayCoordinatorResult).GetProperties();
        Assert.DoesNotContain(
            properties,
            property => property.PropertyType == typeof(string));
        Assert.DoesNotContain(
            properties,
            property => typeof(Exception).IsAssignableFrom(property.PropertyType));
        Assert.DoesNotContain(
            properties,
            property => property.PropertyType == typeof(IPEndPoint));

        var ingressProperties = typeof(PcpRelayIngressSnapshot).GetProperties();
        Assert.All(
            ingressProperties,
            property => Assert.Equal(typeof(long), property.PropertyType));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PcpRelayCoordinatorOptions
            {
                DrainTimeout = TimeSpan.Zero,
            }.Validate());
    }

    [Fact]
    public async Task Coordinator_ReadinessReportsOccupiedListenerFailure()
    {
        var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();

        try
        {
            var transportOptions = CreateTransportOptions();
            var occupiedEndPoint = (IPEndPoint)occupied.LocalEndpoint;
            using var timeoutSource = new CancellationTokenSource(TestTimeout);
            await using var coordinator = CreateCoordinator(
                transportOptions,
                CreateTracker(),
                maxReconnectAttempts: 0,
                occupiedEndPoint);

            var runTask = coordinator.RunAsync(
                IPAddress.Loopback.ToString(),
                upstreamPort: 1,
                cancellationToken: timeoutSource.Token);
            var readiness = await coordinator.WaitForReadinessAsync(
                timeoutSource.Token);
            var result = await runTask.WaitAsync(timeoutSource.Token);

            Assert.Equal(
                PcpRelayCoordinatorReadinessOutcome.Failed,
                readiness);
            Assert.Equal(
                PcpRelayCoordinatorEndReason.DownstreamFailed,
                result.EndReason);
            Assert.Throws<InvalidOperationException>(
                () => coordinator.LocalEndPoint);
        }
        finally
        {
            occupied.Stop();
        }
    }

    [Fact]
    public async Task Coordinator_UnstartedStopCompletesReadiness()
    {
        var transportOptions = CreateTransportOptions();
        await using var coordinator = CreateCoordinator(
            transportOptions,
            CreateTracker(),
            maxReconnectAttempts: 0);

        var readinessTask = coordinator.WaitForReadinessAsync();
        await coordinator.StopAsync();
        var readiness = await readinessTask;

        Assert.Equal(
            PcpRelayCoordinatorReadinessOutcome.Stopped,
            readiness);
        Assert.Equal(PcpRelayCoordinatorState.Stopped, coordinator.State);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.RunAsync(
                IPAddress.Loopback.ToString(),
                upstreamPort: 1));
    }

    [Fact]
    public async Task Coordinator_ReadinessWaitCancellationDoesNotStopOwner()
    {
        var transportOptions = CreateTransportOptions();
        await using var coordinator = CreateCoordinator(
            transportOptions,
            CreateTracker(),
            maxReconnectAttempts: 0);
        using var waitSource = new CancellationTokenSource();

        var canceledWait = coordinator.WaitForReadinessAsync(
            waitSource.Token);
        waitSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => canceledWait);
        Assert.Equal(PcpRelayCoordinatorState.Created, coordinator.State);

        var terminalWait = coordinator.WaitForReadinessAsync();
        await coordinator.StopAsync();

        Assert.Equal(
            PcpRelayCoordinatorReadinessOutcome.Stopped,
            await terminalWait);
    }

    private static PcpRelayCoordinator CreateCoordinator(
        PcpRelayRuntimeComposition runtime,
        TcpTransportOptions transportOptions,
        ChannelStatusTracker tracker,
        int maxReconnectAttempts,
        IPEndPoint? downstreamEndPoint = null)
    {
        return runtime.CreateCoordinator(
            new TcpTransportConnector(transportOptions),
            tracker,
            downstreamEndPoint ?? new IPEndPoint(IPAddress.Loopback, 0),
            CreateCoordinatorOptions(
                transportOptions,
                maxReconnectAttempts));
    }

    private static PcpRelayCoordinator CreateCoordinator(
        TcpTransportOptions transportOptions,
        ChannelStatusTracker tracker,
        int maxReconnectAttempts,
        IPEndPoint? downstreamEndPoint = null,
        RecordingStreamSourceLeaseRegistry? recordingSourceLeaseRegistry = null)
    {
        return new PcpRelayCoordinator(
            new TcpTransportConnector(transportOptions),
            tracker,
            downstreamEndPoint ?? new IPEndPoint(IPAddress.Loopback, 0),
            CreateCoordinatorOptions(
                transportOptions,
                maxReconnectAttempts),
            recordingSourceLeaseRegistry);
    }

    private static PcpRelayCoordinatorOptions CreateCoordinatorOptions(
        TcpTransportOptions transportOptions,
        int maxReconnectAttempts)
    {
        return new PcpRelayCoordinatorOptions
        {
            DrainTimeout = TimeSpan.FromSeconds(2),
            UpstreamSession = new PcpChannelSessionOptions
            {
                HandshakeTimeout = TimeSpan.FromSeconds(2),
                MaxReconnectAttempts = maxReconnectAttempts,
                ReconnectDelay = TimeSpan.FromMilliseconds(10),
                Stream = new PcpChannelStreamOptions
                {
                    ReadIdleTimeout = TimeSpan.FromSeconds(2),
                },
            },
            DownstreamTransport = transportOptions,
            Fanout = new PcpRelayFanoutOptions
            {
                MaxChildren = 4,
                MaxPendingPacketsPerChild = 8,
                MaxPendingBytesPerChild = 1024,
                ChildStopTimeout = TimeSpan.FromSeconds(1),
            },
            DownstreamChild = new PcpDownstreamChildOptions
            {
                HandshakeTimeout = TimeSpan.FromSeconds(2),
                WriteTimeout = TimeSpan.FromSeconds(2),
            },
        };
    }

    private static ChannelStatusTracker CreateTracker()
    {
        return new ChannelStatusTracker(
            new ChannelId(ChannelGuid),
            LocalSessionId);
    }

    private static RecordingSinkStartRequest CreateRecordingRequest(
        ChannelId channelId)
    {
        return new RecordingSinkStartRequest(
            new RecordingCandidate(
                channelId,
                new ChannelMetadataSnapshot(
                    name: "Channel",
                    streamExtension: "flv")),
            Guid.ParseExact("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "N"));
    }

    private static TcpTransportOptions CreateTransportOptions()
    {
        return new TcpTransportOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            MaxConcurrentConnections = 4,
        };
    }

    private static async Task RunTwoUpstreamAttemptsAsync(
        TcpTransportListener listener,
        Task releasePackets,
        CancellationToken cancellationToken)
    {
        await RunAcceptedUpstreamAttemptAsync(
            listener,
            expectedPosition: 0,
            releasePackets,
            [CreatePacketAtom(position: 100, [0x41])],
            cancellationToken);
        await RunAcceptedUpstreamAttemptAsync(
            listener,
            expectedPosition: 100,
            Task.CompletedTask,
            [
                CreatePacketAtom(position: 200, [0x42]),
                CreateQuitAtom(),
            ],
            cancellationToken);
    }

    private static async Task RunAcceptedUpstreamAttemptAsync(
        TcpTransportListener listener,
        long expectedPosition,
        Task releasePackets,
        IReadOnlyList<PcpAtom> postHandshakeAtoms,
        CancellationToken cancellationToken,
        Task? releaseRemainingPackets = null)
    {
        await using var connection = await listener.AcceptAsync(
            cancellationToken);
        var pipeReader = PipeReader.Create(
            connection.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            await ReadAndAssertUpstreamRequestAsync(
                pipeReader,
                expectedPosition,
                cancellationToken);
            await WriteHttpResponseAsync(connection, cancellationToken);
            await ReadAndAssertHeloAsync(pipeReader, cancellationToken);
            await WriteAtomsAsync(
                connection,
                [
                    CreateOlehAtom(),
                    PcpAtom.Leaf(
                        PcpKnownAtomNames.Okay,
                        PcpWireValueCodec.EncodeUInt32(1)),
                ],
                cancellationToken);
            await releasePackets.WaitAsync(cancellationToken);
            if (releaseRemainingPackets is not null && postHandshakeAtoms.Count > 0)
            {
                await WriteAtomsAsync(
                    connection,
                    [postHandshakeAtoms[0]],
                    cancellationToken);
                await releaseRemainingPackets.WaitAsync(cancellationToken);
                await WriteAtomsAsync(
                    connection,
                    postHandshakeAtoms.Skip(1).ToArray(),
                    cancellationToken);
            }
            else
            {
                await WriteAtomsAsync(
                    connection,
                    postHandshakeAtoms,
                    cancellationToken);
            }
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static async Task RunSharedOwnerUpstreamAsync(
        TcpTransportListener listener,
        TaskCompletionSource accepted,
        Action onAccepted,
        Task releaseHead,
        TaskCompletionSource headWritten,
        Task releaseFirstData,
        TaskCompletionSource firstDataWritten,
        Task releaseSecondData,
        TaskCompletionSource secondDataWritten,
        Task releaseThirdData,
        TaskCompletionSource thirdDataWritten,
        Task finishUpstream,
        CancellationToken cancellationToken)
    {
        await using var connection = await listener.AcceptAsync(
            cancellationToken);
        onAccepted();
        var pipeReader = PipeReader.Create(
            connection.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            await ReadAndAssertUpstreamRequestAsync(
                pipeReader,
                expectedPosition: 0,
                cancellationToken);
            await WriteHttpResponseAsync(connection, cancellationToken);
            await ReadAndAssertHeloAsync(pipeReader, cancellationToken);
            await WriteAtomsAsync(
                connection,
                [
                    CreateOlehAtom(),
                    PcpAtom.Leaf(
                        PcpKnownAtomNames.Okay,
                        PcpWireValueCodec.EncodeUInt32(1)),
                ],
                cancellationToken);
            accepted.TrySetResult();

            await releaseHead.WaitAsync(cancellationToken);
            await WriteAtomsAsync(
                connection,
                [CreatePacketAtom(100, [0x48], 0x64616568)],
                cancellationToken);
            headWritten.TrySetResult();

            await releaseFirstData.WaitAsync(cancellationToken);
            await WriteAtomsAsync(
                connection,
                [CreatePacketAtom(101, [0x44], 0x61746164)],
                cancellationToken);
            firstDataWritten.TrySetResult();

            await releaseSecondData.WaitAsync(cancellationToken);
            await WriteAtomsAsync(
                connection,
                [CreatePacketAtom(102, [0x45], 0x61746164)],
                cancellationToken);
            secondDataWritten.TrySetResult();

            await releaseThirdData.WaitAsync(cancellationToken);
            await WriteAtomsAsync(
                connection,
                [CreatePacketAtom(103, [0x46], 0x61746164)],
                cancellationToken);
            thirdDataWritten.TrySetResult();

            await finishUpstream.WaitAsync(cancellationToken);
            try
            {
                await WriteAtomsAsync(
                    connection,
                    [CreateQuitAtom()],
                    cancellationToken);
            }
            catch (IOException)
            {
                // Last consumer shutdown may close the shared PCP connection first.
            }
            catch (ObjectDisposedException)
            {
                // Last consumer shutdown may close the shared PCP connection first.
            }
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static async Task RunHeldRecordingPacketsAsync(
        TcpTransportListener listener,
        byte[] firstPayload,
        byte[] secondPayload,
        TaskCompletionSource accepted,
        Task releaseFirstPacket,
        TaskCompletionSource firstPacketWritten,
        Task releaseSecondPacket,
        TaskCompletionSource secondPacketWritten,
        Task closeUpstream,
        CancellationToken cancellationToken)
    {
        await using var connection = await listener.AcceptAsync(
            cancellationToken);
        var pipeReader = PipeReader.Create(
            connection.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            await ReadAndAssertUpstreamRequestAsync(
                pipeReader,
                expectedPosition: 0,
                cancellationToken);
            await WriteHttpResponseAsync(connection, cancellationToken);
            await ReadAndAssertHeloAsync(pipeReader, cancellationToken);
            await WriteAtomsAsync(
                connection,
                [
                    CreateOlehAtom(),
                    PcpAtom.Leaf(
                        PcpKnownAtomNames.Okay,
                        PcpWireValueCodec.EncodeUInt32(1)),
                ],
                cancellationToken);
            accepted.TrySetResult();

            await releaseFirstPacket.WaitAsync(cancellationToken);
            await WriteAtomsAsync(
                connection,
                [
                    CreatePacketAtom(
                        position: 100,
                        firstPayload,
                        packetType: 0x64616568),
                ],
                cancellationToken);
            firstPacketWritten.TrySetResult();

            await releaseSecondPacket.WaitAsync(cancellationToken);
            await WriteAtomsAsync(
                connection,
                [
                    CreatePacketAtom(
                        position: checked((uint)(100 + firstPayload.Length)),
                        secondPayload,
                        packetType: 0x61746164),
                ],
                cancellationToken);
            secondPacketWritten.TrySetResult();
            await closeUpstream.WaitAsync(cancellationToken);
            await WriteAtomsAsync(
                connection,
                [CreateQuitAtom()],
                cancellationToken);
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static async Task WaitForConditionAsync(
        Func<bool> condition,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TestTimeout;

        while (!condition())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail("The expected PCP recording source registration did not appear.");
            }

            await Task.Delay(10, cancellationToken);
        }
    }

    private static async Task WaitForPartialSizeAsync(
        string directory,
        long expectedSize,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TestTimeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Directory.Exists(directory) &&
                Directory.GetFiles(directory).Any(path =>
                    path.EndsWith(
                        ".partial",
                        StringComparison.OrdinalIgnoreCase) &&
                    new FileInfo(path).Length >= expectedSize))
            {
                return;
            }

            await Task.Delay(10, cancellationToken);
        }

        Assert.Fail(
            $"No partial PCP recording reached {expectedSize} bytes in '{directory}'.");
    }

    private static async Task RunHeldUpstreamAsync(
        TcpTransportListener listener,
        TaskCompletionSource accepted,
        CancellationToken cancellationToken)
    {
        await using var connection = await listener.AcceptAsync(
            cancellationToken);
        var pipeReader = PipeReader.Create(
            connection.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            await ReadAndAssertUpstreamRequestAsync(
                pipeReader,
                expectedPosition: 0,
                cancellationToken);
            await WriteHttpResponseAsync(connection, cancellationToken);
            await ReadAndAssertHeloAsync(pipeReader, cancellationToken);
            await WriteAtomsAsync(
                connection,
                [
                    CreateOlehAtom(),
                    PcpAtom.Leaf(
                        PcpKnownAtomNames.Okay,
                        PcpWireValueCodec.EncodeUInt32(1)),
                ],
                cancellationToken);
            accepted.TrySetResult();

            try
            {
                var buffer = new byte[1];
                _ = await connection.ReadAsync(buffer, cancellationToken);
            }
            catch (IOException)
            {
                // Coordinator cancellation may close or reset the connection.
            }
            catch (SocketException)
            {
                // Coordinator cancellation may close or reset the connection.
            }
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static async Task RunPacketThenTruncatedUpstreamAsync(
        TcpTransportListener listener,
        Task releasePacket,
        CancellationToken cancellationToken)
    {
        await using var connection = await listener.AcceptAsync(
            cancellationToken);
        var pipeReader = PipeReader.Create(
            connection.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            await ReadAndAssertUpstreamRequestAsync(
                pipeReader,
                expectedPosition: 0,
                cancellationToken);
            await WriteHttpResponseAsync(connection, cancellationToken);
            await ReadAndAssertHeloAsync(pipeReader, cancellationToken);
            await WriteAtomsAsync(
                connection,
                [
                    CreateOlehAtom(),
                    PcpAtom.Leaf(
                        PcpKnownAtomNames.Okay,
                        PcpWireValueCodec.EncodeUInt32(1)),
                ],
                cancellationToken);
            await releasePacket.WaitAsync(cancellationToken);
            await WriteAtomsAsync(
                connection,
                [CreatePacketAtom(position: 100, [0x51, 0x52])],
                cancellationToken);

            // Leave an incomplete next atom on the wire. The complete packet
            // above must remain visible in coordinator diagnostics even though
            // the upstream session cannot return a terminal summary.
            await connection.WriteAsync(
                new byte[] { 0x63, 0x68, 0x61 },
                cancellationToken);
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static async Task<DownstreamPeer> ConnectDownstreamAsync(
        IPEndPoint endPoint,
        TcpTransportOptions transportOptions,
        CancellationToken cancellationToken)
    {
        var connection = await new TcpTransportConnector(
            transportOptions).ConnectAsync(
                endPoint.Address.ToString(),
                endPoint.Port,
                cancellationToken);
        var pipeReader = PipeReader.Create(
            connection.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            var handshake = await PcpRelayHandshakeClient.NegotiateAsync(
                connection,
                pipeReader,
                endPoint.Address.ToString(),
                endPoint.Port,
                ChannelGuid,
                Guid.NewGuid(),
                streamPosition: 0,
                pcpVersion: 1,
                new PcpRelayHandshakeSettings(
                    MaxHttpHeaderBytes: 32 * 1024,
                    MaxHandshakeAtomBytes: 64 * 1024,
                    MaxAtomsBeforeTerminal: 16,
                    UserAgent: "PeerCastNova/Test",
                    ServantVersion: PcpHeloCodec.LegacyServantVersion),
                cancellationToken);
            Assert.Equal(PcpCompatibilityOutcome.Accepted, handshake.Outcome);
            return new DownstreamPeer(connection, pipeReader);
        }
        catch
        {
            await pipeReader.CompleteAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task ReadAndAssertUpstreamRequestAsync(
        PipeReader pipeReader,
        long expectedPosition,
        CancellationToken cancellationToken)
    {
        var request = await PcpHttpHeaderBlockReader.ReadRequestAsync(
            pipeReader,
            32 * 1024,
            cancellationToken);

        Assert.Equal("GET", request.Method);
        Assert.Equal(
            $"/channel/{ChannelGuid:N}".ToUpperInvariant(),
            request.RequestTarget.ToUpperInvariant());
        Assert.Equal(
            expectedPosition.ToString(CultureInfo.InvariantCulture),
            request.Headers["x-peercast-pos"]);
    }

    private static async Task ReadAndAssertHeloAsync(
        PipeReader pipeReader,
        CancellationToken cancellationToken)
    {
        await using var atomReader = new PcpAtomPipeMessageReader(pipeReader);
        var helo = await atomReader.ReadNextAsync(cancellationToken);
        var sessionAtom = helo.FindFirst(PcpKnownAtomNames.HeloSessionId);

        Assert.Equal(PcpKnownAtomNames.Helo, helo.Name);
        Assert.NotNull(sessionAtom);
        Assert.Equal(
            LocalSessionId.Value,
            PcpWireValueCodec.DecodeGuid(sessionAtom!.Content.Span));
    }

    private static async Task WriteHttpResponseAsync(
        TcpTransportConnection connection,
        CancellationToken cancellationToken)
    {
        var response = Encoding.ASCII.GetBytes(
            "HTTP/1.0 200 OK\r\n" +
            "Server: PeerCastStation/Test\r\n" +
            "Content-Type:application/x-peercast-pcp\r\n\r\n");
        await connection.WriteAsync(response, cancellationToken);
    }

    private static async Task WriteAtomsAsync(
        TcpTransportConnection connection,
        IReadOnlyList<PcpAtom> atoms,
        CancellationToken cancellationToken)
    {
        var writer = new ArrayBufferWriter<byte>();

        foreach (var atom in atoms)
        {
            PcpAtomTreeCodec.Write(writer, atom);
        }

        await connection.WriteAsync(writer.WrittenMemory, cancellationToken);
    }

    private static PcpAtom CreateOlehAtom()
    {
        return PcpAtom.Parent(
            PcpKnownAtomNames.Oleh,
            PcpAtom.Leaf(
                PcpKnownAtomNames.HeloAgent,
                PcpWireValueCodec.EncodeNullTerminatedUtf8(
                    "PeerCastStation/Test")),
            PcpAtom.Leaf(
                PcpKnownAtomNames.HeloSessionId,
                PcpWireValueCodec.EncodeGuid(RemoteSessionId)),
            PcpAtom.Leaf(
                PcpKnownAtomNames.HeloVersion,
                PcpWireValueCodec.EncodeUInt32(
                    PcpHeloCodec.LegacyServantVersion)));
    }

    private static PcpAtom CreatePacketAtom(
        uint position,
        byte[] payload,
        uint packetType = 1)
    {
        return PcpAtom.Parent(
            PcpKnownAtomNames.Channel,
            PcpAtom.Leaf(
                PcpKnownAtomNames.ChannelId,
                PcpWireValueCodec.EncodeGuid(ChannelGuid)),
            PcpAtom.Parent(
                PcpKnownAtomNames.ChannelPacket,
                PcpAtom.Leaf(
                    PcpKnownAtomNames.Type,
                    PcpWireValueCodec.EncodeUInt32(packetType)),
                PcpAtom.Leaf(
                    PcpKnownAtomNames.Position,
                    PcpWireValueCodec.EncodeUInt32(position)),
                PcpAtom.Leaf(PcpKnownAtomNames.Data, payload)));
    }

    private static PcpAtom CreateQuitAtom()
    {
        return PcpAtom.Leaf(
            PcpKnownAtomNames.Quit,
            PcpWireValueCodec.EncodeUInt32(0));
    }

    private sealed record ChannelPacketSnapshot(
        ChannelId ChannelId,
        ChannelSessionId SessionId,
        long Sequence,
        uint Type,
        byte[] Payload);

    private sealed class CollectingChannelPacketConsumer :
        IPcpChannelPacketConsumer
    {
        private readonly System.Threading.Channels.Channel<ChannelPacketSnapshot>
            _packets = System.Threading.Channels.Channel.CreateUnbounded<
                ChannelPacketSnapshot>();

        public ValueTask ConsumeAsync(
            PcpChannelPacket packet,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _packets.Writer.TryWrite(new(
                packet.ChannelId,
                packet.SessionId,
                packet.Sequence,
                packet.Type,
                packet.Payload.ToArray()));
            return ValueTask.CompletedTask;
        }

        public ValueTask<ChannelPacketSnapshot> ReceiveAsync(
            CancellationToken cancellationToken) =>
            _packets.Reader.ReadAsync(cancellationToken);
    }

    private sealed class FailingChannelPacketConsumer :
        IPcpChannelPacketConsumer
    {
        public ValueTask ConsumeAsync(
            PcpChannelPacket packet,
            CancellationToken cancellationToken) =>
            ValueTask.FromException(
                new IOException("Synthetic playback consumer failure."));
    }

    private sealed class DownstreamPeer : IAsyncDisposable
    {
        private readonly TcpTransportConnection _connection;
        private readonly PipeReader _pipeReader;

        public DownstreamPeer(
            TcpTransportConnection connection,
            PipeReader pipeReader)
        {
            _connection = connection;
            _pipeReader = pipeReader;
        }

        public async Task<byte[][]> ReadPayloadsAsync(
            int count,
            CancellationToken cancellationToken)
        {
            var payloads = new List<byte[]>(count);
            await using var atomReader = new PcpAtomPipeMessageReader(
                _pipeReader,
                maxBufferedBytes: 1024 * 1024);

            while (payloads.Count < count)
            {
                var channel = await atomReader.ReadNextAsync(
                    cancellationToken);
                var packet = channel.FindFirst(
                    PcpKnownAtomNames.ChannelPacket);
                var data = packet?.FindFirst(PcpKnownAtomNames.Data);

                Assert.Equal(PcpKnownAtomNames.Channel, channel.Name);
                Assert.NotNull(data);
                payloads.Add(data!.Content.ToArray());
            }

            return payloads.ToArray();
        }

        public async Task<(uint Type, byte[] Payload)[]> ReadChannelPacketsAsync(
            int count,
            CancellationToken cancellationToken)
        {
            var packets = new List<(uint Type, byte[] Payload)>(count);
            await using var atomReader = new PcpAtomPipeMessageReader(
                _pipeReader,
                maxBufferedBytes: 1024 * 1024);

            while (packets.Count < count)
            {
                var channel = await atomReader.ReadNextAsync(cancellationToken);
                var packet = channel.FindFirst(PcpKnownAtomNames.ChannelPacket);
                var type = packet?.FindFirst(PcpKnownAtomNames.Type);
                var data = packet?.FindFirst(PcpKnownAtomNames.Data);
                Assert.Equal(PcpKnownAtomNames.Channel, channel.Name);
                Assert.NotNull(type);
                Assert.NotNull(data);
                packets.Add((
                    PcpWireValueCodec.DecodeUInt32(type!.Content.Span),
                    data!.Content.ToArray()));
            }

            return packets.ToArray();
        }

        public async ValueTask DisposeAsync()
        {
            await _pipeReader.CompleteAsync();
            await _connection.DisposeAsync();
        }
    }
}
