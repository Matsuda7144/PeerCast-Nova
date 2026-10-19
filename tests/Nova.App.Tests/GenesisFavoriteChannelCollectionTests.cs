namespace PeerCast.Nova.App.Tests;

public sealed class GenesisFavoriteChannelCollectionTests
{
    private const string FirstChannelId = "0123456789ABCDEF0123456789ABCDEF";
    private const string SecondChannelId = "FEDCBA9876543210FEDCBA9876543210";

    [Fact]
    public void AddCapturesStableIdentityAndLastKnownMetadata()
    {
        var collection = new GenesisFavoriteChannelCollection();
        var channel = Channel(
            "Favorite",
            FirstChannelId,
            "pcp://tracker.example/channel/first",
            "description");

        var result = collection.AddOrUpdate(channel);

        Assert.Equal(GenesisFavoriteChannelMutationResult.Added, result);
        var favorite = Assert.Single(collection.Snapshot);
        Assert.Equal($"id:{FirstChannelId}", favorite.StableKey);
        Assert.Equal("Favorite", favorite.Name);
        Assert.Equal(channel.Source, favorite.LastKnownSource);
        Assert.Equal("description", favorite.Description);
        Assert.Equal("FLV", favorite.Type);
        Assert.Equal(FirstChannelId, favorite.ChannelId);
    }

    [Fact]
    public void ExistingIdentityRefreshesMetadataWithoutChangingOrder()
    {
        var collection = new GenesisFavoriteChannelCollection();
        var first = Channel(
            "First",
            FirstChannelId,
            "pcp://old.example/first");
        var second = Channel(
            "Second",
            SecondChannelId,
            "pcp://example/second");
        Assert.Equal(
            GenesisFavoriteChannelMutationResult.Added,
            collection.AddOrUpdate(first));
        Assert.Equal(
            GenesisFavoriteChannelMutationResult.Added,
            collection.AddOrUpdate(second));
        var updatedFirst = Channel(
            "First updated",
            FirstChannelId,
            "pcp://new.example/first",
            "new metadata");

        var result = collection.AddOrUpdate(updatedFirst);

        Assert.Equal(GenesisFavoriteChannelMutationResult.Updated, result);
        Assert.Collection(
            collection.Snapshot,
            favorite =>
            {
                Assert.Equal("First updated", favorite.Name);
                Assert.Equal(updatedFirst.Source, favorite.LastKnownSource);
                Assert.Equal("new metadata", favorite.Description);
            },
            favorite => Assert.Equal("Second", favorite.Name));
        Assert.Equal(
            GenesisFavoriteChannelMutationResult.Unchanged,
            collection.AddOrUpdate(updatedFirst));
    }

    [Fact]
    public void RemoveMatchesStableChannelIdentityAcrossMetadataChanges()
    {
        var collection = new GenesisFavoriteChannelCollection();
        Assert.Equal(
            GenesisFavoriteChannelMutationResult.Added,
            collection.AddOrUpdate(Channel(
                "Original",
                FirstChannelId,
                "pcp://old.example/first")));
        var changed = Channel(
            "Changed",
            FirstChannelId,
            "pcp://new.example/first");

        Assert.True(collection.Contains(changed));
        Assert.True(collection.Remove(changed));
        Assert.False(collection.Contains(changed));
        Assert.Empty(collection.Snapshot);
    }

    [Fact]
    public void ProjectionKeepsOfflineFavoriteWithoutPlayableSource()
    {
        var collection = new GenesisFavoriteChannelCollection();
        var offline = Channel(
            "Offline favorite",
            FirstChannelId,
            "pcp://last-known.example/first");
        collection.AddOrUpdate(offline);

        var projection = collection.Project(Array.Empty<GenesisChannelItem>());

        var entry = Assert.Single(projection);
        Assert.False(entry.IsOnline);
        Assert.Null(entry.CurrentChannel);
        Assert.Null(entry.PlayableSource);
        Assert.Equal("Offline favorite", entry.DisplayName);
        Assert.Equal(offline.Source, entry.Favorite.LastKnownSource);
    }

    [Fact]
    public void ProjectionUsesCurrentChannelInstanceWhenAvailable()
    {
        var collection = new GenesisFavoriteChannelCollection();
        collection.AddOrUpdate(Channel(
            "Saved name",
            FirstChannelId,
            "pcp://saved.example/first"));
        var current = Channel(
            "Current name",
            FirstChannelId,
            "pcp://current.example/first");
        var currentChannels = new List<GenesisChannelItem> { current };

        var projection = collection.Project(currentChannels);

        var entry = Assert.Single(projection);
        Assert.True(entry.IsOnline);
        Assert.Same(current, entry.CurrentChannel);
        Assert.Equal(current.Source, entry.PlayableSource);
        Assert.Equal("Current name", entry.DisplayName);
    }

    [Fact]
    public void PresenceProjectionRequiresCurrentObservationForPlayback()
    {
        var collection = new GenesisFavoriteChannelCollection();
        var channel = Channel(
            "Transiently absent",
            FirstChannelId,
            "pcp://example/first");
        collection.AddOrUpdate(channel);
        var stableKey = GenesisChannelIdentity.GetStableKey(channel);
        var entries = new List<GenesisChannelPresenceEntry>
        {
            new(
                stableKey,
                channel,
                GenesisChannelPresenceState.Active,
                IsCurrentlyObserved: false,
                IsNew: true,
                ConsecutiveMissingSuccessfulRefreshes: 1,
                Array.Empty<string>()),
        };
        var presence = new GenesisChannelPresenceSnapshot(
            entries,
            Array.Empty<GenesisChannelPresenceTransition>());

        var projection = collection.Project(presence);

        var favorite = Assert.Single(projection);
        Assert.False(favorite.IsOnline);
        Assert.Null(favorite.PlayableSource);
        Assert.Equal(channel.Source, favorite.Favorite.LastKnownSource);
    }

    [Fact]
    public void RefreshKnownMetadataUpdatesSavedCopyFromCurrentCatalog()
    {
        var collection = new GenesisFavoriteChannelCollection();
        collection.AddOrUpdate(Channel(
            "Old",
            FirstChannelId,
            "pcp://old.example/first"));
        var updated = Channel(
            "Updated",
            FirstChannelId,
            "pcp://new.example/first",
            "updated metadata");
        var currentChannels = new List<GenesisChannelItem> { updated };

        Assert.True(collection.RefreshKnownMetadata(currentChannels));
        Assert.False(collection.RefreshKnownMetadata(currentChannels));

        var favorite = Assert.Single(collection.Snapshot);
        Assert.Equal("Updated", favorite.Name);
        Assert.Equal(updated.Source, favorite.LastKnownSource);
        Assert.Equal("updated metadata", favorite.Description);
    }

    [Fact]
    public void ReturnedSnapshotIsUnaffectedByLaterMutations()
    {
        var collection = new GenesisFavoriteChannelCollection();
        collection.AddOrUpdate(Channel(
            "First",
            FirstChannelId,
            "pcp://example/first"));
        var snapshot = collection.Snapshot;

        collection.AddOrUpdate(Channel(
            "Second",
            SecondChannelId,
            "pcp://example/second"));

        Assert.Single(snapshot);
        Assert.Equal(2, collection.Snapshot.Count);
    }

    [Fact]
    public void ConstructorRejectsDuplicateStableIdentities()
    {
        var favorite = GenesisFavoriteChannel.FromChannel(Channel(
            "First",
            FirstChannelId,
            "pcp://example/first"));
        var duplicates = new List<GenesisFavoriteChannel>
        {
            favorite,
            favorite,
        };

        Assert.Throws<ArgumentException>(() =>
            new GenesisFavoriteChannelCollection(duplicates));
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
}
