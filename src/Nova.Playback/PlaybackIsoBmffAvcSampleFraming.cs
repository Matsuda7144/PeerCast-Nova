using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAvcSampleFramingResult
{
    Framed,
    InvalidConfiguration,
    MissingCandidate,
    MissingDecoderConfiguration,
    UnsupportedDecoderConfiguration,
    SampleDescriptionMismatch,
    NalUnitLimitExceeded,
    InvalidPayload,
}

public sealed record PlaybackIsoBmffAvcSampleFramingOptions
{
    public const int DefaultMaximumNalUnitCount = 4_096;
    public const int MaximumNalUnitCountHardLimit = 65_536;

    public int MaximumNalUnitCount { get; init; } =
        DefaultMaximumNalUnitCount;
}

public sealed class PlaybackIsoBmffAvcNalUnit
{
    internal PlaybackIsoBmffAvcNalUnit(
        int payloadOffset,
        ReadOnlyMemory<byte> payload)
    {
        PayloadOffset = payloadOffset;
        Payload = payload;
    }

    public int PayloadOffset { get; }

    public int Length => Payload.Length;

    public ReadOnlyMemory<byte> Payload { get; }
}

public sealed class PlaybackIsoBmffAvcSampleFrame
{
    internal PlaybackIsoBmffAvcSampleFrame(
        PlaybackIsoBmffEditedSamplePacketCandidate candidate,
        PlaybackIsoBmffAvcDecoderConfiguration decoderConfiguration,
        int nalUnitPayloadBytes,
        ReadOnlyCollection<PlaybackIsoBmffAvcNalUnit> nalUnits)
    {
        Candidate = candidate;
        DecoderConfiguration = decoderConfiguration;
        NalUnitPayloadBytes = nalUnitPayloadBytes;
        NalUnits = nalUnits;
    }

    public PlaybackIsoBmffEditedSamplePacketCandidate Candidate { get; }

    public PlaybackIsoBmffAvcDecoderConfiguration DecoderConfiguration { get; }

    public byte NalUnitLengthBytes => DecoderConfiguration.NalUnitLengthBytes;

    public int NalUnitPayloadBytes { get; }

    public IReadOnlyList<PlaybackIsoBmffAvcNalUnit> NalUnits { get; }
}

public static class PlaybackIsoBmffAvcSampleFraming
{
    public static PlaybackIsoBmffAvcSampleFramingResult Frame(
        PlaybackIsoBmffEditedSamplePacketCandidate? candidate,
        PlaybackIsoBmffAvcDecoderConfiguration? decoderConfiguration,
        out PlaybackIsoBmffAvcSampleFrame? frame,
        PlaybackIsoBmffAvcSampleFramingOptions? options = null)
    {
        frame = null;
        options ??= new PlaybackIsoBmffAvcSampleFramingOptions();
        if (options.MaximumNalUnitCount is < 1 or
            > PlaybackIsoBmffAvcSampleFramingOptions
                .MaximumNalUnitCountHardLimit)
        {
            return PlaybackIsoBmffAvcSampleFramingResult.InvalidConfiguration;
        }

        if (candidate is null)
        {
            return PlaybackIsoBmffAvcSampleFramingResult.MissingCandidate;
        }

        if (decoderConfiguration is null)
        {
            return PlaybackIsoBmffAvcSampleFramingResult
                .MissingDecoderConfiguration;
        }

        var selection = decoderConfiguration.VisualEntry.Selection;
        if (selection.CodecCandidate is not
                PlaybackIsoBmffSampleEntryCodecCandidate.Avc1 and not
                PlaybackIsoBmffSampleEntryCodecCandidate.Avc3 ||
            selection.MediaKind != PlaybackIsoBmffHandlerKind.Video ||
            decoderConfiguration.NalUnitLengthBytes is < 1 or > 4)
        {
            return PlaybackIsoBmffAvcSampleFramingResult
                .UnsupportedDecoderConfiguration;
        }

        if (candidate.OwnedPayload.MediaData.EditedRange
                .SampleDescriptionIndex != selection.SampleDescriptionIndex)
        {
            return PlaybackIsoBmffAvcSampleFramingResult
                .SampleDescriptionMismatch;
        }

        var sample = candidate.Payload;
        var payload = sample.Span;
        var lengthBytes = decoderConfiguration.NalUnitLengthBytes;
        var units = new List<PlaybackIsoBmffAvcNalUnit>();
        var payloadBytes = 0;
        var offset = 0;
        while (offset != payload.Length)
        {
            if (units.Count >= options.MaximumNalUnitCount)
            {
                return PlaybackIsoBmffAvcSampleFramingResult
                    .NalUnitLimitExceeded;
            }

            if (payload.Length - offset < lengthBytes)
            {
                return PlaybackIsoBmffAvcSampleFramingResult.InvalidPayload;
            }

            uint nalUnitLength = 0;
            for (var index = 0; index < lengthBytes; index++)
            {
                nalUnitLength = (nalUnitLength << 8) | payload[offset + index];
            }

            offset += lengthBytes;
            if (nalUnitLength == 0 ||
                nalUnitLength > int.MaxValue ||
                nalUnitLength > payload.Length - offset)
            {
                return PlaybackIsoBmffAvcSampleFramingResult.InvalidPayload;
            }

            var length = checked((int)nalUnitLength);
            units.Add(new(offset, sample.Slice(offset, length)));
            payloadBytes = checked(payloadBytes + length);
            offset += length;
        }

        if (units.Count == 0)
        {
            return PlaybackIsoBmffAvcSampleFramingResult.InvalidPayload;
        }

        frame = new(
            candidate,
            decoderConfiguration,
            payloadBytes,
            units.AsReadOnly());
        return PlaybackIsoBmffAvcSampleFramingResult.Framed;
    }
}
