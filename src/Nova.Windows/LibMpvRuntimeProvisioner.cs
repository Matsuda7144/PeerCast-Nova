using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;

namespace PeerCast.Nova.Windows;

internal enum LibMpvSetupStage
{
    Fetching,
    Verifying,
    Extracting,
}

internal sealed record LibMpvSetupProgress(
    LibMpvSetupStage Stage,
    long BytesReceived,
    long? TotalBytes)
{
    public string FormatStatus() => Stage switch
    {
        LibMpvSetupStage.Fetching when TotalBytes is > 0 =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"公式 libmpv (LGPL {LibMpvRuntimeProvisioner.Version}) をダウンロード中… {(int)Math.Clamp(BytesReceived * 100L / TotalBytes.Value, 0, 100)}% ({BytesReceived / (1024.0 * 1024.0):F1} / {TotalBytes.Value / (1024.0 * 1024.0):F1} MB)"),
        LibMpvSetupStage.Fetching =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"公式 libmpv (LGPL {LibMpvRuntimeProvisioner.Version}) をダウンロード中… ({BytesReceived / (1024.0 * 1024.0):F1} MB)"),
        LibMpvSetupStage.Verifying =>
            "ダウンロードしたアーカイブの SHA-256 を検証中…",
        LibMpvSetupStage.Extracting =>
            "libmpv-2.dll を展開中…",
        _ => "公式 libmpv を導入中…",
    };
}

internal sealed record LibMpvSetupResult(
    bool Succeeded,
    string? LibraryPath = null,
    string? ErrorMessage = null,
    bool AlreadyPresent = false);

internal sealed record LibMpvAvailabilityStatus(
    bool ConfiguredExists,
    bool DetectedPresent,
    string FieldLabel,
    string ButtonText,
    string SavedStateBadge);

internal sealed class LibMpvRuntimeProvisioner
{
    public const string Version = "v0.42.0";
    public const string LibraryFileName = "libmpv-2.dll";
    public const string OfficialArchiveUrl =
        "https://github.com/mpv-player/mpv/releases/download/v0.42.0/libmpv-v0.42.0-x86_64-w64-mingw32-lgpl.zip";
    public const string OfficialSha256 =
        "8e5f77430efc53ef175804ba6618527e3af0448a0dcabf04d3cc72659d1a6fce";
    public const long MaxArchiveBytes = 128L * 1024 * 1024;
    private const string ApplicationDirectoryName = "PeerCast Nova";

    private readonly string _archiveUrl;
    private readonly string _expectedSha256;
    private readonly HttpMessageHandler? _handler;

    public LibMpvRuntimeProvisioner(
        string archiveUrl = OfficialArchiveUrl,
        string expectedSha256 = OfficialSha256,
        HttpMessageHandler? handler = null)
    {
        _archiveUrl = archiveUrl;
        _expectedSha256 = expectedSha256;
        _handler = handler;
    }

    public static string ResolveDefaultRuntimeDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = string.IsNullOrWhiteSpace(localAppData) ? AppContext.BaseDirectory : localAppData;
        return Path.Combine(root, ApplicationDirectoryName, "runtime", $"libmpv-{Version}-lgpl");
    }

    public static string? TryFindAvailableLibraryPath(string? baseDirectory = null, string? defaultRuntimeDirectory = null)
    {
        var appDir = baseDirectory ?? AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(appDir))
        {
            var besideExe = Path.Combine(appDir, LibraryFileName);
            if (File.Exists(besideExe))
            {
                return Path.GetFullPath(besideExe);
            }
        }

        var runtimeDir = defaultRuntimeDirectory ?? ResolveDefaultRuntimeDirectory();
        if (!string.IsNullOrWhiteSpace(runtimeDir))
        {
            var present = Path.Combine(runtimeDir, LibraryFileName);
            if (File.Exists(present))
            {
                return Path.GetFullPath(present);
            }
        }

        return null;
    }

    public static LibMpvAvailabilityStatus DescribeAvailability(
        string? draftPath,
        string? savedPath,
        string? detectedPath = null,
        Func<string, bool>? fileExists = null)
    {
        var exists = fileExists ?? File.Exists;
        var hasDraft = !string.IsNullOrWhiteSpace(draftPath) && exists(draftPath);
        var hasSaved = !string.IsNullOrWhiteSpace(savedPath) && exists(savedPath);
        var resolvedDetected = detectedPath ?? TryFindAvailableLibraryPath();
        var hasDetected = !string.IsNullOrWhiteSpace(resolvedDetected) && exists(resolvedDetected);

        var fieldLabel = hasDraft
            ? "libmpv DLL（導入済み）"
            : hasDetected
                ? "libmpv DLL（導入済み・未設定）"
                : "libmpv DLL（未導入）";

        var buttonText = hasDraft
            ? "公式 libmpv 導入済み"
            : hasDetected
                ? "導入済み libmpv を適用"
                : "公式 libmpv を自動導入";

        var savedBadge = hasSaved
            ? "導入済み"
            : string.IsNullOrWhiteSpace(savedPath)
                ? (hasDetected ? "未設定・導入済みあり" : "未導入")
                : "ファイルなし";

        return new LibMpvAvailabilityStatus(hasDraft, hasDetected, fieldLabel, buttonText, savedBadge);
    }

    public async Task<LibMpvSetupResult> ProvisionAsync(
        string targetDirectory,
        IProgress<LibMpvSetupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            return new LibMpvSetupResult(false, ErrorMessage: "導入先フォルダーを解決できませんでした。");
        }

        var fullTargetDir = Path.GetFullPath(targetDirectory);
        var targetDllPath = Path.Combine(fullTargetDir, LibraryFileName);
        if (File.Exists(targetDllPath))
        {
            return new LibMpvSetupResult(true, targetDllPath, AlreadyPresent: true);
        }

        Directory.CreateDirectory(fullTargetDir);
        var tempZipPath = Path.Combine(fullTargetDir, $".libmpv-archive-{Guid.NewGuid():N}.zip");
        var stagingDir = Path.Combine(fullTargetDir, $".libmpv-staging-{Guid.NewGuid():N}");

        try
        {
            var actualHash = await FetchAndHashAsync(tempZipPath, progress, cancellationToken).ConfigureAwait(false);
            progress?.Report(new LibMpvSetupProgress(LibMpvSetupStage.Verifying, 0, null));
            if (!string.Equals(actualHash, _expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new LibMpvSetupResult(
                    false,
                    ErrorMessage: "ダウンロードしたアーカイブの SHA-256 検証に失敗しました。ネットワークや配布元を確認してください。");
            }

            progress?.Report(new LibMpvSetupProgress(LibMpvSetupStage.Extracting, 0, null));
            Directory.CreateDirectory(stagingDir);
            var extractedDll = ExtractSafeRuntimeFiles(tempZipPath, stagingDir);
            if (extractedDll is null)
            {
                return new LibMpvSetupResult(
                    false,
                    ErrorMessage: $"アーカイブ内に {LibraryFileName} が見つかりませんでした。");
            }

            foreach (var file in Directory.GetFiles(stagingDir))
            {
                var destination = Path.Combine(fullTargetDir, Path.GetFileName(file));
                File.Move(file, destination, overwrite: true);
            }

            return File.Exists(targetDllPath)
                ? new LibMpvSetupResult(true, targetDllPath)
                : new LibMpvSetupResult(false, ErrorMessage: $"{LibraryFileName} の配置に失敗しました。");
        }
        catch (OperationCanceledException)
        {
            return new LibMpvSetupResult(false, ErrorMessage: "libmpv の自動導入をキャンセルしました。");
        }
        catch (InvalidDataException exception)
        {
            return new LibMpvSetupResult(false, ErrorMessage: exception.Message);
        }
        catch (HttpRequestException)
        {
            return new LibMpvSetupResult(
                false,
                ErrorMessage: "公式 libmpv のダウンロードに失敗しました。インターネット接続を確認してください。");
        }
        catch (Exception)
        {
            return new LibMpvSetupResult(
                false,
                ErrorMessage: "libmpv の自動導入中にエラーが発生しました。手動で参照設定をお試しください。");
        }
        finally
        {
            TryDeleteFile(tempZipPath);
            TryDeleteDirectory(stagingDir);
        }
    }

    private async Task<string> FetchAndHashAsync(
        string destinationZipPath,
        IProgress<LibMpvSetupProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var http = _handler is null
            ? new HttpClient { Timeout = TimeSpan.FromMinutes(5) }
            : new HttpClient(_handler, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PeerCastNova");

        using var response = await http.GetAsync(_archiveUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        if (totalBytes is > MaxArchiveBytes)
        {
            throw new InvalidDataException("アーカイブサイズが上限を超えています。");
        }

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(destinationZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81_920, useAsync: true);

        var buffer = new byte[81_920];
        long received = 0;
        var lastReportedPercent = -1;
        progress?.Report(new LibMpvSetupProgress(LibMpvSetupStage.Fetching, 0, totalBytes));

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            received += read;
            if (received > MaxArchiveBytes)
            {
                throw new InvalidDataException("アーカイブサイズが上限を超えています。");
            }

            hasher.AppendData(buffer, 0, read);
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

            var percent = totalBytes is > 0 ? (int)(received * 100L / totalBytes.Value) : -1;
            if (percent != lastReportedPercent || totalBytes is null)
            {
                lastReportedPercent = percent;
                progress?.Report(new LibMpvSetupProgress(LibMpvSetupStage.Fetching, received, totalBytes));
            }
        }

        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    private static string? ExtractSafeRuntimeFiles(string zipPath, string stagingDirectory)
    {
        string? extractedLibrary = null;
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            // Reject path traversal entries and only extract DLLs or top-level license files.
            var normalizedFull = entry.FullName.Replace('\\', '/');
            if (normalizedFull.Contains("..", StringComparison.Ordinal) || normalizedFull.StartsWith('/'))
            {
                throw new InvalidDataException("アーカイブに不正なパスが含まれています。");
            }

            var isTargetDll = string.Equals(entry.Name, LibraryFileName, StringComparison.OrdinalIgnoreCase);
            var isRootSupportFile = !normalizedFull.Contains('/', StringComparison.Ordinal) &&
                (entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                 entry.Name.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase));
            if (!isTargetDll && !isRootSupportFile)
            {
                continue;
            }

            var destinationPath = Path.Combine(stagingDirectory, entry.Name);
            entry.ExtractToFile(destinationPath, overwrite: true);
            if (isTargetDll)
            {
                extractedLibrary = destinationPath;
            }
        }

        return extractedLibrary;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
