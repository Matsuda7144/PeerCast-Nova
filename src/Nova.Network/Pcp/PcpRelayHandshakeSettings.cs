using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

internal sealed record PcpRelayHandshakeSettings(
    int MaxHttpHeaderBytes,
    int MaxHandshakeAtomBytes,
    int MaxAtomsBeforeTerminal,
    string UserAgent,
    uint ServantVersion)
{
    public void Validate()
    {
        if (MaxHttpHeaderBytes < 64)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxHttpHeaderBytes)} is too small.");
        }

        if (MaxHandshakeAtomBytes < PcpAtomCodec.HeaderSize)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxHandshakeAtomBytes)} is too small.");
        }

        if (MaxAtomsBeforeTerminal < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxAtomsBeforeTerminal)} must be positive.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(UserAgent);
    }
}
