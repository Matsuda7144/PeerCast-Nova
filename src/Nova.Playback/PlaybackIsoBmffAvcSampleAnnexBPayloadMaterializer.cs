namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializeResult
{
    Materialized,
    InvalidConfiguration,
    MissingClassification,
    InvalidClassification,
    PayloadLimitExceeded,
}

public sealed class PlaybackIsoBmffAvcSampleAnnexBPayload
{
    private readonly byte[] _payload;

    internal PlaybackIsoBmffAvcSampleAnnexBPayload(
        PlaybackIsoBmffAvcNalUnitClassification classification,
        int startCodeLength,
        byte[] payload)
    {
        Classification = classification;
        StartCodeLength = startCodeLength;
        _payload = payload;
    }

    public PlaybackIsoBmffAvcNalUnitClassification Classification { get; }

    public int StartCodeLength { get; }

    public ReadOnlyMemory<byte> Payload => _payload;
}

public static class PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializer
{
    private static ReadOnlySpan<byte> StartCode => [0, 0, 0, 1];

    public static PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializeResult
        Materialize(
            PlaybackIsoBmffAvcNalUnitClassification? classification,
            out PlaybackIsoBmffAvcSampleAnnexBPayload? payload,
            PlaybackIsoBmffAvcPayloadMaterializerOptions? options = null)
    {
        payload = null;
        options ??= new PlaybackIsoBmffAvcPayloadMaterializerOptions();
        if (options.MaximumPayloadBytes is < 1 or
            > PlaybackBoundedDemuxOptions.MaximumPacketPayloadBytesHardLimit)
        {
            return PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializeResult
                .InvalidConfiguration;
        }

        if (classification is null)
        {
            return PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializeResult
                .MissingClassification;
        }

        if (classification.NalUnits.Count == 0)
        {
            return PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializeResult
                .InvalidClassification;
        }

        long projectedBytes = 0;
        foreach (var unit in classification.NalUnits)
        {
            if (unit.NalUnit.Payload.IsEmpty)
            {
                return PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializeResult
                    .InvalidClassification;
            }

            projectedBytes +=
                StartCode.Length + (long)unit.NalUnit.Payload.Length;
        }

        if (projectedBytes > options.MaximumPayloadBytes)
        {
            return PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializeResult
                .PayloadLimitExceeded;
        }

        var bytes = new byte[checked((int)projectedBytes)];
        var offset = 0;
        foreach (var unit in classification.NalUnits)
        {
            StartCode.CopyTo(bytes.AsSpan(offset));
            offset += StartCode.Length;
            unit.NalUnit.Payload.Span.CopyTo(bytes.AsSpan(offset));
            offset += unit.NalUnit.Payload.Length;
        }

        payload = new(classification, StartCode.Length, bytes);
        return PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializeResult
            .Materialized;
    }
}
