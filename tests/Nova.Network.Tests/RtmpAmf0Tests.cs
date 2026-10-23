using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.Network.Tests;

public sealed class RtmpAmf0Tests
{
    [Fact]
    public void Literal_number_string_null_object_boolean_and_ECMA_array_decode()
    {
        var values = RtmpAmf0.Decode(Convert.FromHexString(
            "020003616263003FF0000000000000050300017801010000090800000000000179004000000000000000000009"));
        Assert.Equal("abc", values[0]);
        Assert.Equal(1.0, values[1]);
        Assert.Null(values[2]);
        Assert.Equal(true, Assert.IsType<Dictionary<string, object?>>(values[3])["x"]);
        Assert.Equal(2.0, Assert.IsType<Dictionary<string, object?>>(values[4])["y"]);
    }

    [Fact]
    public void Response_encoding_matches_literal_AMF0_bytes()
    {
        Assert.Equal(Convert.FromHexString("020003616263003FF0000000000000050300017802000179000009"),
            RtmpAmf0.Encode("abc", 1.0, null, new Dictionary<string, object?> { ["x"] = "y" }));
    }

    [Theory]
    [InlineData("020001")]
    [InlineData("007FF0000000000000")]
    [InlineData("007FF8000000000000")]
    [InlineData("0102")]
    [InlineData("020001FF")]
    [InlineData("07")]
    [InlineData("0A00000081")]
    [InlineData("030001780500017805000009")]
    public void Truncation_nonfinite_invalid_UTF8_reference_array_budget_or_duplicate_property_is_rejected(string hex)
    {
        Assert.Throws<InvalidDataException>(() => RtmpAmf0.Decode(Convert.FromHexString(hex)));
    }

    [Fact]
    public void Nested_or_many_values_and_oversized_commands_are_rejected()
    {
        var nested = Enumerable.Repeat(new byte[] { 3, 0, 1, 0x78 }, 6).SelectMany(bytes => bytes)
            .Concat(new byte[] { 5 }).ToArray();
        Assert.Throws<InvalidDataException>(() => RtmpAmf0.Decode(nested));
        Assert.Throws<InvalidDataException>(() => RtmpAmf0.Decode(Enumerable.Repeat((byte)5, 257).ToArray()));
        Assert.Throws<InvalidDataException>(() => RtmpAmf0.Decode(new byte[65537]));
    }
}
