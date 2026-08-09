using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffFragmentSampleDefaultsResolverTests
{
    [Fact]
    public void Resolve_AppliesEntryThenFragmentThenTrackDefaultPrecedence()
    {
        var trackExtends = ParseTrex(7, 2, 10, 20, 30);
        var fragmentHeader = ParseTfhd(7, 3, 11, 21, 31);
        var trackRun = ParseTrun(
            firstSampleFlags: null,
            new(12, null, 32, 1),
            new(13, null, 33, 2));

        Assert.Equal(
            PlaybackIsoBmffFragmentSampleDefaultsResolveResult.Resolved,
            PlaybackIsoBmffFragmentSampleDefaultsResolver.Resolve(
                trackExtends,
                fragmentHeader,
                trackRun,
                out var resolvedRun));
        var resolved = Assert.IsType<PlaybackIsoBmffResolvedFragmentRun>(
            resolvedRun);
        Assert.Equal(7U, resolved.TrackId);
        Assert.Equal(3U, resolved.SampleDescriptionIndex);
        Assert.Equal(
            [
                new PlaybackIsoBmffResolvedFragmentSample(12, 21, 32, 1),
                new PlaybackIsoBmffResolvedFragmentSample(13, 21, 33, 2),
            ],
            resolved.Samples);
        Assert.IsNotType<PlaybackIsoBmffResolvedFragmentSample[]>(
            resolved.Samples);
    }

    [Fact]
    public void Resolve_FirstSampleFlagsOverrideDefaultOnlyForFirstEntry()
    {
        var trackRun = ParseTrun(
            firstSampleFlags: 9,
            new(null, null, null, null),
            new(null, null, null, null));

        Assert.Equal(
            PlaybackIsoBmffFragmentSampleDefaultsResolveResult.Resolved,
            PlaybackIsoBmffFragmentSampleDefaultsResolver.Resolve(
                ParseTrex(1, 1, 2, 3, 4),
                ParseTfhd(1, null, null, null, null),
                trackRun,
                out var resolvedRun));
        Assert.Equal(
            [
                new PlaybackIsoBmffResolvedFragmentSample(2, 3, 9, null),
                new PlaybackIsoBmffResolvedFragmentSample(2, 3, 4, null),
            ],
            Assert.IsType<PlaybackIsoBmffResolvedFragmentRun>(
                resolvedRun).Samples);
    }

    [Fact]
    public void Resolve_UsesTrackDefaultsWhenFragmentAndEntriesAreAbsent()
    {
        var trackExtends = ParseTrex(1, 4, 5, 6, 7);
        var fragmentHeader = ParseTfhd(1, null, null, null, null);
        var trackRun = ParseTrun(
            firstSampleFlags: null,
            new PlaybackIsoBmffTrackRunEntry(null, null, null, null));

        Assert.Equal(
            PlaybackIsoBmffFragmentSampleDefaultsResolveResult.Resolved,
            PlaybackIsoBmffFragmentSampleDefaultsResolver.Resolve(
                trackExtends,
                fragmentHeader,
                trackRun,
                out var resolvedRun));
        var resolved = Assert.IsType<PlaybackIsoBmffResolvedFragmentRun>(
            resolvedRun);
        Assert.Equal(4U, resolved.SampleDescriptionIndex);
        Assert.Equal(
            new PlaybackIsoBmffResolvedFragmentSample(5, 6, 7, null),
            Assert.Single(resolved.Samples));
    }

    [Fact]
    public void Resolve_PresentZeroFragmentDefaultsOverrideTrackDefaults()
    {
        var trackExtends = ParseTrex(1, 1, 5, 6, 7);
        var fragmentHeader = ParseTfhd(1, 2, 0, 0, 0);
        var trackRun = ParseTrun(
            firstSampleFlags: null,
            new PlaybackIsoBmffTrackRunEntry(null, null, null, null));

        Assert.Equal(
            PlaybackIsoBmffFragmentSampleDefaultsResolveResult.Resolved,
            PlaybackIsoBmffFragmentSampleDefaultsResolver.Resolve(
                trackExtends,
                fragmentHeader,
                trackRun,
                out var resolvedRun));
        Assert.Equal(
            new PlaybackIsoBmffResolvedFragmentSample(0, 0, 0, null),
            Assert.Single(
                Assert.IsType<PlaybackIsoBmffResolvedFragmentRun>(
                    resolvedRun).Samples));
    }

    [Fact]
    public void Resolve_EmptyRunIsValidAndImmutable()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleDefaultsResolveResult.Resolved,
            PlaybackIsoBmffFragmentSampleDefaultsResolver.Resolve(
                ParseTrex(1, 1, 0, 0, 0),
                ParseTfhd(1, null, null, null, null),
                ParseTrun(null),
                out var resolvedRun));
        Assert.Empty(
            Assert.IsType<PlaybackIsoBmffResolvedFragmentRun>(
                resolvedRun).Samples);
    }

    [Fact]
    public void Resolve_RejectsMismatchedTrackIdentity()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleDefaultsResolveResult.TrackIdMismatch,
            PlaybackIsoBmffFragmentSampleDefaultsResolver.Resolve(
                ParseTrex(1, 1, 0, 0, 0),
                ParseTfhd(2, null, null, null, null),
                ParseTrun(null),
                out var resolvedRun));
        Assert.Null(resolvedRun);
    }

    [Fact]
    public void Resolve_RejectsPresentZeroSampleDescriptionIndex()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleDefaultsResolveResult
                .InvalidSampleDescriptionIndex,
            PlaybackIsoBmffFragmentSampleDefaultsResolver.Resolve(
                ParseTrex(1, 1, 0, 0, 0),
                ParseTfhd(1, 0, null, null, null),
                ParseTrun(null),
                out var resolvedRun));
        Assert.Null(resolvedRun);
    }

    [Fact]
    public void Resolve_NullInputsAreRejectedWithoutProjection()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleDefaultsResolveResult.InvalidInput,
            PlaybackIsoBmffFragmentSampleDefaultsResolver.Resolve(
                null,
                null,
                null,
                out var resolvedRun));
        Assert.Null(resolvedRun);
    }

    private static PlaybackIsoBmffTrackExtendsBox ParseTrex(
        uint trackId,
        uint sampleDescriptionIndex,
        uint duration,
        uint size,
        uint flags)
    {
        var box = CreateFullBox(0x74726578, 0, 20);
        WriteUInt32(box, 12, trackId);
        WriteUInt32(box, 16, sampleDescriptionIndex);
        WriteUInt32(box, 20, duration);
        WriteUInt32(box, 24, size);
        WriteUInt32(box, 28, flags);
        Assert.Equal(
            PlaybackIsoBmffTrackExtendsBoxParseResult.Parsed,
            PlaybackIsoBmffTrackExtendsBoxParser.Parse(box, out var parsed));
        return Assert.IsType<PlaybackIsoBmffTrackExtendsBox>(parsed);
    }

    private static PlaybackIsoBmffTrackFragmentHeaderBox ParseTfhd(
        uint trackId,
        uint? sampleDescriptionIndex,
        uint? duration,
        uint? size,
        uint? flags)
    {
        uint fullBoxFlags = 0;
        fullBoxFlags |= sampleDescriptionIndex.HasValue ? 0x000002U : 0;
        fullBoxFlags |= duration.HasValue ? 0x000008U : 0;
        fullBoxFlags |= size.HasValue ? 0x000010U : 0;
        fullBoxFlags |= flags.HasValue ? 0x000020U : 0;
        var optionalCount =
            (sampleDescriptionIndex.HasValue ? 1 : 0) +
            (duration.HasValue ? 1 : 0) +
            (size.HasValue ? 1 : 0) +
            (flags.HasValue ? 1 : 0);
        var box = CreateFullBox(0x74666864, fullBoxFlags, 4 + (optionalCount * 4));
        WriteUInt32(box, 12, trackId);
        var cursor = 16;
        WriteOptional(box, ref cursor, sampleDescriptionIndex);
        WriteOptional(box, ref cursor, duration);
        WriteOptional(box, ref cursor, size);
        WriteOptional(box, ref cursor, flags);
        Assert.Equal(
            PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.Parsed,
            PlaybackIsoBmffTrackFragmentHeaderBoxParser.Parse(
                box,
                out var parsed));
        return Assert.IsType<PlaybackIsoBmffTrackFragmentHeaderBox>(parsed);
    }

    private static PlaybackIsoBmffTrackRunBox ParseTrun(
        uint? firstSampleFlags,
        params PlaybackIsoBmffTrackRunEntry[] entries)
    {
        var hasDuration = entries.Any(entry => entry.SampleDuration.HasValue);
        var hasSize = entries.Any(entry => entry.SampleSize.HasValue);
        var hasFlags = entries.Any(entry => entry.SampleFlags.HasValue);
        var hasComposition = entries.Any(
            entry => entry.SampleCompositionTimeOffset.HasValue);
        uint flags = firstSampleFlags.HasValue ? 0x000004U : 0;
        flags |= hasDuration ? 0x000100U : 0;
        flags |= hasSize ? 0x000200U : 0;
        flags |= hasFlags ? 0x000400U : 0;
        flags |= hasComposition ? 0x000800U : 0;
        var perEntrySize =
            (hasDuration ? 4 : 0) +
            (hasSize ? 4 : 0) +
            (hasFlags ? 4 : 0) +
            (hasComposition ? 4 : 0);
        var box = CreateFullBox(
            0x7472756E,
            flags,
            4 + (firstSampleFlags.HasValue ? 4 : 0) +
                (entries.Length * perEntrySize));
        WriteUInt32(box, 12, (uint)entries.Length);
        var cursor = 16;
        WriteOptional(box, ref cursor, firstSampleFlags);
        foreach (var entry in entries)
        {
            WriteOptional(box, ref cursor, hasDuration ? entry.SampleDuration ?? 0 : null);
            WriteOptional(box, ref cursor, hasSize ? entry.SampleSize ?? 0 : null);
            WriteOptional(box, ref cursor, hasFlags ? entry.SampleFlags ?? 0 : null);
            WriteOptional(
                box,
                ref cursor,
                hasComposition
                    ? (uint)(entry.SampleCompositionTimeOffset ?? 0)
                    : null);
        }

        Assert.Equal(
            PlaybackIsoBmffTrackRunBoxParseResult.Parsed,
            PlaybackIsoBmffTrackRunBoxParser.Parse(box, out var parsed));
        return Assert.IsType<PlaybackIsoBmffTrackRunBox>(parsed);
    }

    private static byte[] CreateFullBox(
        uint type,
        uint flags,
        int bodySize)
    {
        var box = new byte[12 + bodySize];
        WriteUInt32(box, 0, (uint)box.Length);
        WriteUInt32(box, 4, type);
        box[9] = (byte)(flags >> 16);
        box[10] = (byte)(flags >> 8);
        box[11] = (byte)flags;
        return box;
    }

    private static void WriteOptional(
        byte[] box,
        ref int cursor,
        uint? value)
    {
        if (!value.HasValue)
        {
            return;
        }

        WriteUInt32(box, cursor, value.Value);
        cursor += 4;
    }

    private static void WriteUInt32(byte[] box, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(offset, 4), value);
}
