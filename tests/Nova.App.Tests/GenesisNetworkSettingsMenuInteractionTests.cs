namespace PeerCast.Nova.App.Tests;

public sealed class GenesisNetworkSettingsMenuInteractionTests
{
    [Fact]
    public async Task NetworkRelaySelectionUsesManagementAction()
    {
        var soundPlayer = new RecordingSoundPlayer();
        var initial = GenesisChannelNotificationPreferences.Default;
        var settings = new GenesisChannelNotificationSettings(initial, soundPlayer);
        var openCount = 0;

        await GenesisChannelNotificationSettingsMenuInteraction.ApplyAsync(
            settings,
            GenesisChannelNotificationSettingsCommand.NetworkRelay,
            showYellowPagesAsync: null,
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
