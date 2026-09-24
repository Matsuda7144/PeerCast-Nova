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

    private readonly Lazy<string> _executablePath;

    public PcrPlayerExternalPlayerLauncher(string? explicitPath)
        : this(() => ResolveExecutablePath(
            explicitPath,
            Environment.GetEnvironmentVariable(ExecutablePathEnvironmentVariable),
            Environment.GetEnvironmentVariable("PATH")))
    {
    }

    internal PcrPlayerExternalPlayerLauncher(Func<string> executablePathResolver)
    {
        ArgumentNullException.ThrowIfNull(executablePathResolver);
        _executablePath = new(
            executablePathResolver,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

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
        string streamUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamUrl);
        if (!Uri.TryCreate(streamUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) ||
            !uri.IsLoopback ||
            !uri.AbsolutePath.StartsWith("/stream/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "PCRPlayer must receive a loopback HTTP-FLV stream URL.",
                nameof(streamUrl));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(streamUrl);
        return startInfo;
    }

    public void ValidateConfiguration() => _ = _executablePath.Value;

    public IDailyExternalPlayerLifetime Launch(string streamUrl)
    {
        var process = new Process
        {
            StartInfo = CreateStartInfo(_executablePath.Value, streamUrl),
        };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("PCRPlayer did not start.");
            }

            return new SystemPcrPlayerProcessLifetime(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private static string ResolveConfiguredPath(string configuredPath, string? searchPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new PcrPlayerExecutableResolutionException(
                "The configured PCRPlayer path is empty. Specify " +
                "--pcrplayer-executable <path> or remove the invalid override.");
        }

        var normalized = configuredPath.Trim();
        if (!string.Equals(
                Path.GetFileName(normalized),
                ExecutableFileName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new PcrPlayerExecutableResolutionException(
                "The configured executable must be PCRPlayer64.exe. Correct " +
                "--pcrplayer-executable or NOVA_PCRPLAYER_EXE; no fallback was attempted.");
        }

        if (string.Equals(normalized, ExecutableFileName, StringComparison.OrdinalIgnoreCase))
        {
            return FindOnPath(normalized, searchPath) ??
                throw new PcrPlayerExecutableResolutionException(
                    $"{ExecutableFileName} was not found on PATH. Correct the configured " +
                    "executable path; no fallback was attempted.");
        }

        string absolutePath;
        try
        {
            absolutePath = Path.GetFullPath(normalized);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new PcrPlayerExecutableResolutionException(
                "The configured PCRPlayer path is invalid. Correct " +
                "--pcrplayer-executable or NOVA_PCRPLAYER_EXE; no fallback was attempted.");
        }

        return File.Exists(absolutePath)
            ? absolutePath
            : throw new PcrPlayerExecutableResolutionException(
                "The configured PCRPlayer executable was not found. Correct " +
                "--pcrplayer-executable or NOVA_PCRPLAYER_EXE; no fallback was attempted.");
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
