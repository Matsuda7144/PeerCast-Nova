using System.Text.Json;

namespace PeerCast.Nova.App;

internal interface IGenesisGuiNextConnectionPreferencesStore
{
    ValueTask<GenesisGuiNextConnectionPreferences> LoadAsync(
        CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        GenesisGuiNextConnectionPreferences preferences,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Persists GUI Next connection preferences with the same atomic JSON snapshot
/// mechanism used by the other user-owned GUI settings.
/// </summary>
internal sealed class JsonGenesisGuiNextConnectionPreferencesStore :
    IGenesisGuiNextConnectionPreferencesStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly Action? _checkAccess;

    public JsonGenesisGuiNextConnectionPreferencesStore(string path, Action? checkAccess = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Connection preference store path is required.",
                nameof(path));
        }

        _path = Path.GetFullPath(path);
        _checkAccess = checkAccess;
    }

    public async ValueTask<GenesisGuiNextConnectionPreferences> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _checkAccess?.Invoke();
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
            return GenesisGuiNextConnectionPreferences.Default;
        }
        catch (DirectoryNotFoundException)
        {
            return GenesisGuiNextConnectionPreferences.Default;
        }

        await using (stream.ConfigureAwait(false))
        {
            var preferences = await JsonSerializer
                .DeserializeAsync<GenesisGuiNextConnectionPreferences>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            if (preferences is null)
                throw new InvalidDataException("Connection preference store did not contain an object.");
            preferences.Validate();
            return preferences;
        }
    }

    public async ValueTask SaveAsync(
        GenesisGuiNextConnectionPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        _checkAccess?.Invoke();
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
            _checkAccess?.Invoke();
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
            _checkAccess?.Invoke();
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            try
            {
                _checkAccess?.Invoke();
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

internal sealed class UnavailableGenesisGuiNextConnectionPreferencesStore : IGenesisGuiNextConnectionPreferencesStore
{
    internal static UnavailableGenesisGuiNextConnectionPreferencesStore Instance { get; } = new();
    public ValueTask<GenesisGuiNextConnectionPreferences> LoadAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromException<GenesisGuiNextConnectionPreferences>(new IOException("Settings root unavailable."));
    public ValueTask SaveAsync(GenesisGuiNextConnectionPreferences preferences, CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new IOException("Settings root unavailable."));
}

internal sealed class DefaultGenesisGuiNextConnectionPreferencesStore :
    IGenesisGuiNextConnectionPreferencesStore
{
    public static DefaultGenesisGuiNextConnectionPreferencesStore Instance { get; } = new();

    private DefaultGenesisGuiNextConnectionPreferencesStore()
    {
    }

    public ValueTask<GenesisGuiNextConnectionPreferences> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(GenesisGuiNextConnectionPreferences.Default);
    }

    public ValueTask SaveAsync(
        GenesisGuiNextConnectionPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
