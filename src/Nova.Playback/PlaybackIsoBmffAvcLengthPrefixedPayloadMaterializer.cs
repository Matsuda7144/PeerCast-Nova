namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAvcLengthPrefixedPayloadMaterializeResult
{
    Materialized,
    InvalidConfiguration,
    MissingPlan,
    InvalidPlan,
    PayloadLimitExceeded,
}

public sealed record PlaybackIsoBmffAvcPayloadMaterializerOptions
{
    public int MaximumPayloadBytes { get; init; } =
        PlaybackBoundedDemuxOptions.DefaultMaximumPacketPayloadBytes;
}

public sealed class PlaybackIsoBmffAvcLengthPrefixedPayload
{
    private readonly byte[] _payload;

    internal PlaybackIsoBmffAvcLengthPrefixedPayload(
        PlaybackIsoBmffAvcParameterSetPrefixPlan plan,
        byte[] payload)
    {
        Plan = plan;
        _payload = payload;
    }

    public PlaybackIsoBmffAvcParameterSetPrefixPlan Plan { get; }

    public byte NalUnitLengthBytes => Plan.NalUnitLengthBytes;

    public ReadOnlyMemory<byte> Payload => _payload;
}

public static class PlaybackIsoBmffAvcLengthPrefixedPayloadMaterializer
{
    public static PlaybackIsoBmffAvcLengthPrefixedPayloadMaterializeResult
        Materialize(
            PlaybackIsoBmffAvcParameterSetPrefixPlan? plan,
            out PlaybackIsoBmffAvcLengthPrefixedPayload? payload,
            PlaybackIsoBmffAvcPayloadMaterializerOptions? options = null)
    {
        payload = null;
        options ??= new PlaybackIsoBmffAvcPayloadMaterializerOptions();
        if (options.MaximumPayloadBytes is < 1 or
            > PlaybackBoundedDemuxOptions.MaximumPacketPayloadBytesHardLimit)
        {
            return PlaybackIsoBmffAvcLengthPrefixedPayloadMaterializeResult
                .InvalidConfiguration;
        }

        if (plan is null)
        {
            return PlaybackIsoBmffAvcLengthPrefixedPayloadMaterializeResult
                .MissingPlan;
        }

        var lengthBytes = plan.NalUnitLengthBytes;
        if (lengthBytes is < 1 or > 4 || plan.NalUnits.Count == 0)
        {
            return PlaybackIsoBmffAvcLengthPrefixedPayloadMaterializeResult
                .InvalidPlan;
        }

        long projectedBytes = 0;
        foreach (var unit in plan.NalUnits)
        {
            if (unit.Payload.IsEmpty ||
                !LengthIsRepresentable(unit.Payload.Length, lengthBytes))
            {
                return PlaybackIsoBmffAvcLengthPrefixedPayloadMaterializeResult
                    .InvalidPlan;
            }

            projectedBytes += lengthBytes + (long)unit.Payload.Length;
        }

        if (projectedBytes != plan.EncodedPayloadBytes)
        {
            return PlaybackIsoBmffAvcLengthPrefixedPayloadMaterializeResult
                .InvalidPlan;
        }

        if (projectedBytes > options.MaximumPayloadBytes)
        {
            return PlaybackIsoBmffAvcLengthPrefixedPayloadMaterializeResult
                .PayloadLimitExceeded;
        }

        var bytes = new byte[checked((int)projectedBytes)];
        var offset = 0;
        foreach (var unit in plan.NalUnits)
        {
            var length = checked((uint)unit.Payload.Length);
            for (var index = lengthBytes - 1; index >= 0; index--)
            {
                bytes[offset + index] = (byte)length;
                length >>= 8;
            }

            offset += lengthBytes;
            unit.Payload.Span.CopyTo(bytes.AsSpan(offset));
            offset += unit.Payload.Length;
        }

        payload = new(plan, bytes);
        return PlaybackIsoBmffAvcLengthPrefixedPayloadMaterializeResult
            .Materialized;
    }

    private static bool LengthIsRepresentable(int length, byte lengthBytes) =>
        lengthBytes == 4 || (uint)length < (1u << (lengthBytes * 8));
}
