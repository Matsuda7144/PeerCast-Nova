namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextNotificationSettingsFeedbackTests
{
    [Fact]
    public void ResolverMapsOnlyPersistenceFailureToWarning()
    {
        var warning = GenesisGuiNextNotificationSettingsFeedbackResolver.Resolve(
            GenesisChannelNotificationSettingsMutationResult.PersistenceFailed);

        Assert.NotNull(warning);
        Assert.Equal("Notification settings could not be saved.", warning.Message);
        Assert.Equal("Notifications", warning.Caption);
        Assert.Null(GenesisGuiNextNotificationSettingsFeedbackResolver.Resolve(
            GenesisChannelNotificationSettingsMutationResult.Changed));
        Assert.Null(GenesisGuiNextNotificationSettingsFeedbackResolver.Resolve(
            GenesisChannelNotificationSettingsMutationResult.Unchanged));
    }

    [Fact]
    public void PersistenceFailurePresentsWarningAndLeavesSettingsUnchanged()
    {
        var initial = GenesisChannelNotificationPreferences.Default;
        var settings = CreateSettings(initial);
        settings.BindPersistence(static _ => false);
        var presented = new List<GenesisGuiNextNotificationSettingsFeedbackPresentation>();
        GenesisGuiNextNotificationSettingsFeedback.Bind(settings, presented.Add);

        var result = settings.SetPlaySound(!initial.PlaySound);

        Assert.Equal(
            GenesisChannelNotificationSettingsMutationResult.PersistenceFailed,
            result);
        Assert.Equal(initial, settings.Current);
        var warning = Assert.Single(presented);
        Assert.Equal("Notification settings could not be saved.", warning.Message);
        Assert.Equal("Notifications", warning.Caption);
    }

    [Fact]
    public void SuccessfulAndUnchangedMutationsDoNotPresentWarning()
    {
        var initial = GenesisChannelNotificationPreferences.Default;
        var settings = CreateSettings(initial);
        settings.BindPersistence(static _ => true);
        var presented = new List<GenesisGuiNextNotificationSettingsFeedbackPresentation>();
        GenesisGuiNextNotificationSettingsFeedback.Bind(settings, presented.Add);

        Assert.Equal(
            GenesisChannelNotificationSettingsMutationResult.Changed,
            settings.SetNotifyNewChannels(!initial.NotifyNewChannels));
        Assert.Equal(
            GenesisChannelNotificationSettingsMutationResult.Unchanged,
            settings.SetNotifyNewChannels(!initial.NotifyNewChannels));

        Assert.Empty(presented);
    }

    [Fact]
    public void FeedbackFailureCannotReplacePersistenceResult()
    {
        var initial = GenesisChannelNotificationPreferences.Default;
        var settings = CreateSettings(initial);
        settings.BindPersistence(static _ => false);
        GenesisGuiNextNotificationSettingsFeedback.Bind(
            settings,
            static _ => throw new InvalidOperationException("Presentation failed."));

        var result = settings.SetNotifyFavoriteChannels(
            !initial.NotifyFavoriteChannels);

        Assert.Equal(
            GenesisChannelNotificationSettingsMutationResult.PersistenceFailed,
            result);
        Assert.Equal(initial, settings.Current);
    }

    [Fact]
    public void SystemApplicationStateBindsNotificationSettingsFeedback()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "GenesisGuiNextApplicationState.cs"));

        Assert.Contains(
            "SystemGenesisGuiNextNotificationSettingsFeedback.Bind(state.Notifications);",
            source,
            StringComparison.Ordinal);
    }

    private static GenesisChannelNotificationSettings CreateSettings(
        GenesisChannelNotificationPreferences initial) =>
        new(initial, SilentSoundPlayer.Instance);

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
