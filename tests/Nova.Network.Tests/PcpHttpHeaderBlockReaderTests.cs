using System.IO.Pipelines;
using System.Text;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpHttpHeaderBlockReaderTests
{
    private const int HeaderLimit = 32 * 1024;

    [Fact]
    public async Task Response_AllowsBoundedObsTextOnlyInFieldValue()
    {
        var bytes = Encoding.ASCII.GetBytes(
                "HTTP/1.0 200 OK\r\n" +
                "Server:PeerCastStation/Test\r\n" +
                "Content-Type:application/x-peercast-pcp\r\n" +
                "x-peercast-name:")
            .Concat(Encoding.UTF8.GetBytes("テスト"))
            .Concat("\r\n\r\n"u8.ToArray())
            .ToArray();
        var reader = await CreateCompletedReaderAsync(bytes);

        try
        {
            var response = await PcpHttpHeaderBlockReader.ReadResponseAsync(
                reader,
                HeaderLimit,
                CancellationToken.None);

            Assert.Equal(200, response.StatusCode);
            Assert.Equal("PeerCastStation/Test", response.Server);
            Assert.Equal(
                "application/x-peercast-pcp",
                response.ContentType);
            Assert.True(response.Headers.ContainsKey("x-peercast-name"));
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task Request_RejectsObsTextInFieldValue()
    {
        var bytes = Encoding.ASCII.GetBytes(
                "GET /channel/test HTTP/1.0\r\nx-name:")
            .Concat(new byte[] { 0xe3, 0x81, 0x82 })
            .Concat("\r\n\r\n"u8.ToArray())
            .ToArray();
        var reader = await CreateCompletedReaderAsync(bytes);

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(
                async () => await PcpHttpHeaderBlockReader.ReadRequestAsync(
                    reader,
                    HeaderLimit,
                    CancellationToken.None));
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task Response_RejectsObsTextInStatusLine()
    {
        var bytes = Encoding.ASCII.GetBytes("HTTP/1.0 200 ")
            .Concat(new byte[] { 0xe3, 0x81, 0x82 })
            .Concat("\r\nServer:test\r\n\r\n"u8.ToArray())
            .ToArray();
        var reader = await CreateCompletedReaderAsync(bytes);

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(
                async () => await PcpHttpHeaderBlockReader.ReadResponseAsync(
                    reader,
                    HeaderLimit,
                    CancellationToken.None));
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task Response_RejectsObsTextInFieldName()
    {
        var bytes = Encoding.ASCII.GetBytes(
                "HTTP/1.0 200 OK\r\nx-")
            .Concat(new byte[] { 0xe3, 0x81, 0x82 })
            .Concat(":value\r\n\r\n"u8.ToArray())
            .ToArray();
        var reader = await CreateCompletedReaderAsync(bytes);

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(
                async () => await PcpHttpHeaderBlockReader.ReadResponseAsync(
                    reader,
                    HeaderLimit,
                    CancellationToken.None));
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    [Theory]
    [InlineData(0x1f)]
    [InlineData(0x7f)]
    public async Task Response_RejectsControlByteInFieldValue(
        byte controlByte)
    {
        var bytes = Encoding.ASCII.GetBytes(
                "HTTP/1.0 200 OK\r\nx-value:")
            .Concat(new[] { controlByte })
            .Concat("\r\n\r\n"u8.ToArray())
            .ToArray();
        var reader = await CreateCompletedReaderAsync(bytes);

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(
                async () => await PcpHttpHeaderBlockReader.ReadResponseAsync(
                    reader,
                    HeaderLimit,
                    CancellationToken.None));
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    private static async Task<PipeReader> CreateCompletedReaderAsync(
        byte[] bytes)
    {
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(bytes);
        await pipe.Writer.CompleteAsync();
        return pipe.Reader;
    }
}
