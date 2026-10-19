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
    PersistenceFailed,
}

internal sealed class GenesisGuiNextPlaybackSettings
{
    private readonly object _gate = new();
    private readonly GenesisGuiNextPlaybackPathSource? _pathSource;
    private string? _pcrPlayerExecutablePath;
    private Func<string?, bool>? _persist;

    public GenesisGuiNextPlaybackSettings(
        GenesisGuiNextPlaybackPreferences preferences,
        GenesisGuiNextPlaybackPathSource? pathSource = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        _pcrPlayerExecutablePath = NormalizePath(preferences.PcrPlayerExecutablePath);
        _pathSource = pathSource;
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
