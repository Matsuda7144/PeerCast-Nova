namespace PeerCast.Nova.Protocol.Pcp.Tests;

public sealed class PcpPreambleTests
{
    private static readonly PcpProtocolLimits Limits = new();

    [Fact]
    public void Preamble_UsesExpectedWireBytes()
    {
        ReadOnlySpan<byte> expected =
        [
            0x70, 0x63, 0x70, 0x0A,
            0x04, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x00, 0x00,
        ];

        Span<byte> actual = stackalloc byte[PcpPreamble.Size];
        PcpPreamble.Write(actual);

        Assert.True(actual.SequenceEqual(expected));
    }

    [Fact]
    public void SupportedPreamble_RoundTrips()
    {
        Span<byte> buffer = stackalloc byte[PcpPreamble.Size];
        PcpPreamble.Write(buffer);

        var success = PcpPreamble.TryRead(
            buffer,
            Limits,
            out var version,
            out var error);

        Assert.True(success);
        Assert.Equal(PcpPreamble.SupportedProtocolVersion, version);
        Assert.Equal(PcpParseError.None, error);
    }

    [Fact]
    public void UnsupportedVersion_IsRejected()
    {
        Span<byte> buffer = stackalloc byte[PcpPreamble.Size];
        PcpPreamble.Write(buffer, protocolVersion: 2);

        var success = PcpPreamble.TryRead(
            buffer,
            Limits,
            out var version,
            out var error);

        Assert.False(success);
        Assert.Equal((uint)2, version);
        Assert.Equal(PcpParseError.UnsupportedProtocolVersion, error);
    }
}
