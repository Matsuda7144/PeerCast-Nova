namespace PeerCast.Nova.App;

/// <summary>
/// Composes GUI Next tab projection with the platform notification runtime. The
/// tab projection is always retained, even when native notifications are not
/// available for the current window or platform.
/// </summary>
internal static class SystemGenesisGuiNextNotificationRuntime
{
    public static GenesisChannelNotificationRuntime Create(
        long windowToken,
        GenesisGuiNextApplicationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Create(
            state.Tabs,
            () => SystemGenesisChannelNotificationRuntime.Create(
                windowToken,
                state.Favorites,
                () => state.Notifications.Current));
    }

    internal static GenesisChannelNotificationRuntime Create(
        GenesisGuiNextTabsViewModel tabs,
        Func<GenesisChannelNotificationRuntime> notificationRuntimeFactory)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(notificationRuntimeFactory);

        GenesisChannelNotificationRuntime notificationRuntime;
        try
        {
            notificationRuntime = notificationRuntimeFactory() ??
                GenesisChannelNotificationRuntime.Empty;
        }
        catch
        {
            // Native notification initialization is auxiliary. GUI Next channel
            // projection must stay attached to the committed presence stream even
            // if the platform notification surface cannot be initialized.
            notificationRuntime = GenesisChannelNotificationRuntime.Empty;
        }

        return new(
            new GenesisGuiNextChannelPresencePublisher(
                tabs,
                notificationRuntime.Publisher),
            notificationRuntime.Lifetime);
    }
}
