using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Media;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4HttpStreamHostTests
{
    private static readonly ChannelId Channel = new(Guid.Parse("023F465DE905412C8F25DC52FDA0ED0E"));
    private static readonly ChannelSessionId Session = new(Guid.Parse("170F1F4DFA6448E9844528A28220B144"));
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Muxed_initialization_and_fragments_arrive_before_producer_completion()
    {
        using var deadline = new CancellationTokenSource(TestDeadline);
        var source = CreateMuxer();
        using var muxer = source.Muxer;
        var initialization = CreateInitialization(muxer);
        await using var host = new PlaybackFmp4HttpStreamHost(Channel, Session);
        using var http = CreateClient();

        Assert.Equal("127.0.0.1", host.StreamUri.Host);
        Assert.True(host.StreamUri.Port > 0);
        Assert.Equal($"/fmp4/{Channel}", host.StreamUri.AbsolutePath);
        var responseTask = http.GetAsync(host.StreamUri, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        Assert.True(await host.WaitForClientAsync(deadline.Token));

        var borrowedInitialization = initialization.ToArray();
        Assert.True(host.TryPublishSegment(Session, borrowedInitialization));
        borrowedInitialization.AsSpan().Clear();
        var first = CreateMedia(muxer, source.VideoTrackId, source.AudioTrackId, 0);
        var borrowedMedia = first.ToArray();
        Assert.True(host.TryPublishSegment(Session, borrowedMedia));
        borrowedMedia.AsSpan().Clear();

        using var response = await responseTask;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.TransferEncodingChunked);
        Assert.Null(response.Content.Headers.ContentLength);
        using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
        await AssertBytesAsync(body, initialization, deadline.Token);
        // The first moof/mdat pair spans multiple HTTP chunks.
        await AssertBytesAsync(body, first, deadline.Token);

        var second = CreateMedia(muxer, source.VideoTrackId, source.AudioTrackId, 1);
        Assert.True(host.TryPublishSegment(Session, second));
        await AssertBytesAsync(body, second, deadline.Token);
        Assert.True(await host.CompleteAsync().WaitAsync(deadline.Token));
        Assert.Equal(0, await body.ReadAsync(new byte[1], deadline.Token));
    }

    [Fact]
    public async Task Late_join_starts_at_next_independent_fragment_and_receives_its_followers()
    {
        using var deadline = new CancellationTokenSource(TestDeadline);
        var fixture = CreateFixture();
        await using var host = new PlaybackFmp4HttpStreamHost(Channel, Session);
        using var http = CreateClient();
        Assert.True(host.TryPublishSegment(Session, fixture.Initialization));
        Assert.True(host.TryPublishSegment(Session, fixture.Media[0]));

        var responseTask = http.GetAsync(host.StreamUri, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        Assert.True(await host.WaitForClientAsync(deadline.Token));
        Assert.True(host.TryPublishSegment(Session, fixture.Media[1]));
        Assert.True(host.TryPublishSegment(Session, fixture.Media[2]));
        Assert.True(host.TryPublishSegment(Session, fixture.Media[3]));

        using var response = await responseTask;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
        await AssertBytesAsync(body, fixture.Initialization, deadline.Token);
        await AssertBytesAsync(body, fixture.Media[2], deadline.Token);
        await AssertBytesAsync(body, fixture.Media[3], deadline.Token);
        Assert.True(await host.CompleteAsync().WaitAsync(deadline.Token));
        Assert.Equal(0, await body.ReadAsync(new byte[1], deadline.Token));
    }

    [Fact]
    public async Task Invalid_generations_and_fragments_do_not_change_the_published_stream()
    {
        using var deadline = new CancellationTokenSource(TestDeadline);
        var fixture = CreateFixture();
        await using var host = new PlaybackFmp4HttpStreamHost(Channel, Session);
        using var http = CreateClient();
        var wrongSession = new ChannelSessionId(Guid.NewGuid());
        Assert.False(host.TryPublishSegment(wrongSession, fixture.Initialization));
        Assert.False(host.TryPublishSegment(Session, fixture.Media[0]));
        Assert.False(host.TryPublishSegment(Session, new byte[PlaybackFmp4HttpStreamHost.MaximumInitializationBytes + 1]));
        Assert.True(host.TryPublishSegment(Session, fixture.Initialization));
        Assert.False(host.TryPublishSegment(Session, fixture.Initialization));

        var responseTask = http.GetAsync(host.StreamUri, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        Assert.True(await host.WaitForClientAsync(deadline.Token));
        Assert.False(host.TryPublishSegment(Session, fixture.Media[0].AsSpan(0, fixture.Media[0].Length - 1)));
        Assert.False(host.TryPublishSegment(Session, new byte[PlaybackFmp4HttpStreamHost.MaximumMediaSegmentBytes + 1]));
        Assert.True(host.TryPublishSegment(Session, fixture.Media[0]));
        Assert.False(host.TryPublishSegment(Session, fixture.Media[0]));
        Assert.False(host.TryPublishSegment(Session, fixture.Media[2]));
        Assert.False(host.TryPublishSegment(wrongSession, fixture.Media[1]));
        Assert.True(host.TryPublishSegment(Session, fixture.Media[1]));

        using var response = await responseTask;
        using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
        await AssertBytesAsync(body, fixture.Initialization, deadline.Token);
        await AssertBytesAsync(body, fixture.Media[0], deadline.Token);
        await AssertBytesAsync(body, fixture.Media[1], deadline.Token);
        Assert.True(await host.CompleteAsync().WaitAsync(deadline.Token));
        Assert.Equal(0, await body.ReadAsync(new byte[1], deadline.Token));
        Assert.False(host.TryPublishSegment(Session, fixture.Media[2]));
    }

    [Fact]
    public async Task Source_end_before_a_join_point_returns_an_empty_failure()
    {
        using var deadline = new CancellationTokenSource(TestDeadline);
        var fixture = CreateFixture();
        await using var host = new PlaybackFmp4HttpStreamHost(Channel, Session);
        using var http = CreateClient();
        Assert.True(host.TryPublishSegment(Session, fixture.Initialization));
        Assert.True(host.TryPublishSegment(Session, fixture.Media[0]));

        var responseTask = http.GetAsync(host.StreamUri, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        Assert.True(await host.WaitForClientAsync(deadline.Token));
        Assert.True(host.TryPublishSegment(Session, fixture.Media[1]));
        Assert.False(await host.CompleteAsync().WaitAsync(deadline.Token));

        using var response = await responseTask;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(deadline.Token));
    }

    [Fact]
    public async Task Stop_during_active_response_closes_without_a_normal_chunked_end()
    {
        using var deadline = new CancellationTokenSource(TestDeadline);
        var fixture = CreateFixture();
        await using var host = new PlaybackFmp4HttpStreamHost(Channel, Session);
        using var http = CreateClient();
        var responseTask = http.GetAsync(host.StreamUri, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        Assert.True(await host.WaitForClientAsync(deadline.Token));
        Assert.True(host.TryPublishSegment(Session, fixture.Initialization));
        Assert.True(host.TryPublishSegment(Session, fixture.Media[0]));
        using var response = await responseTask;
        using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
        await AssertBytesAsync(body, fixture.Initialization, deadline.Token);
        await AssertBytesAsync(body, fixture.Media[0], deadline.Token);

        await host.DisposeAsync().AsTask().WaitAsync(deadline.Token);

        await Assert.ThrowsAnyAsync<IOException>(() => body.CopyToAsync(Stream.Null, deadline.Token));
        Assert.False(host.TryPublishSegment(Session, fixture.Media[1]));
    }

    [Fact]
    public async Task Idle_disconnected_client_releases_connection_for_next_request()
    {
        using var deadline = new CancellationTokenSource(TestDeadline);
        await using var host = new PlaybackFmp4HttpStreamHost(Channel, Session);
        using var http = CreateClient();
        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, host.StreamUri.Port, deadline.Token);
            using var stream = tcp.GetStream();
            using var request = new StreamWriter(stream, leaveOpen: true);
            await request.WriteAsync($"GET {host.StreamUri.AbsolutePath} HTTP/1.1\r\nHost: {host.StreamUri.Authority}\r\n\r\n");
            await request.FlushAsync(deadline.Token);
            Assert.True(await host.WaitForClientAsync(deadline.Token));
        }

        using var response = await http.GetAsync(new Uri(host.StreamUri, "/missing"), deadline.Token);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Requests_must_use_get_and_the_loopback_authority()
    {
        using var deadline = new CancellationTokenSource(TestDeadline);
        await using var host = new PlaybackFmp4HttpStreamHost(Channel, Session);
        using var http = CreateClient();
        using var post = await http.PostAsync(host.StreamUri, null, deadline.Token);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, host.StreamUri);
        request.Headers.Host = "different.example";
        using var response = await http.SendAsync(request, deadline.Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await host.CompleteAsync();
        Assert.False(await host.WaitForClientAsync(deadline.Token));
    }

    [Fact]
    public void Buffer_counts_in_flight_fragment_until_write_is_acknowledged()
    {
        using var buffer = new PlaybackFmp4HttpClientBuffer(CancellationToken.None);
        for (var index = 0; index < PlaybackFmp4HttpClientBuffer.MaximumPendingFragments; index++)
        {
            Assert.True(buffer.TryEnqueue(null, [0x42]));
        }

        Assert.True(buffer.Reader.TryRead(out var inFlight));
        Assert.False(buffer.TryEnqueue(null, [0x43]));
        buffer.AcknowledgeWrite(inFlight);
        Assert.True(buffer.TryEnqueue(null, [0x43]));
    }

    [Fact]
    public void Buffer_counts_bootstrap_bytes_with_queued_and_in_flight_media()
    {
        using var buffer = new PlaybackFmp4HttpClientBuffer(CancellationToken.None);
        var media = new byte[1024 * 1024];
        Assert.True(buffer.TryEnqueue(new byte[64 * 1024], media));
        Assert.True(buffer.Reader.TryRead(out var inFlight));
        Assert.True(buffer.TryEnqueue(null, media));
        Assert.True(buffer.TryEnqueue(null, media));
        Assert.False(buffer.TryEnqueue(null, media));

        buffer.AcknowledgeWrite(inFlight);
        Assert.True(buffer.TryEnqueue(null, media));
        Assert.True(buffer.TryEnqueue(null, media));
        Assert.False(buffer.TryEnqueue(null, [0x42]));
    }

    private static HttpClient CreateClient() =>
        new(new SocketsHttpHandler { UseProxy = false });

    private static async Task AssertBytesAsync(Stream stream, byte[] expected, CancellationToken cancellationToken)
    {
        var received = new byte[expected.Length];
        await stream.ReadExactlyAsync(received, cancellationToken);
        Assert.Equal(expected, received);
    }

    private static (byte[] Initialization, byte[][] Media) CreateFixture()
    {
        var source = CreateMuxer();
        using var muxer = source.Muxer;
        var initialization = CreateInitialization(muxer);
        var segments = new byte[4][];
        for (var index = 0; index < segments.Length; index++)
        {
            segments[index] = CreateMedia(muxer, source.VideoTrackId, source.AudioTrackId, index);
        }

        return (initialization, segments);
    }

    private static (Fmp4Muxer Muxer, uint VideoTrackId, uint AudioTrackId) CreateMuxer()
    {
        var retained = Fmp4PcpLoopbackAcceptanceFixture.Load();
        Assert.Equal(PlaybackFmp4LiveEncodedSampleBatchAdapterCreateResult.Created,
            PlaybackFmp4LiveEncodedSampleBatchAdapter.Create(retained.InitializationSegment, out var adapter));
        var tracks = Assert.IsType<PlaybackFmp4LiveEncodedSampleBatchAdapter>(adapter).StreamDescription.TrackDescriptions
            .Select(track => new Fmp4MuxTrackDescription(
                track.TrackId,
                track.MediaRole == PlaybackEncodedMediaRole.Video ? Fmp4MuxMediaKind.Video : Fmp4MuxMediaKind.Audio,
                track.CodecVariant == PlaybackEncodedCodecVariant.Avc1 ? Fmp4MuxCodec.Avc1 : Fmp4MuxCodec.AacLc,
                checked((uint)track.TimeBase.Denominator),
                track.CodecConfiguration.ToArray(),
                track.Width ?? 0,
                track.Height ?? 0,
                track.SampleRate ?? 0,
                track.ChannelCount ?? 0))
            .ToArray();
        var video = tracks.Single(track => track.MediaKind == Fmp4MuxMediaKind.Video);
        var audio = tracks.Single(track => track.MediaKind == Fmp4MuxMediaKind.Audio);
        Assert.Equal(Fmp4MuxerCreateResult.Created, Fmp4Muxer.Create(tracks, out var created));
        return (Assert.IsType<Fmp4Muxer>(created), video.TrackId, audio.TrackId);
    }

    private static byte[] CreateInitialization(Fmp4Muxer muxer)
    {
        Assert.Equal(Fmp4MuxerResult.Produced, muxer.GenerateInitializationSegment(out var initialization));
        return Assert.IsType<byte[]>(initialization);
    }

    private static byte[] CreateMedia(Fmp4Muxer muxer, uint videoTrackId, uint audioTrackId, int index)
    {
        var independent = index % 2 == 0;
        // Synthetic payloads exercise HTTP ordering and splitting, with real
        // muxer-generated box layout and random-access sample flags.
        var videoPayload = new byte[32 * 1024];
        videoPayload[0] = (byte)index;
        Fmp4MuxEncodedSample[] samples =
        [
            new(videoTrackId, index * 1_000, index * 1_000, 1_000, videoPayload,
                independent ? Fmp4MuxRandomAccessKind.IndependentlyDecodable : Fmp4MuxRandomAccessKind.NotIndependentlyDecodable),
            new(audioTrackId, index * 1_024, index * 1_024, 1_024, new byte[] { 0x22, 0x10 },
                Fmp4MuxRandomAccessKind.NotApplicable),
        ];
        Assert.Equal(Fmp4MuxerResult.Produced, muxer.MuxFragment(samples, out var media));
        return Assert.IsType<Fmp4MuxMediaSegment>(media).Bytes;
    }
}
