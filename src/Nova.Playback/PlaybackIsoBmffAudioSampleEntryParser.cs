using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAudioSampleEntryParseResult
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

public sealed record PlaybackIsoBmffAudioSampleEntryParserOptions
{
    public const int DefaultMaximumChildBoxCount = 64;
    public const int MaximumChildBoxCountHardLimit = 256;

    public int MaximumChildBoxCount { get; init; } =
        DefaultMaximumChildBoxCount;
}

public sealed class PlaybackIsoBmffAudioSampleEntry
{
    internal PlaybackIsoBmffAudioSampleEntry(
        PlaybackIsoBmffEditedSampleDescriptionSelection selection,
        ushort dataReferenceIndex,
        ushort channelCount,
        ushort sampleSizeBits,
        uint sampleRateFixedPoint,
        ReadOnlyCollection<PlaybackIsoBmffBoxLocation> children)
    {
        Selection = selection;
        DataReferenceIndex = dataReferenceIndex;
        ChannelCount = channelCount;
        SampleSizeBits = sampleSizeBits;
        SampleRateFixedPoint = sampleRateFixedPoint;
        Children = children;
    }

    public PlaybackIsoBmffEditedSampleDescriptionSelection Selection { get; }

    public ushort DataReferenceIndex { get; }

    public ushort ChannelCount { get; }

    public ushort SampleSizeBits { get; }

    public uint SampleRateFixedPoint { get; }

    public ushort IntegerSampleRate =>
        checked((ushort)(SampleRateFixedPoint >> 16));

    public ushort FractionalSampleRate =>
        checked((ushort)(SampleRateFixedPoint & ushort.MaxValue));

    public IReadOnlyList<PlaybackIsoBmffBoxLocation> Children { get; }
}

public static class PlaybackIsoBmffAudioSampleEntryParser
{
    private const int FixedPayloadLength = 28;

    public static PlaybackIsoBmffAudioSampleEntryParseResult Parse(
        ReadOnlySpan<byte> sampleDescriptionBox,
        PlaybackIsoBmffEditedSampleDescriptionSelection? selection,
        out PlaybackIsoBmffAudioSampleEntry? audioEntry,
        PlaybackIsoBmffAudioSampleEntryParserOptions? options = null)
    {
        audioEntry = null;
        options ??= new PlaybackIsoBmffAudioSampleEntryParserOptions();
        if (options.MaximumChildBoxCount is < 0 or
            > PlaybackIsoBmffAudioSampleEntryParserOptions
                .MaximumChildBoxCountHardLimit)
        {
            return PlaybackIsoBmffAudioSampleEntryParseResult
                .InvalidConfiguration;
        }

        if (selection is null)
        {
            return PlaybackIsoBmffAudioSampleEntryParseResult.MissingSelection;
        }

        if (selection.CodecCandidate !=
                PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a ||
            selection.MediaKind != PlaybackIsoBmffHandlerKind.Audio)
        {
            return PlaybackIsoBmffAudioSampleEntryParseResult
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
            return PlaybackIsoBmffAudioSampleEntryParseResult
                .EntryLocationMismatch;
        }

        var payload = sampleDescriptionBox.Slice(
            actual.PayloadOffset,
            actual.PayloadLength);
        if (payload.Length < FixedPayloadLength ||
            ContainsNonzero(payload[..6]) ||
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(6, 2)) == 0 ||
            ContainsNonzero(payload.Slice(8, 8)) ||
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(16, 2)) == 0 ||
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(18, 2)) == 0 ||
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(20, 2)) != 0 ||
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(22, 2)) != 0 ||
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(24, 2)) == 0)
        {
            return PlaybackIsoBmffAudioSampleEntryParseResult
                .InvalidFixedFields;
        }

        var childrenPayload = payload[FixedPayloadLength..];
        var children = new List<PlaybackIsoBmffBoxLocation>();
        var childOffset = 0;
        while (childOffset != childrenPayload.Length)
        {
            if (children.Count >= options.MaximumChildBoxCount)
            {
                return PlaybackIsoBmffAudioSampleEntryParseResult
                    .ChildLimitExceeded;
            }

            if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                    childrenPayload,
                    childOffset,
                    out var child) !=
                    PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
                child is null)
            {
                return PlaybackIsoBmffAudioSampleEntryParseResult
                    .InvalidChildSequence;
            }

            children.Add(new(
                child.Header,
                actual.PayloadOffset + FixedPayloadLength + child.Offset,
                child.BoxLength));
            childOffset = child.NextOffset;
        }

        audioEntry = new(
            selection,
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(6, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(16, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(18, 2)),
            BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(24, 4)),
            children.AsReadOnly());
        return PlaybackIsoBmffAudioSampleEntryParseResult.Parsed;
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
