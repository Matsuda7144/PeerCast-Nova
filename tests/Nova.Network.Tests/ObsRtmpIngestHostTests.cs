using System.Net;
using System.Net.Sockets;
using System.Text;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.Network.Tests;

public sealed class ObsRtmpIngestHostTests
{
    [Fact]
    public async Task PublishingContractUnconnectedCallerCancellationClosesListener()
    {
        using var caller = new CancellationTokenSource();
        await using var host = Create(new());
        Assert.Throws<InvalidOperationException>(() => host.LocalEndPoint);
        host.Start(caller.Token);
        var endpoint = host.LocalEndPoint;
        Assert.False(host.Completion.IsCompleted);
        await caller.CancelAsync();
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ObsRtmpIngestEndReason.Cancelled, result.EndReason);
        Assert.Equal(0, result.AcceptedPacketCount);
        var probe = new TcpListener(endpoint);
        try { probe.Start(); }
        finally { probe.Stop(); }
    }

    [Fact]
    public async Task PublishingContractIngestStartRacingDisposeIsSerialized()
    {
        for (var iteration = 0; iteration < 16; iteration++)
        {
            await using var host = Create(new());
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var start = Task.Run(async () =>
            {
                await release.Task;
                try { host.Start(); }
                catch (ObjectDisposedException) { }
            });
            var dispose = Task.Run(async () =>
            {
                await release.Task;
                await host.DisposeAsync();
            });
            release.TrySetResult();
            await Task.WhenAll(start, dispose).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Throws<ObjectDisposedException>(() => host.Start());
        }
    }

    [Fact]
    public async Task OBS_publish_transcript_delivers_ordered_HEAD_DATA_to_existing_HTTP_FLV_consumer()
    {
        await using var http = new PcpHttpFlvPlaybackHost();
        http.Start();
        await using var source = http.Create(ObsFlvPublishingSessionTests.Channel, ObsFlvPublishingSessionTests.Session);
        var consumer = new ObsRtmpTestConsumer { BeforeAccept = source.ConsumeAsync };
        await using var host = Create(consumer);
        host.Start();
        Assert.Equal(IPAddress.Loopback, host.LocalEndPoint.Address);
        using var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.LocalEndPoint);
        await client.PublishAsync();
        await SendConfigurationAsync(client);
        await consumer.WaitForCountAsync(1);

        using var viewer = new TcpClient(AddressFamily.InterNetwork);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await viewer.ConnectAsync(http.LocalEndPoint, deadline.Token);
        var stream = viewer.GetStream();
        var request = Encoding.ASCII.GetBytes(
            $"GET /stream/{ObsFlvPublishingSessionTests.Channel} HTTP/1.1\r\nHost: localhost\r\n\r\n");
        await stream.WriteAsync(request, deadline.Token);
        var response = new List<byte>();
        while (response.Count < 4 || !response.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 }))
        {
            var buffer = new byte[1];
            await stream.ReadExactlyAsync(buffer, deadline.Token);
            response.Add(buffer[0]);
        }

        Assert.StartsWith("HTTP/1.1 200 OK", Encoding.ASCII.GetString(response.ToArray()));
        var head = Assert.Single(consumer.Packets).Payload;
        var retained = new PcpFlvBootstrapRetention();
        retained.Observe(head);
        var bootstrap = retained.Snapshot();
        // HTTP-FLV emits the same AVC-before-AAC bootstrap contract as ingest.
        var expectedHttpHead = bootstrap.Header.ToArray().Concat(bootstrap.Metadata.ToArray())
            .Concat(bootstrap.VideoConfiguration.ToArray()).Concat(bootstrap.AudioConfiguration.ToArray()).ToArray();
        var receivedHead = new byte[expectedHttpHead.Length];
        await stream.ReadExactlyAsync(receivedHead, deadline.Token);
        Assert.Equal(expectedHttpHead, receivedHead);
        await client.SendAsync(9, 1, 0xFE123456, ObsFlvPublishingSessionTests.Idr);
        await client.SendAsync(8, 1, 0xFE123457, ObsFlvPublishingSessionTests.Audio);
        await consumer.WaitForCountAsync(3);
        var expected = consumer.Packets.Skip(1).SelectMany(packet => packet.Payload).ToArray();
        var received = new byte[expected.Length];
        await stream.ReadExactlyAsync(received, deadline.Token);
        Assert.Equal(expected, received);
        await client.SendCommandAsync(0, "deleteStream", 5.0, null, 1.0);
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ObsRtmpIngestEndReason.PublisherStopped, result.EndReason);
        Assert.Equal(3, result.AcceptedPacketCount);
        Assert.Equal(head.Length + expected.Length, result.AcceptedByteCount);
        Assert.Throws<InvalidOperationException>(() => host.Start());
    }

    [Fact]
    public async Task Additional_publisher_is_closed_without_displacing_the_first()
    {
        var consumer = new ObsRtmpTestConsumer();
        await using var host = Create(consumer);
        host.Start();
        using var first = new ObsRtmpTestClient();
        await first.ConnectAsync(host.LocalEndPoint);
        await first.PublishAsync();
        using var second = new TcpClient(AddressFamily.InterNetwork);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await second.ConnectAsync(host.LocalEndPoint, deadline.Token);
        Assert.Equal(0, await second.GetStream().ReadAsync(new byte[1], deadline.Token));
        await SendConfigurationAsync(first);
        await first.SendAsync(9, 1, 0, ObsFlvPublishingSessionTests.Idr);
        await consumer.WaitForCountAsync(2);
        await first.SendCommandAsync(0, "FCUnpublish", 5.0, null, "nova");
        Assert.Equal(ObsRtmpIngestEndReason.PublisherStopped,
            (await host.Completion.WaitAsync(TimeSpan.FromSeconds(10))).EndReason);
    }

    [Theory]
    [InlineData("play")]
    [InlineData("createStream")]
    [InlineData("publish")]
    public async Task Unsupported_play_or_second_stream_publish_is_rejected(string command)
    {
        var consumer = new ObsRtmpTestConsumer();
        await using var host = Create(consumer);
        host.Start();
        using var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.LocalEndPoint);
        await client.PublishAsync();
        if (command == "publish")
        {
            await client.SendCommandAsync(1, command, 0.0, null, "nova", "live");
        }
        else
        {
            await client.SendCommandAsync(0, command, 5.0, null);
        }

        Assert.Equal(ObsRtmpIngestEndReason.ProtocolRejected,
            (await host.Completion.WaitAsync(TimeSpan.FromSeconds(10))).EndReason);
        Assert.Empty(consumer.Packets);
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("live?password=secret")]
    public async Task Wrong_application_is_rejected_without_media_delivery(string application)
    {
        var consumer = new ObsRtmpTestConsumer();
        await using var host = Create(consumer);
        host.Start();
        using var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.LocalEndPoint);
        await client.SendCommandAsync(0, "connect", 1.0,
            new Dictionary<string, object?> { ["app"] = application });
        Assert.Equal(ObsRtmpIngestEndReason.ProtocolRejected,
            (await host.Completion.WaitAsync(TimeSpan.FromSeconds(10))).EndReason);
        Assert.Empty(consumer.Packets);
    }

    [Fact]
    public async Task Wrong_stream_name_and_media_on_wrong_stream_are_rejected()
    {
        await using var host = Create(new());
        host.Start();
        using var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.LocalEndPoint);
        await client.PublishAsync();
        await client.SendAsync(9, 2, 0, ObsFlvPublishingSessionTests.VideoConfiguration);
        Assert.Equal(ObsRtmpIngestEndReason.ProtocolRejected,
            (await host.Completion.WaitAsync(TimeSpan.FromSeconds(10))).EndReason);

        await using var secondHost = Create(new());
        secondHost.Start();
        using var second = new ObsRtmpTestClient();
        await second.ConnectAsync(secondHost.LocalEndPoint);
        await second.PublishAsync();
        await second.SendCommandAsync(0, "FCUnpublish", 5.0, null, "wrong");
        Assert.Equal(ObsRtmpIngestEndReason.ProtocolRejected,
            (await secondHost.Completion.WaitAsync(TimeSpan.FromSeconds(10))).EndReason);
    }

    [Fact]
    public async Task EOF_is_disconnected_and_does_not_admit_a_reconnect()
    {
        await using var host = Create(new());
        host.Start();
        var endpoint = host.LocalEndPoint;
        using (var client = new ObsRtmpTestClient())
        {
            await client.ConnectAsync(endpoint);
            await client.PublishAsync();
            await SendConfigurationAsync(client);
        }

        Assert.Equal(ObsRtmpIngestEndReason.Disconnected,
            (await host.Completion.WaitAsync(TimeSpan.FromSeconds(10))).EndReason);
        using var reconnect = new TcpClient(AddressFamily.InterNetwork);
        await Assert.ThrowsAsync<SocketException>(() => reconnect.ConnectAsync(endpoint.Address, endpoint.Port));
    }

    [Fact]
    public async Task Consumer_backpressure_blocks_further_ingest_and_disposal_cancels_the_borrowed_callback()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new ObsRtmpTestConsumer
        {
            BeforeAccept = async (_, token) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
        };
        var host = Create(consumer);
        host.Start();
        using var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.LocalEndPoint);
        await client.PublishAsync();
        await SendConfigurationAsync(client);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await client.SendAsync(9, 1, 0, ObsFlvPublishingSessionTests.Idr);
        await client.SendCommandAsync(0, "deleteStream", 5.0, null, 1.0);
        Assert.False(host.Completion.IsCompleted);
        await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        await host.DisposeAsync();
        Assert.Equal(ObsRtmpIngestEndReason.Cancelled, (await host.Completion).EndReason);
        Assert.Empty(consumer.Packets);
        Assert.Throws<ObjectDisposedException>(() => host.Start());
    }

    [Fact]
    public async Task Consumer_failure_stops_generation_without_retry_or_accounting_acceptance()
    {
        var consumer = new ObsRtmpTestConsumer
        {
            BeforeAccept = (_, _) => throw new IOException("Test-only consumer rejection"),
        };
        await using var host = Create(consumer);
        host.Start();
        using var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.LocalEndPoint);
        await client.PublishAsync();
        await SendConfigurationAsync(client);
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ObsRtmpIngestEndReason.ConsumerRejected, result.EndReason);
        Assert.Equal(0, result.AcceptedPacketCount);
        Assert.Empty(consumer.Packets);
    }

    [Fact]
    public async Task Silent_client_hits_handshake_deadline()
    {
        await using var host = Create(new(), new ObsRtmpIngestOptions { Port = 0, IoTimeout = TimeSpan.FromMilliseconds(100) });
        host.Start();
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(host.LocalEndPoint);
        Assert.Equal(ObsRtmpIngestEndReason.TimedOut,
            (await host.Completion.WaitAsync(TimeSpan.FromSeconds(10))).EndReason);
    }

    [Fact]
    public async Task Disposal_before_a_publisher_arrives_cancels_accept_and_precancelled_start_opens_no_listener()
    {
        var host = Create(new());
        host.Start();
        await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ObsRtmpIngestEndReason.Cancelled, (await host.Completion).EndReason);
        await using var unused = Create(new());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => unused.Start(cancellation.Token));
        Assert.Throws<InvalidOperationException>(() => unused.LocalEndPoint);
    }

    [Fact]
    public async Task Ping_is_answered_and_command_chunk_size_changes_are_applied()
    {
        await using var host = Create(new());
        host.Start();
        using var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.LocalEndPoint);
        await client.PublishAsync();
        await client.SendAsync(4, 0, 0, [0, 6, 1, 2, 3, 4], id: 2);
        var pong = await client.ReadAsync();
        Assert.Equal(4, pong.Type);
        Assert.Equal(new byte[] { 0, 7, 1, 2, 3, 4 }, pong.Payload);
        await client.SetChunkBytesAsync(4096);
        await client.SendAsync(18, 1, 0, ObsRtmpTestClient.Command("@setDataFrame", "onMetaData",
            new Dictionary<string, object?> { ["description"] = new string('a', 300) }));
        await client.SendCommandAsync(0, "deleteStream", 5.0, null, 1.0);
        Assert.Equal(ObsRtmpIngestEndReason.PublisherStopped,
            (await host.Completion.WaitAsync(TimeSpan.FromSeconds(10))).EndReason);
    }

    [Theory]
    [InlineData(17, 1, "00")]
    [InlineData(22, 1, "00")]
    [InlineData(5, 0, "00000000")]
    [InlineData(1, 0, "80000080")]
    [InlineData(1, 1, "00000080")]
    [InlineData(4, 0, "000601")]
    public async Task Unsupported_message_modes_and_invalid_control_frames_are_rejected(int type, int streamId, string hex)
    {
        await using var host = Create(new());
        host.Start();
        using var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.LocalEndPoint);
        await client.PublishAsync();
        await client.SendAsync((byte)type, (uint)streamId, 0, Convert.FromHexString(hex));
        Assert.Equal(ObsRtmpIngestEndReason.ProtocolRejected,
            (await host.Completion.WaitAsync(TimeSpan.FromSeconds(10))).EndReason);
    }

    [Fact]
    public async Task Media_before_publish_is_rejected()
    {
        await using var host = Create(new());
        host.Start();
        using var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.LocalEndPoint);
        await client.SendAsync(9, 1, 0, ObsFlvPublishingSessionTests.VideoConfiguration);
        Assert.Equal(ObsRtmpIngestEndReason.ProtocolRejected,
            (await host.Completion.WaitAsync(TimeSpan.FromSeconds(10))).EndReason);
    }

    [Fact]
    public async Task Awaited_consumer_release_resumes_publication_in_order()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new ObsRtmpTestConsumer
        {
            BeforeAccept = async (packet, token) =>
            {
                if (packet.Sequence == 0)
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(token);
                }
            },
        };
        await using var host = Create(consumer);
        host.Start();
        using var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.LocalEndPoint);
        await client.PublishAsync();
        await SendConfigurationAsync(client);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await client.SendAsync(9, 1, 0, ObsFlvPublishingSessionTests.Idr);
        await client.SendAsync(8, 1, 0, ObsFlvPublishingSessionTests.Audio);
        await client.SendCommandAsync(1, "closeStream", 0.0, null);
        Assert.Empty(consumer.Packets);
        Assert.False(host.Completion.IsCompleted);
        release.TrySetResult();
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ObsRtmpIngestEndReason.PublisherStopped, result.EndReason);
        Assert.Equal(3, result.AcceptedPacketCount);
        Assert.Equal(new long[] { 0, 1, 2 }, consumer.Packets.Select(packet => packet.Sequence));
    }

    [Fact]
    public async Task Caller_cancellation_stops_an_active_publisher()
    {
        using var cancellation = new CancellationTokenSource();
        await using var host = Create(new());
        host.Start(cancellation.Token);
        using var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.LocalEndPoint);
        await client.PublishAsync();
        await cancellation.CancelAsync();
        Assert.Equal(ObsRtmpIngestEndReason.Cancelled,
            (await host.Completion.WaitAsync(TimeSpan.FromSeconds(10))).EndReason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Non_RTMP3_handshake_is_rejected(int version)
    {
        await using var host = Create(new());
        host.Start();
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(host.LocalEndPoint);
        var hello = new byte[1537];
        hello[0] = (byte)version;
        await client.GetStream().WriteAsync(hello);
        Assert.Equal(ObsRtmpIngestEndReason.ProtocolRejected,
            (await host.Completion.WaitAsync(TimeSpan.FromSeconds(10))).EndReason);
    }

    [Fact]
    public async Task Incorrect_handshake_echo_is_rejected()
    {
        await using var host = Create(new());
        host.Start();
        using var client = new TcpClient(AddressFamily.InterNetwork);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(host.LocalEndPoint, deadline.Token);
        var hello = new byte[1537];
        hello[0] = 3;
        var stream = client.GetStream();
        await stream.WriteAsync(hello, deadline.Token);
        await stream.ReadExactlyAsync(new byte[3073], deadline.Token);
        await stream.WriteAsync(new byte[1536], deadline.Token);
        Assert.Equal(ObsRtmpIngestEndReason.ProtocolRejected,
            (await host.Completion.WaitAsync(TimeSpan.FromSeconds(10))).EndReason);
    }

    private static ObsRtmpIngestHost Create(ObsRtmpTestConsumer consumer, ObsRtmpIngestOptions? options = null) =>
        new(ObsFlvPublishingSessionTests.Channel, ObsFlvPublishingSessionTests.Session, consumer,
            options ?? new ObsRtmpIngestOptions { Port = 0 });

    private static async Task SendConfigurationAsync(ObsRtmpTestClient client)
    {
        await client.SendAsync(9, 1, 0, ObsFlvPublishingSessionTests.VideoConfiguration);
        await client.SendAsync(8, 1, 0, ObsFlvPublishingSessionTests.AudioConfiguration);
    }
}
