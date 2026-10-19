using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelCatalogStartupOptionsTests
{
    [Fact]
    public void DefaultStartupOptionsUsePresenceCapableSpChannelCatalog()
    {
        var options = new NovaWindowsStartupOptions();

        Assert.IsAssignableFrom<IGenesisAggregatedChannelCatalog>(options.ChannelCatalog);
    }

    [Fact]
    public void StartupOptionsAcceptExplicitChannelCatalog()
    {
        var catalog = new RecordingCatalog();
        var options = new NovaWindowsStartupOptions
        {
            ChannelCatalog = catalog,
        };

        Assert.Same(catalog, options.ChannelCatalog);
    }

    [Fact]
    public void CompositionRejectsNullChannelCatalogBeforeHostCreation()
    {
        var factory = new RejectingHostFactory();
        var options = new NovaWindowsStartupOptions
        {
            ChannelCatalog = null!,
        };

        Assert.Throws<ArgumentNullException>(() =>
            new NovaWindowsStartupComposition(options, factory));
        Assert.False(factory.CreateCalled);
    }

    private sealed class RecordingCatalog : IGenesisChannelCatalog
    {
        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(GenesisChannelCatalogSnapshot.Empty);
        }
    }

    private sealed class RejectingHostFactory : INovaWindowsStartupHostFactory
    {
        public bool CreateCalled { get; private set; }

        public INovaWindowsStartupHost Create(PlaybackWindowsPhysicalRunnerOptions options)
        {
            CreateCalled = true;
            throw new InvalidOperationException("Host creation should not be reached.");
        }
    }
}
