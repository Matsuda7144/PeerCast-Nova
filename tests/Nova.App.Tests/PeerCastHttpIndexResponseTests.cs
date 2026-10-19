using System.Text;

namespace PeerCast.Nova.App.Tests;

public sealed class PeerCastHttpIndexResponseTests
{
    [Fact]
    public void SuccessfulUtf8ResponseReturnsBody()
    {
        var body = "配信テスト<>value\n";
        var response = BuildResponse(
            "HTTP/1.0 200 OK",
            body,
            $"Content-Length: {Encoding.UTF8.GetByteCount(body)}",
            "Content-Type: text/plain; charset=utf-8");

        var decoded = PeerCastHttpIndexResponse.Decode(response);

        Assert.Equal(body, decoded);
    }

    [Fact]
    public void BodyWithoutContentLengthUsesConnectionClosedBytes()
    {
        var body = "Example<>value\n";
        var response = BuildResponse("HTTP/1.0 200 OK", body);

        var decoded = PeerCastHttpIndexResponse.Decode(response);

        Assert.Equal(body, decoded);
    }

    [Fact]
    public void DeclaredShiftJisResponseReturnsJapaneseBody()
    {
        var body = Convert.FromHexString("947A904D836583588367");
        var response = BuildResponse(
            "HTTP/1.0 200 OK",
            body,
            $"Content-Length: {body.Length}",
            "Content-Type: text/plain; charset=Shift_JIS");

        var decoded = PeerCastHttpIndexResponse.Decode(response);

        Assert.Equal("配信テスト", decoded);
    }

    [Fact]
    public void UndeclaredLegacyJapaneseBodyFallsBackToShiftJis()
    {
        var body = Convert.FromHexString("947A904D836583588367");
        var response = BuildResponse(
            "HTTP/1.0 200 OK",
            body,
            $"Content-Length: {body.Length}");

        var decoded = PeerCastHttpIndexResponse.Decode(response);

        Assert.Equal("配信テスト", decoded);
    }

    [Theory]
    [InlineData("HTTP/1.0 301 Moved Permanently")]
    [InlineData("HTTP/1.0 404 Not Found")]
    [InlineData("not-http")]
    public void NonSuccessfulOrMalformedStatusIsRejected(string statusLine)
    {
        var response = BuildResponse(statusLine, "body");

        Assert.Throws<InvalidDataException>(() =>
            PeerCastHttpIndexResponse.Decode(response));
    }

    [Fact]
    public void PartialContentResponseIsRejected()
    {
        var response = BuildResponse(
            "HTTP/1.1 206 Partial Content",
            "partial",
            "Content-Length: 7",
            "Content-Range: bytes 0-6/100");

        Assert.Throws<InvalidDataException>(() =>
            PeerCastHttpIndexResponse.Decode(response));
    }

    [Fact]
    public void TruncatedContentLengthIsRejected()
    {
        var response = BuildResponse(
            "HTTP/1.0 200 OK",
            "short",
            "Content-Length: 100");

        Assert.Throws<InvalidDataException>(() =>
            PeerCastHttpIndexResponse.Decode(response));
    }

    [Fact]
    public void ConflictingContentLengthsAreRejected()
    {
        var response = BuildResponse(
            "HTTP/1.0 200 OK",
            "body",
            "Content-Length: 4",
            "Content-Length: 5");

        Assert.Throws<InvalidDataException>(() =>
            PeerCastHttpIndexResponse.Decode(response));
    }

    [Fact]
    public void ChunkedTransferEncodingIsRejected()
    {
        var response = BuildResponse(
            "HTTP/1.1 200 OK",
            "5\r\nhello\r\n0\r\n\r\n",
            "Transfer-Encoding: chunked");

        Assert.Throws<InvalidDataException>(() =>
            PeerCastHttpIndexResponse.Decode(response));
    }

    [Fact]
    public void EncodedContentResponseIsRejected()
    {
        var response = BuildResponse(
            "HTTP/1.0 200 OK",
            "encoded body",
            "Content-Encoding: gzip");

        Assert.Throws<InvalidDataException>(() =>
            PeerCastHttpIndexResponse.Decode(response));
    }

    [Fact]
    public void UnsupportedDeclaredCharsetIsRejected()
    {
        var response = BuildResponse(
            "HTTP/1.0 200 OK",
            "body",
            "Content-Type: text/plain; charset=iso-2022-jp");

        Assert.Throws<InvalidDataException>(() =>
            PeerCastHttpIndexResponse.Decode(response));
    }

    [Fact]
    public void ConflictingDeclaredCharsetsAreRejected()
    {
        var response = BuildResponse(
            "HTTP/1.0 200 OK",
            "body",
            "Content-Type: text/plain; charset=utf-8",
            "Content-Type: text/plain; charset=shift_jis");

        Assert.Throws<InvalidDataException>(() =>
            PeerCastHttpIndexResponse.Decode(response));
    }

    private static byte[] BuildResponse(
        string statusLine,
        string body,
        params string[] headers) =>
        BuildResponse(
            statusLine,
            Encoding.UTF8.GetBytes(body),
            headers);

    private static byte[] BuildResponse(
        string statusLine,
        byte[] body,
        params string[] headers)
    {
        var headerText = headers.Length == 0
            ? $"{statusLine}\r\n\r\n"
            : $"{statusLine}\r\n{string.Join("\r\n", headers)}\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(headerText);
        var response = new byte[headerBytes.Length + body.Length];
        headerBytes.CopyTo(response, 0);
        body.CopyTo(response, headerBytes.Length);
        return response;
    }
}
