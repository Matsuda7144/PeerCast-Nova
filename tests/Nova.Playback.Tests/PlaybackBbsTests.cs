using System.Net;
using System.Net.Http.Headers;
using System.Text;
using PeerCast.Nova.Playback.Bbs;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackBbsTests
{
    [Theory]
    [InlineData("https://jbbs.shitaraba.net/game/12345/", BbsBoardKind.Shitaraba, "game", "12345", null)]
    [InlineData("http://jbbs.livedoor.jp/bbs/read.cgi/game/12345/1700000000/l50", BbsBoardKind.Shitaraba, "game", "12345", "1700000000")]
    [InlineData("https://bbs.jpnkn.com/nijifunbbs/", BbsBoardKind.Jpnkn, null, "nijifunbbs", null)]
    [InlineData("http://bbs.jpnkn.com/test/read.cgi/nijifunbbs/1700000001/l50", BbsBoardKind.Jpnkn, null, "nijifunbbs", "1700000001")]
    public void UrlParserRecognizesShitarabaAndJpnknBoardsAndThreads(
        string url, BbsBoardKind expectedKind, string? expectedCategory, string expectedBoard, string? expectedThread)
    {
        Assert.True(BbsUrlParser.TryParse(url, out var target));
        Assert.NotNull(target);
        Assert.Equal(expectedKind, target.Kind);
        Assert.Equal(expectedCategory, target.Category);
        Assert.Equal(expectedBoard, target.Board);
        Assert.Equal(expectedThread, target.ThreadKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/game/12345/")]
    [InlineData("https://bbs.jpnkn.com/test/")]
    [InlineData("ftp://jbbs.shitaraba.net/game/12345/")]
    public void UrlParserRejectsUnsupportedUrls(string? url)
    {
        Assert.False(BbsUrlParser.TryParse(url, out var target));
        Assert.Null(target);
    }

    [Fact]
    public void ParsersDecodeShitarabaSubjectAndRawModePosts()
    {
        var subject = "1700000002.cgi,自由時間#002(219)\n1700000001.cgi,自由時間#001(1000)\n";
        var threads = BbsParsers.ParseSubject(subject, BbsBoardKind.Shitaraba);

        Assert.Equal(2, threads.Count);
        Assert.Equal(new BbsThreadSummary("1700000002", "自由時間#002", 219), threads[0]);
        Assert.Equal(new BbsThreadSummary("1700000001", "自由時間#001", 1000), threads[1]);

        var rawMode =
            "218<>多分私は774人目だと思うから<>sage<>2026/09/14(月) 20:41:07<>久しぶりに練習してる姿見た気がする<>自由時間#002<>wUDpE/D2\n" +
            "219<>多分私は774人目だと思うから<>sage<>2026/11/22(日) 20:35:06<>練習<br>頑張ってください&gt;&gt;218<><>MYKOiq52\n";
        var posts = BbsParsers.ParseShitarabaRawMode(rawMode);

        Assert.Equal(2, posts.Count);
        Assert.Equal(218, posts[0].Number);
        Assert.Equal("自由時間#002", posts[0].ThreadTitle);
        Assert.Equal("wUDpE/D2", posts[0].Id);
        Assert.Equal(219, posts[1].Number);
        Assert.Equal("練習\n頑張ってください>>218", posts[1].Body);
        Assert.Equal("219: 多分私は774人目だと思うから [sage] 2026/11/22(日) 20:35:06 ID:MYKOiq52", posts[1].FormatHeader());
        Assert.Equal(">>219 練習 頑張ってください>>218", posts[1].FormatCompactSingleLine());
    }

    [Fact]
    public void ParsersDecodeJpnknSubjectAndDatPosts()
    {
        var subject = "1700000010.dat<>モンキーDルフィ (901)\n";
        var threads = BbsParsers.ParseSubject(subject, BbsBoardKind.Jpnkn);

        Assert.Single(threads);
        Assert.Equal(new BbsThreadSummary("1700000010", "モンキーDルフィ", 901), threads[0]);

        var dat =
            "<b>変態★</b><><>2025/12/02(火) 21:02:44 ID:???<>&gt;&gt;210<br>テスト本文<>モンキーDルフィ\n" +
            "名無し<>sage<>2025/12/04(木) 22:05:33 ID:AbCdEf12<>2行目<>\n";
        var posts = BbsParsers.ParseJpnknDat(dat, startNumber: 211);

        Assert.Equal(2, posts.Count);
        Assert.Equal(211, posts[0].Number);
        Assert.Equal("変態★", posts[0].Name);
        Assert.Equal("???", posts[0].Id);
        Assert.Equal(">>210\nテスト本文", posts[0].Body);
        Assert.Equal("モンキーDルフィ", posts[0].ThreadTitle);
        Assert.Equal(212, posts[1].Number);
        Assert.Equal("AbCdEf12", posts[1].Id);
    }

    [Fact]
    public async Task ShitarabaClientFetchesIncrementallyAndPostsInEucJp()
    {
        var requests = new List<(string Method, string Url, string? Body, string? Referer)>();
        using var handler = new StubHttpHandler(async (request, token) =>
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(token);
            requests.Add((request.Method.Method, request.RequestUri!.AbsoluteUri, body, request.Headers.Referrer?.AbsoluteUri));

            if (request.RequestUri.AbsoluteUri.EndsWith("/rawmode.cgi/game/12345/1700000002/1-", StringComparison.Ordinal))
            {
                return CreateEncodedResponse(
                    "1<>名無し<>sage<>2026/11/22(日) 20:00:00<>最初のレス<>テストスレ<>ID1\n",
                    BbsTextCodec.EucJp,
                    "text/plain; charset=EUC-JP");
            }

            if (request.RequestUri.AbsoluteUri.EndsWith("/rawmode.cgi/game/12345/1700000002/2-", StringComparison.Ordinal))
            {
                return CreateEncodedResponse(
                    "2<>名無し<>sage<>2026/11/22(日) 20:01:00<>追加レス<><>ID2\n",
                    BbsTextCodec.EucJp,
                    "text/plain; charset=EUC-JP");
            }

            if (request.Method == HttpMethod.Post)
            {
                return CreateEncodedResponse(
                    "<html><head><title>書きこみました。</title></head><body>書きこみました。</body></html>",
                    BbsTextCodec.EucJp,
                    "text/html; charset=EUC-JP");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        Assert.True(BbsUrlParser.TryParse("https://jbbs.shitaraba.net/bbs/read.cgi/game/12345/1700000002/", out var target));
        using var client = new PlaybackBbsClient(handler);

        var first = await client.FetchPostsAsync(target!, BbsFetchState.Initial, CancellationToken.None);
        Assert.Single(first.AddedPosts);
        Assert.Equal("テストスレ", first.ThreadTitle);
        Assert.Equal(1, first.NextState.ExistingPostCount);

        var second = await client.FetchPostsAsync(target!, first.NextState, CancellationToken.None);
        Assert.Single(second.AddedPosts);
        Assert.Equal(2, second.AddedPosts[0].Number);
        Assert.Equal(2, second.NextState.ExistingPostCount);

        var postResult = await client.PostAsync(target!, new BbsPostRequest("名前", "sage", "1行目\n2行目"), CancellationToken.None);
        Assert.True(postResult.Succeeded);
        var postReq = requests[^1];
        Assert.Equal("https://jbbs.shitaraba.net/bbs/write.cgi/game/12345/1700000002/", postReq.Url);
        Assert.Equal("https://jbbs.shitaraba.net/bbs/read.cgi/game/12345/1700000002/", postReq.Referer);
        Assert.Contains("%0D%0A", postReq.Body, StringComparison.Ordinal);
        Assert.Contains("DIR=game&BBS=12345&KEY=1700000002", postReq.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JpnknClientSupportsRangeIncrementalFetchAndConfirmationRetry()
    {
        var postAttempts = 0;
        long? observedRangeFrom = null;
        var datLine1 = BbsTextCodec.ShiftJis.GetBytes("名無し<>sage<>2026/11/22(日) 20:00:00 ID:AAA<>レス1<>JPNKNスレ\n");
        var datLine2 = BbsTextCodec.ShiftJis.GetBytes("名無し<>sage<>2026/11/22(日) 20:01:00 ID:BBB<>レス2<>\n");

        using var handler = new StubHttpHandler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith(".dat", StringComparison.Ordinal))
            {
                if (request.Headers.Range is { } range)
                {
                    observedRangeFrom = range.Ranges.First().From;
                    var partial = new HttpResponseMessage(HttpStatusCode.PartialContent)
                    {
                        Content = new ByteArrayContent(datLine2),
                    };
                    partial.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain; charset=Shift_JIS");
                    return partial;
                }

                var full = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(datLine1),
                };
                full.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain; charset=Shift_JIS");
                full.Content.Headers.LastModified = new DateTimeOffset(2026, 11, 22, 11, 0, 0, TimeSpan.Zero);
                return full;
            }

            if (request.Method == HttpMethod.Post)
            {
                var body = await request.Content!.ReadAsStringAsync(token);
                postAttempts++;
                if (postAttempts == 1)
                {
                    return CreateEncodedResponse(
                        "<html><head><title>■ 書きこみ確認 ■</title></head><body><input type=\"hidden\" name=\"suka\" value=\"pontan\"></body></html>",
                        BbsTextCodec.ShiftJis,
                        "text/html; charset=Shift_JIS");
                }

                Assert.Contains("suka=pontan", body, StringComparison.Ordinal);
                return CreateEncodedResponse(
                    "<html><head><title>書きこみました。</title></head><body>書きこみました。</body></html>",
                    BbsTextCodec.ShiftJis,
                    "text/html; charset=Shift_JIS");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        Assert.True(BbsUrlParser.TryParse("https://bbs.jpnkn.com/test/read.cgi/nijifunbbs/1700000010/", out var target));
        using var client = new PlaybackBbsClient(handler);

        var first = await client.FetchPostsAsync(target!, BbsFetchState.Initial, CancellationToken.None);
        Assert.Single(first.AddedPosts);
        Assert.Equal(datLine1.Length, first.NextState.ExistingDatByteLength);

        var second = await client.FetchPostsAsync(target!, first.NextState, CancellationToken.None);
        Assert.Equal(datLine1.Length, observedRangeFrom);
        Assert.False(second.ResetExisting);
        Assert.Single(second.AddedPosts);
        Assert.Equal(2, second.AddedPosts[0].Number);
        Assert.Equal("レス2", second.AddedPosts[0].Body);

        var postResult = await client.PostAsync(target!, new BbsPostRequest("", "sage", "テスト投稿"), CancellationToken.None);
        Assert.True(postResult.Succeeded);
        Assert.Equal(2, postAttempts);
    }

    [Fact]
    public async Task SessionAutoSelectsActiveThreadAndEmitsNewPostsOnlyAfterInitialLoad()
    {
        var fetchCalls = 0;
        var stubClient = new FakeBbsClient(
            threads:
            [
                new("100", "満杯スレ", 1000),
                new("200", "現行スレ", 42),
            ],
            fetchPosts: (target, state) =>
            {
                fetchCalls++;
                if (fetchCalls == 1)
                {
                    return new BbsFetchResult(
                        [new BbsPost(1, "名無し", "sage", "2026/11/22 20:00:00", "AAA", "初期レス", "現行スレ")],
                        ResetExisting: false,
                        NotModified: false,
                        ThreadTitle: "現行スレ",
                        NextState: state with { ExistingPostCount = 1 });
                }

                return new BbsFetchResult(
                    [new BbsPost(2, "名無し", "sage", "2026/11/22 20:01:00", "BBB", "新着レス")],
                    ResetExisting: false,
                    NotModified: false,
                    ThreadTitle: null,
                    NextState: state with { ExistingPostCount = 2 });
            });

        Assert.True(BbsUrlParser.TryParse("https://jbbs.shitaraba.net/game/12345/", out var boardTarget));
        await using var session = new PlaybackBbsSession(boardTarget!, stubClient, TimeSpan.FromMinutes(10));
        var receivedBatches = new List<IReadOnlyList<BbsPost>>();
        session.NewPostsReceived += receivedBatches.Add;

        await session.RefreshAsync();
        Assert.Equal("200", session.Snapshot.ThreadKey);
        Assert.Equal("[ 現行スレ ] (1)", session.Snapshot.FormatThreadBarText());
        Assert.Empty(receivedBatches);

        await session.RefreshAsync();
        Assert.Equal("[ 現行スレ ] (2)", session.Snapshot.FormatThreadBarText());
        Assert.Single(receivedBatches);
        Assert.Equal(2, receivedBatches[0][0].Number);
    }

    private static HttpResponseMessage CreateEncodedResponse(string text, Encoding encoding, string contentType)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(encoding.GetBytes(text)),
        };
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return response;
    }

    private sealed class StubHttpHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }

    private sealed class FakeBbsClient(
        IReadOnlyList<BbsThreadSummary> threads,
        Func<BbsTarget, BbsFetchState, BbsFetchResult> fetchPosts) : IPlaybackBbsClient
    {
        public Task<IReadOnlyList<BbsThreadSummary>> GetThreadsAsync(BbsTarget target, CancellationToken cancellationToken) =>
            Task.FromResult(threads);

        public Task<BbsFetchResult> FetchPostsAsync(BbsTarget target, BbsFetchState state, CancellationToken cancellationToken) =>
            Task.FromResult(fetchPosts(target, state));

        public Task<BbsPostResult> PostAsync(BbsTarget target, BbsPostRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new BbsPostResult(true));

        public void Dispose()
        {
        }
    }
}
