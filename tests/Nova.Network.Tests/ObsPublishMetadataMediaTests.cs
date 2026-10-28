using PeerCast.Nova.Network.Publishing;

namespace PeerCast.Nova.Network.Tests;

public sealed class ObsPublishMetadataMediaTests
{
    [Fact]
    public async Task ChangingChannelMetadataLeavesActualHttpFlvBodyAndObsEosAccountingIdentical()
    {
        var baseline = await PublishAsync(new() { Ingest = new() { Port = 0 } });
        var configured = await PublishAsync(new()
        {
            ChannelName = "日本語 🎥",
            Genre = "音楽",
            Description = "description",
            Comment = "comment",
            ContactUrl = "https://example.invalid/",
            BitrateKbps = 1234,
            Ingest = new() { Port = 0 },
        });
        Assert.NotEmpty(baseline);
        Assert.Equal(baseline, configured);
    }

    private static async Task<byte[]> PublishAsync(ObsPublishChannelOptions options)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var host = new ObsPublishChannelHost(options);
        host.Start();
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        Assert.True(await host.HeadAvailable.WaitAsync(timeout.Token));
        using var http = new HttpClient();
        using var response = await http.GetAsync(host.HttpStreamUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        var body = response.Content.ReadAsByteArrayAsync(timeout.Token);
        await publisher.SendAsync(9, 1, 37, ObsFlvPublishingSessionTests.Idr);
        await publisher.SendAsync(8, 1, 38, ObsFlvPublishingSessionTests.Audio);
        await ObsPublishChannelHostTests.StopAsync(publisher);
        var result = await host.Completion.WaitAsync(timeout.Token);
        Assert.Equal(ObsPublishChannelEndReason.PublisherStopped, result.EndReason);
        Assert.True(result.ShutdownCompleted);
        Assert.True(result.HttpDrained);
        Assert.Equal(3, result.AcceptedPacketCount);
        Assert.Equal(1, result.HeadCount);
        var bytes = await body;
        Assert.Equal(result.AcceptedByteCount, bytes.Length);
        Assert.Equal(result.AcceptedByteCount, host.Snapshot.StreamPosition);
        return bytes;
    }
}
