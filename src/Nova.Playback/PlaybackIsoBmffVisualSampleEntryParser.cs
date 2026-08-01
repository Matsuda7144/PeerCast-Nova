using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffVisualSampleEntryParseResult
{
    Parsed,
    InvalidConfiguration,
    MissingSelection,
    UnsupportedSelection,
    EntryLocationMismatch,
    InvalidFixedFields,
    ChildLimitExceeded,
    InvalidChildSequence,
}

public sealed record PlaybackIsoBmffVisualSampleEntryParserOptions
{
    public const int DefaultMaximumChildBoxCount = 64;
    public const int MaximumChildBoxCountHardLimit = 256;

    public int MaximumChildBoxCount { get; init; } =
        DefaultMaximumChildBoxCount;
}

public sealed class PlaybackIsoBmffVisualSampleEntry
{
    private readonly byte[] _compressorName;

    internal PlaybackIsoBmffVisualSampleEntry(
        PlaybackIsoBmffEditedSampleDescriptionSelection selection,
        ushort dataReferenceIndex,
        ushort width,
        ushort height,
        uint horizontalResolutionFixedPoint,
        uint verticalResolutionFixedPoint,
        ushort frameCount,
        byte[] compressorName,
        ushort depth,
        ReadOnlyCollection<PlaybackIsoBmffBoxLocation> children)
    {
        Selection = selection;
        DataReferenceIndex = dataReferenceIndex;
        Width = width;
        Height = height;
        HorizontalResolutionFixedPoint = horizontalResolutionFixedPoint;
        VerticalResolutionFixedPoint = verticalResolutionFixedPoint;
        FrameCount = frameCount;
        _compressorName = compressorName;
        Depth = depth;
        Children = children;
    }

    public PlaybackIsoBmffEditedSampleDescriptionSelection Selection { get; }

    public ushort DataReferenceIndex { get; }

    public ushort Width { get; }

    public ushort Height { get; }

    public uint HorizontalResolutionFixedPoint { get; }

    public uint VerticalResolutionFixedPoint { get; }

    public ushort FrameCount { get; }

    public ReadOnlyMemory<byte> CompressorName => _compressorName;

    public ushort Depth { get; }

    public IReadOnlyList<PlaybackIsoBmffBoxLocation> Children { get; }
}

public static class PlaybackIsoBmffVisualSampleEntryParser
{
    private const int FixedPayloadLength = 78;

    public static PlaybackIsoBmffVisualSampleEntryParseResult Parse(
        ReadOnlySpan<byte> sampleDescriptionBox,
        PlaybackIsoBmffEditedSampleDescriptionSelection? selection,
        out PlaybackIsoBmffVisualSampleEntry? visualEntry,
        PlaybackIsoBmffVisualSampleEntryParserOptions? options = null)
    {
        visualEntry = null;
        options ??= new PlaybackIsoBmffVisualSampleEntryParserOptions();
        if (options.MaximumChildBoxCount is < 0 or
            > PlaybackIsoBmffVisualSampleEntryParserOptions
                .MaximumChildBoxCountHardLimit)
        {
            return PlaybackIsoBmffVisualSampleEntryParseResult
                .InvalidConfiguration;
        }

        if (selection is null)
        {
            return PlaybackIsoBmffVisualSampleEntryParseResult.MissingSelection;
        }

        if (selection.CodecCandidate is not
            PlaybackIsoBmffSampleEntryCodecCandidate.Avc1 and not
            PlaybackIsoBmffSampleEntryCodecCandidate.Avc3 ||
            selection.MediaKind != PlaybackIsoBmffHandlerKind.Video)
        {
            return PlaybackIsoBmffVisualSampleEntryParseResult
                .UnsupportedSelection;
        }

        var recorded = selection.Entry;
        if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                sampleDescriptionBox,
                recorded.Offset,
                out var actual) !=
                PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
            actual is null ||
            actual.Offset != recorded.Offset ||
            actual.BoxLength != recorded.BoxLength ||
            actual.Header.Type != recorded.Header.Type ||
            actual.Header.HeaderSize != recorded.Header.HeaderSize)
        {
            return PlaybackIsoBmffVisualSampleEntryParseResult
                .EntryLocationMismatch;
        }

        var payload = sampleDescriptionBox.Slice(
            actual.PayloadOffset,
            actual.PayloadLength);
        if (payload.Length < FixedPayloadLength ||
            ContainsNonzero(payload[..6]) ||
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(6, 2)) == 0 ||
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(8, 2)) != 0 ||
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(10, 2)) != 0 ||
            ContainsNonzero(payload.Slice(12, 12)) ||
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(24, 2)) == 0 ||
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(26, 2)) == 0 ||
            BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(36, 4)) != 0 ||
            payload[42] > 31 ||
            BinaryPrimitives.ReadInt16BigEndian(payload.Slice(76, 2)) != -1)
        {
            return PlaybackIsoBmffVisualSampleEntryParseResult
                .InvalidFixedFields;
        }

        var childrenPayload = payload[FixedPayloadLength..];
        var children = new List<PlaybackIsoBmffBoxLocation>();
        var childOffset = 0;
        while (childOffset != childrenPayload.Length)
        {
            if (children.Count >= options.MaximumChildBoxCount)
            {
                return PlaybackIsoBmffVisualSampleEntryParseResult
                    .ChildLimitExceeded;
            }

            if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                    childrenPayload,
                    childOffset,
                    out var child) !=
                    PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
                child is null)
            {
                return PlaybackIsoBmffVisualSampleEntryParseResult
                    .InvalidChildSequence;
            }

            children.Add(new(
                child.Header,
                actual.PayloadOffset + FixedPayloadLength + child.Offset,
                child.BoxLength));
            childOffset = child.NextOffset;
        }

        var compressorLength = payload[42];
        visualEntry = new(
            selection,
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(6, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(24, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(26, 2)),
            BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(28, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(32, 4)),
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(40, 2)),
            payload.Slice(43, compressorLength).ToArray(),
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(74, 2)),
            children.AsReadOnly());
        return PlaybackIsoBmffVisualSampleEntryParseResult.Parsed;
    }

    private static bool ContainsNonzero(ReadOnlySpan<byte> value)
    {
        foreach (var item in value)
        {
            if (item != 0)
            {
                return true;
            }
        }

        return false;
    }
}
