using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

internal sealed record GenesisGuiNextApplicationStateInitializationFeedbackPresentation(
    string Message,
    string Caption);

internal static class GenesisGuiNextApplicationStateInitializationFeedback
{
    private const string Message =
        "Saved GUI settings could not be initialized. Favorites, notifications, and editable Yellow Page settings are unavailable for this session.";
    private const string Caption = "PeerCast Nova";

    public static GenesisGuiNextApplicationStateInitializationFeedbackPresentation Resolve() =>
        new(Message, Caption);

    public static void Report(
        Action<GenesisGuiNextApplicationStateInitializationFeedbackPresentation> present)
    {
        ArgumentNullException.ThrowIfNull(present);
        try
        {
            present(Resolve());
        }
        catch
        {
            // Startup feedback is auxiliary presentation. A state initialization
            // failure must remain bounded and cannot reject the main GUI lifetime.
        }
    }
}

internal static class SystemGenesisGuiNextApplicationStateInitializationFeedback
{
    private const uint MessageBoxOk = 0x00000000;
    private const uint MessageBoxIconWarning = 0x00000030;

    public static void Report(long windowToken)
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

        GenesisGuiNextApplicationStateInitializationFeedback.Report(
            presentation =>
            {
                _ = MessageBoxW(
                    owner,
                    presentation.Message,
                    presentation.Caption,
                    MessageBoxOk | MessageBoxIconWarning);
            });
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(
        nint window,
        string text,
        string caption,
        uint type);
}
