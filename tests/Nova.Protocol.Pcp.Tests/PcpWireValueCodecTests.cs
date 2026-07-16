using System.Net;

namespace PeerCast.Nova.Protocol.Pcp.Tests;

public sealed class PcpWireValueCodecTests
{
    [Fact]
    public void Guid_RoundTripsInNetworkOrder()
    {
        var value = Guid.ParseExact(
            "00112233445566778899aabbccddeeff",
            "N");

        var encoded = PcpWireValueCodec.EncodeGuid(value);

        ReadOnlySpan<byte> expected =
        [
            0x00, 0x11, 0x22, 0x33,
            0x44, 0x55,
            0x66, 0x77,
            0x88, 0x99, 0xAA, 0xBB,
            0xCC, 0xDD, 0xEE, 0xFF,
        ];

        Assert.True(encoded.AsSpan().SequenceEqual(expected));

        Assert.Equal(value, PcpWireValueCodec.DecodeGuid(encoded));
    }

    [Fact]
    public void SpanWriters_UseExistingWireFormatsWithoutAllocatingResults()
    {
        Span<byte> guid = stackalloc byte[16];
        Span<byte> uint16 = stackalloc byte[sizeof(ushort)];
        Span<byte> uint32 = stackalloc byte[sizeof(uint)];
        var value = Guid.ParseExact(
            "00112233445566778899aabbccddeeff",
            "N");

        PcpWireValueCodec.WriteGuid(guid, value);
        PcpWireValueCodec.WriteUInt16(uint16, 0x1234);
        PcpWireValueCodec.WriteUInt32(uint32, 0x12345678);

        ReadOnlySpan<byte> expectedGuid =
        [
            0x00, 0x11, 0x22, 0x33,
            0x44, 0x55,
            0x66, 0x77,
            0x88, 0x99, 0xAA, 0xBB,
            0xCC, 0xDD, 0xEE, 0xFF,
        ];
        ReadOnlySpan<byte> expectedUInt16 = [0x34, 0x12];
        ReadOnlySpan<byte> expectedUInt32 = [0x78, 0x56, 0x34, 0x12];

        Assert.True(guid.SequenceEqual(expectedGuid));
        Assert.True(uint16.SequenceEqual(expectedUInt16));
        Assert.True(uint32.SequenceEqual(expectedUInt32));
    }

    [Fact]
    public void String_IsNulTerminated()
    {
        var encoded = PcpWireValueCodec.EncodeNullTerminatedUtf8("Nova");

        ReadOnlySpan<byte> expected = [0x4E, 0x6F, 0x76, 0x61, 0x00];
        Assert.True(encoded.AsSpan().SequenceEqual(expected));

        Assert.Equal(
            "Nova",
            PcpWireValueCodec.DecodeNullTerminatedUtf8(encoded));
    }

    [Fact]
    public void String_RejectsEmbeddedNulAndInvalidUtf8()
    {
        Assert.Throws<InvalidDataException>(
            () => PcpWireValueCodec.DecodeNullTerminatedUtf8(
                [0x41, 0x00, 0x42, 0x00]));

        Assert.Throws<InvalidDataException>(
            () => PcpWireValueCodec.DecodeNullTerminatedUtf8(
                [0xC3, 0x28, 0x00]));
    }

    [Fact]
    public void LegacyIpv4_ReversesWireBytes()
    {
        var address = IPAddress.Parse("192.0.2.10");

        var encoded = PcpWireValueCodec.EncodeLegacyIpv4(address);

        ReadOnlySpan<byte> expected = [10, 2, 0, 192];
        Assert.True(encoded.AsSpan().SequenceEqual(expected));

        Assert.Equal(
            address,
            PcpWireValueCodec.DecodeLegacyIpv4(encoded));
    }
}
