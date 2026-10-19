using System.Text.Json;

namespace PeerCast.Nova.App;

internal interface IGenesisYellowPageConfigurationStore
{
    ValueTask<IReadOnlyList<GenesisYellowPageDefinition>> LoadAsync(
        CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        IReadOnlyList<GenesisYellowPageDefinition> definitions,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// JSON persistence for GUI Next Yellow Page definitions. A missing store starts
/// from the built-in SP definition, while a valid empty array remains empty so a
/// user can intentionally run with no configured Yellow Pages.
/// </summary>
internal sealed class JsonGenesisYellowPageConfigurationStore :
    IGenesisYellowPageConfigurationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public JsonGenesisYellowPageConfigurationStore(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Yellow Page configuration store path is required.",
                nameof(path));
        }

        _path = Path.GetFullPath(path);
    }

    public async ValueTask<IReadOnlyList<GenesisYellowPageDefinition>> LoadAsync(
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
            return DefaultDefinitions();
        }
        catch (DirectoryNotFoundException)
        {
            return DefaultDefinitions();
        }

        await using (stream.ConfigureAwait(false))
        {
            var persisted = await JsonSerializer
                .DeserializeAsync<PersistedYellowPageDefinition[]>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            if (persisted is null)
            {
                throw new InvalidDataException(
                    "Yellow Page configuration store did not contain an array.");
            }

            var definitions = persisted
                .Select(static definition => definition.ToDefinition())
                .ToArray();
            return new GenesisYellowPageConfiguration(definitions).Definitions;
        }
    }

    public async ValueTask SaveAsync(
        IReadOnlyList<GenesisYellowPageDefinition> definitions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = new GenesisYellowPageConfiguration(definitions).Definitions;
        var persisted = snapshot
            .Select(static definition =>
                PersistedYellowPageDefinition.FromDefinition(definition))
            .ToArray();

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
                        persisted,
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

    private static System.Collections.ObjectModel.ReadOnlyCollection<GenesisYellowPageDefinition>
        DefaultDefinitions() =>
            Array.AsReadOnly(new[] { GenesisYellowPageDefaults.Sp });

    private sealed record PersistedYellowPageDefinition(
        string Id,
        string Name,
        string IndexUri,
        string? FallbackTrackerUri,
        bool IsEnabled,
        string? Namespace)
    {
        public GenesisYellowPageDefinition ToDefinition() =>
            new(
                Id,
                Name,
                new Uri(IndexUri, UriKind.Absolute),
                FallbackTrackerUri is null
                    ? null
                    : new Uri(FallbackTrackerUri, UriKind.Absolute),
                IsEnabled,
                Namespace);

        public static PersistedYellowPageDefinition FromDefinition(
            GenesisYellowPageDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            return new(
                definition.Id,
                definition.Name,
                definition.IndexUri.AbsoluteUri,
                definition.FallbackTrackerUri?.AbsoluteUri,
                definition.IsEnabled,
                definition.Namespace);
        }
    }
}

internal sealed class DefaultGenesisYellowPageConfigurationStore :
    IGenesisYellowPageConfigurationStore
{
    public static DefaultGenesisYellowPageConfigurationStore Instance { get; } = new();

    private DefaultGenesisYellowPageConfigurationStore()
    {
    }

    public ValueTask<IReadOnlyList<GenesisYellowPageDefinition>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<GenesisYellowPageDefinition>>(
            Array.AsReadOnly(new[] { GenesisYellowPageDefaults.Sp }));
    }

    public ValueTask SaveAsync(
        IReadOnlyList<GenesisYellowPageDefinition> definitions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
