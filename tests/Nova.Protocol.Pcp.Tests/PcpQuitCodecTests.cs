namespace PeerCast.Nova.Protocol.Pcp.Tests;

public sealed class PcpQuitCodecTests
{
    [Theory]
    [InlineData(0U, PcpQuitReason.Unspecified)]
    [InlineData(1000U, PcpQuitReason.Normal)]
    [InlineData(1003U, PcpQuitReason.Unavailable)]
    [InlineData(1005U, PcpQuitReason.NotIdentified)]
    [InlineData(1007U, PcpQuitReason.BadAgent)]
    [InlineData(1008U, PcpQuitReason.Other)]
    [InlineData(uint.MaxValue, PcpQuitReason.Other)]
    public void Parse_PreservesCodeAndClassifiesReason(uint code, PcpQuitReason reason)
    {
        var atom = PcpQuitCodec.Create(code);
        var quit = PcpQuitCodec.Parse(atom);
        Assert.Equal(code, quit.Code);
        Assert.Equal(reason, quit.Reason);
    }

    [Fact]
    public void Create_UnavailableUsesLegacyLittleEndianCode()
    {
        var atom = PcpQuitCodec.Create(PcpQuitCodec.UnavailableCode);
        Assert.Equal(PcpKnownAtomNames.Quit, atom.Name);
        Assert.Equal(new byte[] { 0xEB, 0x03, 0x00, 0x00 }, atom.Content.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(8)]
    public void Parse_RejectsMissingOrMalformedCode(int length)
    {
        Assert.Throws<InvalidDataException>(() => PcpQuitCodec.Parse(
            PcpAtom.Leaf(PcpKnownAtomNames.Quit, new byte[length])));
    }

    [Fact]
    public void Parse_RejectsParentAndWrongAtom()
    {
        Assert.Throws<InvalidDataException>(() => PcpQuitCodec.Parse(
            PcpAtom.Parent(PcpKnownAtomNames.Quit)));
        Assert.Throws<InvalidDataException>(() => PcpQuitCodec.Parse(
            PcpAtom.Leaf(PcpKnownAtomNames.Okay, new byte[4])));
    }
}
