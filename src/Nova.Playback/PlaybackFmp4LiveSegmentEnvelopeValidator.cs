namespace PeerCast.Nova.Playback;

public enum PlaybackFmp4LiveSegmentKind
{
    Initialization,
    Media,
}

public enum PlaybackFmp4LiveSegmentEnvelopeValidationResult
{
    Accepted,
    EmptySegment,
    SegmentTooLarge,
    InvalidBox,
    ExtendsToEnd,
    InvalidShape,
}

public sealed class PlaybackFmp4LiveSegmentEnvelope
{
    internal PlaybackFmp4LiveSegmentEnvelope(
        PlaybackFmp4LiveSegmentKind kind,
        int segmentLength,
        int firstBoxLength,
        int secondBoxLength)
    {
        Kind = kind;
        SegmentLength = segmentLength;
        FirstBoxLength = firstBoxLength;
        SecondBoxLength = secondBoxLength;
    }

    public PlaybackFmp4LiveSegmentKind Kind { get; }

    public int SegmentLength { get; }

    public int FirstBoxLength { get; }

    public int SecondBoxLength { get; }
}

public static class PlaybackFmp4LiveSegmentEnvelopeValidator
{
    private const uint FileTypeBoxType = 0x66747970;
    private const uint MovieBoxType = 0x6d6f6f76;
    private const uint MovieFragmentBoxType = 0x6d6f6f66;
    private const uint MediaDataBoxType = 0x6d646174;

    public static PlaybackFmp4LiveSegmentEnvelopeValidationResult Validate(
        ReadOnlySpan<byte> segment,
        int maxSegmentBytes,
        out PlaybackFmp4LiveSegmentEnvelope? envelope)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSegmentBytes);

        envelope = null;
        if (segment.IsEmpty)
        {
            return PlaybackFmp4LiveSegmentEnvelopeValidationResult.EmptySegment;
        }

        if (segment.Length > maxSegmentBytes)
        {
            return PlaybackFmp4LiveSegmentEnvelopeValidationResult.SegmentTooLarge;
        }

        var firstResult = PlaybackIsoBmffBoxSequenceReader.ReadNext(segment, 0, out var first);
        if (firstResult != PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable || first is null)
        {
            return PlaybackFmp4LiveSegmentEnvelopeValidationResult.InvalidBox;
        }

        if (first.Header.ExtendsToEnd)
        {
            return PlaybackFmp4LiveSegmentEnvelopeValidationResult.ExtendsToEnd;
        }

        var secondResult = PlaybackIsoBmffBoxSequenceReader.ReadNext(
            segment,
            first.NextOffset,
            out var second);
        if (secondResult == PlaybackIsoBmffBoxSequenceReadResult.EndOfContainer)
        {
            return PlaybackFmp4LiveSegmentEnvelopeValidationResult.InvalidShape;
        }

        if (secondResult != PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable || second is null)
        {
            return PlaybackFmp4LiveSegmentEnvelopeValidationResult.InvalidBox;
        }

        if (second.Header.ExtendsToEnd)
        {
            return PlaybackFmp4LiveSegmentEnvelopeValidationResult.ExtendsToEnd;
        }

        var trailingResult = PlaybackIsoBmffBoxSequenceReader.ReadNext(
            segment,
            second.NextOffset,
            out _);
        if (trailingResult == PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable)
        {
            return PlaybackFmp4LiveSegmentEnvelopeValidationResult.InvalidShape;
        }

        if (trailingResult != PlaybackIsoBmffBoxSequenceReadResult.EndOfContainer)
        {
            return PlaybackFmp4LiveSegmentEnvelopeValidationResult.InvalidBox;
        }

        if (first.Header.Type is not FileTypeBoxType and not MovieFragmentBoxType)
        {
            return PlaybackFmp4LiveSegmentEnvelopeValidationResult.InvalidShape;
        }

        if ((first.Header.Type == FileTypeBoxType && second.Header.Type != MovieBoxType) ||
            (first.Header.Type == MovieFragmentBoxType && second.Header.Type != MediaDataBoxType))
        {
            return PlaybackFmp4LiveSegmentEnvelopeValidationResult.InvalidShape;
        }

        var kind = first.Header.Type == FileTypeBoxType
            ? PlaybackFmp4LiveSegmentKind.Initialization
            : PlaybackFmp4LiveSegmentKind.Media;
        envelope = new PlaybackFmp4LiveSegmentEnvelope(
            kind,
            segment.Length,
            first.BoxLength,
            second.BoxLength);

        return PlaybackFmp4LiveSegmentEnvelopeValidationResult.Accepted;
    }
}
