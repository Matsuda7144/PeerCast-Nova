using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackPcpFmp4SegmentAssemblerTests
{
    private const uint HeadPacketType = 0x64616568;
    private const uint DataPacketType = 0x61746164;
    private const string FixtureSha256 =
        "3780066C30D70C5063368D708C3850410CAD03840E0B302966206C94AA0425EC";

    [Fact]
    public async Task Fixture_CompleteInitializationInOneHeadCallback()
    {
        var fixture = ReadFixtureSegments();
        using var assembler = new PlaybackPcpFmp4SegmentAssembler();
        var segments = new List<(PlaybackFmp4LiveSegmentKind Kind, byte[] Bytes)>();

        await AppendAsync(assembler, HeadPacketType, fixture.Initialization, segments);

        var segment = Assert.Single(segments);
        Assert.Equal(PlaybackFmp4LiveSegmentKind.Initialization, segment.Kind);
        Assert.Equal(fixture.Initialization, segment.Bytes);
        assembler.Complete();
    }

    [Fact]
    public async Task Fixture_InitializationAndBoxHeaderCanCrossHeadCallbacks()
    {
        var fixture = ReadFixtureSegments();
        var ftypLength = ReadBoxLength(fixture.Initialization, 0);
        var moovLength = ReadBoxLength(fixture.Initialization, ftypLength);
        var splitInsideMoov = ftypLength + (moovLength / 2);
        using var assembler = new PlaybackPcpFmp4SegmentAssembler();
        var segments = new List<(PlaybackFmp4LiveSegmentKind Kind, byte[] Bytes)>();

        await AppendAsync(assembler, HeadPacketType, fixture.Initialization.AsMemory(0, 5), segments);
        Assert.Equal(5, assembler.CarryByteCount);
        await AppendAsync(
            assembler,
            HeadPacketType,
            fixture.Initialization.AsMemory(5, splitInsideMoov - 5),
            segments);
        Assert.True(assembler.HasPartialSegment);
        await AppendAsync(
            assembler,
            HeadPacketType,
            fixture.Initialization.AsMemory(splitInsideMoov),
            segments);

        var segment = Assert.Single(segments);
        Assert.Equal(PlaybackFmp4LiveSegmentKind.Initialization, segment.Kind);
        Assert.Equal(fixture.Initialization, segment.Bytes);
        Assert.Equal(0, assembler.CarryByteCount);
    }

    [Fact]
    public async Task Fixture_MediaCanSplitInsideMoofAndMdat()
    {
        var fixture = ReadFixtureSegments();
        var media = fixture.Media[0];
        var moofLength = ReadBoxLength(media, 0);
        using var assembler = new PlaybackPcpFmp4SegmentAssembler();
        var segments = new List<(PlaybackFmp4LiveSegmentKind Kind, byte[] Bytes)>();

        await AppendAsync(assembler, DataPacketType, media.AsMemory(0, 11), segments);
        await AppendAsync(assembler, DataPacketType, media.AsMemory(11, moofLength + 2), segments);
        await AppendAsync(assembler, DataPacketType, media.AsMemory(moofLength + 13), segments);

        var segment = Assert.Single(segments);
        Assert.Equal(PlaybackFmp4LiveSegmentKind.Media, segment.Kind);
        Assert.Equal(media, segment.Bytes);
        assembler.Complete();
    }

    [Fact]
    public async Task Fixture_SplitBetweenMoofAndMdatAfterMdatHeader()
    {
        var fixture = ReadFixtureSegments();
        var media = fixture.Media[0];
        var moofLength = ReadBoxLength(media, 0);
        using var assembler = new PlaybackPcpFmp4SegmentAssembler();
        var segments = new List<(PlaybackFmp4LiveSegmentKind Kind, byte[] Bytes)>();

        await AppendAsync(
            assembler,
            DataPacketType,
            media.AsMemory(0, moofLength + 8),
            segments);
        Assert.True(assembler.HasPartialSegment);
        await AppendAsync(
            assembler,
            DataPacketType,
            media.AsMemory(moofLength + 8),
            segments);

        Assert.Equal(media, Assert.Single(segments).Bytes);
        assembler.Complete();
    }

    [Fact]
    public async Task Fixture_MultipleMediaSegmentsAndBoundaryShareOneDataCallback()
    {
        var fixture = ReadFixtureSegments();
        using var assembler = new PlaybackPcpFmp4SegmentAssembler();
        var segments = new List<(PlaybackFmp4LiveSegmentKind Kind, byte[] Bytes)>();
        var joined = fixture.Media[0].Concat(fixture.Media[1]).ToArray();

        await AppendAsync(assembler, DataPacketType, joined, segments);

        Assert.Equal(2, segments.Count);
        Assert.All(segments, item =>
            Assert.Equal(PlaybackFmp4LiveSegmentKind.Media, item.Kind));
        Assert.Equal(fixture.Media[0], segments[0].Bytes);
        Assert.Equal(fixture.Media[1], segments[1].Bytes);
        assembler.Complete();
    }

    [Fact]
    public async Task RejectsUnsupportedPacketTypeAndCrossTypePartialCarry()
    {
        var fixture = ReadFixtureSegments();
        using var assembler = new PlaybackPcpFmp4SegmentAssembler();
        var segments = new List<(PlaybackFmp4LiveSegmentKind Kind, byte[] Bytes)>();

        await AppendAsync(assembler, HeadPacketType, fixture.Initialization.AsMemory(0, 12), segments);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await assembler.AppendAsync(
                DataPacketType,
                fixture.Initialization.AsMemory(12),
                (_, _, _) => ValueTask.CompletedTask));
        assembler.Reset();
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await assembler.AppendAsync(
                0x6174656d,
                ReadOnlyMemory<byte>.Empty,
                (_, _, _) => ValueTask.CompletedTask));
        Assert.Equal(0, assembler.CarryByteCount);
    }

    [Fact]
    public async Task RejectsOversizedDeclaredBoxBeforeGrowingCarry()
    {
        using var assembler = new PlaybackPcpFmp4SegmentAssembler(new()
        {
            MaximumSegmentBytes = 64,
        });
        var header = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), 4096);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), 0x66747970);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await assembler.AppendAsync(
                HeadPacketType,
                header,
                (_, _, _) => ValueTask.CompletedTask));

        Assert.Equal(8, assembler.CarryByteCount);
        Assert.True(assembler.CarryByteCount <= 64);
        assembler.Reset();
        Assert.Equal(0, assembler.CarryByteCount);
    }

    [Fact]
    public async Task RejectsSmallZeroAndMalformedExtendedBoxSizes()
    {
        using var assembler = new PlaybackPcpFmp4SegmentAssembler();
        var accept = new List<(PlaybackFmp4LiveSegmentKind Kind, byte[] Bytes)>();
        var tooSmall = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(tooSmall.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt32BigEndian(tooSmall.AsSpan(4, 4), 0x66747970);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await AppendAsync(assembler, HeadPacketType, tooSmall, accept));
        assembler.Reset();

        var extendsToEnd = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(extendsToEnd.AsSpan(0, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(extendsToEnd.AsSpan(4, 4), 0x66747970);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await AppendAsync(assembler, HeadPacketType, extendsToEnd, accept));
        assembler.Reset();

        var invalidExtended = new byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(invalidExtended.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(invalidExtended.AsSpan(4, 4), 0x66747970);
        BinaryPrimitives.WriteUInt64BigEndian(invalidExtended.AsSpan(8, 8), 15);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await AppendAsync(assembler, HeadPacketType, invalidExtended, accept));
        Assert.True(assembler.CarryByteCount <= invalidExtended.Length);
    }

    [Fact]
    public async Task AcceptsValidExtendedSizeHeaderAndRejectsTruncatedFinalBox()
    {
        using var assembler = new PlaybackPcpFmp4SegmentAssembler();
        var output = new List<(PlaybackFmp4LiveSegmentKind Kind, byte[] Bytes)>();
        var initialization = new byte[24];
        BinaryPrimitives.WriteUInt32BigEndian(initialization.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(initialization.AsSpan(4, 4), 0x66747970);
        BinaryPrimitives.WriteUInt64BigEndian(initialization.AsSpan(8, 8), 16);
        BinaryPrimitives.WriteUInt32BigEndian(initialization.AsSpan(16, 4), 8);
        BinaryPrimitives.WriteUInt32BigEndian(initialization.AsSpan(20, 4), 0x6d6f6f76);

        await AppendAsync(assembler, HeadPacketType, initialization, output);
        Assert.Equal(initialization, Assert.Single(output).Bytes);
        assembler.Reset();
        await AppendAsync(assembler, DataPacketType, initialization.AsMemory(0, 12), output);
        Assert.Throws<InvalidDataException>(() => assembler.Complete());
    }

    [Fact]
    public async Task FixtureIdentityMatchesAcceptedPresentationFixture()
    {
        var fixture = ReadFixture();
        Assert.Equal(FixtureSha256, Convert.ToHexString(SHA256.HashData(fixture)));
        Assert.Equal(14_777, fixture.Length);
        var split = SplitFixture(fixture);
        Assert.Equal(2, split.Media.Length);
    }

    private static async Task AppendAsync(
        PlaybackPcpFmp4SegmentAssembler assembler,
        uint packetType,
        ReadOnlyMemory<byte> payload,
        List<(PlaybackFmp4LiveSegmentKind Kind, byte[] Bytes)> segments)
    {
        await assembler.AppendAsync(
            packetType,
            payload,
            (kind, bytes, _) =>
            {
                segments.Add((kind, bytes.ToArray()));
                return ValueTask.CompletedTask;
            });
    }

    private static int ReadBoxLength(ReadOnlySpan<byte> bytes, int offset)
    {
        var result = PlaybackIsoBmffBoxHeaderParser.Parse(bytes[offset..], out var header);
        Assert.Equal(PlaybackIsoBmffBoxHeaderParseResult.Parsed, result);
        return checked((int)header!.BoxSize!.Value);
    }

    private static byte[] ReadFixture()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "PeerCast.Nova.Playback.Tests.Fixtures.fmp4-avc1-aac.mp4");
        Assert.NotNull(stream);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static (byte[] Initialization, byte[][] Media) ReadFixtureSegments() =>
        SplitFixture(ReadFixture());

    private static (byte[] Initialization, byte[][] Media) SplitFixture(byte[] fixture)
    {
        var boxes = new List<byte[]>();
        var offset = 0;
        while (offset < fixture.Length)
        {
            var result = PlaybackIsoBmffBoxSequenceReader.ReadNext(
                fixture,
                offset,
                out var box);
            Assert.Equal(PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable, result);
            var location = Assert.IsType<PlaybackIsoBmffBoxLocation>(box);
            boxes.Add(fixture.AsSpan(offset, location.BoxLength).ToArray());
            offset = location.NextOffset;
        }

        Assert.Equal(6, boxes.Count);
        return (
            boxes[0].Concat(boxes[1]).ToArray(),
            [
                boxes[2].Concat(boxes[3]).ToArray(),
                boxes[4].Concat(boxes[5]).ToArray(),
            ]);
    }
}
