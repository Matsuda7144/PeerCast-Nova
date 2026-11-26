namespace PeerCast.Nova.App;

internal enum GenesisPlaybackPlayerMode { External = 0, Internal = 1 }

internal sealed record GenesisGuiNextPlaybackPreferences(
    string? PcrPlayerExecutablePath,
    GenesisPlaybackPlayerMode PlayerMode = GenesisPlaybackPlayerMode.External,
    string? LibMpvLibraryPath = null,
    bool LowLatencyInternalPlayback = false,
    string? ExternalPlayerArguments = null)
{
    public static GenesisGuiNextPlaybackPreferences Default { get; } =
        new(PcrPlayerExecutablePath: null);
}

internal sealed class GenesisGuiNextPlaybackPreferencesSource
{
    private GenesisGuiNextPlaybackPreferences _current = GenesisGuiNextPlaybackPreferences.Default;

    public GenesisGuiNextPlaybackPreferences Current => Volatile.Read(ref _current);

    internal void Set(GenesisGuiNextPlaybackPreferences preferences) => Volatile.Write(ref _current, preferences);
}

internal enum GenesisGuiNextPlaybackSettingsUpdateResult
{
    Saved,
    Unchanged,
    InvalidPath,
    UnsupportedFileExtension,
    FileNotFound,
    DirectorySelected,
    AccessDenied,
    PathCheckFailed,
    LibraryRequired,
    Conflict,
    PersistenceFailed,
}

internal sealed class GenesisGuiNextPlaybackSettings
{
    private readonly object _gate = new();
    private readonly GenesisGuiNextPlaybackPreferencesSource? _preferencesSource;
    private readonly Func<string, GenesisGuiNextPlaybackSettingsUpdateResult?> _validateExecutablePath;
    private GenesisGuiNextPlaybackPreferences _preferences;
    private Func<GenesisGuiNextPlaybackPreferences, bool>? _persist;

    public GenesisGuiNextPlaybackSettings(
        GenesisGuiNextPlaybackPreferences preferences,
        GenesisGuiNextPlaybackPreferencesSource? preferencesSource = null,
        Func<string, GenesisGuiNextPlaybackSettingsUpdateResult?>? validateExecutablePath = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        _preferences = preferences with
        {
            PcrPlayerExecutablePath = NormalizePath(preferences.PcrPlayerExecutablePath),
            LibMpvLibraryPath = NormalizePath(preferences.LibMpvLibraryPath),
            PlayerMode = preferences.PlayerMode == GenesisPlaybackPlayerMode.Internal
                ? GenesisPlaybackPlayerMode.Internal : GenesisPlaybackPlayerMode.External,
            ExternalPlayerArguments = NormalizeArguments(preferences.ExternalPlayerArguments),
        };
        _preferencesSource = preferencesSource;
        _validateExecutablePath = validateExecutablePath ?? ValidateExecutablePath;
        _preferencesSource?.Set(_preferences);
    }

    public string? PcrPlayerExecutablePath => Preferences.PcrPlayerExecutablePath;

    internal GenesisGuiNextPlaybackPreferences Preferences
    {
        get
        {
            lock (_gate)
            {
                return _preferences;
            }
        }
    }

    internal void BindPersistence(Func<GenesisGuiNextPlaybackPreferences, bool> persist)
    {
        ArgumentNullException.ThrowIfNull(persist);
        lock (_gate)
        {
            _persist = persist;
        }
    }

    public GenesisGuiNextPlaybackSettingsUpdateResult SetPcrPlayerExecutablePath(
        string? path,
        GenesisGuiNextPlaybackPreferences? expected = null)
    {
        var normalized = NormalizePath(path);
        if (normalized is not null && _validateExecutablePath(normalized) is { } validationFailure)
        {
            return validationFailure;
        }

        lock (_gate)
        {
            return expected is not null && expected != _preferences
                ? GenesisGuiNextPlaybackSettingsUpdateResult.Conflict
                : Persist(_preferences with { PcrPlayerExecutablePath = normalized });
        }
    }

    internal (GenesisGuiNextPlaybackSettingsUpdateResult Result, GenesisPlaybackPlayerMode? InvalidPathMode) Save(
        GenesisGuiNextPlaybackPreferences expected,
        GenesisGuiNextPlaybackPreferences draft)
    {
        var requested = draft with
        {
            PcrPlayerExecutablePath = NormalizePath(draft.PcrPlayerExecutablePath),
            LibMpvLibraryPath = NormalizePath(draft.LibMpvLibraryPath),
            ExternalPlayerArguments = NormalizeArguments(draft.ExternalPlayerArguments),
        };
        var internalSelected = requested.PlayerMode == GenesisPlaybackPlayerMode.Internal;
        if (internalSelected && requested.LibMpvLibraryPath is null)
        {
            return (GenesisGuiNextPlaybackSettingsUpdateResult.LibraryRequired, GenesisPlaybackPlayerMode.Internal);
        }

        if (requested.LibMpvLibraryPath is { } libraryPath &&
            (internalSelected || libraryPath != expected.LibMpvLibraryPath) &&
            ValidatePlayerPath(libraryPath, ".dll") is { } libraryFailure)
        {
            return (libraryFailure, GenesisPlaybackPlayerMode.Internal);
        }

        if (requested.PcrPlayerExecutablePath is { } executablePath &&
            (!internalSelected || executablePath != expected.PcrPlayerExecutablePath) &&
            _validateExecutablePath(executablePath) is { } executableFailure)
        {
            return (executableFailure, GenesisPlaybackPlayerMode.External);
        }

        lock (_gate)
        {
            return (expected == _preferences ? Persist(requested) : GenesisGuiNextPlaybackSettingsUpdateResult.Conflict, null);
        }
    }

    private GenesisGuiNextPlaybackSettingsUpdateResult Persist(GenesisGuiNextPlaybackPreferences preferences)
    {
        if (preferences == _preferences)
        {
            return GenesisGuiNextPlaybackSettingsUpdateResult.Unchanged;
        }

        if (_persist is not null && !_persist(preferences))
        {
            return GenesisGuiNextPlaybackSettingsUpdateResult.PersistenceFailed;
        }

        _preferences = preferences;
        _preferencesSource?.Set(preferences);
        return GenesisGuiNextPlaybackSettingsUpdateResult.Saved;
    }

    internal static GenesisGuiNextPlaybackSettingsUpdateResult? ValidateExecutablePath(string path) =>
        ValidatePlayerPath(path, ".exe");

    private static GenesisGuiNextPlaybackSettingsUpdateResult? ValidatePlayerPath(string path, string extension) =>
        ValidatePlayerPath(
            path,
            extension,
            Path.IsPathFullyQualified,
            Path.GetFileName,
            File.GetAttributes,
            static candidatePath =>
            {
                using var file = File.Open(candidatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            });

    internal static GenesisGuiNextPlaybackSettingsUpdateResult? ValidatePlayerPath(
        string path,
        string extension,
        Func<string, bool> isPathFullyQualified,
        Func<string, string> getFileName,
        Func<string, FileAttributes> getAttributes,
        Action<string> checkReadable)
    {
        ArgumentNullException.ThrowIfNull(isPathFullyQualified);
        ArgumentNullException.ThrowIfNull(getFileName);
        ArgumentNullException.ThrowIfNull(getAttributes);
        ArgumentNullException.ThrowIfNull(checkReadable);

        try
        {
            if (!isPathFullyQualified(path))
            {
                return GenesisGuiNextPlaybackSettingsUpdateResult.InvalidPath;
            }

            var fileName = getFileName(path);
            if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return GenesisGuiNextPlaybackSettingsUpdateResult.InvalidPath;
            }

            var attributes = getAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                return GenesisGuiNextPlaybackSettingsUpdateResult.DirectorySelected;
            }

            if (!string.Equals(Path.GetExtension(fileName), extension, StringComparison.OrdinalIgnoreCase))
            {
                return GenesisGuiNextPlaybackSettingsUpdateResult.UnsupportedFileExtension;
            }

            checkReadable(path);
            return null;
        }
        catch (FileNotFoundException)
        {
            return GenesisGuiNextPlaybackSettingsUpdateResult.FileNotFound;
        }
        catch (DirectoryNotFoundException)
        {
            return GenesisGuiNextPlaybackSettingsUpdateResult.FileNotFound;
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

    internal static string? NormalizeArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return null;
        }

        var normalized = new string(arguments.Where(static ch => !char.IsControl(ch)).ToArray()).Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        return normalized.Length <= 1024 ? normalized : normalized[..1024];
    }
}
