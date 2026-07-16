using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

public sealed record PcpCompatibilityOptions
{
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public int MaxHttpHeaderBytes { get; init; } = 32 * 1024;

    public int MaxHandshakeAtomBytes { get; init; } = 1024 * 1024;

    public int MaxAtomsBeforeTerminal { get; init; } = 64;

    public string UserAgent { get; init; } = "PeerCastNova/0.0.6c2-fix1";

    public uint ServantVersion { get; init; } = PcpHeloCodec.LegacyServantVersion;

    public void Validate()
    {
        if (HandshakeTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(HandshakeTimeout)} must be positive.");
        }

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
