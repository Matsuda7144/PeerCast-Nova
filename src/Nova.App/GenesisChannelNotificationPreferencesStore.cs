using System.Text.Json;

namespace PeerCast.Nova.App;

internal interface IGenesisChannelNotificationPreferenceStore
{
    ValueTask<GenesisChannelNotificationPreferences> LoadAsync(
        CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        GenesisChannelNotificationPreferences preferences,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// JSON persistence for GUI Next notification preferences. Writes are staged
/// beside the target and atomically replaced so interrupted saves cannot publish
/// partial preference data.
/// </summary>
internal sealed class JsonGenesisChannelNotificationPreferenceStore :
    IGenesisChannelNotificationPreferenceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly Action? _checkAccess;

    public JsonGenesisChannelNotificationPreferenceStore(string path, Action? checkAccess = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Notification preference store path is required.",
                nameof(path));
        }

        _path = Path.GetFullPath(path);
        _checkAccess = checkAccess;
    }

    public async ValueTask<GenesisChannelNotificationPreferences> LoadAsync(
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
            return GenesisChannelNotificationPreferences.Default;
        }
        catch (DirectoryNotFoundException)
        {
            return GenesisChannelNotificationPreferences.Default;
        }

        await using (stream.ConfigureAwait(false))
        {
            var preferences = await JsonSerializer
                .DeserializeAsync<GenesisChannelNotificationPreferences>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            return preferences ?? throw new InvalidDataException(
                "Notification preference store did not contain an object.");
        }
    }

    public async ValueTask SaveAsync(
        GenesisChannelNotificationPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        _checkAccess?.Invoke();
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
                // A failed best-effort cleanup must not hide the original save result.
            }
            catch (UnauthorizedAccessException)
            {
                // A failed best-effort cleanup must not hide the original save result.
            }
        }
    }
}
