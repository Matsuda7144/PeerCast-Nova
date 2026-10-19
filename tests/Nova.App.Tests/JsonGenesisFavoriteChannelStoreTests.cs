using System.Text.Json;

namespace PeerCast.Nova.App.Tests;

public sealed class JsonGenesisFavoriteChannelStoreTests
{
    private const string FirstChannelId = "0123456789ABCDEF0123456789ABCDEF";
    private const string SecondChannelId = "FEDCBA9876543210FEDCBA9876543210";

    [Fact]
    public async Task MissingStoreLoadsEmptyFavorites()
    {
        using var temporary = new TemporaryDirectory();
        var store = new JsonGenesisFavoriteChannelStore(
            Path.Combine(temporary.Path, "missing", "favorites.json"));

        var loaded = await store.LoadAsync();

        Assert.Empty(loaded);
    }

    [Fact]
    public async Task SaveAndLoadRoundTripPreservesFavoriteOrderAndMetadata()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "favorites", "favorites.json");
        var store = new JsonGenesisFavoriteChannelStore(path);
        var firstChannel = Channel(
            "First",
            FirstChannelId,
            "pcp://example/first",
            "first metadata") with
        {
            YellowPage = "SP",
        };
        var first = GenesisFavoriteChannel.FromChannel(firstChannel);
        var second = GenesisFavoriteChannel.FromChannel(Channel(
            "Second",
            SecondChannelId,
            "pcp://example/second",
            "second metadata"));
        var favorites = new List<GenesisFavoriteChannel> { first, second };

        await store.SaveAsync(favorites);
        var loaded = await store.LoadAsync();

        Assert.True(File.Exists(path));
        Assert.Collection(
            loaded,
            favorite => Assert.Equal(first, favorite),
            favorite => Assert.Equal(second, favorite));
    }

    [Fact]
    public async Task SaveAndLoadRoundTripPreservesAggregatedYellowPageMetadata()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "favorites.json");
        var store = new JsonGenesisFavoriteChannelStore(path);
        var aggregatedYellowPage = string.Join(
            ", ",
            new string('A', GenesisYellowPageDefinition.MaximumNameCharacters),
            new string('B', GenesisYellowPageDefinition.MaximumNameCharacters));
        Assert.True(
            aggregatedYellowPage.Length > GenesisYellowPageDefinition.MaximumNameCharacters);
        var channel = Channel(
            "Aggregated",
            FirstChannelId,
            "pcp://example/aggregated") with
        {
            YellowPage = aggregatedYellowPage,
        };
        var favorite = GenesisFavoriteChannel.FromChannel(channel);

        await store.SaveAsync([favorite]);
        var loaded = await store.LoadAsync();

        Assert.Equal(
            aggregatedYellowPage,
            Assert.Single(loaded).LastKnownYellowPage);
    }

    [Fact]
    public async Task LegacyStoreWithoutYellowPageMetadataStillLoads()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "favorites.json");
        var current = GenesisFavoriteChannel.FromChannel(Channel(
            "Legacy",
            FirstChannelId,
            "pcp://example/legacy"));
        var legacy = new[]
        {
            new
            {
                current.StableKey,
                current.Name,
                current.LastKnownSource,
                current.Description,
                current.Type,
                current.ChannelId,
            },
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(legacy));
        var store = new JsonGenesisFavoriteChannelStore(path);

        var loaded = await store.LoadAsync();

        var favorite = Assert.Single(loaded);
        Assert.Equal(current.StableKey, favorite.StableKey);
        Assert.Null(favorite.LastKnownYellowPage);
    }

    [Fact]
    public async Task LoadRejectsOversizedYellowPageMetadata()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "favorites.json");
        var invalid = GenesisFavoriteChannel.FromChannel(Channel(
            "First",
            FirstChannelId,
            "pcp://example/first")) with
        {
            LastKnownYellowPage = new string(
                'Y',
                GenesisChannelCatalogSanitizer.MaximumYellowPageCharacters + 1),
        };
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(new[] { invalid }));
        var store = new JsonGenesisFavoriteChannelStore(path);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.LoadAsync());
    }

    [Fact]
    public async Task SubsequentSaveReplacesPreviouslyPublishedFavorites()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "favorites.json");
        var store = new JsonGenesisFavoriteChannelStore(path);
        var first = GenesisFavoriteChannel.FromChannel(Channel(
            "First",
            FirstChannelId,
            "pcp://example/first"));
        var second = GenesisFavoriteChannel.FromChannel(Channel(
            "Second",
            SecondChannelId,
            "pcp://example/second"));
        var initial = new List<GenesisFavoriteChannel> { first };
        var replacement = new List<GenesisFavoriteChannel> { second };

        await store.SaveAsync(initial);
        await store.SaveAsync(replacement);
        var loaded = await store.LoadAsync();

        Assert.Equal(second, Assert.Single(loaded));
        Assert.Empty(Directory.GetFiles(
            temporary.Path,
            ".favorites.json.*.tmp",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task SaveRejectsDuplicateIdentitiesBeforePublishingFile()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "favorites.json");
        var store = new JsonGenesisFavoriteChannelStore(path);
        var favorite = GenesisFavoriteChannel.FromChannel(Channel(
            "First",
            FirstChannelId,
            "pcp://example/first"));
        var duplicates = new List<GenesisFavoriteChannel>
        {
            favorite,
            favorite,
        };

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.SaveAsync(duplicates));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task LoadRejectsInconsistentStableIdentity()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "favorites.json");
        var invalid = GenesisFavoriteChannel.FromChannel(Channel(
            "First",
            FirstChannelId,
            "pcp://example/first")) with
        {
            StableKey = $"id:{SecondChannelId}",
        };
        var serialized = new List<GenesisFavoriteChannel> { invalid };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(serialized));
        var store = new JsonGenesisFavoriteChannelStore(path);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.LoadAsync());
    }

    [Fact]
    public async Task PreCancelledOperationsDoNotReadOrPublish()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "favorites.json");
        var store = new JsonGenesisFavoriteChannelStore(path);
        var favorites = new List<GenesisFavoriteChannel>
        {
            GenesisFavoriteChannel.FromChannel(Channel(
                "First",
                FirstChannelId,
                "pcp://example/first")),
        };
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await store.LoadAsync(cancellationSource.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await store.SaveAsync(favorites, cancellationSource.Token));

        Assert.False(File.Exists(path));
    }

    private static GenesisChannelItem Channel(
        string name,
        string channelId,
        string source,
        string? description = null) =>
        new(
            name,
            source,
            Description: description,
            Type: "FLV",
            ChannelId: channelId);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"PeerCast-Nova-Favorites-{Guid.NewGuid():N}");
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
