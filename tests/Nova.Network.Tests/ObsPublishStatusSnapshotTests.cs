using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.Network.Tests;

public sealed class ObsPublishStatusSnapshotTests
{
    [Fact]
    public async Task UnstartedRepeatedReadsAndDisposalDoNotBindOccupiedPorts()
    {
        var occupied = new TcpListener(System.Net.IPAddress.Loopback, 0);
        occupied.Start();
        try
        {
            var endpoint = (System.Net.IPEndPoint)occupied.LocalEndpoint;
            await using var host = new ObsPublishChannelHost(new()
            {
                Ingest = new() { Port = endpoint.Port },
                RelayPort = endpoint.Port,
                Http = new() { Port = endpoint.Port },
            });
            var first = host.PublishingStatus;
            Assert.Equal(ChannelSessionState.Created, first.State);
            Assert.False(first.ListenerReady);
            Assert.False(first.PublisherConnected);
            Assert.False(first.HeadAvailable);
            Assert.Equal(0, host.RecordingRegistry.Count);
            Assert.Throws<InvalidOperationException>(() => { _ = host.Completion; });
            Assert.Throws<InvalidOperationException>(() => host.IngestEndPoint);
            var reads = await Task.WhenAll(Enumerable.Range(0, 64)
                .Select(_ => Task.Run(() => host.PublishingStatus)));
            Assert.All(reads, status => Assert.Equal(first, status));
            await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            var stopped = host.PublishingStatus;
            Assert.Equal(ChannelSessionState.Stopped, stopped.State);
            Assert.Equal(ObsPublishChannelEndReason.HostCancelled, stopped.EndReason);
            Assert.True(stopped.Revision > first.Revision);
            Assert.Equal(stopped, host.PublishingStatus);
            Assert.Equal(endpoint, occupied.LocalEndpoint);
        }
        finally { occupied.Stop(); }
    }

    [Fact]
    public async Task ListenerAndTcpObservationsAdvanceRevisionEvenWithoutTrackerTransition()
    {
        await using var host = ObsPublishChannelHostTests.Create();
        var created = host.PublishingStatus;
        host.Start();
        var listening = host.PublishingStatus;
        Assert.True(listening.ListenerReady);
        Assert.True(listening.Revision > created.Revision);
        Assert.Equal(listening, host.PublishingStatus);
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host.IngestEndPoint);
        await WaitAsync(() => host.PublishingStatus.PublisherConnected);
        var connected = host.PublishingStatus;
        Assert.Equal(listening.State, connected.State);
        Assert.True(connected.Revision > listening.Revision);
        Assert.False(connected.HeadAvailable);
        Assert.Equal(connected, host.PublishingStatus);
    }

    [Fact]
    public async Task Listener_and_TCP_are_distinct_from_accepted_media()
    {
        await using var host = ObsPublishChannelHostTests.Create();
        Assert.Equal(ChannelSessionState.Created, host.PublishingStatus.State);
        Assert.False(host.PublishingStatus.ListenerReady);
        host.Start();
        var waiting = host.PublishingStatus;
        Assert.True(waiting.ListenerReady);
        Assert.False(waiting.PublisherConnected);
        Assert.False(waiting.HeadAvailable);
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host.IngestEndPoint);
        await WaitAsync(() => host.PublishingStatus.PublisherConnected);
        Assert.False(host.PublishingStatus.HeadAvailable);
        Assert.NotEqual(ChannelSessionState.Receiving, host.PublishingStatus.State);
    }

    [Fact]
    public async Task Accepted_HEAD_exposes_identity_and_URL_until_terminal_cleanup()
    {
        await using var host = ObsPublishChannelHostTests.Create();
        host.Start();
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        Assert.True(await host.HeadAvailable.WaitAsync(TimeSpan.FromSeconds(10)));
        var active = host.PublishingStatus;
        Assert.Equal(ChannelSessionState.Receiving, active.State);
        Assert.True(active.HeadAvailable);
        Assert.Equal(host.ChannelId, active.ChannelId);
        Assert.Equal(host.SessionId, active.SessionId);
        Assert.Equal(host.HttpStreamUrl, active.HttpStreamUrl);
        await ObsPublishChannelHostTests.StopAsync(publisher);
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        var stopped = host.PublishingStatus;
        Assert.Equal(result.EndReason, stopped.EndReason);
        Assert.Equal(ChannelSessionState.Stopped, stopped.State);
        Assert.False(stopped.ListenerReady);
        Assert.False(stopped.PublisherConnected);
        Assert.False(stopped.HeadAvailable);
        Assert.Null(stopped.HttpStreamUrl);
        Assert.True(stopped.Revision > active.Revision);
        Assert.True(active.HeadAvailable); // Earlier immutable snapshot was not mutated.
    }

    [Fact]
    public async Task Rejection_retains_only_stage_and_reason_in_status()
    {
        await using var host = ObsPublishChannelHostTests.Create();
        host.Start();
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await publisher.SendAsync(8, 1, 0, new byte[] { 0xaf, 0, 0xff, 0xff });
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        var snapshot = host.PublishingStatus;
        Assert.Equal(ObsPublishChannelEndReason.ProtocolRejected, snapshot.EndReason);
        Assert.Equal(result.Rejection!.Stage, snapshot.RejectionStage);
        Assert.Equal(result.Rejection.Reason, snapshot.RejectionReason);
        Assert.Equal(ObsRtmpRejectionReason.UnsupportedAudioSpecificConfig, snapshot.RejectionReason);
        Assert.DoesNotContain(typeof(ObsPublishStatusSnapshot).GetProperties(),
            property => property.PropertyType == typeof(ObsRtmpRejectionContext));
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }
}
