using System.Runtime.InteropServices;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed record GenesisGuiNextTestSoundFeedbackPresentation(
    string Message,
    string Caption);

internal static class GenesisGuiNextTestSoundFeedbackResolver
{
    private const string PlaybackFailureMessage =
        "Notification sound could not be played.";
    private const string Caption = "Notifications";

    public static GenesisGuiNextTestSoundFeedbackPresentation? Resolve(
        GenesisChannelNotificationSettingsTestSoundResult result) =>
        result == GenesisChannelNotificationSettingsTestSoundResult.Failed
            ? new(PlaybackFailureMessage, Caption)
            : null;
}

internal static class GenesisGuiNextTestSoundFeedback
{
    public static void Bind(
        GenesisChannelNotificationSettings settings,
        Action<GenesisGuiNextTestSoundFeedbackPresentation> present)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(present);
        settings.BindTestSoundFeedback(result =>
        {
            var presentation = GenesisGuiNextTestSoundFeedbackResolver.Resolve(result);
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
                // Test-sound feedback is auxiliary presentation. It must never alter
                // the completed sound action or the Settings UI lifetime.
            }
        });
    }
}

internal static class SystemGenesisGuiNextTestSoundFeedback
{
    private const uint MessageBoxOk = 0x00000000;
    private const uint MessageBoxIconWarning = 0x00000030;

    public static void Bind(GenesisChannelNotificationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        GenesisGuiNextTestSoundFeedback.Bind(settings, Present);
    }

    private static void Present(
        GenesisGuiNextTestSoundFeedbackPresentation presentation)
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

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

}
