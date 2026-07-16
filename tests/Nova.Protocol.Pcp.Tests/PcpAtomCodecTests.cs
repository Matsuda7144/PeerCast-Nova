namespace PeerCast.Nova.Protocol.Pcp.Tests;

public sealed class PcpAtomCodecTests
{
    private static readonly PcpProtocolLimits Limits = new();

    [Fact]
    public void LeafHeader_RoundTrips()
    {
        var expected = new PcpAtomHeader(
            PcpAtomName.FromAscii("test"),
            isParent: false,
            value: 1_024);

        Span<byte> buffer = stackalloc byte[PcpAtomCodec.HeaderSize];
        PcpAtomCodec.WriteHeader(buffer, expected);

        var success = PcpAtomCodec.TryReadHeader(
            buffer,
            Limits,
            out var actual,
            out var error);

        Assert.True(success);
        Assert.Equal(PcpParseError.None, error);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParentHeader_RoundTrips()
    {
        var expected = new PcpAtomHeader(
            PcpAtomName.FromAscii("root"),
            isParent: true,
            value: 12);

        Span<byte> buffer = stackalloc byte[PcpAtomCodec.HeaderSize];
        PcpAtomCodec.WriteHeader(buffer, expected);

        var success = PcpAtomCodec.TryReadHeader(
            buffer,
            Limits,
            out var actual,
            out var error);

        Assert.True(success);
        Assert.Equal(PcpParseError.None, error);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TruncatedHeader_ReturnsNeedMoreData()
    {
        Span<byte> buffer = stackalloc byte[PcpAtomCodec.HeaderSize - 1];

        var success = PcpAtomCodec.TryReadHeader(
            buffer,
            Limits,
            out _,
            out var error);

        Assert.False(success);
        Assert.Equal(PcpParseError.NeedMoreData, error);
    }

    [Fact]
    public void OversizedLeaf_ReturnsContentLimitError()
    {
        var strictLimits = new PcpProtocolLimits
        {
            MaxContentLength = 4,
        };

        Span<byte> buffer = stackalloc byte[PcpAtomCodec.HeaderSize];
        PcpAtomCodec.WriteHeader(
            buffer,
            new PcpAtomHeader(
                PcpAtomName.FromAscii("data"),
                isParent: false,
                value: 5));

        var success = PcpAtomCodec.TryReadHeader(
            buffer,
            strictLimits,
            out _,
            out var error);

        Assert.False(success);
        Assert.Equal(PcpParseError.ContentLengthLimitExceeded, error);
    }

    [Fact]
    public void ExcessiveChildCount_ReturnsChildLimitError()
    {
        var strictLimits = new PcpProtocolLimits
        {
            MaxChildCount = 2,
        };

        Span<byte> buffer = stackalloc byte[PcpAtomCodec.HeaderSize];
        PcpAtomCodec.WriteHeader(
            buffer,
            new PcpAtomHeader(
                PcpAtomName.FromAscii("root"),
                isParent: true,
                value: 3));

        var success = PcpAtomCodec.TryReadHeader(
            buffer,
            strictLimits,
            out _,
            out var error);

        Assert.False(success);
        Assert.Equal(PcpParseError.ChildCountLimitExceeded, error);
    }
}
