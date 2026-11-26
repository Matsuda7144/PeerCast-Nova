using PeerCast.Nova.Foundation.Configuration;

namespace PeerCast.Nova.App.Tests;

public sealed class NovaSettingsStoreMigrationTests
{
    [Fact]
    public void CreateDefault_UsesPrimaryPeerCastNovaDirectory()
    {
        var store = NovaSettingsStore.CreateDefault();

        Assert.Equal(NovaAppDataPaths.GetDefaultSettingsFilePath(), store.FilePath);
        Assert.Equal(
            Path.Combine(NovaAppDataPaths.GetPrimaryDataDirectory(), "settings.json"),
            store.FilePath);
    }

    [Fact]
    public async Task Load_FallsBackToLegacySettingsWhenPrimaryDoesNotExist()
    {
        using var scope = new TempSettingsScope();
        var legacyStore = new NovaSettingsStore(scope.LegacyPath);
        await legacyStore.SaveAsync(new NovaOptions
        {
            AutoPortMapping = true,
            ListenPort = 8144,
            NodeName = "LegacyNode",
        });

        var store = new NovaSettingsStore(scope.PrimaryPath, legacyFallbackPath: scope.LegacyPath);

        var loaded = store.Load();
        Assert.True(loaded.AutoPortMapping);
        Assert.Equal(8144, loaded.ListenPort);
        Assert.Equal("LegacyNode", loaded.NodeName);
        Assert.False(File.Exists(scope.PrimaryPath));
    }

    [Fact]
    public async Task SaveAsync_MigratesLegacySettingsToPrimaryAndPrefersPrimaryOnSubsequentLoads()
    {
        using var scope = new TempSettingsScope();
        var legacyStore = new NovaSettingsStore(scope.LegacyPath);
        await legacyStore.SaveAsync(new NovaOptions
        {
            AutoPortMapping = true,
            EnableDetailedDiagnostics = true,
        });

        var store = new NovaSettingsStore(scope.PrimaryPath, legacyFallbackPath: scope.LegacyPath);
        await store.UpdateAsync(current => current with { NodeName = "MigratedNode", ListenPort = 9144 });

        Assert.True(File.Exists(scope.PrimaryPath));

        // 旧ファイルを別値で書き換えても、移行後のプライマリが優先されること
        await legacyStore.SaveAsync(new NovaOptions { AutoPortMapping = false, ListenPort = 7144 });

        var reloaded = store.Load();
        Assert.True(reloaded.AutoPortMapping);
        Assert.True(reloaded.EnableDetailedDiagnostics);
        Assert.Equal("MigratedNode", reloaded.NodeName);
        Assert.Equal(9144, reloaded.ListenPort);
    }

    [Fact]
    public async Task Load_DoesNotReadFallbackWhenLegacyFallbackIsNull()
    {
        using var scope = new TempSettingsScope();
        var legacyStore = new NovaSettingsStore(scope.LegacyPath);
        await legacyStore.SaveAsync(new NovaOptions { AutoPortMapping = true });

        var store = new NovaSettingsStore(scope.PrimaryPath);

        var loaded = store.Load();
        Assert.False(loaded.AutoPortMapping);
    }

    private sealed class TempSettingsScope : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "nova-settings-migration-" + Guid.NewGuid().ToString("N"));

        public string PrimaryPath => Path.Combine(_root, "PeerCast Nova", "settings.json");
        public string LegacyPath => Path.Combine(_root, "PeerCast", "Nova", "settings.json");

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
