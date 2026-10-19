using System.Text;

namespace PeerCast.Nova.App.Tests;

public sealed class PeerCastIndexTextEncodingTests
{
    [Fact]
    public void Utf8WithoutCharsetDecodesJapaneseText()
    {
        var bytes = Encoding.UTF8.GetBytes("配信テスト");

        var decoded = PeerCastIndexTextEncoding.Decode(bytes, charset: null);

        Assert.Equal("配信テスト", decoded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("utf-8")]
    public void Utf8BomIsRemoved(string? charset)
    {
        var bytes = Encoding.UTF8.GetBytes("\uFEFF配信テスト");

        var decoded = PeerCastIndexTextEncoding.Decode(bytes, charset);

        Assert.Equal("配信テスト", decoded);
    }

    [Fact]
    public void DeclaredShiftJisDecodesJapaneseText()
    {
        var bytes = Convert.FromHexString("947A904D836583588367");

        var decoded = PeerCastIndexTextEncoding.Decode(bytes, "Shift_JIS");

        Assert.Equal("配信テスト", decoded);
    }

    [Fact]
    public void InvalidUtf8WithoutCharsetFallsBackToShiftJis()
    {
        var bytes = Convert.FromHexString("947A904D836583588367");

        var decoded = PeerCastIndexTextEncoding.Decode(bytes, charset: null);

        Assert.Equal("配信テスト", decoded);
    }

    [Fact]
    public void UnsupportedDeclaredCharsetIsRejected()
    {
        var bytes = Encoding.UTF8.GetBytes("test");

        Assert.Throws<InvalidDataException>(() =>
            PeerCastIndexTextEncoding.Decode(bytes, "iso-2022-jp"));
    }
}
