namespace PeerCast.Nova.App;

internal sealed record GenesisGuiNextPlaybackPreferences(string? PcrPlayerExecutablePath)
{
    public static GenesisGuiNextPlaybackPreferences Default { get; } =
        new(PcrPlayerExecutablePath: null);
}

internal sealed class GenesisGuiNextPlaybackPathSource
{
    private string? _current;

    public string? Current => Volatile.Read(ref _current);

    internal void Set(string? path) => Interlocked.Exchange(ref _current, path);
}

internal enum GenesisGuiNextPlaybackSettingsUpdateResult
{
    Saved,
    Unchanged,
    InvalidPath,
    UnsupportedExecutableName,
    ExecutableNotFound,
    DirectorySelected,
    AccessDenied,
    PathCheckFailed,
    PersistenceFailed,
}

internal sealed class GenesisGuiNextPlaybackSettings
{
    private readonly object _gate = new();
    private readonly GenesisGuiNextPlaybackPathSource? _pathSource;
    private readonly Func<string, GenesisGuiNextPlaybackSettingsUpdateResult?> _validateExecutablePath;
    private string? _pcrPlayerExecutablePath;
    private Func<string?, bool>? _persist;

    public GenesisGuiNextPlaybackSettings(
        GenesisGuiNextPlaybackPreferences preferences,
        GenesisGuiNextPlaybackPathSource? pathSource = null,
        Func<string, GenesisGuiNextPlaybackSettingsUpdateResult?>? validateExecutablePath = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        _pcrPlayerExecutablePath = NormalizePath(preferences.PcrPlayerExecutablePath);
        _pathSource = pathSource;
        _validateExecutablePath = validateExecutablePath ?? ValidateExecutablePath;
        _pathSource?.Set(_pcrPlayerExecutablePath);
    }

    public string? PcrPlayerExecutablePath
    {
        get
        {
            lock (_gate)
            {
                return _pcrPlayerExecutablePath;
            }
        }
    }

    internal void BindPersistence(Func<string?, bool> persist)
    {
        ArgumentNullException.ThrowIfNull(persist);
        lock (_gate)
        {
            _persist = persist;
        }
    }

    public GenesisGuiNextPlaybackSettingsUpdateResult SetPcrPlayerExecutablePath(
        string? path)
    {
        var normalized = NormalizePath(path);
        if (normalized is not null && _validateExecutablePath(normalized) is { } validationFailure)
        {
            return validationFailure;
        }

        lock (_gate)
        {
            if (string.Equals(
                    _pcrPlayerExecutablePath,
                    normalized,
                    StringComparison.Ordinal))
            {
                return GenesisGuiNextPlaybackSettingsUpdateResult.Unchanged;
            }

            if (_persist is not null && !_persist(normalized))
            {
                return GenesisGuiNextPlaybackSettingsUpdateResult.PersistenceFailed;
            }

            _pcrPlayerExecutablePath = normalized;
            _pathSource?.Set(normalized);
            return GenesisGuiNextPlaybackSettingsUpdateResult.Saved;
        }
    }

    internal static GenesisGuiNextPlaybackSettingsUpdateResult? ValidateExecutablePath(string path) =>
        ValidateExecutablePath(
            path,
            Path.IsPathFullyQualified,
            Path.GetFileName,
            File.GetAttributes);

    internal static GenesisGuiNextPlaybackSettingsUpdateResult? ValidateExecutablePath(
        string path,
        Func<string, bool> isPathFullyQualified,
        Func<string, string> getFileName,
        Func<string, FileAttributes> getAttributes)
    {
        ArgumentNullException.ThrowIfNull(isPathFullyQualified);
        ArgumentNullException.ThrowIfNull(getFileName);
        ArgumentNullException.ThrowIfNull(getAttributes);

        try
        {
            if (!isPathFullyQualified(path))
            {
                return GenesisGuiNextPlaybackSettingsUpdateResult.InvalidPath;
            }

            if (!string.Equals(
                    getFileName(path),
                    PcrPlayerExternalPlayerLauncher.ExecutableFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return GenesisGuiNextPlaybackSettingsUpdateResult.UnsupportedExecutableName;
            }

            var attributes = getAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                return GenesisGuiNextPlaybackSettingsUpdateResult.DirectorySelected;
            }

            return null;
        }
        catch (FileNotFoundException)
        {
            return GenesisGuiNextPlaybackSettingsUpdateResult.ExecutableNotFound;
        }
        catch (DirectoryNotFoundException)
        {
            return GenesisGuiNextPlaybackSettingsUpdateResult.ExecutableNotFound;
        }
        catch (UnauthorizedAccessException)
        {
            return GenesisGuiNextPlaybackSettingsUpdateResult.AccessDenied;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return GenesisGuiNextPlaybackSettingsUpdateResult.InvalidPath;
        }
        catch (Exception exception) when (
            exception is IOException or System.Security.SecurityException)
        {
            return GenesisGuiNextPlaybackSettingsUpdateResult.PathCheckFailed;
        }
    }

    internal static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = path.Trim();
        if (normalized.Length >= 2 &&
            normalized[0] == normalized[^1] &&
            normalized[0] is '"' or '\'')
        {
            normalized = normalized[1..^1].Trim();
        }

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
