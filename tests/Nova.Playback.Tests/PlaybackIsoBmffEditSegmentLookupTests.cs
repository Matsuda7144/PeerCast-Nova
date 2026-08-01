using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffEditSegmentLookupTests
{
    [Fact]
    public void TryLocate_ProjectsEntryBoundsAndRelativeOffset()
    {
        var editList = Parse(
            (3UL, -1L, (short)1, (short)0),
            (5UL, 7L, (short)1, (short)0));

        var result = PlaybackIsoBmffEditSegmentLookup.TryLocate(
            6,
            editList,
            out var location);

        Assert.Equal(PlaybackIsoBmffEditSegmentLookupResult.Located, result);
        var located = Assert.IsType<PlaybackIsoBmffEditSegmentLocation>(location);
        Assert.Equal(1, located.EntryIndex);
        Assert.Equal(editList.Entries[1], located.Entry);
        Assert.Equal((ulong)3, located.MovieStartUnits);
        Assert.Equal((ulong)8, located.MovieEndUnits);
        Assert.Equal((ulong)3, located.SegmentOffsetUnits);
        Assert.False(located.IsEmptyEdit);
    }

    [Fact]
    public void TryLocate_EmptyEditIsReportedWithoutApplyingPolicy()
    {
        var editList = Parse((4UL, -1L, (short)1, (short)0));

        var result = PlaybackIsoBmffEditSegmentLookup.TryLocate(
            0,
            editList,
            out var location);

        Assert.Equal(PlaybackIsoBmffEditSegmentLookupResult.Located, result);
        Assert.True(Assert.IsType<PlaybackIsoBmffEditSegmentLocation>(location)
            .IsEmptyEdit);
    }

    [Fact]
    public void TryLocate_ZeroDurationEntriesAreSkipped()
    {
        var editList = Parse(
            (0UL, 9L, (short)1, (short)0),
            (2UL, 11L, (short)1, (short)0));

        var result = PlaybackIsoBmffEditSegmentLookup.TryLocate(
            0,
            editList,
            out var location);

        Assert.Equal(PlaybackIsoBmffEditSegmentLookupResult.Located, result);
        Assert.Equal(
            1,
            Assert.IsType<PlaybackIsoBmffEditSegmentLocation>(location)
                .EntryIndex);
    }

    [Fact]
    public void TryLocate_UsesExclusiveSegmentAndTableEnds()
    {
        var editList = Parse(
            (2UL, 0L, (short)1, (short)0),
            (3UL, 2L, (short)1, (short)0));

        Assert.Equal(
            PlaybackIsoBmffEditSegmentLookupResult.Located,
            PlaybackIsoBmffEditSegmentLookup.TryLocate(
                2,
                editList,
                out var boundary));
        Assert.Equal(
            1,
            Assert.IsType<PlaybackIsoBmffEditSegmentLocation>(boundary)
                .EntryIndex);
        Assert.Equal(
            PlaybackIsoBmffEditSegmentLookupResult.MovieTimeOutOfRange,
            PlaybackIsoBmffEditSegmentLookup.TryLocate(
                5,
                editList,
                out var atEnd));
        Assert.Null(atEnd);
    }

    [Fact]
    public void TryLocate_MissingAndEmptyListsFailClosed()
    {
        Assert.Equal(
            PlaybackIsoBmffEditSegmentLookupResult.MissingEditList,
            PlaybackIsoBmffEditSegmentLookup.TryLocate(0, null, out var missing));
        Assert.Null(missing);

        var empty = Parse();
        Assert.Equal(
            PlaybackIsoBmffEditSegmentLookupResult.MovieTimeOutOfRange,
            PlaybackIsoBmffEditSegmentLookup.TryLocate(0, empty, out var absent));
        Assert.Null(absent);
    }

    private static PlaybackIsoBmffEditListBox Parse(
        params (ulong Duration, long MediaTime, short RateInteger,
            short RateFraction)[] entries)
    {
        const int headerSize = 8;
        const int fixedPayloadSize = 8;
        const int entrySize = 20;
        var box = new byte[
            headerSize + fixedPayloadSize + (entries.Length * entrySize)];
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), 0x656C7374);
        box[headerSize] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(headerSize + 4, 4),
            (uint)entries.Length);
        for (var index = 0; index < entries.Length; index++)
        {
            var offset = headerSize + fixedPayloadSize + (index * entrySize);
            var entry = entries[index];
            BinaryPrimitives.WriteUInt64BigEndian(
                box.AsSpan(offset, 8),
                entry.Duration);
            BinaryPrimitives.WriteInt64BigEndian(
                box.AsSpan(offset + 8, 8),
                entry.MediaTime);
            BinaryPrimitives.WriteInt16BigEndian(
                box.AsSpan(offset + 16, 2),
                entry.RateInteger);
            BinaryPrimitives.WriteInt16BigEndian(
                box.AsSpan(offset + 18, 2),
                entry.RateFraction);
        }

        Assert.Equal(
            PlaybackIsoBmffEditListBoxParseResult.Parsed,
            PlaybackIsoBmffEditListBoxParser.Parse(box, out var editList));
        return Assert.IsType<PlaybackIsoBmffEditListBox>(editList);
    }
}
