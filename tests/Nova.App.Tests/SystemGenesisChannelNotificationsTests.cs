namespace PeerCast.Nova.App.Tests;

public sealed class SystemGenesisChannelNotificationsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeBalloonLeavesSoundOwnershipWithDispatcher(bool playSound)
    {
        var presenter = new RecordingNativeDataPresenter();
        var soundPlayer = new RecordingSoundPlayer();
        var dispatcher = new GenesisChannelNotificationDispatcher(presenter, soundPlayer);
        var notification = new GenesisChannelNotification(
            new GenesisChannelItem("Channel", "pcp://example/channel"),
            GenesisChannelNotificationReason.NewChannel,
            playSound);

        await dispatcher.DispatchAsync(notification);

        // NIIF_INFO | NIIF_NOSOUND | NIIF_RESPECT_QUIET_TIME. The native
        // balloon must stay silent even when the dispatcher plays one sound.
        Assert.Equal(0x00000091u, Assert.Single(presenter.InfoFlags));
        Assert.Equal(playSound ? 1 : 0, soundPlayer.PlayCount);
    }

    [Fact]
    public void NotificationTitlePrefersFavoriteReasonWhenCombined()
    {
        Assert.Equal(
            "Favorite channel is live",
            SystemGenesisChannelNotificationPresenter.BuildNotificationTitle(
                GenesisChannelNotificationReason.NewChannel |
                    GenesisChannelNotificationReason.FavoriteChannel));
        Assert.Equal(
            "New channel is live",
            SystemGenesisChannelNotificationPresenter.BuildNotificationTitle(
                GenesisChannelNotificationReason.NewChannel));
    }

    [Fact]
    public void NotificationBodyFallsBackToSourceAndFitsNativeBuffer()
    {
        var source = $"pcp://example/{new string('x', 300)}";
        var notification = new GenesisChannelNotification(
            new GenesisChannelItem(string.Empty, source),
            GenesisChannelNotificationReason.NewChannel,
            PlaySound: true);

        var body = SystemGenesisChannelNotificationPresenter.BuildNotificationBody(
            notification);

        Assert.Equal(255, body.Length);
        Assert.StartsWith("pcp://example/", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonWindowsRuntimeFallsBackToNoopPublisher()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var runtime = SystemGenesisChannelNotificationRuntime.Create(7144);

        Assert.Same(
            EmptyGenesisChannelPresenceNotificationPublisher.Instance,
            runtime.Publisher);
        await runtime.Lifetime.DisposeAsync();
    }

    [Fact]
    public async Task NonWindowsSystemSoundIsBoundedNoop()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await SystemGenesisSystemNotificationSoundPlayer.Instance.PlayAsync();
    }

    private sealed class RecordingNativeDataPresenter : IGenesisChannelNotificationPresenter
    {
        public List<uint> InfoFlags { get; } = [];

        public ValueTask PresentAsync(
            GenesisChannelNotification notification,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var data = SystemGenesisChannelNotificationPresenter.CreateNotificationData(
                (nint)1, (nint)2, notification);
            InfoFlags.Add(data.InfoFlags);
            return ValueTask.CompletedTask;
        }
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
