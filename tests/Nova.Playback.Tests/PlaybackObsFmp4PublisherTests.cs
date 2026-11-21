using System.Buffers.Binary;
using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Rtmp;
using PeerCast.Nova.Network.Tests;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackObsFmp4PublisherTests
{
    private const uint Origin = 0x01000040;
    private const int AudioSamplesPerFrame = 1024;
    private static readonly byte[] AvcConfiguration = PlaybackBoundedFlvParserTests.BuildAvcConfiguration();

    [Theory]
    [InlineData(48_000, false)]
    [InlineData(44_100, true)]
    public async Task Rtmp_access_units_reach_fmp4_http_before_unpublish(int sampleRate, bool lateJoin)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = deadline.Token;
        var channelId = new ChannelId(Guid.NewGuid());
        var sessionId = new ChannelSessionId(Guid.NewGuid());
        await using var output = new PlaybackFmp4HttpStreamHost(channelId, sessionId);
        using var publisher = new PlaybackObsFmp4Publisher(sessionId, output);
        await using var ingest = ObsRtmpIngestHost.CreateForEncodedMedia(channelId, sessionId, publisher, new() { Port = 0 });
        ingest.Start(token);
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
        Task<HttpResponseMessage>? responseTask = null;
        if (!lateJoin)
        {
            responseTask = client.GetAsync(output.StreamUri, HttpCompletionOption.ResponseHeadersRead, token);
            Assert.True(await output.WaitForClientAsync(token));
        }

        using var source = new ObsRtmpTestClient();
        await source.ConnectAsync(ingest.LocalEndPoint);
        await source.PublishAsync();
        var asc = sampleRate == 48_000 ? new byte[] { 0x11, 0x90 } : new byte[] { 0x12, 0x10 };
        await SendConfigurationAsync(source, asc);
        await source.SendAsync(8, 1, Origin - 30, Audio(0xfe));
        await source.SendAsync(9, 1, Origin - 20, Video(false, 0, 0xfe));
        await source.SendAsync(9, 1, Origin, Video(true, 0, 0x31));
        await source.SendAsync(9, 1, Origin + 33, Video(false, -2, 0x32));
        await source.SendAsync(9, 1, Origin + 70, Video(false, 1, 0x33));
        var audioIndex = 0;
        var firstAudioCount = AudioFramesStartingBefore(100, sampleRate);
        await SendAudioThroughAsync(firstAudioCount);
        await source.SendAsync(9, 1, Origin + 100, Video(true, 0, 0x34));

        if (lateJoin)
        {
            // The pong is written after the previous media callback returned.
            // It makes the first fragment precede HTTP subscription without a timer.
            await source.SendAsync(4, 0, 0, [0, 6, 0, 0, 0, 42], id: 2);
            var pong = await source.ReadAsync();
            Assert.Equal(4, pong.Type);
            Assert.Equal(new byte[] { 0, 7, 0, 0, 0, 42 }, pong.Payload);
            responseTask = client.GetAsync(output.StreamUri, HttpCompletionOption.ResponseHeadersRead, token);
            Assert.True(await output.WaitForClientAsync(token));
        }

        await source.SendAsync(9, 1, Origin + 133, Video(false, 0, 0x35));
        await source.SendAsync(9, 1, Origin + 166, Video(false, 0, 0x36));
        var secondAudioEnd = AudioFramesStartingBefore(200, sampleRate);
        await SendAudioThroughAsync(secondAudioEnd);
        await source.SendAsync(9, 1, Origin + 200, Video(true, 0, 0x37));

        using var response = await responseTask!;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.TransferEncodingChunked);
        using var body = await response.Content.ReadAsStreamAsync(token);
        var initialization = await ReadSegmentAsync(body, token);
        Assert.Equal(PlaybackFmp4LiveEncodedSampleBatchAdapterCreateResult.Created,
            PlaybackFmp4LiveEncodedSampleBatchAdapter.Create(initialization, out var reader));
        var adapter = Assert.IsType<PlaybackFmp4LiveEncodedSampleBatchAdapter>(reader);
        var video = adapter.StreamDescription.TrackDescriptions.Single(track => track.TrackId == 1);
        Assert.Equal(320, video.Width);
        Assert.Equal(240, video.Height);
        Assert.Equal(AvcConfiguration, video.CodecConfiguration.ToArray());
        var audio = adapter.StreamDescription.TrackDescriptions.Single(track => track.TrackId == 2);
        Assert.Equal(sampleRate, audio.TimeBase.Denominator);
        Assert.Equal(asc, audio.CodecConfiguration.ToArray());

        if (!lateJoin)
        {
            using var first = await ReadBatchAsync(adapter, body, token);
            Assert.Equal(1u, first.FragmentSequence);
            AssertVideo(first, [0L, 33L, 70L], [0L, 31L, 71L], [33L, 37L, 30L], 0x31);
            AssertAudio(first, 0, firstAudioCount);
        }

        using (var second = await ReadBatchAsync(adapter, body, token))
        {
            Assert.Equal(2u, second.FragmentSequence);
            AssertVideo(second, [100L, 133L, 166L], [100L, 133L, 166L], [33L, 33L, 34L], 0x34);
            AssertAudio(second, firstAudioCount, secondAudioEnd);
        }

        Assert.False(ingest.Completion.IsCompleted);
        await SendAudioThroughAsync(secondAudioEnd + 1);
        await source.SendCommandAsync(0, "FCUnpublish", 6.0, null, "nova");
        var result = await ingest.Completion.WaitAsync(token);
        Assert.Equal(ObsRtmpIngestEndReason.PublisherStopped, result.EndReason);
        Assert.Equal(0, result.AcceptedPacketCount);
        publisher.Complete();
        var completed = output.CompleteAsync();
        using (var final = await ReadBatchAsync(adapter, body, token))
        {
            Assert.Equal(3u, final.FragmentSequence);
            AssertVideo(final, [200L], [200L], [34L], 0x37);
            AssertAudio(final, secondAudioEnd, secondAudioEnd + 1);
        }

        Assert.Equal(0, await body.ReadAsync(new byte[1], token));
        Assert.True(await completed.WaitAsync(token));

        async Task SendAudioThroughAsync(int count)
        {
            for (; audioIndex < count; audioIndex++)
            {
                await source.SendAsync(8, 1, Origin + AudioTimestamp(audioIndex, sampleRate), Audio((byte)audioIndex));
            }
        }
    }

    [Fact]
    public async Task Audio_timestamp_discontinuity_ends_encoded_ingress()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var channelId = new ChannelId(Guid.NewGuid());
        var sessionId = new ChannelSessionId(Guid.NewGuid());
        await using var output = new PlaybackFmp4HttpStreamHost(channelId, sessionId);
        using var publisher = new PlaybackObsFmp4Publisher(sessionId, output);
        await using var ingest = ObsRtmpIngestHost.CreateForEncodedMedia(channelId, sessionId, publisher, new() { Port = 0 });
        ingest.Start(deadline.Token);
        using var source = new ObsRtmpTestClient();
        await source.ConnectAsync(ingest.LocalEndPoint);
        await source.PublishAsync();
        await SendConfigurationAsync(source, [0x11, 0x90]);
        await source.SendAsync(9, 1, Origin, Video(true, 0, 0x31));
        await source.SendAsync(8, 1, Origin, Audio(0));
        await source.SendAsync(8, 1, Origin + 43, Audio(1));

        var result = await ingest.Completion.WaitAsync(deadline.Token);
        Assert.Equal(ObsRtmpIngestEndReason.ConsumerRejected, result.EndReason);
        Assert.Contains("continuous sample clock", publisher.Rejection, StringComparison.Ordinal);
        Assert.Equal(0, publisher.PublishedFragments);
    }

    [Fact]
    public async Task Timeline_staging_is_bounded_when_video_stops_advancing()
    {
        var sessionId = new ChannelSessionId(Guid.NewGuid());
        await using var output = new PlaybackFmp4HttpStreamHost(new ChannelId(Guid.NewGuid()), sessionId);
        using var publisher = new PlaybackObsFmp4Publisher(sessionId, output);
        publisher.Configure(AvcConfiguration, [0x11, 0x90]);
        publisher.AcceptVideo(Origin, 0, true, Video(true, 0, 0x31).AsSpan(5));
        for (var index = 0; index < 255; index++)
        {
            publisher.AcceptAudio(Origin + AudioTimestamp(index, 48_000), [0x5a, 0x42]);
        }

        var error = Assert.Throws<InvalidDataException>(() =>
            publisher.AcceptAudio(Origin + AudioTimestamp(255, 48_000), [0x5a, 0x42]));
        Assert.Contains("pending access-unit limit", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, publisher.PublishedFragments);
    }

    private static async Task SendConfigurationAsync(ObsRtmpTestClient client, byte[] asc)
    {
        await client.SendAsync(8, 1, 0, [0xaf, 0, .. asc]);
        await client.SendAsync(9, 1, 0, [0x17, 0, 0, 0, 0, .. AvcConfiguration]);
    }

    private static byte[] Video(bool idr, int compositionOffset, byte value) =>
        [
            idr ? (byte)0x17 : (byte)0x27, 1,
            unchecked((byte)(compositionOffset >> 16)), unchecked((byte)(compositionOffset >> 8)), unchecked((byte)compositionOffset),
            0, 0, 0, 2, idr ? (byte)0x65 : (byte)0x41, value,
        ];

    private static byte[] Audio(byte value) => [0xaf, 1, value, 0x5a];

    private static uint AudioTimestamp(int index, int sampleRate) =>
        (uint)((index * AudioSamplesPerFrame * 1_000 + sampleRate / 2) / sampleRate);

    private static int AudioFramesStartingBefore(int milliseconds, int sampleRate) =>
        (milliseconds * sampleRate + 1_000 * AudioSamplesPerFrame - 1) / (1_000 * AudioSamplesPerFrame);

    private static async Task<byte[]> ReadSegmentAsync(Stream source, CancellationToken token)
    {
        using var bytes = new MemoryStream();
        var header = new byte[8];
        for (var box = 0; box < 2; box++)
        {
            await source.ReadExactlyAsync(header, token);
            var size = BinaryPrimitives.ReadInt32BigEndian(header);
            Assert.InRange(size, 8, PlaybackFmp4HttpStreamHost.MaximumMediaSegmentBytes);
            var payload = new byte[size - 8];
            await source.ReadExactlyAsync(payload, token);
            bytes.Write(header);
            bytes.Write(payload);
        }

        return bytes.ToArray();
    }

    private static async Task<PlaybackEncodedSampleBatch> ReadBatchAsync(
        PlaybackFmp4LiveEncodedSampleBatchAdapter adapter, Stream stream, CancellationToken token)
    {
        var segment = await ReadSegmentAsync(stream, token);
        Assert.Equal(PlaybackEncodedSampleBatchCreationResult.Created, adapter.CreateBatch(segment, out var batch));
        return Assert.IsType<PlaybackEncodedSampleBatch>(batch);
    }

    private static void AssertVideo(
        PlaybackEncodedSampleBatch batch, long[] dts, long[] pts, long[] durations, byte startValue)
    {
        var video = batch.Samples.Where(sample => sample.TrackId == 1).ToArray();
        Assert.Equal(dts, video.Select(sample => sample.DtsTicks));
        Assert.Equal(pts, video.Select(sample => sample.PtsTicks));
        Assert.Equal(durations, video.Select(sample => sample.DurationTicks));
        for (var index = 0; index < video.Length; index++)
        {
            Assert.Equal(index == 0 ? PlaybackEncodedRandomAccessClassification.IndependentlyDecodable :
                PlaybackEncodedRandomAccessClassification.NotIndependentlyDecodable, video[index].RandomAccessClassification);
            var payload = new byte[video[index].PayloadSlice.Length];
            batch.CopyPayloadTo(video[index], payload);
            Assert.Equal(Video(index == 0, 0, (byte)(startValue + index))[5..], payload);
        }
    }

    private static void AssertAudio(PlaybackEncodedSampleBatch batch, int startIndex, int endIndex)
    {
        var audio = batch.Samples.Where(sample => sample.TrackId == 2).ToArray();
        Assert.Equal(endIndex - startIndex, audio.Length);
        for (var index = 0; index < audio.Length; index++)
        {
            var expected = (startIndex + index) * AudioSamplesPerFrame;
            Assert.Equal(expected, audio[index].DtsTicks);
            Assert.Equal(expected, audio[index].PtsTicks);
            Assert.Equal(AudioSamplesPerFrame, audio[index].DurationTicks);
            var payload = new byte[audio[index].PayloadSlice.Length];
            batch.CopyPayloadTo(audio[index], payload);
            Assert.Equal(new byte[] { (byte)(startIndex + index), 0x5a }, payload);
        }
    }
}
