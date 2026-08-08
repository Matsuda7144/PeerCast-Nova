namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAvcAnnexBPayloadMaterializeResult
{
    Materialized,
    InvalidConfiguration,
    MissingPlan,
    InvalidPlan,
    PayloadLimitExceeded,
}

public sealed class PlaybackIsoBmffAvcAnnexBPayload
{
    private readonly byte[] _payload;

    internal PlaybackIsoBmffAvcAnnexBPayload(
        PlaybackIsoBmffAvcParameterSetPrefixPlan plan,
        int startCodeLength,
        byte[] payload)
    {
        Plan = plan;
        StartCodeLength = startCodeLength;
        _payload = payload;
    }

    public PlaybackIsoBmffAvcParameterSetPrefixPlan Plan { get; }

    public int StartCodeLength { get; }

    public ReadOnlyMemory<byte> Payload => _payload;
}

public static class PlaybackIsoBmffAvcAnnexBPayloadMaterializer
{
    private static ReadOnlySpan<byte> StartCode => [0, 0, 0, 1];

    public static PlaybackIsoBmffAvcAnnexBPayloadMaterializeResult Materialize(
        PlaybackIsoBmffAvcParameterSetPrefixPlan? plan,
        out PlaybackIsoBmffAvcAnnexBPayload? payload,
        PlaybackIsoBmffAvcPayloadMaterializerOptions? options = null)
    {
        payload = null;
        options ??= new PlaybackIsoBmffAvcPayloadMaterializerOptions();
        if (options.MaximumPayloadBytes is < 1 or
            > PlaybackBoundedDemuxOptions.MaximumPacketPayloadBytesHardLimit)
        {
            return PlaybackIsoBmffAvcAnnexBPayloadMaterializeResult
                .InvalidConfiguration;
        }

        if (plan is null)
        {
            return PlaybackIsoBmffAvcAnnexBPayloadMaterializeResult.MissingPlan;
        }

        if (plan.NalUnits.Count == 0)
        {
            return PlaybackIsoBmffAvcAnnexBPayloadMaterializeResult.InvalidPlan;
        }

        long projectedBytes = 0;
        foreach (var unit in plan.NalUnits)
        {
            if (unit.Payload.IsEmpty)
            {
                return PlaybackIsoBmffAvcAnnexBPayloadMaterializeResult
                    .InvalidPlan;
            }

            projectedBytes += StartCode.Length + (long)unit.Payload.Length;
        }

        if (projectedBytes > options.MaximumPayloadBytes)
        {
            return PlaybackIsoBmffAvcAnnexBPayloadMaterializeResult
                .PayloadLimitExceeded;
        }

        var bytes = new byte[checked((int)projectedBytes)];
        var offset = 0;
        foreach (var unit in plan.NalUnits)
        {
            StartCode.CopyTo(bytes.AsSpan(offset));
            offset += StartCode.Length;
            unit.Payload.Span.CopyTo(bytes.AsSpan(offset));
            offset += unit.Payload.Length;
        }

        payload = new(plan, StartCode.Length, bytes);
        return PlaybackIsoBmffAvcAnnexBPayloadMaterializeResult.Materialized;
    }
}
