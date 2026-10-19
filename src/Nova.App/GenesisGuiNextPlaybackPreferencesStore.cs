using System.Text.Json;

namespace PeerCast.Nova.App;

internal interface IGenesisGuiNextPlaybackPreferencesStore
{
    ValueTask<GenesisGuiNextPlaybackPreferences> LoadAsync(
        CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        GenesisGuiNextPlaybackPreferences preferences,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Persists GUI Next playback preferences with the same atomic JSON snapshot
/// mechanism used by the other user-owned GUI settings.
/// </summary>
internal sealed class JsonGenesisGuiNextPlaybackPreferencesStore :
    IGenesisGuiNextPlaybackPreferencesStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public JsonGenesisGuiNextPlaybackPreferencesStore(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Playback preference store path is required.",
                nameof(path));
        }

        _path = Path.GetFullPath(path);
    }

    public async ValueTask<GenesisGuiNextPlaybackPreferences> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileStream stream;
        try
        {
            stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (FileNotFoundException)
        {
            return GenesisGuiNextPlaybackPreferences.Default;
        }
        catch (DirectoryNotFoundException)
        {
            return GenesisGuiNextPlaybackPreferences.Default;
        }

        await using (stream.ConfigureAwait(false))
        {
            var preferences = await JsonSerializer
                .DeserializeAsync<GenesisGuiNextPlaybackPreferences>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            return preferences ?? throw new InvalidDataException(
                "Playback preference store did not contain an object.");
        }
    }

    public async ValueTask SaveAsync(
        GenesisGuiNextPlaybackPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = Path.Combine(
            directory ?? string.Empty,
            $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        preferences,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // Best-effort cleanup must not hide the original save result.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup must not hide the original save result.
            }
        }
    }
}

internal sealed class DefaultGenesisGuiNextPlaybackPreferencesStore :
    IGenesisGuiNextPlaybackPreferencesStore
{
    public static DefaultGenesisGuiNextPlaybackPreferencesStore Instance { get; } = new();

    private DefaultGenesisGuiNextPlaybackPreferencesStore()
    {
    }

    public ValueTask<GenesisGuiNextPlaybackPreferences> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(GenesisGuiNextPlaybackPreferences.Default);
    }

    public ValueTask SaveAsync(
        GenesisGuiNextPlaybackPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
