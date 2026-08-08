using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAvcParameterSetPrefixPlanResult
{
    Planned,
    InvalidConfiguration,
    MissingClassification,
    InvalidDecoderConfiguration,
    InvalidParameterSet,
    MissingRequiredParameterSet,
    ParameterSetLengthNotRepresentable,
    NalUnitLimitExceeded,
    PayloadLimitExceeded,
}

public enum PlaybackIsoBmffAvcPrefixedNalUnitSource
{
    ConfigurationSequence,
    ConfigurationPicture,
    ConfigurationSequenceExtension,
    Sample,
}

public sealed record PlaybackIsoBmffAvcParameterSetPrefixPlanOptions
{
    public int MaximumNalUnitCount { get; init; } =
        PlaybackIsoBmffAvcSampleFramingOptions.DefaultMaximumNalUnitCount;

    public int MaximumPayloadBytes { get; init; } =
        PlaybackBoundedDemuxOptions.DefaultMaximumPacketPayloadBytes;
}

public sealed class PlaybackIsoBmffAvcPrefixedNalUnit
{
    internal PlaybackIsoBmffAvcPrefixedNalUnit(
        PlaybackIsoBmffAvcPrefixedNalUnitSource source,
        ReadOnlyMemory<byte> payload)
    {
        Source = source;
        Payload = payload;
    }

    public PlaybackIsoBmffAvcPrefixedNalUnitSource Source { get; }

    public ReadOnlyMemory<byte> Payload { get; }
}

public sealed class PlaybackIsoBmffAvcParameterSetPrefixPlan
{
    internal PlaybackIsoBmffAvcParameterSetPrefixPlan(
        PlaybackIsoBmffAvcNalUnitClassification classification,
        int parameterSetCount,
        int encodedPayloadBytes,
        ReadOnlyCollection<PlaybackIsoBmffAvcPrefixedNalUnit> nalUnits)
    {
        Classification = classification;
        ParameterSetCount = parameterSetCount;
        EncodedPayloadBytes = encodedPayloadBytes;
        NalUnits = nalUnits;
    }

    public PlaybackIsoBmffAvcNalUnitClassification Classification { get; }

    public byte NalUnitLengthBytes =>
        Classification.Frame.NalUnitLengthBytes;

    public int ParameterSetCount { get; }

    public int EncodedPayloadBytes { get; }

    public IReadOnlyList<PlaybackIsoBmffAvcPrefixedNalUnit> NalUnits { get; }
}

public static class PlaybackIsoBmffAvcParameterSetPrefixPlanBuilder
{
    public static PlaybackIsoBmffAvcParameterSetPrefixPlanResult Build(
        PlaybackIsoBmffAvcNalUnitClassification? classification,
        out PlaybackIsoBmffAvcParameterSetPrefixPlan? plan,
        PlaybackIsoBmffAvcParameterSetPrefixPlanOptions? options = null)
    {
        plan = null;
        options ??= new PlaybackIsoBmffAvcParameterSetPrefixPlanOptions();
        if (options.MaximumNalUnitCount is < 1 or
                > PlaybackIsoBmffAvcSampleFramingOptions
                    .MaximumNalUnitCountHardLimit ||
            options.MaximumPayloadBytes is < 1 or
                > PlaybackBoundedDemuxOptions
                    .MaximumPacketPayloadBytesHardLimit)
        {
            return PlaybackIsoBmffAvcParameterSetPrefixPlanResult
                .InvalidConfiguration;
        }

        if (classification is null)
        {
            return PlaybackIsoBmffAvcParameterSetPrefixPlanResult
                .MissingClassification;
        }

        var frame = classification.Frame;
        var configuration = frame.DecoderConfiguration;
        if (configuration.NalUnitLengthBytes is < 1 or > 4)
        {
            return PlaybackIsoBmffAvcParameterSetPrefixPlanResult
                .InvalidDecoderConfiguration;
        }
        var outputCount = configuration.ParameterSets.Count +
            classification.NalUnits.Count;
        if (outputCount > options.MaximumNalUnitCount)
        {
            return PlaybackIsoBmffAvcParameterSetPrefixPlanResult
                .NalUnitLimitExceeded;
        }

        var units = new List<PlaybackIsoBmffAvcPrefixedNalUnit>(outputCount);
        long encodedPayloadBytes = 0;
        var hasSequence = false;
        var hasPicture = false;
        foreach (var parameterSet in configuration.ParameterSets)
        {
            var payload = parameterSet.Payload;
            if (payload.IsEmpty || !HasExpectedNalUnitType(parameterSet))
            {
                return PlaybackIsoBmffAvcParameterSetPrefixPlanResult
                    .InvalidParameterSet;
            }

            if (!LengthIsRepresentable(
                    payload.Length,
                    configuration.NalUnitLengthBytes))
            {
                return PlaybackIsoBmffAvcParameterSetPrefixPlanResult
                    .ParameterSetLengthNotRepresentable;
            }

            encodedPayloadBytes +=
                configuration.NalUnitLengthBytes + (long)payload.Length;
            units.Add(new(ToSource(parameterSet.Kind), payload));
            hasSequence |= parameterSet.Kind ==
                PlaybackIsoBmffAvcParameterSetKind.Sequence;
            hasPicture |= parameterSet.Kind ==
                PlaybackIsoBmffAvcParameterSetKind.Picture;
        }

        if (!hasSequence || !hasPicture)
        {
            return PlaybackIsoBmffAvcParameterSetPrefixPlanResult
                .MissingRequiredParameterSet;
        }

        foreach (var unit in classification.NalUnits)
        {
            encodedPayloadBytes +=
                configuration.NalUnitLengthBytes + (long)unit.NalUnit.Length;
            units.Add(new(
                PlaybackIsoBmffAvcPrefixedNalUnitSource.Sample,
                unit.NalUnit.Payload));
        }

        if (encodedPayloadBytes > options.MaximumPayloadBytes)
        {
            return PlaybackIsoBmffAvcParameterSetPrefixPlanResult
                .PayloadLimitExceeded;
        }

        plan = new(
            classification,
            configuration.ParameterSets.Count,
            checked((int)encodedPayloadBytes),
            units.AsReadOnly());
        return PlaybackIsoBmffAvcParameterSetPrefixPlanResult.Planned;
    }

    private static bool HasExpectedNalUnitType(
        PlaybackIsoBmffAvcParameterSet parameterSet)
    {
        var header = parameterSet.Payload.Span[0];
        if ((header & 0x80) != 0)
        {
            return false;
        }

        var type = header & 0x1F;
        return parameterSet.Kind switch
        {
            PlaybackIsoBmffAvcParameterSetKind.Sequence => type == 7,
            PlaybackIsoBmffAvcParameterSetKind.Picture => type == 8,
            PlaybackIsoBmffAvcParameterSetKind.SequenceExtension => type == 13,
            _ => false,
        };
    }

    private static bool LengthIsRepresentable(int length, byte lengthBytes) =>
        lengthBytes == 4 || (uint)length < (1u << (lengthBytes * 8));

    private static PlaybackIsoBmffAvcPrefixedNalUnitSource ToSource(
        PlaybackIsoBmffAvcParameterSetKind kind) => kind switch
        {
            PlaybackIsoBmffAvcParameterSetKind.Sequence =>
                PlaybackIsoBmffAvcPrefixedNalUnitSource.ConfigurationSequence,
            PlaybackIsoBmffAvcParameterSetKind.Picture =>
                PlaybackIsoBmffAvcPrefixedNalUnitSource.ConfigurationPicture,
            PlaybackIsoBmffAvcParameterSetKind.SequenceExtension =>
                PlaybackIsoBmffAvcPrefixedNalUnitSource
                    .ConfigurationSequenceExtension,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
}
