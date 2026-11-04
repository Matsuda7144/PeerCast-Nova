using PeerCast.Nova.Foundation.Configuration;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisAppearanceSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "NovaAppearance-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_root, "settings.json");
    private NovaSettingsStore Store => new(SettingsPath);

    public GenesisAppearanceSettingsTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void AbsentSettingsUseClassicWithoutWriting()
    {
        var settings = new GenesisAppearanceSettings(Store);
        Assert.Equal(NovaGuiMode.Classic, settings.CurrentRuntimeMode);
        Assert.Null(settings.Error);
        Assert.False(File.Exists(SettingsPath));
    }

    [Theory]
    [InlineData(NovaGuiMode.Classic, NovaGuiMode.Modern)]
    [InlineData(NovaGuiMode.Modern, NovaGuiMode.Classic)]
    public async Task SavePreservesLatestOtherSettingsAndAppliesOnlyToNextStartup(NovaGuiMode initial, NovaGuiMode next)
    {
        await Store.SaveAsync(new() { GuiMode = initial });
        var settings = new GenesisAppearanceSettings(Store);
        var other = new NovaOptions
        {
            GuiMode = initial,
            NodeName = "existing",
            ListenPort = 17144,
            AutoPortMapping = true,
            EnableDetailedDiagnostics = true,
            RecordingOutputDirectory = Path.Combine(_root, "recordings")
        };
        await Store.SaveAsync(other); // Another settings editor saved after this startup.
        Assert.True(settings.Save(next));
        Assert.Equal(initial, settings.CurrentRuntimeMode);
        Assert.Equal(next, settings.ConfiguredMode);
        Assert.Equal(other with { GuiMode = next }, Store.Load());
        Assert.Equal(next, new GenesisAppearanceSettings(Store).CurrentRuntimeMode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"GuiMode\":\"unknown\"}")]
    [InlineData("{\"GuiMode\":99}")]
    [InlineData("{\"GuiMode\":null}")]
    [InlineData("{\"GuiMode\":{\"unexpected\":[true,2]},\"ListenPort\":17144}")]
    public void OnlyInvalidAppearanceFieldFallsBackWithoutChangingDocument(string json)
    {
        File.WriteAllText(SettingsPath, json);
        var settings = new GenesisAppearanceSettings(Store);
        Assert.Equal(NovaGuiMode.Classic, settings.CurrentRuntimeMode);
        Assert.Null(settings.Error);
        Assert.Equal(json, File.ReadAllText(SettingsPath));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{\"GuiMode\":\"Modern\",\"ListenPort\":\"bad\"}")]
    public void BrokenDocumentsAreReportedAndCannotBeOverwrittenByAppearanceSave(string json)
    {
        File.WriteAllText(SettingsPath, json);
        var settings = new GenesisAppearanceSettings(Store);
        Assert.Equal(NovaGuiMode.Classic, settings.CurrentRuntimeMode);
        Assert.NotNull(settings.Error);
        Assert.False(settings.Save(NovaGuiMode.Modern));
        Assert.NotNull(settings.Error);
        Assert.Equal(NovaGuiMode.Classic, settings.ConfiguredMode);
        Assert.Equal(json, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void WriteFailureDoesNotClaimSavedOrChangeMode()
    {
        var settings = new GenesisAppearanceSettings(Store);
        Directory.CreateDirectory(SettingsPath);
        Assert.False(settings.Save(NovaGuiMode.Modern));
        Assert.Equal(NovaGuiMode.Classic, settings.CurrentRuntimeMode);
        Assert.Equal(NovaGuiMode.Classic, settings.ConfiguredMode);
        Assert.NotNull(settings.Error);
    }

    [Fact]
    public async Task AppearanceMenuDoesNotInvokeRuntimeOrMutateNotificationPreferences()
    {
        var notifications = new GenesisChannelNotificationSettings(GenesisChannelNotificationPreferences.Default, new SilentSound());
        var before = notifications.Current;
        var appearance = 0;
        ValueTask Other(CancellationToken _) => throw new InvalidOperationException("Unexpected settings route.");
        await GenesisChannelNotificationSettingsMenuInteraction.ApplyAsync(notifications,
            GenesisChannelNotificationSettingsCommand.Appearance, Other, Other, Other, Other, Other,
            showAppearanceAsync: _ => { appearance++; return ValueTask.CompletedTask; });
        Assert.Equal(1, appearance);
        Assert.Equal(before, notifications.Current);
    }

    [Theory]
    [InlineData(NovaGuiMode.Classic)]
    [InlineData(NovaGuiMode.Modern)]
    public async Task NativeSurfaceReceivesStartupModeBeforeCreatingControls(NovaGuiMode mode)
    {
        await Store.SaveAsync(new() { GuiMode = mode });
        var settings = new GenesisAppearanceSettings(Store);
        var surface = new CapturingSurface();
        var target = new SystemGenesisInteractiveMainWindowSurfaceTarget(surface, new UnusedBridge(),
            SpGenesisChannelCatalog.Create(), TimeSpan.FromMinutes(1), _ => null, _ => null,
            (_, _) => GenesisChannelNotificationRuntime.Empty, _ => null,
            GenesisUnavailableNetworkSettingsController.Instance, appearanceSettings: settings);
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        Assert.Null(await target.OpenAsync(1, viewModel, CancellationToken.None));
        Assert.Same(settings, surface.Settings);
        Assert.Equal(mode == NovaGuiMode.Modern, surface.Modern);
        Assert.True(settings.Save(mode == NovaGuiMode.Modern ? NovaGuiMode.Classic : NovaGuiMode.Modern));
        Assert.Equal(mode == NovaGuiMode.Modern, viewModel.UsesModernAppearance);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class SilentSound : IGenesisSystemNotificationSoundPlayer
    {
        public ValueTask PlayAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class CapturingSurface : IGenesisMainWindowSurfaceTarget
    {
        internal GenesisAppearanceSettings? Settings;
        internal bool Modern;
        public ValueTask<IAsyncDisposable?> OpenAsync(long windowToken, GenesisMainViewModel viewModel, CancellationToken token)
        {
            Settings = viewModel.AppearanceSettings;
            Modern = viewModel.UsesModernAppearance;
            return ValueTask.FromResult<IAsyncDisposable?>(null);
        }
    }

    private sealed class UnusedBridge : IGenesisMainWindowInteractionBridgeFactory
    {
        public IAsyncDisposable? Create(long windowToken, GenesisMainViewModel viewModel) => throw new InvalidOperationException();
    }
}
