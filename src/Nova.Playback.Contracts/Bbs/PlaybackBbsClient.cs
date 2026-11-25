using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace PeerCast.Nova.Playback.Bbs;

public interface IPlaybackBbsClient : IDisposable
{
    Task<IReadOnlyList<BbsThreadSummary>> GetThreadsAsync(BbsTarget target, CancellationToken cancellationToken);
    Task<BbsFetchResult> FetchPostsAsync(BbsTarget target, BbsFetchState state, CancellationToken cancellationToken);
    Task<BbsPostResult> PostAsync(BbsTarget target, BbsPostRequest request, CancellationToken cancellationToken);
}

public sealed partial class PlaybackBbsClient : IPlaybackBbsClient
{
    private const string DefaultUserAgent = "Monazilla/1.00 (PeerCastNova/1.0)";
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public PlaybackBbsClient(HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        if (handler is null)
        {
            var socketsHandler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                CookieContainer = new CookieContainer(),
                UseCookies = true,
            };
            _httpClient = new HttpClient(socketsHandler, disposeHandler: true);
        }
        else
        {
            _httpClient = new HttpClient(handler, disposeHandler: false);
        }

        _ownsHttpClient = true;
        _httpClient.Timeout = timeout ?? TimeSpan.FromSeconds(15);
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
    }

    public async Task<IReadOnlyList<BbsThreadSummary>> GetThreadsAsync(BbsTarget target, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, target.SubjectUrl);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var text = BbsTextCodec.Decode(bytes, target.Kind, response.Content.Headers.ContentType?.CharSet);
        return BbsParsers.ParseSubject(text, target.Kind);
    }

    public Task<BbsFetchResult> FetchPostsAsync(BbsTarget target, BbsFetchState state, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(target.ThreadKey))
        {
            throw new InvalidOperationException("A thread key is required to fetch posts.");
        }

        return target.Kind switch
        {
            BbsBoardKind.Shitaraba => FetchShitarabaPostsAsync(target, state, cancellationToken),
            BbsBoardKind.Jpnkn => FetchJpnknPostsAsync(target, state, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
    }

    public Task<BbsPostResult> PostAsync(BbsTarget target, BbsPostRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(target.ThreadKey))
        {
            return Task.FromResult(new BbsPostResult(false, "スレッドが選択されていません。"));
        }

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Task.FromResult(new BbsPostResult(false, "本文を入力してください。"));
        }

        return target.Kind switch
        {
            BbsBoardKind.Shitaraba => PostShitarabaAsync(target, request, cancellationToken),
            BbsBoardKind.Jpnkn => PostJpnknAsync(target, request, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<BbsFetchResult> FetchShitarabaPostsAsync(
        BbsTarget target, BbsFetchState state, CancellationToken cancellationToken)
    {
        var fromNumber = state.ExistingPostCount + 1;
        var url = target.GetRawModeUrl(fromNumber)!;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return new BbsFetchResult([], ResetExisting: false, NotModified: true, ThreadTitle: null, state);
        }

        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var text = BbsTextCodec.Decode(bytes, BbsBoardKind.Shitaraba, response.Content.Headers.ContentType?.CharSet);
        var posts = BbsParsers.ParseShitarabaRawMode(text);

        var maxNumber = posts.Count > 0 ? posts[^1].Number : state.ExistingPostCount;
        var threadTitle = posts.FirstOrDefault(static p => !string.IsNullOrWhiteSpace(p.ThreadTitle))?.ThreadTitle;
        var nextState = state with { ExistingPostCount = Math.Max(state.ExistingPostCount, maxNumber) };
        return new BbsFetchResult(posts, ResetExisting: false, NotModified: posts.Count == 0, threadTitle, nextState);
    }

    private async Task<BbsFetchResult> FetchJpnknPostsAsync(
        BbsTarget target, BbsFetchState state, CancellationToken cancellationToken)
    {
        var url = target.GetDatUrl()!;
        var attemptPartial = state.ExistingDatByteLength > 0;
        var response = await SendJpnknDatRequestAsync(url, state, attemptPartial, cancellationToken).ConfigureAwait(false);
        try
        {
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && attemptPartial)
            {
                response.Dispose();
                response = await SendJpnknDatRequestAsync(url, BbsFetchState.Initial, usePartial: false, cancellationToken)
                    .ConfigureAwait(false);
                attemptPartial = false;
            }

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new BbsFetchResult([], ResetExisting: false, NotModified: true, ThreadTitle: null, state);
            }

            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var isPartial = attemptPartial && response.StatusCode == HttpStatusCode.PartialContent;
            var text = BbsTextCodec.Decode(bytes, BbsBoardKind.Jpnkn, response.Content.Headers.ContentType?.CharSet);
            var startNumber = isPartial ? state.ExistingPostCount + 1 : 1;
            var posts = BbsParsers.ParseJpnknDat(text, startNumber);

            var lastModified = response.Content.Headers.LastModified?.ToString("R", CultureInfo.InvariantCulture)
                ?? state.LastModified;
            var nextByteLength = isPartial ? state.ExistingDatByteLength + bytes.Length : bytes.Length;
            var nextCount = isPartial ? state.ExistingPostCount + posts.Count : posts.Count;
            var threadTitle = posts.FirstOrDefault(static p => !string.IsNullOrWhiteSpace(p.ThreadTitle))?.ThreadTitle;
            var nextState = new BbsFetchState(nextCount, lastModified, nextByteLength);
            return new BbsFetchResult(
                posts,
                ResetExisting: !isPartial,
                NotModified: isPartial && posts.Count == 0,
                threadTitle,
                nextState);
        }
        finally
        {
            response.Dispose();
        }
    }

    private Task<HttpResponseMessage> SendJpnknDatRequestAsync(
        string url, BbsFetchState state, bool usePartial, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (usePartial && state.ExistingDatByteLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(state.ExistingDatByteLength, null);
        }

        if (!string.IsNullOrEmpty(state.LastModified))
        {
            request.Headers.TryAddWithoutValidation("If-Modified-Since", state.LastModified);
        }

        return _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<BbsPostResult> PostShitarabaAsync(
        BbsTarget target, BbsPostRequest postRequest, CancellationToken cancellationToken)
    {
        var writeUrl = target.GetWriteUrl()!;
        KeyValuePair<string, string>[] fields =
        [
            new("DIR", target.Category ?? string.Empty),
            new("BBS", target.Board),
            new("KEY", target.ThreadKey!),
            new("NAME", postRequest.Name),
            new("MAIL", postRequest.Mail),
            new("MESSAGE", postRequest.Message),
            new("submit", "書き込む"),
        ];

        var encodedBody = BbsTextCodec.EncodeFormBody(fields, BbsTextCodec.EucJp);
        using var request = new HttpRequestMessage(HttpMethod.Post, writeUrl)
        {
            Content = new ByteArrayContent(Encoding.ASCII.GetBytes(encodedBody)),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        if (target.ThreadUrl is { } referer)
        {
            request.Headers.Referrer = new Uri(referer);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var html = BbsTextCodec.Decode(bytes, BbsBoardKind.Shitaraba, response.Content.Headers.ContentType?.CharSet);
        return EvaluatePostResponse(response.IsSuccessStatusCode, html);
    }

    private async Task<BbsPostResult> PostJpnknAsync(
        BbsTarget target, BbsPostRequest postRequest, CancellationToken cancellationToken)
    {
        var writeUrl = target.GetWriteUrl()!;
        var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var baseFields = new List<KeyValuePair<string, string>>
        {
            new("bbs", target.Board),
            new("key", target.ThreadKey!),
            new("time", unixTime),
            new("FROM", postRequest.Name),
            new("mail", postRequest.Mail),
            new("MESSAGE", postRequest.Message),
            new("submit", "書き込む"),
        };

        var (ok, html) = await SendJpnknPostAsync(writeUrl, target.ThreadUrl, baseFields, cancellationToken)
            .ConfigureAwait(false);
        var firstEvaluation = EvaluatePostResponse(ok, html);
        if (firstEvaluation.Succeeded || !LooksLikeConfirmationPage(html))
        {
            return firstEvaluation;
        }

        // Standard 2ch/jpnkn confirmation flow: resubmit once with confirmation hidden inputs and received cookies.
        var confirmFields = new List<KeyValuePair<string, string>>(baseFields);
        foreach (Match match in HiddenInputRegex().Matches(html))
        {
            var name = match.Groups["name"].Value;
            var value = WebUtility.HtmlDecode(match.Groups["value"].Value);
            if (!string.IsNullOrEmpty(name) &&
                !confirmFields.Any(kv => kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                confirmFields.Add(new(name, value));
            }
        }

        var (confirmOk, confirmHtml) = await SendJpnknPostAsync(writeUrl, target.ThreadUrl, confirmFields, cancellationToken)
            .ConfigureAwait(false);
        return EvaluatePostResponse(confirmOk, confirmHtml);
    }

    private async Task<(bool Ok, string Html)> SendJpnknPostAsync(
        string writeUrl,
        string? refererUrl,
        IReadOnlyList<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken)
    {
        var encodedBody = BbsTextCodec.EncodeFormBody(fields, BbsTextCodec.ShiftJis);
        using var request = new HttpRequestMessage(HttpMethod.Post, writeUrl)
        {
            Content = new ByteArrayContent(Encoding.ASCII.GetBytes(encodedBody)),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        if (refererUrl is not null)
        {
            request.Headers.Referrer = new Uri(refererUrl);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var html = BbsTextCodec.Decode(bytes, BbsBoardKind.Jpnkn, response.Content.Headers.ContentType?.CharSet);
        return (response.IsSuccessStatusCode, html);
    }

    internal static BbsPostResult EvaluatePostResponse(bool isSuccessStatusCode, string html)
    {
        if (html.Contains("書きこみました", StringComparison.Ordinal) ||
            html.Contains("書き込みました", StringComparison.Ordinal) ||
            html.Contains("2ch_X:true", StringComparison.OrdinalIgnoreCase))
        {
            return new BbsPostResult(true);
        }

        var titleMatch = HtmlTitleRegex().Match(html);
        var title = titleMatch.Success ? BbsTextCodec.ConvertHtmlToPlainText(titleMatch.Groups["title"].Value) : string.Empty;
        if (title.Contains("書きこみました", StringComparison.Ordinal) ||
            title.Contains("書き込みました", StringComparison.Ordinal))
        {
            return new BbsPostResult(true);
        }

        if (!isSuccessStatusCode)
        {
            return new BbsPostResult(false, string.IsNullOrEmpty(title) ? "書き込みに失敗しました。" : title);
        }

        var plain = BbsTextCodec.ConvertHtmlToPlainText(html);
        var summary = !string.IsNullOrWhiteSpace(title)
            ? title
            : plain.Length > 120
                ? plain[..120]
                : plain;
        return new BbsPostResult(false, string.IsNullOrWhiteSpace(summary) ? "書き込みに失敗しました。" : summary);
    }

    private static bool LooksLikeConfirmationPage(string html) =>
        html.Contains("書きこみ確認", StringComparison.Ordinal) ||
        html.Contains("書き込み確認", StringComparison.Ordinal) ||
        html.Contains("2ch_X:cookie", StringComparison.OrdinalIgnoreCase) ||
        html.Contains("承諾して書き込む", StringComparison.Ordinal);

    [GeneratedRegex(@"<title[^>]*>(?<title>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTitleRegex();

    [GeneratedRegex(@"<input[^>]*type\s*=\s*[""']?hidden[""']?[^>]*name\s*=\s*[""'](?<name>[^""']+)[""'][^>]*value\s*=\s*[""'](?<value>[^""']*)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HiddenInputRegex();
}
