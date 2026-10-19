namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextTestSoundFeedbackTests
{
    [Fact]
    public void ResolverMapsOnlyPlaybackFailureToWarning()
    {
        var warning = GenesisGuiNextTestSoundFeedbackResolver.Resolve(
            GenesisChannelNotificationSettingsTestSoundResult.Failed);

        Assert.NotNull(warning);
        Assert.Equal("Notification sound could not be played.", warning.Message);
        Assert.Equal("Notifications", warning.Caption);
        Assert.Null(GenesisGuiNextTestSoundFeedbackResolver.Resolve(
            GenesisChannelNotificationSettingsTestSoundResult.Played));
    }

    [Fact]
    public async Task PlaybackFailurePresentsWarningWithoutChangingPreferences()
    {
        var initial = GenesisChannelNotificationPreferences.Default;
        var settings = new GenesisChannelNotificationSettings(
            initial,
            ThrowingSoundPlayer.Instance);
        var presented = new List<GenesisGuiNextTestSoundFeedbackPresentation>();
        GenesisGuiNextTestSoundFeedback.Bind(settings, presented.Add);

        await settings.TestSoundAsync();

        Assert.Equal(initial, settings.Current);
        var warning = Assert.Single(presented);
        Assert.Equal("Notification sound could not be played.", warning.Message);
        Assert.Equal("Notifications", warning.Caption);
    }

    [Fact]
    public async Task SuccessfulPlaybackDoesNotPresentWarning()
    {
        var settings = new GenesisChannelNotificationSettings(
            GenesisChannelNotificationPreferences.Default,
            SilentSoundPlayer.Instance);
        var presented = new List<GenesisGuiNextTestSoundFeedbackPresentation>();
        GenesisGuiNextTestSoundFeedback.Bind(settings, presented.Add);

        await settings.TestSoundAsync();

        Assert.Empty(presented);
    }

    [Fact]
    public async Task FeedbackFailureCannotFaultTestSoundAction()
    {
        var settings = new GenesisChannelNotificationSettings(
            GenesisChannelNotificationPreferences.Default,
            ThrowingSoundPlayer.Instance);
        GenesisGuiNextTestSoundFeedback.Bind(
            settings,
            static _ => throw new InvalidOperationException("Presentation failed."));

        await settings.TestSoundAsync();
    }

    [Fact]
    public async Task CancellationStillPropagatesWithoutFeedback()
    {
        var settings = new GenesisChannelNotificationSettings(
            GenesisChannelNotificationPreferences.Default,
            SilentSoundPlayer.Instance);
        var presented = new List<GenesisGuiNextTestSoundFeedbackPresentation>();
        GenesisGuiNextTestSoundFeedback.Bind(settings, presented.Add);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => settings.TestSoundAsync(cancellation.Token).AsTask());

        Assert.Empty(presented);
    }

    [Fact]
    public void SystemApplicationStateBindsTestSoundFeedback()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "GenesisGuiNextApplicationState.cs"));

        Assert.Contains(
            "SystemGenesisGuiNextTestSoundFeedback.Bind(state.Notifications);",
            source,
            StringComparison.Ordinal);
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

    private sealed class ThrowingSoundPlayer : IGenesisSystemNotificationSoundPlayer
    {
        public static ThrowingSoundPlayer Instance { get; } = new();

        private ThrowingSoundPlayer()
        {
        }

        public ValueTask PlayAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Sound playback failed.");
        }
    }

    private sealed class SilentSoundPlayer : IGenesisSystemNotificationSoundPlayer
    {
        public static SilentSoundPlayer Instance { get; } = new();

        private SilentSoundPlayer()
        {
        }

        public ValueTask PlayAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }
}
