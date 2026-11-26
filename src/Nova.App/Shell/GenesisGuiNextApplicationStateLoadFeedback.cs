using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed record GenesisGuiNextApplicationStateLoadFeedbackPresentation(
    string Message,
    string Caption);

internal static class GenesisGuiNextApplicationStateLoadFeedback
{
    private const string Caption = "PeerCast Nova";

    public static GenesisGuiNextApplicationStateLoadFeedbackPresentation? Resolve(
        GenesisGuiNextApplicationStateLoadFailure failures)
    {
        if (failures == GenesisGuiNextApplicationStateLoadFailure.None)
        {
            return null;
        }

        var sections = new List<string>();
        if ((failures & GenesisGuiNextApplicationStateLoadFailure.Favorites) != 0)
        {
            sections.Add("Favorites");
        }

        if ((failures &
                GenesisGuiNextApplicationStateLoadFailure.NotificationPreferences) != 0)
        {
            sections.Add("Notification settings");
        }

        if ((failures & GenesisGuiNextApplicationStateLoadFailure.YellowPages) != 0)
        {
            sections.Add("Yellow Pages");
        }

        if ((failures & GenesisGuiNextApplicationStateLoadFailure.PlaybackPreferences) != 0)
        {
            sections.Add("Playback settings");
        }

        if ((failures & GenesisGuiNextApplicationStateLoadFailure.ConnectionPreferences) != 0)
        {
            sections.Add("接続・帯域・リレー設定");
        }

        if (sections.Count == 0)
        {
            return null;
        }

        return new(
            $"Some saved GUI data could not be loaded. Defaults are being used for: {string.Join(", ", sections)}. The affected saved data will not be overwritten automatically; changing those items can replace it.",
            Caption);
    }

    public static void Report(
        GenesisGuiNextApplicationStateLoadFailure failures,
        Action<GenesisGuiNextApplicationStateLoadFeedbackPresentation> present)
    {
        ArgumentNullException.ThrowIfNull(present);
        var presentation = Resolve(failures);
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
            // Startup feedback is auxiliary presentation. It must not turn a
            // usable fallback application state into a startup failure.
        }
    }
}

internal static class SystemGenesisGuiNextApplicationStateLoadFeedback
{
    private const uint MessageBoxOk = 0x00000000;
    private const uint MessageBoxIconWarning = 0x00000030;

    public static void Report(
        long windowToken,
        GenesisGuiNextApplicationStateLoadFailure failures)
    {
        if (!OperatingSystem.IsWindows() || windowToken == 0)
        {
            return;
        }

        var owner = (nint)windowToken;
        if (!IsWindow(owner))
        {
            return;
        }

        GenesisGuiNextApplicationStateLoadFeedback.Report(
            failures,
            presentation =>
            {
                _ = MessageBoxW(
                    owner,
                    presentation.Message,
                    presentation.Caption,
                    MessageBoxOk | MessageBoxIconWarning);
            });
    }

}
