using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace PeerCast.Nova.App;

internal readonly record struct NovaSemanticVersion(
    int Major,
    int Minor,
    int Patch,
    string? PreRelease = null) : IComparable<NovaSemanticVersion>
{
    public static bool TryParse(string? raw, out NovaSemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        var buildSeparator = trimmed.IndexOf('+');
        if (buildSeparator >= 0)
        {
            trimmed = trimmed[..buildSeparator];
        }

        if (trimmed.Length == 0)
        {
            return false;
        }

        string corePart;
        string? preRelease = null;
        var preSeparator = trimmed.IndexOf('-');
        if (preSeparator >= 0)
        {
            corePart = trimmed[..preSeparator];
            var candidatePre = trimmed[(preSeparator + 1)..];
            if (string.IsNullOrWhiteSpace(candidatePre))
            {
                return false;
            }

            preRelease = candidatePre;
        }
        else
        {
            corePart = trimmed;
        }

        var segments = corePart.Split('.');
        if (segments.Length is < 2 or > 4)
        {
            return false;
        }

        if (!int.TryParse(segments[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
        {
            return false;
        }

        var patch = 0;
        if (segments.Length >= 3 &&
            !int.TryParse(segments[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch))
        {
            return false;
        }

        if (segments.Length == 4 &&
            !int.TryParse(segments[3], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        version = new NovaSemanticVersion(major, minor, patch, preRelease);
        return true;
    }

    public int CompareTo(NovaSemanticVersion other)
    {
        var majorCompare = Major.CompareTo(other.Major);
        if (majorCompare != 0)
        {
            return majorCompare;
        }

        var minorCompare = Minor.CompareTo(other.Minor);
        if (minorCompare != 0)
        {
            return minorCompare;
        }

        var patchCompare = Patch.CompareTo(other.Patch);
        if (patchCompare != 0)
        {
            return patchCompare;
        }

        if (PreRelease is null && other.PreRelease is null)
        {
            return 0;
        }

        if (PreRelease is null)
        {
            return 1;
        }

        if (other.PreRelease is null)
        {
            return -1;
        }

        var leftParts = PreRelease.Split('.');
        var rightParts = other.PreRelease.Split('.');
        var count = Math.Min(leftParts.Length, rightParts.Length);
        for (var i = 0; i < count; i++)
        {
            var leftIsNum = int.TryParse(leftParts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var leftNum);
            var rightIsNum = int.TryParse(rightParts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var rightNum);

            int partCompare;
            if (leftIsNum && rightIsNum)
            {
                partCompare = leftNum.CompareTo(rightNum);
            }
            else if (leftIsNum != rightIsNum)
            {
                partCompare = leftIsNum ? -1 : 1;
            }
            else
            {
                partCompare = string.CompareOrdinal(leftParts[i], rightParts[i]);
            }

            if (partCompare != 0)
            {
                return partCompare;
            }
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }

    public string ToTagString() =>
        $"v{Major}.{Minor}.{Patch}{(PreRelease is null ? string.Empty : $"-{PreRelease}")}";

    public override string ToString() => ToTagString();
}

internal enum NovaUpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    NoReleasePublished,
    Failed,
}

internal sealed record NovaUpdateCheckResult(
    NovaUpdateCheckStatus Status,
    NovaSemanticVersion CurrentVersion,
    NovaSemanticVersion? LatestVersion,
    string? ReleasePageUrl,
    string? InstallerDownloadUrl,
    DateTimeOffset? PublishedAt,
    string Message);

internal sealed class NovaReleaseUpdateChecker : IDisposable
{
    internal const string DefaultRepositoryOwner = "Matsuda7144";
    internal const string DefaultRepositoryName = "PeerCast-Nova";
    internal const int MaxResponseBytes = 512 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    internal static readonly Uri DefaultLatestReleaseApiUri =
        new($"https://api.github.com/repos/{DefaultRepositoryOwner}/{DefaultRepositoryName}/releases/latest");

    private const string ApprovedReleasePagePrefix =
        $"https://github.com/{DefaultRepositoryOwner}/{DefaultRepositoryName}/releases/";

    private const string ApprovedDownloadPrefix =
        $"https://github.com/{DefaultRepositoryOwner}/{DefaultRepositoryName}/releases/download/";

    private readonly HttpClient _httpClient;
    private readonly Uri _apiUri;
    private readonly NovaSemanticVersion _currentVersion;

    public NovaReleaseUpdateChecker(
        HttpMessageHandler? httpMessageHandler = null,
        Uri? apiUri = null,
        NovaSemanticVersion? currentVersion = null)
    {
        var handler = httpMessageHandler ?? new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
        _httpClient = new HttpClient(handler, disposeHandler: httpMessageHandler is null)
        {
            Timeout = RequestTimeout,
        };
        _apiUri = apiUri ?? DefaultLatestReleaseApiUri;
        _currentVersion = currentVersion ?? GetCurrentVersion();
    }

    public static NovaSemanticVersion GetCurrentVersion(Assembly? assembly = null)
    {
        var targetAssembly = assembly ?? typeof(NovaReleaseUpdateChecker).Assembly;
        var informational = targetAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (NovaSemanticVersion.TryParse(informational, out var parsedInfo))
        {
            return parsedInfo;
        }

        var assemblyVersion = targetAssembly.GetName().Version;
        if (assemblyVersion is not null)
        {
            return new NovaSemanticVersion(
                Math.Max(0, assemblyVersion.Major),
                Math.Max(0, assemblyVersion.Minor),
                Math.Max(0, assemblyVersion.Build));
        }

        return new NovaSemanticVersion(0, 2, 0);
    }

    public static bool IsApprovedReleasePageUrl(string? url) =>
        IsApprovedGitHubUrl(url, ApprovedReleasePagePrefix);

    public static bool IsApprovedDownloadUrl(string? url) =>
        IsApprovedGitHubUrl(url, ApprovedDownloadPrefix);

    public static bool TryOpenApprovedReleaseUrl(string? url)
    {
        if (!IsApprovedReleasePageUrl(url) && !IsApprovedDownloadUrl(url))
        {
            return false;
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo(url!)
            {
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<NovaUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _apiUri);
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue(
                "PeerCast-Nova",
                $"{_currentVersion.Major}.{_currentVersion.Minor}.{_currentVersion.Patch}"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new NovaUpdateCheckResult(
                    NovaUpdateCheckStatus.NoReleasePublished,
                    _currentVersion,
                    null,
                    null,
                    null,
                    null,
                    $"公開済みのリリースはまだありません（現在: {_currentVersion.ToTagString()}）。");
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                return new NovaUpdateCheckResult(
                    NovaUpdateCheckStatus.Failed,
                    _currentVersion,
                    null,
                    null,
                    null,
                    null,
                    "GitHub API のアクセス制限に達しました。しばらくしてから再度お試しください。");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new NovaUpdateCheckResult(
                    NovaUpdateCheckStatus.Failed,
                    _currentVersion,
                    null,
                    null,
                    null,
                    null,
                    $"更新確認に失敗しました（HTTP {(int)response.StatusCode}）。");
            }

            if (response.Content.Headers.ContentLength is > MaxResponseBytes)
            {
                return new NovaUpdateCheckResult(
                    NovaUpdateCheckStatus.Failed,
                    _currentVersion,
                    null,
                    null,
                    null,
                    null,
                    "更新情報の応答サイズが上限を超えています。");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var payload = await ReadBoundedAsync(stream, cancellationToken).ConfigureAwait(false);
            if (payload is null)
            {
                return new NovaUpdateCheckResult(
                    NovaUpdateCheckStatus.Failed,
                    _currentVersion,
                    null,
                    null,
                    null,
                    null,
                    "更新情報の応答サイズが上限を超えています。");
            }

            return ParseReleasePayload(payload);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return new NovaUpdateCheckResult(
                NovaUpdateCheckStatus.Failed,
                _currentVersion,
                null,
                null,
                null,
                null,
                "更新確認がタイムアウトしました。ネットワーク接続を確認してください。");
        }
        catch (HttpRequestException)
        {
            return new NovaUpdateCheckResult(
                NovaUpdateCheckStatus.Failed,
                _currentVersion,
                null,
                null,
                null,
                null,
                "GitHub Releases に接続できませんでした。ネットワーク接続を確認してください。");
        }
        catch (Exception)
        {
            return new NovaUpdateCheckResult(
                NovaUpdateCheckStatus.Failed,
                _currentVersion,
                null,
                null,
                null,
                null,
                "更新情報の取得中にエラーが発生しました。");
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private NovaUpdateCheckResult ParseReleasePayload(byte[] payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return CreateInvalidResponseResult();
            }

            var isDraft = root.TryGetProperty("draft", out var draftProp) && draftProp.ValueKind == JsonValueKind.True;
            var isPreRelease = root.TryGetProperty("prerelease", out var preProp) && preProp.ValueKind == JsonValueKind.True;
            var rawTag = root.TryGetProperty("tag_name", out var tagProp) && tagProp.ValueKind == JsonValueKind.String
                ? tagProp.GetString()
                : null;

            if (!NovaSemanticVersion.TryParse(rawTag, out var latestVersion))
            {
                return CreateInvalidResponseResult();
            }

            DateTimeOffset? publishedAt = null;
            if (root.TryGetProperty("published_at", out var pubProp) &&
                pubProp.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(pubProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDate))
            {
                publishedAt = parsedDate;
            }

            var rawHtmlUrl = root.TryGetProperty("html_url", out var htmlProp) && htmlProp.ValueKind == JsonValueKind.String
                ? htmlProp.GetString()
                : null;
            var approvedReleaseUrl = IsApprovedReleasePageUrl(rawHtmlUrl) ? rawHtmlUrl : null;

            if (isDraft || isPreRelease)
            {
                return new NovaUpdateCheckResult(
                    NovaUpdateCheckStatus.UpToDate,
                    _currentVersion,
                    latestVersion,
                    approvedReleaseUrl,
                    null,
                    publishedAt,
                    $"現在のバージョン {_currentVersion.ToTagString()} は最新です。");
            }

            var downloadUrl = ExtractPreferredDownloadUrl(root);
            if (latestVersion.CompareTo(_currentVersion) > 0 && approvedReleaseUrl is not null)
            {
                return new NovaUpdateCheckResult(
                    NovaUpdateCheckStatus.UpdateAvailable,
                    _currentVersion,
                    latestVersion,
                    approvedReleaseUrl,
                    downloadUrl,
                    publishedAt,
                    $"新しいバージョン {latestVersion.ToTagString()} が公開されています（現在: {_currentVersion.ToTagString()}）。");
            }

            return new NovaUpdateCheckResult(
                NovaUpdateCheckStatus.UpToDate,
                _currentVersion,
                latestVersion,
                approvedReleaseUrl,
                downloadUrl,
                publishedAt,
                $"現在のバージョン {_currentVersion.ToTagString()} は最新です。");
        }
        catch (JsonException)
        {
            return CreateInvalidResponseResult();
        }
    }

    private static string? ExtractPreferredDownloadUrl(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assetsProp) || assetsProp.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? fallbackZipUrl = null;
        foreach (var asset in assetsProp.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = asset.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                ? nameProp.GetString()
                : null;
            var browserUrl = asset.TryGetProperty("browser_download_url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String
                ? urlProp.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(name) || !IsApprovedDownloadUrl(browserUrl))
            {
                continue;
            }

            if (name.EndsWith("-win-x64-setup.exe", StringComparison.OrdinalIgnoreCase))
            {
                return browserUrl;
            }

            if (fallbackZipUrl is null &&
                name.EndsWith("-win-x64-self-contained.zip", StringComparison.OrdinalIgnoreCase))
            {
                fallbackZipUrl = browserUrl;
            }
        }

        return fallbackZipUrl;
    }

    private NovaUpdateCheckResult CreateInvalidResponseResult() =>
        new(
            NovaUpdateCheckStatus.Failed,
            _currentVersion,
            null,
            null,
            null,
            null,
            "GitHub Releases の応答形式を解釈できませんでした。");

    private static async Task<byte[]?> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var memory = new MemoryStream();
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return memory.ToArray();
            }

            if (memory.Length + read > MaxResponseBytes)
            {
                return null;
            }

            memory.Write(buffer, 0, read);
        }
    }

    private static bool IsApprovedGitHubUrl(string? url, string requiredPrefix)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return uri.AbsoluteUri.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
