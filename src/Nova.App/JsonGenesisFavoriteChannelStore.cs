using System.Collections.ObjectModel;
using System.Text.Json;

namespace PeerCast.Nova.App;

/// <summary>
/// JSON persistence for GUI Next favorites. Writes are staged beside the target
/// and atomically replaced so an interrupted save does not publish partial JSON.
/// The concrete application-data path is supplied by startup composition later.
/// </summary>
internal sealed class JsonGenesisFavoriteChannelStore : IGenesisFavoriteChannelStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public JsonGenesisFavoriteChannelStore(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Favorite channel store path is required.",
                nameof(path));
        }

        _path = Path.GetFullPath(path);
    }

    public async ValueTask<IReadOnlyList<GenesisFavoriteChannel>> LoadAsync(
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
            return Array.Empty<GenesisFavoriteChannel>();
        }
        catch (DirectoryNotFoundException)
        {
            return Array.Empty<GenesisFavoriteChannel>();
        }

        await using (stream.ConfigureAwait(false))
        {
            var favorites = await JsonSerializer.DeserializeAsync<List<GenesisFavoriteChannel>>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            if (favorites is null)
            {
                throw new InvalidDataException(
                    "Favorite channel store did not contain a JSON array.");
            }

            return Validate(favorites);
        }
    }

    public async ValueTask SaveAsync(
        IReadOnlyList<GenesisFavoriteChannel> favorites,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(favorites);
        cancellationToken.ThrowIfCancellationRequested();
        var validated = Validate(favorites);
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
                        validated,
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

    private static ReadOnlyCollection<GenesisFavoriteChannel> Validate(
        IReadOnlyList<GenesisFavoriteChannel> favorites)
    {
        var validated = new List<GenesisFavoriteChannel>(favorites.Count);
        foreach (var favorite in favorites)
        {
            if (favorite is null ||
                string.IsNullOrWhiteSpace(favorite.StableKey) ||
                string.IsNullOrWhiteSpace(favorite.Name) ||
                string.IsNullOrWhiteSpace(favorite.LastKnownSource) ||
                favorite.Name.Length > GenesisChannelCatalogSanitizer.MaximumNameCharacters ||
                favorite.LastKnownSource.Length >
                    GenesisChannelCatalogSanitizer.MaximumSourceCharacters ||
                favorite.Description?.Length >
                    GenesisChannelCatalogSanitizer.MaximumDescriptionCharacters ||
                favorite.Type?.Length > GenesisChannelCatalogSanitizer.MaximumTypeCharacters ||
                favorite.LastKnownYellowPage?.Length >
                    GenesisChannelCatalogSanitizer.MaximumYellowPageCharacters)
            {
                throw new InvalidDataException(
                    "Favorite channel store contains invalid metadata.");
            }

            var storedChannel = new GenesisChannelItem(
                favorite.Name,
                favorite.LastKnownSource,
                Description: favorite.Description,
                Type: favorite.Type,
                ChannelId: favorite.ChannelId);
            if (!string.Equals(
                favorite.StableKey,
                GenesisChannelIdentity.GetStableKey(storedChannel),
                StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Favorite channel store contains an inconsistent identity.");
            }

            validated.Add(favorite);
        }

        try
        {
            return new GenesisFavoriteChannelCollection(validated).Snapshot;
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Favorite channel store contains duplicate identities.",
                exception);
        }
    }
}
