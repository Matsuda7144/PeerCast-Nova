namespace PeerCast.Nova.App.Tests;

public sealed class JsonGenesisChannelNotificationPreferenceStoreTests
{
    [Fact]
    public async Task MissingStoreLoadsDefaultPreferences()
    {
        using var temporary = new TemporaryDirectory();
        var store = new JsonGenesisChannelNotificationPreferenceStore(
            Path.Combine(temporary.Path, "missing", "notifications.json"));

        var loaded = await store.LoadAsync();

        Assert.Equal(GenesisChannelNotificationPreferences.Default, loaded);
    }

    [Fact]
    public async Task SaveAndLoadRoundTripPreservesPreferences()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "settings", "notifications.json");
        var store = new JsonGenesisChannelNotificationPreferenceStore(path);
        var expected = new GenesisChannelNotificationPreferences(
            NotifyNewChannels: false,
            NotifyFavoriteChannels: true,
            PlaySound: false);

        await store.SaveAsync(expected);
        var loaded = await store.LoadAsync();

        Assert.True(File.Exists(path));
        Assert.Equal(expected, loaded);
    }

    [Fact]
    public async Task NullJsonIsRejected()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "notifications.json");
        await File.WriteAllTextAsync(path, "null");
        var store = new JsonGenesisChannelNotificationPreferenceStore(path);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.LoadAsync());
    }

    [Fact]
    public async Task PreCancelledOperationsDoNotReadOrPublish()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "notifications.json");
        var store = new JsonGenesisChannelNotificationPreferenceStore(path);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await store.LoadAsync(cancellationSource.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await store.SaveAsync(
                GenesisChannelNotificationPreferences.Default,
                cancellationSource.Token));

        Assert.False(File.Exists(path));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"PeerCast-Nova-NotificationPreferences-{Guid.NewGuid():N}");
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
