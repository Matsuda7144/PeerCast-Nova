using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

public sealed record PcpPostHandshakeObservationOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(2);

    public int MaxTopLevelAtoms { get; init; } = 4;

    public int MaxAtomBytes { get; init; } = 256 * 1024;

    public void Validate()
    {
        if (Timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(Timeout)} must be positive.");
        }

        if (MaxTopLevelAtoms < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxTopLevelAtoms)} must be positive.");
        }

        if (MaxAtomBytes < PcpAtomCodec.HeaderSize)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxAtomBytes)} must fit at least one atom header.");
        }
    }
}
