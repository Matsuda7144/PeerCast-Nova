using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal enum GenesisGuiNextChannelContextFailure
{
    MenuOpen,
    ClipboardCopy,
    ContactClipboardCopy,
    DetailsClipboardCopy,
    ExternalOpen,
    RecordingDirectoryMissing,
    RecordingCommand,
}

internal sealed record GenesisGuiNextChannelContextFeedbackPresentation(
    string Message,
    string Caption);

internal static class GenesisGuiNextChannelContextFeedback
{
    private const string Caption = "Channels";

    public static GenesisGuiNextChannelContextFeedbackPresentation Resolve(
        GenesisGuiNextChannelContextFailure failure) =>
        failure switch
        {
            GenesisGuiNextChannelContextFailure.MenuOpen =>
                new("Channel menu could not be opened. Try again.", Caption),
            GenesisGuiNextChannelContextFailure.ClipboardCopy =>
                new("Channel source could not be copied to the clipboard.", Caption),
            GenesisGuiNextChannelContextFailure.ContactClipboardCopy =>
                new("コンタクトURLをクリップボードへコピーできませんでした。", Caption),
            GenesisGuiNextChannelContextFailure.DetailsClipboardCopy =>
                new("チャンネル情報をクリップボードへコピーできませんでした。", Caption),
            GenesisGuiNextChannelContextFailure.ExternalOpen =>
                new("URLまたは録画フォルダを開けませんでした。", Caption),
            GenesisGuiNextChannelContextFailure.RecordingDirectoryMissing =>
                new("現在の録画保存先フォルダが存在しないか、利用できません。", Caption),
            GenesisGuiNextChannelContextFailure.RecordingCommand =>
                new("録画操作を完了できませんでした。録画状態を確認してください。", Caption),
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null),
        };

    public static void Report(
        GenesisGuiNextChannelContextFailure failure,
        Action<GenesisGuiNextChannelContextFeedbackPresentation> present)
    {
        ArgumentNullException.ThrowIfNull(present);
        try
        {
            present(Resolve(failure));
        }
        catch
        {
            // Channel-context feedback is auxiliary presentation. A native menu or
            // clipboard failure must remain bounded and cannot fault the GUI lifetime.
        }
    }
}

internal static class SystemGenesisGuiNextChannelContextFeedback
{
    private const uint MessageBoxOk = 0x00000000;
    private const uint MessageBoxIconWarning = 0x00000030;

    public static void ReportMenuOpenFailure(nint owner) =>
        Report(owner, GenesisGuiNextChannelContextFailure.MenuOpen);

    public static void ReportClipboardCopyFailure(nint owner) =>
        Report(owner, GenesisGuiNextChannelContextFailure.ClipboardCopy);

    public static void Report(
        nint owner,
        GenesisGuiNextChannelContextFailure failure)
    {
        if (!OperatingSystem.IsWindows() ||
            owner == 0 ||
            !IsWindow(owner))
        {
            return;
        }

        GenesisGuiNextChannelContextFeedback.Report(
            failure,
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
