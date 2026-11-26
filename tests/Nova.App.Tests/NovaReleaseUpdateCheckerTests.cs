using System.Net;
using System.Text;

namespace PeerCast.Nova.App.Tests;

public sealed class NovaReleaseUpdateCheckerTests
{
    [Theory]
    [InlineData("0.2.0", 0, 2, 0, null)]
    [InlineData("v0.2.1", 0, 2, 1, null)]
    [InlineData("V1.4.2+abc123", 1, 4, 2, null)]
    [InlineData("v1.0.0-beta.2+meta", 1, 0, 0, "beta.2")]
    [InlineData("0.3", 0, 3, 0, null)]
    public void TryParse_ValidVersionsSucceed(
        string raw,
        int expectedMajor,
        int expectedMinor,
        int expectedPatch,
        string? expectedPreRelease)
    {
        Assert.True(NovaSemanticVersion.TryParse(raw, out var version));
        Assert.Equal(expectedMajor, version.Major);
        Assert.Equal(expectedMinor, version.Minor);
        Assert.Equal(expectedPatch, version.Patch);
        Assert.Equal(expectedPreRelease, version.PreRelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("v1")]
    [InlineData("v1.2.3-")]
    [InlineData("abc.def.ghi")]
    public void TryParse_InvalidVersionsReturnFalse(string? raw)
    {
        Assert.False(NovaSemanticVersion.TryParse(raw, out _));
    }

    [Fact]
    public void CompareTo_FollowsSemanticVersionPrecedence()
    {
        Assert.True(NovaSemanticVersion.TryParse("v0.3.0", out var v030));
        Assert.True(NovaSemanticVersion.TryParse("v0.2.1", out var v021));
        Assert.True(NovaSemanticVersion.TryParse("v0.3.0-beta.1", out var v030Beta1));
        Assert.True(NovaSemanticVersion.TryParse("v0.3.0-beta.2", out var v030Beta2));
        Assert.True(NovaSemanticVersion.TryParse("v0.3.0-beta.10", out var v030Beta10));

        Assert.True(v030.CompareTo(v021) > 0);
        Assert.True(v030.CompareTo(v030Beta10) > 0);
        Assert.True(v030Beta10.CompareTo(v030Beta2) > 0);
        Assert.True(v030Beta2.CompareTo(v030Beta1) > 0);
        Assert.Equal(0, v030.CompareTo(new NovaSemanticVersion(0, 3, 0)));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_PrefersInstallerAssetWhenNewerReleaseExists()
    {
        const string json = """
            {
              "tag_name": "v0.3.0",
              "html_url": "https://github.com/Matsuda7144/PeerCast-Nova/releases/tag/v0.3.0",
              "draft": false,
              "prerelease": false,
              "published_at": "2026-11-26T12:00:00Z",
              "assets": [
                {
                  "name": "PeerCast-Nova-0.3.0-win-x64-self-contained.zip",
                  "browser_download_url": "https://github.com/Matsuda7144/PeerCast-Nova/releases/download/v0.3.0/PeerCast-Nova-0.3.0-win-x64-self-contained.zip"
                },
                {
                  "name": "PeerCast-Nova-0.3.0-win-x64-setup.exe",
                  "browser_download_url": "https://github.com/Matsuda7144/PeerCast-Nova/releases/download/v0.3.0/PeerCast-Nova-0.3.0-win-x64-setup.exe"
                }
              ]
            }
            """;

        using var handler = new StubHttpMessageHandler(static _ => JsonResponse(json));
        using var checker = new NovaReleaseUpdateChecker(handler, currentVersion: new NovaSemanticVersion(0, 2, 0));

        var result = await checker.CheckForUpdatesAsync();

        Assert.Equal(NovaUpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal(new NovaSemanticVersion(0, 3, 0), result.LatestVersion);
        Assert.Equal("https://github.com/Matsuda7144/PeerCast-Nova/releases/tag/v0.3.0", result.ReleasePageUrl);
        Assert.Equal(
            "https://github.com/Matsuda7144/PeerCast-Nova/releases/download/v0.3.0/PeerCast-Nova-0.3.0-win-x64-setup.exe",
            result.InstallerDownloadUrl);
        Assert.Contains("v0.3.0", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_FallsBackToSelfContainedZipWhenInstallerIsAbsent()
    {
        const string json = """
            {
              "tag_name": "v0.2.1",
              "html_url": "https://github.com/Matsuda7144/PeerCast-Nova/releases/tag/v0.2.1",
              "draft": false,
              "prerelease": false,
              "assets": [
                {
                  "name": "PeerCast-Nova-0.2.1-win-x64-self-contained.zip",
                  "browser_download_url": "https://github.com/Matsuda7144/PeerCast-Nova/releases/download/v0.2.1/PeerCast-Nova-0.2.1-win-x64-self-contained.zip"
                }
              ]
            }
            """;

        using var handler = new StubHttpMessageHandler(static _ => JsonResponse(json));
        using var checker = new NovaReleaseUpdateChecker(handler, currentVersion: new NovaSemanticVersion(0, 2, 0));

        var result = await checker.CheckForUpdatesAsync();

        Assert.Equal(NovaUpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal(
            "https://github.com/Matsuda7144/PeerCast-Nova/releases/download/v0.2.1/PeerCast-Nova-0.2.1-win-x64-self-contained.zip",
            result.InstallerDownloadUrl);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsUpToDateForSameVersionOrDraftOrPreRelease()
    {
        const string sameVersionJson = """
            {
              "tag_name": "v0.2.0",
              "html_url": "https://github.com/Matsuda7144/PeerCast-Nova/releases/tag/v0.2.0",
              "draft": false,
              "prerelease": false,
              "assets": []
            }
            """;

        using (var handler = new StubHttpMessageHandler(static _ => JsonResponse(sameVersionJson)))
        using (var checker = new NovaReleaseUpdateChecker(handler, currentVersion: new NovaSemanticVersion(0, 2, 0)))
        {
            var result = await checker.CheckForUpdatesAsync();
            Assert.Equal(NovaUpdateCheckStatus.UpToDate, result.Status);
        }

        const string preReleaseJson = """
            {
              "tag_name": "v0.3.0-rc.1",
              "html_url": "https://github.com/Matsuda7144/PeerCast-Nova/releases/tag/v0.3.0-rc.1",
              "draft": false,
              "prerelease": true,
              "assets": []
            }
            """;

        using (var handler = new StubHttpMessageHandler(static _ => JsonResponse(preReleaseJson)))
        using (var checker = new NovaReleaseUpdateChecker(handler, currentVersion: new NovaSemanticVersion(0, 2, 0)))
        {
            var result = await checker.CheckForUpdatesAsync();
            Assert.Equal(NovaUpdateCheckStatus.UpToDate, result.Status);
            Assert.Null(result.InstallerDownloadUrl);
        }

        const string draftJson = """
            {
              "tag_name": "v0.3.0",
              "html_url": "https://github.com/Matsuda7144/PeerCast-Nova/releases/tag/v0.3.0",
              "draft": true,
              "prerelease": false,
              "assets": []
            }
            """;

        using (var handler = new StubHttpMessageHandler(static _ => JsonResponse(draftJson)))
        using (var checker = new NovaReleaseUpdateChecker(handler, currentVersion: new NovaSemanticVersion(0, 2, 0)))
        {
            var result = await checker.CheckForUpdatesAsync();
            Assert.Equal(NovaUpdateCheckStatus.UpToDate, result.Status);
        }
    }

    [Fact]
    public async Task CheckForUpdatesAsync_RejectsUntrustedExternalUrlsAndHttpScheme()
    {
        const string untrustedJson = """
            {
              "tag_name": "v0.9.0",
              "html_url": "https://evil.example/PeerCast-Nova/releases/tag/v0.9.0",
              "draft": false,
              "prerelease": false,
              "assets": [
                {
                  "name": "PeerCast-Nova-0.9.0-win-x64-setup.exe",
                  "browser_download_url": "http://github.com/Matsuda7144/PeerCast-Nova/releases/download/v0.9.0/PeerCast-Nova-0.9.0-win-x64-setup.exe"
                }
              ]
            }
            """;

        using var handler = new StubHttpMessageHandler(static _ => JsonResponse(untrustedJson));
        using var checker = new NovaReleaseUpdateChecker(handler, currentVersion: new NovaSemanticVersion(0, 2, 0));

        var result = await checker.CheckForUpdatesAsync();

        Assert.Equal(NovaUpdateCheckStatus.UpToDate, result.Status);
        Assert.Null(result.ReleasePageUrl);
        Assert.Null(result.InstallerDownloadUrl);

        Assert.False(NovaReleaseUpdateChecker.IsApprovedReleasePageUrl("http://github.com/Matsuda7144/PeerCast-Nova/releases/tag/v0.9.0"));
        Assert.False(NovaReleaseUpdateChecker.IsApprovedReleasePageUrl("https://evil.example/releases/tag/v0.9.0"));
        Assert.False(NovaReleaseUpdateChecker.TryOpenApprovedReleaseUrl("https://evil.example/releases/tag/v0.9.0"));
        Assert.True(NovaReleaseUpdateChecker.IsApprovedReleasePageUrl("https://github.com/Matsuda7144/PeerCast-Nova/releases/tag/v0.3.0"));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, nameof(NovaUpdateCheckStatus.NoReleasePublished))]
    [InlineData(HttpStatusCode.Forbidden, nameof(NovaUpdateCheckStatus.Failed))]
    [InlineData(HttpStatusCode.TooManyRequests, nameof(NovaUpdateCheckStatus.Failed))]
    [InlineData(HttpStatusCode.InternalServerError, nameof(NovaUpdateCheckStatus.Failed))]
    public async Task CheckForUpdatesAsync_MapsHttpStatusCodesSafely(
        HttpStatusCode statusCode,
        string expectedStatus)
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(statusCode));
        using var checker = new NovaReleaseUpdateChecker(handler, currentVersion: new NovaSemanticVersion(0, 2, 0));

        var result = await checker.CheckForUpdatesAsync();

        Assert.Equal(Enum.Parse<NovaUpdateCheckStatus>(expectedStatus), result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ConvertsNetworkErrorsInvalidJsonAndOversizedPayloadsToFailed()
    {
        using (var networkHandler = new StubHttpMessageHandler(static _ => throw new HttpRequestException("offline")))
        using (var checker = new NovaReleaseUpdateChecker(networkHandler, currentVersion: new NovaSemanticVersion(0, 2, 0)))
        {
            var result = await checker.CheckForUpdatesAsync();
            Assert.Equal(NovaUpdateCheckStatus.Failed, result.Status);
        }

        using (var invalidJsonHandler = new StubHttpMessageHandler(static _ => JsonResponse("{ invalid-json")))
        using (var checker = new NovaReleaseUpdateChecker(invalidJsonHandler, currentVersion: new NovaSemanticVersion(0, 2, 0)))
        {
            var result = await checker.CheckForUpdatesAsync();
            Assert.Equal(NovaUpdateCheckStatus.Failed, result.Status);
        }

        var oversizedPayload = new string('a', NovaReleaseUpdateChecker.MaxResponseBytes + 16);
        using (var oversizedHandler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(oversizedPayload)),
        }))
        using (var checker = new NovaReleaseUpdateChecker(oversizedHandler, currentVersion: new NovaSemanticVersion(0, 2, 0)))
        {
            var result = await checker.CheckForUpdatesAsync();
            Assert.Equal(NovaUpdateCheckStatus.Failed, result.Status);
        }
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
