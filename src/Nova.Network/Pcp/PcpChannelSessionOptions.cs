using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Finite connection, handshake, reconnect, Pipe, and stream limits for one
/// stable local channel session.
/// </summary>
public sealed record PcpChannelSessionOptions
{
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public int MaxHttpHeaderBytes { get; init; } = 32 * 1024;

    public int MaxHandshakeAtomBytes { get; init; } = 1024 * 1024;

    public int MaxAtomsBeforeTerminal { get; init; } = 64;

    public int MaxReconnectAttempts { get; init; } = 2;

    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(1);

    public int StreamPipeBufferSize { get; init; } = 64 * 1024;

    public int StreamPipeMinimumReadSize { get; init; } = 4 * 1024;

    public int PcpVersion { get; init; } = 1;

    public string UserAgent { get; init; } = "PeerCastNova/0.0.6c2-fix1";

    public uint ServantVersion { get; init; } =
        PcpHeloCodec.LegacyServantVersion;

    public PcpChannelStreamOptions Stream { get; init; } = new();

    public void Validate()
    {
        if (HandshakeTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(HandshakeTimeout)} must be positive.");
        }

        if (MaxReconnectAttempts < 0)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxReconnectAttempts)} cannot be negative.");
        }

        if (ReconnectDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(ReconnectDelay)} cannot be negative.");
        }

        if (StreamPipeBufferSize < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(StreamPipeBufferSize)} must be positive.");
        }

        if (StreamPipeMinimumReadSize < 1 ||
            StreamPipeMinimumReadSize > StreamPipeBufferSize)
        {
            throw new InvalidOperationException(
                $"{nameof(StreamPipeMinimumReadSize)} must be positive and no greater than {nameof(StreamPipeBufferSize)}.");
        }

        if (PcpVersion < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(PcpVersion)} must be positive.");
        }

        ArgumentNullException.ThrowIfNull(Stream);
        Stream.Validate();
        CreateHandshakeSettings().Validate();
    }

    internal PcpRelayHandshakeSettings CreateHandshakeSettings()
    {
        return new PcpRelayHandshakeSettings(
            MaxHttpHeaderBytes,
            MaxHandshakeAtomBytes,
            MaxAtomsBeforeTerminal,
            UserAgent,
            ServantVersion);
    }
}
