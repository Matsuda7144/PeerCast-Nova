namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelNotificationSettingsButtonBridgeTests
{
    [Fact]
    public async Task MenuSelectionsToggleApplicationNotificationSettings()
    {
        var settings = new GenesisChannelNotificationSettings(
            GenesisChannelNotificationPreferences.Default,
            new RecordingSoundPlayer());

        await GenesisChannelNotificationSettingsMenuInteraction.ApplyAsync(
            settings,
            GenesisChannelNotificationSettingsCommand.NotifyNewChannels);
        await GenesisChannelNotificationSettingsMenuInteraction.ApplyAsync(
            settings,
            GenesisChannelNotificationSettingsCommand.NotifyFavoriteChannels);
        await GenesisChannelNotificationSettingsMenuInteraction.ApplyAsync(
            settings,
            GenesisChannelNotificationSettingsCommand.PlayNotificationSound);

        Assert.Equal(
            new GenesisChannelNotificationPreferences(
                NotifyNewChannels: false,
                NotifyFavoriteChannels: false,
                PlaySound: false),
            settings.Current);
    }

    [Fact]
    public async Task TestSoundSelectionUsesApplicationSettingsAction()
    {
        var soundPlayer = new RecordingSoundPlayer();
        var settings = new GenesisChannelNotificationSettings(
            new(
                NotifyNewChannels: false,
                NotifyFavoriteChannels: false,
                PlaySound: false),
            soundPlayer);

        await GenesisChannelNotificationSettingsMenuInteraction.ApplyAsync(
            settings,
            GenesisChannelNotificationSettingsCommand.TestSound);

        Assert.Equal(1, soundPlayer.PlayCount);
        Assert.False(settings.Current.PlaySound);
    }

    [Fact]
    public async Task YellowPagesSelectionUsesManagementAction()
    {
        var soundPlayer = new RecordingSoundPlayer();
        var initial = GenesisChannelNotificationPreferences.Default;
        var settings = new GenesisChannelNotificationSettings(initial, soundPlayer);
        var openCount = 0;

        await GenesisChannelNotificationSettingsMenuInteraction.ApplyAsync(
            settings,
            GenesisChannelNotificationSettingsCommand.YellowPages,
            _ =>
            {
                openCount++;
                return ValueTask.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(1, openCount);
        Assert.Equal(initial, settings.Current);
        Assert.Equal(0, soundPlayer.PlayCount);
    }

    [Fact]
    public async Task PlaybackSelectionUsesPlaybackSettingsAction()
    {
        var settings = new GenesisChannelNotificationSettings(
            GenesisChannelNotificationPreferences.Default,
            new RecordingSoundPlayer());
        var openCount = 0;

        await GenesisChannelNotificationSettingsMenuInteraction.ApplyAsync(
            settings,
            GenesisChannelNotificationSettingsCommand.Playback,
            showYellowPagesAsync: null,
            showNetworkRelayAsync: null,
            _ =>
            {
                openCount++;
                return ValueTask.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(1, openCount);
        Assert.Equal(GenesisChannelNotificationPreferences.Default, settings.Current);
    }

    [Fact]
    public async Task UnknownSelectionLeavesApplicationSettingsUnchanged()
    {
        var soundPlayer = new RecordingSoundPlayer();
        var initial = new GenesisChannelNotificationPreferences(
            NotifyNewChannels: false,
            NotifyFavoriteChannels: true,
            PlaySound: false);
        var settings = new GenesisChannelNotificationSettings(initial, soundPlayer);

        await GenesisChannelNotificationSettingsMenuInteraction.ApplyAsync(
            settings,
            (GenesisChannelNotificationSettingsCommand)7144);

        Assert.Equal(initial, settings.Current);
        Assert.Equal(0, soundPlayer.PlayCount);
    }

    [Fact]
    public void SettingsSurfaceFeedbackMapsOnlyUnavailableStateToWarning()
    {
        var warning = GenesisGuiNextSettingsSurfaceFeedback.Resolve(available: false);

        Assert.NotNull(warning);
        Assert.Equal(
            "Settings could not be initialized. Notification, Yellow Page, Network / Relay, and Playback settings are unavailable for this session.",
            warning.Message);
        Assert.Equal("Settings", warning.Caption);
        Assert.Null(GenesisGuiNextSettingsSurfaceFeedback.Resolve(available: true));
    }

    [Fact]
    public void AvailableSettingsSurfaceDoesNotPresentWarning()
    {
        var presented = new List<GenesisGuiNextSettingsSurfaceFeedbackPresentation>();

        GenesisGuiNextSettingsSurfaceFeedback.ReportIfUnavailable(
            available: true,
            presented.Add);

        Assert.Empty(presented);
    }

    [Fact]
    public void UnavailableSettingsSurfacePresentsExactlyOneWarning()
    {
        var presented = new List<GenesisGuiNextSettingsSurfaceFeedbackPresentation>();

        GenesisGuiNextSettingsSurfaceFeedback.ReportIfUnavailable(
            available: false,
            presented.Add);

        var warning = Assert.Single(presented);
        Assert.Equal("Settings", warning.Caption);
    }

    [Fact]
    public void SettingsSurfaceFeedbackFailureCannotFaultGuiLifetime()
    {
        var exception = Record.Exception(() =>
            GenesisGuiNextSettingsSurfaceFeedback.ReportIfUnavailable(
                available: false,
                static _ => throw new InvalidOperationException("Presentation failed.")));

        Assert.Null(exception);
    }

    [Fact]
    public void NativePlacementReportsCaptureAndOpenFailuresThroughSharedFeedback()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "GenesisChannelNotificationSettingsButtonBridge.cs"));

        Assert.Contains(
            "SystemGenesisGuiNextSettingsSurfaceFeedback.ReportIfUnavailable(\n            owner,\n            placement is not null);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "private IAsyncDisposable? FailOpen()",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "SystemGenesisGuiNextSettingsSurfaceFeedback.ReportIfUnavailable(\n            _owner,\n            available: false);",
            source,
            StringComparison.Ordinal);
        Assert.True(
            source.Split("return FailOpen();", StringSplitOptions.None).Length - 1 >= 4,
            "All bounded native placement-open rejection paths should report failure.");
    }

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Repository file was not found: {Path.Combine(relativeParts)}");
    }

    private sealed class RecordingSoundPlayer : IGenesisSystemNotificationSoundPlayer
    {
        public int PlayCount { get; private set; }

        public ValueTask PlayAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlayCount++;
            return ValueTask.CompletedTask;
        }
    }
}
