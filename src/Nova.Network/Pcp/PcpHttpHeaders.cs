namespace PeerCast.Nova.Network.Pcp;

public sealed record PcpHttpRequestHeader(
    string Method,
    string RequestTarget,
    Version HttpVersion,
    IReadOnlyDictionary<string, string> Headers);

public sealed record PcpHttpResponseHeader(
    Version HttpVersion,
    int StatusCode,
    string ReasonPhrase,
    IReadOnlyDictionary<string, string> Headers)
{
    public string? Server =>
        Headers.TryGetValue("Server", out var value) ? value : null;

    public string? ContentType =>
        Headers.TryGetValue("Content-Type", out var value) ? value : null;
}
