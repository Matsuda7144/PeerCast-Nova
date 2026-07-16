using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Finite HTTP, PCP control, packet, Pipe, handshake, and write limits for one
/// accepted downstream relay child.
/// </summary>
public sealed record PcpDownstreamChildOptions
{
    public const int MaximumPacketBytes = 16 * 1024 * 1024;
    public const int MaximumControlAtomBytes = 1024 * 1024;

    public TimeSpan HandshakeTimeout { get; init; } =
        TimeSpan.FromSeconds(10);

    public TimeSpan WriteTimeout { get; init; } =
        TimeSpan.FromSeconds(10);

    public int MaxHttpHeaderBytes { get; init; } = 32 * 1024;

    public int MaxControlAtomBytes { get; init; } = 64 * 1024;

    public int MaxPacketBytes { get; init; } = 1024 * 1024;

    public int PipeBufferSize { get; init; } = 64 * 1024;

    public int PipeMinimumReadSize { get; init; } = 4 * 1024;

    public int PcpVersion { get; init; } = 1;

    public string UserAgent { get; init; } = "PeerCastNova/0.0.6c2-fix1";

    public uint ServantVersion { get; init; } =
        PcpHeloCodec.LegacyServantVersion;

    public void Validate()
    {
        ValidateTimeout(HandshakeTimeout, nameof(HandshakeTimeout));
        ValidateTimeout(WriteTimeout, nameof(WriteTimeout));

        if (MaxHttpHeaderBytes is < 64 or > 1024 * 1024)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxHttpHeaderBytes)} must be between 64 bytes and one MiB.");
        }

        if (MaxControlAtomBytes is < PcpAtomCodec.HeaderSize or
            > MaximumControlAtomBytes)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxControlAtomBytes)} is outside the finite supported range.");
        }

        if (MaxPacketBytes is < 0 or > MaximumPacketBytes)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxPacketBytes)} is outside the finite supported range.");
        }

        if (PipeBufferSize is < 1 or > 1024 * 1024)
        {
            throw new InvalidOperationException(
                $"{nameof(PipeBufferSize)} is outside the finite supported range.");
        }

        if (PipeMinimumReadSize < 1 ||
            PipeMinimumReadSize > PipeBufferSize)
        {
            throw new InvalidOperationException(
                $"{nameof(PipeMinimumReadSize)} must be positive and no greater than {nameof(PipeBufferSize)}.");
        }

        if (PcpVersion != 1)
        {
            throw new InvalidOperationException(
                "Only the independently verified PCP version 1 is supported downstream.");
        }

        ValidateAsciiHeaderValue(UserAgent, nameof(UserAgent));
    }

    internal PcpProtocolLimits CreateControlLimits()
    {
        return new PcpProtocolLimits
        {
            MaxContentLength = MaxControlAtomBytes,
        };
    }

    private static void ValidateTimeout(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero || value > TimeSpan.FromMinutes(1))
        {
            throw new InvalidOperationException(
                $"{name} must be positive and no greater than one minute.");
        }
    }

    private static void ValidateAsciiHeaderValue(
        string value,
        string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);

        foreach (var character in value)
        {
            if (character < 0x20 || character > 0x7e)
            {
                throw new ArgumentException(
                    "The HTTP server agent must contain printable ASCII only.",
                    name);
            }
        }
    }
}
