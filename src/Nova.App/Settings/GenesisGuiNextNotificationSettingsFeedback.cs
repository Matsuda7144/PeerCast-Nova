using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed record GenesisGuiNextNotificationSettingsFeedbackPresentation(
    string Message,
    string Caption);

internal static class GenesisGuiNextNotificationSettingsFeedbackResolver
{
    private const string PersistenceFailureMessage =
        "Notification settings could not be saved.";
    private const string Caption = "Notifications";

    public static GenesisGuiNextNotificationSettingsFeedbackPresentation? Resolve(
        GenesisChannelNotificationSettingsMutationResult result) =>
        result == GenesisChannelNotificationSettingsMutationResult.PersistenceFailed
            ? new(PersistenceFailureMessage, Caption)
            : null;
}

internal static class GenesisGuiNextNotificationSettingsFeedback
{
    public static void Bind(
        GenesisChannelNotificationSettings settings,
        Action<GenesisGuiNextNotificationSettingsFeedbackPresentation> present)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(present);
        settings.BindFeedback(result =>
        {
            var presentation =
                GenesisGuiNextNotificationSettingsFeedbackResolver.Resolve(result);
            if (presentation is null)
            {
                return;
            }

            try
            {
                present(presentation);
            }
            catch
            {
                // Notification-settings feedback is auxiliary presentation. It must
                // never alter the already-completed persistence result.
            }
        });
    }
}

internal static class SystemGenesisGuiNextNotificationSettingsFeedback
{
    private const uint MessageBoxOk = 0x00000000;
    private const uint MessageBoxIconWarning = 0x00000030;

    public static void Bind(GenesisChannelNotificationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        GenesisGuiNextNotificationSettingsFeedback.Bind(settings, Present);
    }

    private static void Present(
        GenesisGuiNextNotificationSettingsFeedbackPresentation presentation)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _ = MessageBoxW(
            GetForegroundWindow(),
            presentation.Message,
            presentation.Caption,
            MessageBoxOk | MessageBoxIconWarning);
    }
}
