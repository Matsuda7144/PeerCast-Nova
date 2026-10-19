namespace PeerCast.Nova.App.Tests;

public sealed class JsonGenesisYellowPageConfigurationStoreTests
{
    [Fact]
    public async Task MissingStoreLoadsDefaultSpDefinition()
    {
        using var temporary = new TemporaryDirectory();
        var store = new JsonGenesisYellowPageConfigurationStore(
            Path.Combine(temporary.Path, "missing", "yellow-pages.json"));

        var loaded = await store.LoadAsync();

        Assert.Equal([GenesisYellowPageDefaults.Sp], loaded);
    }

    [Fact]
    public async Task SaveAndLoadRoundTripPreservesDefinitionsAndOrder()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "settings", "yellow-pages.json");
        var store = new JsonGenesisYellowPageConfigurationStore(path);
        var custom = new GenesisYellowPageDefinition(
            "custom",
            "Custom",
            new Uri("https://example.test/index.txt"),
            new Uri("pcp://example.test:7144/"),
            isEnabled: true,
            yellowPageNamespace: "custom-space");
        IReadOnlyList<GenesisYellowPageDefinition> expected =
        [
            GenesisYellowPageDefaults.Sp with { IsEnabled = false },
            custom,
        ];

        await store.SaveAsync(expected);
        var loaded = await store.LoadAsync();

        Assert.True(File.Exists(path));
        Assert.Equal(expected, loaded);
    }

    [Fact]
    public async Task ValidEmptyConfigurationRoundTripsAsEmpty()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "yellow-pages.json");
        var store = new JsonGenesisYellowPageConfigurationStore(path);

        await store.SaveAsync(Array.Empty<GenesisYellowPageDefinition>());
        var loaded = await store.LoadAsync();

        Assert.Empty(loaded);
    }

    [Fact]
    public async Task NullJsonIsRejected()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "yellow-pages.json");
        await File.WriteAllTextAsync(path, "null");
        var store = new JsonGenesisYellowPageConfigurationStore(path);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.LoadAsync());
    }

    [Fact]
    public async Task DuplicateIdsAreRejectedOnLoad()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "yellow-pages.json");
        await File.WriteAllTextAsync(
            path,
            """
            [
              {
                "Id": "duplicate",
                "Name": "First",
                "IndexUri": "https://example.test/first.txt",
                "FallbackTrackerUri": null,
                "IsEnabled": true,
                "Namespace": null
              },
              {
                "Id": "duplicate",
                "Name": "Second",
                "IndexUri": "https://example.test/second.txt",
                "FallbackTrackerUri": null,
                "IsEnabled": true,
                "Namespace": null
              }
            ]
            """);
        var store = new JsonGenesisYellowPageConfigurationStore(path);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.LoadAsync());
    }

    [Fact]
    public async Task PreCancelledOperationsDoNotReadOrPublish()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "yellow-pages.json");
        var store = new JsonGenesisYellowPageConfigurationStore(path);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await store.LoadAsync(cancellationSource.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await store.SaveAsync(
                [GenesisYellowPageDefaults.Sp],
                cancellationSource.Token));

        Assert.False(File.Exists(path));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"PeerCast-Nova-YellowPages-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}
