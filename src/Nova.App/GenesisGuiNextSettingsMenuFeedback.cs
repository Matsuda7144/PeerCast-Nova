using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed record GenesisGuiNextSettingsMenuFeedbackPresentation(
    string Message,
    string Caption);

internal static class GenesisGuiNextSettingsMenuFeedback
{
    private const string Message =
        "Settings menu could not be opened. Try again.";
    private const string Caption = "Settings";

    public static GenesisGuiNextSettingsMenuFeedbackPresentation Resolve() =>
        new(Message, Caption);

    public static void ReportOpenFailure(
        Action<GenesisGuiNextSettingsMenuFeedbackPresentation> present)
    {
        ArgumentNullException.ThrowIfNull(present);
        try
        {
            present(Resolve());
        }
        catch
        {
            // Settings-menu feedback is auxiliary presentation. A native menu
            // failure must remain bounded and cannot fault the GUI lifetime.
        }
    }
}

internal static class SystemGenesisGuiNextSettingsMenuFeedback
{
    private const uint MessageBoxOk = 0x00000000;
    private const uint MessageBoxIconWarning = 0x00000030;

    public static void ReportOpenFailure(nint owner)
    {
        if (!OperatingSystem.IsWindows() ||
            owner == 0 ||
            !IsWindow(owner))
        {
            return;
        }

        GenesisGuiNextSettingsMenuFeedback.ReportOpenFailure(
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
