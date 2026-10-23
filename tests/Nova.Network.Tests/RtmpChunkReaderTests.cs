using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.Network.Tests;

public sealed class RtmpChunkReaderTests
{
    [Fact]
    public async Task Literal_formats_zero_one_two_three_reconstruct_inherited_headers_and_timestamps()
    {
        byte[] wire =
        [
            6, 0, 0, 10, 0, 0, 2, 9, 1, 0, 0, 0, 0xA1, 0xA2,
            0x46, 0, 0, 3, 0, 0, 1, 8, 0xB1,
            0x86, 0, 0, 2, 0xC1,
            0xC6, 0xD1,
        ];
        using var input = new MemoryStream(wire);
        var reader = Create(input);
        var first = await reader.ReadAsync(CancellationToken.None);
        Assert.Equal(10U, first.Timestamp);
        Assert.Equal(9, first.Type);
        Assert.Equal(1U, first.StreamId);
        Assert.Equal(new byte[] { 0xA1, 0xA2 }, first.Payload);
        Assert.Equal(13U, (await reader.ReadAsync(CancellationToken.None)).Timestamp);
        Assert.Equal(15U, (await reader.ReadAsync(CancellationToken.None)).Timestamp);
        var fourth = await reader.ReadAsync(CancellationToken.None);
        Assert.Equal(17U, fourth.Timestamp);
        Assert.Equal(8, fourth.Type);
        Assert.Equal(new byte[] { 0xD1 }, fourth.Payload);
    }

    [Fact]
    public async Task Extended_timestamp_survives_continuations_and_fragmented_reads()
    {
        var payload = Enumerable.Range(0, 260).Select(i => (byte)i).ToArray();
        var wire = ObsRtmpTestClient.WireMessage(9, 1, 0xFE123456, payload);
        using var input = new FragmentedStream(wire);
        var message = await Create(input).ReadAsync(CancellationToken.None);
        Assert.Equal(0xFE123456U, message.Timestamp);
        Assert.Equal(payload, message.Payload);
    }

    [Fact]
    public async Task Extended_delta_and_new_format_three_messages_inherit_delta()
    {
        byte[] wire =
        [
            6, 0, 0, 10, 0, 0, 1, 9, 1, 0, 0, 0, 0xAA,
            0x86, 0xFF, 0xFF, 0xFF, 1, 0, 0, 0, 0xBB,
            0xC6, 1, 0, 0, 0, 0xCC,
        ];
        using var input = new MemoryStream(wire);
        var reader = Create(input);
        await reader.ReadAsync(CancellationToken.None);
        Assert.Equal(0x0100000AU, (await reader.ReadAsync(CancellationToken.None)).Timestamp);
        Assert.Equal(0x0200000AU, (await reader.ReadAsync(CancellationToken.None)).Timestamp);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Extended_basic_headers_accept_observed_chunk_contexts(byte marker)
    {
        var wire = new List<byte> { marker, 0 };
        if (marker == 1)
        {
            wire.Add(1);
        }

        wire.AddRange([0, 0, 0, 0, 0, 1, 8, 1, 0, 0, 0, 0xAF]);
        using var input = new MemoryStream(wire.ToArray());
        var message = await Create(input).ReadAsync(CancellationToken.None);
        Assert.Equal(8, message.Type);
        Assert.Equal(new byte[] { 0xAF }, message.Payload);
    }

    [Fact]
    public async Task Interleaved_chunk_streams_complete_independently_under_global_budget()
    {
        var first = ObsRtmpTestClient.WireMessage(9, 1, 2, new byte[130]);
        var second = ObsRtmpTestClient.WireMessage(8, 1, 3, [0xAF, 1, 0x22], id: 7);
        var wire = first[..140].Concat(second).Concat(first[140..]).ToArray();
        using var input = new MemoryStream(wire);
        var reader = Create(input);
        Assert.Equal(8, (await reader.ReadAsync(CancellationToken.None)).Type);
        Assert.Equal(130, (await reader.ReadAsync(CancellationToken.None)).Payload.Length);
    }

    [Fact]
    public async Task Abort_discards_only_the_selected_incomplete_body()
    {
        var first = ObsRtmpTestClient.WireMessage(9, 1, 2, new byte[130]);
        var abort = ObsRtmpTestClient.WireMessage(2, 0, 0, [0, 0, 0, 6], id: 2);
        var next = ObsRtmpTestClient.WireMessage(9, 1, 3, [0x11], id: 6);
        using var input = new MemoryStream(first[..140].Concat(abort).Concat(next).ToArray());
        var reader = Create(input);
        Assert.Equal(2, (await reader.ReadAsync(CancellationToken.None)).Type);
        reader.Abort(6);
        Assert.Equal(new byte[] { 0x11 }, (await reader.ReadAsync(CancellationToken.None)).Payload);
    }

    [Theory]
    [InlineData("4600000000000109AA")]
    [InlineData("060000001000010901000000")]
    [InlineData("060000000000000901000000")]
    public async Task Invalid_context_or_message_length_is_rejected_before_body_allocation(string hex)
    {
        using var input = new MemoryStream(Convert.FromHexString(hex));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await Create(input).ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Aggregate_incomplete_messages_and_context_count_are_bounded()
    {
        var first = ObsRtmpTestClient.WireMessage(9, 1, 2, new byte[130]);
        var second = ObsRtmpTestClient.WireMessage(8, 1, 3, new byte[130], id: 7);
        var wire = first[..140].Concat(second).ToArray();
        using var input = new MemoryStream(wire);
        var reader = Create(input, new ObsRtmpIngestOptions { MaximumMessageBytes = 160, MaximumBufferedBytes = 200 });
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await reader.ReadAsync(CancellationToken.None));
        using var input2 = new MemoryStream(
            ObsRtmpTestClient.WireMessage(8, 1, 0, [1])
            .Concat(ObsRtmpTestClient.WireMessage(8, 1, 0, [2], id: 7)).ToArray());
        var bounded = Create(input2, new ObsRtmpIngestOptions { MaximumChunkStreams = 1 });
        await bounded.ReadAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await bounded.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Acknowledgements_are_emitted_before_large_message_completion()
    {
        using var input = new FragmentedStream(ObsRtmpTestClient.WireMessage(9, 1, 0, new byte[500]));
        var acknowledgements = new List<uint>();
        var reader = new RtmpChunkReader(input, new(), (count, _) =>
        {
            acknowledgements.Add(count);
            return ValueTask.CompletedTask;
        })
        { AcknowledgementWindow = 100 };
        await reader.ReadAsync(CancellationToken.None);
        Assert.True(acknowledgements.Count >= 5);
        Assert.InRange(acknowledgements[0], 3173U, 3175U);
        Assert.True(acknowledgements[0] < 3073 + 500);
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(65537U)]
    [InlineData(0x80000080U)]
    public void Invalid_chunk_size_is_rejected(uint value)
    {
        using var input = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => Create(input).SetChunkBytes(value));
    }

    [Fact]
    public async Task Truncated_body_is_never_returned_as_a_message()
    {
        using var input = new MemoryStream(ObsRtmpTestClient.WireMessage(8, 1, 0, [1, 2])[..^1]);
        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
            await Create(input).ReadAsync(CancellationToken.None));
    }

    private static RtmpChunkReader Create(Stream stream, ObsRtmpIngestOptions? options = null) =>
        new(stream, options ?? new(), (_, _) => ValueTask.CompletedTask);

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 3)], cancellationToken);
    }
}
