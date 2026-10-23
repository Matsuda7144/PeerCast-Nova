using System.Globalization;
using System.Text;

namespace PeerCast.Nova.App;

internal sealed class SystemPeerCastIndexTextSource : IPeerCastIndexDiagnosticTextSource
{
    private const int MaximumResponseBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(12);

    public static SystemPeerCastIndexTextSource Instance { get; } = new();

    private SystemPeerCastIndexTextSource()
    {
    }

    public async ValueTask<string> ReadAsync(
        Uri indexUri,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadCoreAsync(
                indexUri,
                collectDiagnostics: false,
                cancellationToken)
            .ConfigureAwait(false);
        return result.Text;
    }

    public ValueTask<PeerCastIndexTextReadResult> ReadWithDiagnosticsAsync(
        Uri indexUri,
        CancellationToken cancellationToken = default) =>
        ReadCoreAsync(indexUri, collectDiagnostics: true, cancellationToken);

    private static async ValueTask<PeerCastIndexTextReadResult> ReadCoreAsync(
        Uri indexUri,
        bool collectDiagnostics,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(indexUri);
        string stage = "ValidateUri";
        int? statusCode = null;
        int? responseByteCount = null;
        try
        {
            ValidateIndexUri(indexUri);
            cancellationToken.ThrowIfCancellationRequested();

            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeoutCancellation.CancelAfter(RequestTimeout);

            using var client = new System.Net.Sockets.TcpClient();
            var port = indexUri.IsDefaultPort ? 80 : indexUri.Port;
            stage = "Connect";
            await client.ConnectAsync(
                    indexUri.DnsSafeHost,
                    port,
                    timeoutCancellation.Token)
                .ConfigureAwait(false);

            await using var stream = client.GetStream();
            var request = BuildRequest(indexUri);
            stage = "WriteRequest";
            await stream.WriteAsync(request.AsMemory(), timeoutCancellation.Token)
                .ConfigureAwait(false);
            await stream.FlushAsync(timeoutCancellation.Token).ConfigureAwait(false);

            stage = "ReadResponse";
            var response = await ReadResponseAsync(stream, timeoutCancellation.Token)
                .ConfigureAwait(false);
            stage = "DecodeResponse";
            string text;
            if (collectDiagnostics)
            {
                text = PeerCastHttpIndexResponse.Decode(
                    response,
                    out statusCode,
                    out responseByteCount);
            }
            else
            {
                text = PeerCastHttpIndexResponse.Decode(response);
            }

            return new PeerCastIndexTextReadResult(
                text,
                statusCode,
                responseByteCount);
        }
        catch (Exception exception) when (collectDiagnostics)
        {
            GenesisYellowPageRefreshDiagnosticAmbient.Current?.Trace.TryRecord(
                "HttpReadFailed",
                stage: "Http",
                outcome: exception is not OperationCanceledException
                    ? "Failed"
                    : cancellationToken.IsCancellationRequested ? "Cancelled" : "Timeout",
                httpStatusCode: statusCode,
                responseByteCount: responseByteCount,
                exceptionType: exception.GetType().Name,
                failureStage: stage);
            throw;
        }
    }

    private static void ValidateIndexUri(Uri indexUri)
    {
        if (!indexUri.IsAbsoluteUri ||
            indexUri.Scheme != Uri.UriSchemeHttp ||
            string.IsNullOrWhiteSpace(indexUri.Host) ||
            !string.IsNullOrEmpty(indexUri.UserInfo))
        {
            throw new ArgumentException(
                "Only absolute unauthenticated HTTP index URIs are supported by the SP transport.",
                nameof(indexUri));
        }
    }

    private static byte[] BuildRequest(Uri indexUri)
    {
        var target = string.IsNullOrEmpty(indexUri.PathAndQuery)
            ? "/"
            : indexUri.PathAndQuery;
        var authority = indexUri.IsDefaultPort
            ? indexUri.Host
            : indexUri.Authority;
        var request = string.Concat(
            "GET ",
            target,
            " HTTP/1.0\r\nHost: ",
            authority,
            "\r\nUser-Agent: PeerCast-Nova/Genesis\r\n",
            "Accept: text/plain, */*;q=0.1\r\n",
            "Accept-Encoding: identity\r\n",
            "Connection: close\r\n\r\n");
        return Encoding.ASCII.GetBytes(request);
    }

    private static async Task<byte[]> ReadResponseAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > MaximumResponseBytes)
            {
                throw new InvalidDataException("The SP index response is too large.");
            }

            await output.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken)
                .ConfigureAwait(false);

            var expectedResponseLength = TryGetDeclaredResponseLength(output);
            if (expectedResponseLength is not null &&
                output.Length >= expectedResponseLength.Value)
            {
                break;
            }
        }

        return output.ToArray();
    }

    private static int? TryGetDeclaredResponseLength(MemoryStream response)
    {
        var length = checked((int)response.Length);
        var bytes = response.GetBuffer().AsSpan(0, length);
        var headerEnd = bytes.IndexOf("\r\n\r\n"u8);
        if (headerEnd < 0)
        {
            return null;
        }

        var headerText = Encoding.ASCII.GetString(bytes[..headerEnd]);
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        int? contentLength = null;
        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0 ||
                !string.Equals(
                    line[..separator].Trim(),
                    "Content-Length",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!int.TryParse(
                    line[(separator + 1)..].Trim(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsedContentLength) ||
                parsedContentLength < 0 ||
                contentLength is not null && contentLength.Value != parsedContentLength)
            {
                return null;
            }

            contentLength = parsedContentLength;
        }

        if (contentLength is null)
        {
            return null;
        }

        var expectedResponseLength =
            (long)headerEnd + "\r\n\r\n"u8.Length + contentLength.Value;
        if (expectedResponseLength > MaximumResponseBytes)
        {
            throw new InvalidDataException("The SP index response is too large.");
        }

        return checked((int)expectedResponseLength);
    }
}
