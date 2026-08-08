using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAvcNalUnitClassificationResult
{
    Classified,
    MissingFrame,
    InvalidNalUnit,
}

public sealed class PlaybackIsoBmffAvcClassifiedNalUnit
{
    internal PlaybackIsoBmffAvcClassifiedNalUnit(
        PlaybackIsoBmffAvcNalUnit nalUnit,
        byte nalReferenceIdc,
        byte nalUnitType)
    {
        NalUnit = nalUnit;
        NalReferenceIdc = nalReferenceIdc;
        NalUnitType = nalUnitType;
    }

    public PlaybackIsoBmffAvcNalUnit NalUnit { get; }

    public byte NalReferenceIdc { get; }

    public byte NalUnitType { get; }

    public bool IsVcl => NalUnitType is >= 1 and <= 5 or >= 19 and <= 21;

    public bool IsIdr => NalUnitType == 5;

    public bool IsParameterSet => NalUnitType is 7 or 8 or 13 or 15;
}

public sealed class PlaybackIsoBmffAvcNalUnitClassification
{
    internal PlaybackIsoBmffAvcNalUnitClassification(
        PlaybackIsoBmffAvcSampleFrame frame,
        bool containsVcl,
        bool containsIdr,
        bool containsParameterSet,
        ReadOnlyCollection<PlaybackIsoBmffAvcClassifiedNalUnit> nalUnits)
    {
        Frame = frame;
        ContainsVcl = containsVcl;
        ContainsIdr = containsIdr;
        ContainsParameterSet = containsParameterSet;
        NalUnits = nalUnits;
    }

    public PlaybackIsoBmffAvcSampleFrame Frame { get; }

    public bool ContainsVcl { get; }

    public bool ContainsIdr { get; }

    public bool ContainsParameterSet { get; }

    public IReadOnlyList<PlaybackIsoBmffAvcClassifiedNalUnit> NalUnits { get; }
}

public static class PlaybackIsoBmffAvcNalUnitClassifier
{
    public static PlaybackIsoBmffAvcNalUnitClassificationResult Classify(
        PlaybackIsoBmffAvcSampleFrame? frame,
        out PlaybackIsoBmffAvcNalUnitClassification? classification)
    {
        classification = null;
        if (frame is null)
        {
            return PlaybackIsoBmffAvcNalUnitClassificationResult.MissingFrame;
        }

        var units = new List<PlaybackIsoBmffAvcClassifiedNalUnit>(
            frame.NalUnits.Count);
        var containsVcl = false;
        var containsIdr = false;
        var containsParameterSet = false;
        foreach (var unit in frame.NalUnits)
        {
            if (unit.Payload.IsEmpty)
            {
                return PlaybackIsoBmffAvcNalUnitClassificationResult
                    .InvalidNalUnit;
            }

            var header = unit.Payload.Span[0];
            var nalUnitType = (byte)(header & 0x1F);
            if ((header & 0x80) != 0 || nalUnitType == 0)
            {
                return PlaybackIsoBmffAvcNalUnitClassificationResult
                    .InvalidNalUnit;
            }

            var classified = new PlaybackIsoBmffAvcClassifiedNalUnit(
                unit,
                (byte)((header >> 5) & 0x03),
                nalUnitType);
            containsVcl |= classified.IsVcl;
            containsIdr |= classified.IsIdr;
            containsParameterSet |= classified.IsParameterSet;
            units.Add(classified);
        }

        classification = new(
            frame,
            containsVcl,
            containsIdr,
            containsParameterSet,
            units.AsReadOnly());
        return PlaybackIsoBmffAvcNalUnitClassificationResult.Classified;
    }
}
