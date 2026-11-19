using System.Diagnostics;

namespace PeerCast.Nova.App;

internal interface IDailyExternalPlayerLifetime : IAsyncDisposable
{
    Task Completion { get; }
}

internal sealed class PcrPlayerExternalPlayerLauncher
{
    internal const string ExecutablePathEnvironmentVariable = "NOVA_PCRPLAYER_EXE";
    internal const string ExecutableFileName = "PCRPlayer64.exe";

    private readonly Func<string?> _explicitPathProvider;

    public PcrPlayerExternalPlayerLauncher(string? explicitPath)
        : this(() => explicitPath)
    {
    }

    private PcrPlayerExternalPlayerLauncher(Func<string?> explicitPathProvider)
    {
        ArgumentNullException.ThrowIfNull(explicitPathProvider);
        _explicitPathProvider = explicitPathProvider;
    }

    internal static PcrPlayerExternalPlayerLauncher CreateWithPathProvider(
        Func<string?> explicitPathProvider) =>
        new(explicitPathProvider);

    internal static string ResolveExecutablePath(
        string? explicitPath,
        string? environmentPath,
        string? searchPath)
    {
        if (explicitPath is not null)
        {
            return ResolveConfiguredPath(explicitPath, searchPath);
        }

        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            return ResolveConfiguredPath(environmentPath, searchPath);
        }

        return FindOnPath(ExecutableFileName, searchPath) ??
            throw new PcrPlayerExecutableResolutionException(
                $"{ExecutableFileName} was not found. Configure " +
                "--pcrplayer-executable <path>, set " +
                $"{ExecutablePathEnvironmentVariable}, or add its directory to PATH.");
    }

    internal static ProcessStartInfo CreateStartInfo(
        string executablePath,
        string streamUrl,
        string? channelName = null,
        string? contactUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamUrl);
        if (!Uri.TryCreate(streamUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) ||
            !uri.IsLoopback ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !IsPlayerPath(uri.AbsolutePath))
        {
            throw new ArgumentException(
                "The external player must receive a loopback HTTP-FLV playback URL.",
                nameof(streamUrl));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(streamUrl);
        if (string.Equals(Path.GetFileName(executablePath), ExecutableFileName, StringComparison.OrdinalIgnoreCase) &&
            (channelName is not null || contactUrl is not null))
        {
            startInfo.ArgumentList.Add(new string((channelName ?? string.Empty).Take(512).Where(c => !char.IsControl(c)).ToArray()));
            startInfo.ArgumentList.Add(GenesisContactUrl.Normalize(contactUrl) ?? string.Empty);
        }
        return startInfo;
    }

    private static bool IsPlayerPath(string path)
    {
        var id = path.StartsWith("/stream/", StringComparison.Ordinal) ? path[8..] :
            path.StartsWith("/pls/", StringComparison.Ordinal) && path.EndsWith(".pls", StringComparison.Ordinal) ? path[5..^4] : string.Empty;
        return id.Length == 32 && Guid.TryParseExact(id, "N", out var value) && value != Guid.Empty;
    }

    public void ValidateConfiguration() => _ = ResolveCurrentExecutablePath();

    public IDailyExternalPlayerLifetime Launch(string streamUrl, string? channelName = null, string? contactUrl = null)
    {
        var process = new Process
        {
            StartInfo = CreateStartInfo(ResolveCurrentExecutablePath(), streamUrl, channelName, contactUrl),
        };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The external player did not start.");
            }

            return new SystemPcrPlayerProcessLifetime(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private string ResolveCurrentExecutablePath() =>
        ResolveExecutablePath(
            _explicitPathProvider(),
            Environment.GetEnvironmentVariable(ExecutablePathEnvironmentVariable),
            Environment.GetEnvironmentVariable("PATH"));

    private static string ResolveConfiguredPath(string configuredPath, string? searchPath)
    {
        var normalized = GenesisGuiNextPlaybackSettings.NormalizePath(configuredPath);
        if (normalized is null)
        {
            throw new PcrPlayerExecutableResolutionException(
                "The configured external player path is empty. Specify " +
                "--pcrplayer-executable <path> or remove the invalid override.");
        }

        // Preserve the legacy override that requests PCRPlayer by name on PATH.
        if (string.Equals(normalized, ExecutableFileName, StringComparison.OrdinalIgnoreCase))
        {
            return FindOnPath(normalized, searchPath) ??
                throw new PcrPlayerExecutableResolutionException(
                    $"{ExecutableFileName} was not found on PATH. Correct the configured " +
                    "executable path; no fallback was attempted.");
        }

        if (GenesisGuiNextPlaybackSettings.ValidateExecutablePath(normalized) is { } failure)
        {
            var reason = failure switch
            {
                GenesisGuiNextPlaybackSettingsUpdateResult.UnsupportedExecutableExtension => "must specify an .exe file",
                GenesisGuiNextPlaybackSettingsUpdateResult.ExecutableNotFound => "does not exist",
                GenesisGuiNextPlaybackSettingsUpdateResult.DirectorySelected => "points to a directory",
                GenesisGuiNextPlaybackSettingsUpdateResult.AccessDenied => "is not accessible",
                GenesisGuiNextPlaybackSettingsUpdateResult.InvalidPath => "must be a valid absolute path",
                _ => "could not be checked",
            };
            throw new PcrPlayerExecutableResolutionException(
                $"The configured external player path {reason}. Correct the path in Playback Settings, " +
                "--pcrplayer-executable, or NOVA_PCRPLAYER_EXE; no fallback was attempted.");
        }

        return Path.GetFullPath(normalized);
    }

    private static string? FindOnPath(string executableFileName, string? searchPath)
    {
        if (string.IsNullOrEmpty(searchPath))
        {
            return null;
        }

        foreach (var entry in searchPath.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = entry.Trim().Trim('"');
            if (directory.Length == 0)
            {
                continue;
            }

            try
            {
                var candidate = Path.GetFullPath(Path.Combine(directory, executableFileName));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Skip malformed PATH entries; search only directly in each entry.
            }
        }

        return null;
    }

    private sealed class SystemPcrPlayerProcessLifetime : IDailyExternalPlayerLifetime
    {
        private static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromSeconds(3);
        private readonly Process _process;
        private readonly Task _completion;
        private int _disposed;

        public SystemPcrPlayerProcessLifetime(Process process)
        {
            _process = process;
            _completion = process.WaitForExitAsync();
        }

        public Task Completion => _completion;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                if (!_completion.IsCompleted && _process.CloseMainWindow())
                {
                    try
                    {
                        await _completion.WaitAsync(GracefulCloseTimeout).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        // Keep shutdown graceful; never terminate the user process forcibly.
                    }
                }
            }
            finally
            {
                _process.Dispose();
            }
        }
    }
}

internal sealed class PcrPlayerExecutableResolutionException(string message) :
    InvalidOperationException(message);
