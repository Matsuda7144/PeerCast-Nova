namespace PeerCast.Nova.Protocol.Pcp.Tests;

public sealed class PcpAtomNameTests
{
    [Fact]
    public void FromAscii_ThreeCharacters_NulPadsWireValue()
    {
        var name = PcpAtomName.FromAscii("sid");
        Span<byte> bytes = stackalloc byte[PcpAtomName.Size];

        name.WriteTo(bytes);

        ReadOnlySpan<byte> expected = [0x73, 0x69, 0x64, 0x00];
        Assert.True(bytes.SequenceEqual(expected));
        Assert.Equal("sid", name.ToString());
    }

    [Fact]
    public void FromAscii_TwoCharacters_NulPadsWireValue()
    {
        var name = PcpAtomName.FromAscii("ok");
        Span<byte> bytes = stackalloc byte[PcpAtomName.Size];

        name.WriteTo(bytes);

        ReadOnlySpan<byte> expected = [0x6F, 0x6B, 0x00, 0x00];
        Assert.True(bytes.SequenceEqual(expected));
        Assert.Equal("ok", name.ToString());
    }

    [Fact]
    public void ToString_EscapesControlInteriorNulAndBackslashBytes()
    {
        var controlAndBackslash = new PcpAtomName(
            0x1B,
            (byte)'\\',
            0,
            0);

        var interiorNul = new PcpAtomName(
            (byte)'a',
            0,
            (byte)'b',
            0);

        Assert.Equal("\\x1B\\\\", controlAndBackslash.ToString());
        Assert.Equal("a\\x00b", interiorNul.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("abcde")]
    public void FromAscii_InvalidLength_Throws(string value)
    {
        Assert.Throws<ArgumentException>(() => PcpAtomName.FromAscii(value));
    }
}
