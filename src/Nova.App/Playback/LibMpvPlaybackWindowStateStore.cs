using System.Text.Json;
using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App;

internal sealed class LibMpvPlaybackWindowStateStore
{
    private const string FileName = "embedded-player-window.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly object _sync = new();
    private readonly string? _filePath;
    private LibMpvPlaybackWindowState _current;

    public LibMpvPlaybackWindowStateStore(string? filePath = null)
    {
        _filePath = filePath;
        _current = LoadCore(filePath);
    }

    public static LibMpvPlaybackWindowStateStore CreateSystem()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = string.IsNullOrWhiteSpace(localAppData)
            ? null
            : Path.Combine(localAppData, SystemGenesisGuiNextApplicationState.ApplicationDirectoryName, FileName);
        return new LibMpvPlaybackWindowStateStore(path);
    }

    public LibMpvPlaybackWindowState Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public void Update(LibMpvPlaybackWindowState state)
    {
        var normalized = state.Normalized();
        string? path;
        lock (_sync)
        {
            if (normalized == _current)
            {
                return;
            }

            _current = normalized;
            path = _filePath;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        SaveCore(path, normalized);
    }

    private static LibMpvPlaybackWindowState LoadCore(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return LibMpvPlaybackWindowState.Default;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var loaded = JsonSerializer.Deserialize<LibMpvPlaybackWindowState>(json, JsonOptions);
            return (loaded ?? LibMpvPlaybackWindowState.Default).Normalized();
        }
        catch
        {
            return LibMpvPlaybackWindowState.Default;
        }
    }

    private static void SaveCore(string filePath, LibMpvPlaybackWindowState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            // Ignore persistence failures so playback continues uninterrupted.
        }
    }
}
