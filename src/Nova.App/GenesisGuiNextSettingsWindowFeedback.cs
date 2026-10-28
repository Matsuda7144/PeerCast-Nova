using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

internal enum GenesisGuiNextSettingsWindowKind
{
    YellowPages,
    NetworkRelay,
    Playback,
    Recording,
    Connection,
    Publishing,
}

internal sealed record GenesisGuiNextSettingsWindowFeedbackPresentation(
    string Message,
    string Caption);

internal static class GenesisGuiNextSettingsWindowFeedback
{
    public static GenesisGuiNextSettingsWindowFeedbackPresentation Resolve(
        GenesisGuiNextSettingsWindowKind kind) =>
        kind switch
        {
            GenesisGuiNextSettingsWindowKind.YellowPages =>
                new(
                    "Yellow Page settings could not be opened.",
                    "Yellow Pages"),
            GenesisGuiNextSettingsWindowKind.NetworkRelay =>
                new(
                    "Network / Relay settings could not be opened.",
                    "Network / Relay"),
            GenesisGuiNextSettingsWindowKind.Playback =>
                new(
                    "Playback settings could not be opened.",
                    "Playback"),
            GenesisGuiNextSettingsWindowKind.Recording =>
                new("録画保存先の設定を開けませんでした。", "録画保存先"),
            GenesisGuiNextSettingsWindowKind.Connection =>
                new("接続・帯域・リレー設定を開けませんでした。", "接続 / 帯域 / リレー数"),
            GenesisGuiNextSettingsWindowKind.Publishing =>
                new("配信設定を開けませんでした。", "配信設定"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    public static void ReportIfFailed(
        bool opened,
        GenesisGuiNextSettingsWindowKind kind,
        Action<GenesisGuiNextSettingsWindowFeedbackPresentation> present)
    {
        ArgumentNullException.ThrowIfNull(present);
        if (opened)
        {
            return;
        }

        var presentation = Resolve(kind);
        try
        {
            present(presentation);
        }
        catch
        {
            // Opening feedback is auxiliary presentation. It must never replace
            // the bounded Settings action that already failed to create a window.
        }
    }
}

internal static class SystemGenesisGuiNextSettingsWindowFeedback
{
    private const uint MessageBoxOk = 0x00000000;
    private const uint MessageBoxIconWarning = 0x00000030;

    public static void ReportIfFailed(
        nint owner,
        bool opened,
        GenesisGuiNextSettingsWindowKind kind)
    {
        if (opened ||
            !OperatingSystem.IsWindows() ||
            owner == 0 ||
            !IsWindow(owner))
        {
            return;
        }

        GenesisGuiNextSettingsWindowFeedback.ReportIfFailed(
            opened: false,
            kind,
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
