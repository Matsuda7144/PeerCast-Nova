using System.Diagnostics;
using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal enum GenesisRecordingFileContextCommand
{
    None = 0,
    PlayFile = 1,
    RevealInExplorer = 2,
    CopyPath = 3,
    DeleteFile = 4,
}

internal sealed record GenesisRecordingFileContextTarget(
    string FilePath,
    string DisplayName,
    bool IsLibraryRow);

internal interface IGenesisRecordingFileContextEffects
{
    bool FileExists(string filePath);
    void PlayFile(string filePath);
    void RevealInExplorer(string filePath);
    void CopyPath(string filePath);
    bool ConfirmDelete(string displayName, string filePath);
    void DeleteFile(string filePath);
    void ReportError(string title, string message);
}

internal static class GenesisRecordingFileContextActions
{
    public static bool TryCapture(
        GenesisGuiNextNativeTabRow? row,
        out GenesisRecordingFileContextTarget? target)
    {
        if (row?.RecordingLibrary is { } libraryRow)
        {
            var path = libraryRow.Path;
            if (!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path))
            {
                var displayName = !string.IsNullOrWhiteSpace(libraryRow.Entry.Filename)
                    ? libraryRow.Entry.Filename
                    : Path.GetFileName(path);
                target = new(path, displayName, IsLibraryRow: true);
                return true;
            }
        }
        else if (row?.RecordingHistory is { } historyRow)
        {
            var path = historyRow.Fact.WriteResult?.Path;
            if (!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path))
            {
                var displayName = !string.IsNullOrWhiteSpace(historyRow.Filename) &&
                    historyRow.Filename != "—"
                    ? historyRow.Filename
                    : Path.GetFileName(path);
                target = new(path, displayName, IsLibraryRow: false);
                return true;
            }
        }

        target = null;
        return false;
    }

    public static async Task<bool> ExecuteAsync(
        GenesisRecordingFileContextTarget target,
        GenesisRecordingFileContextCommand command,
        IGenesisRecordingFileContextEffects effects,
        Func<Task>? refreshAfterDelete = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(effects);

        try
        {
            switch (command)
            {
                case GenesisRecordingFileContextCommand.PlayFile:
                    if (!effects.FileExists(target.FilePath))
                    {
                        effects.ReportError(
                            "録画ファイル操作",
                            $"録画ファイルが見つかりません:\n{target.FilePath}");
                        return false;
                    }

                    effects.PlayFile(target.FilePath);
                    return true;

                case GenesisRecordingFileContextCommand.RevealInExplorer:
                    if (!effects.FileExists(target.FilePath))
                    {
                        effects.ReportError(
                            "録画ファイル操作",
                            $"録画ファイルが見つかりません:\n{target.FilePath}");
                        return false;
                    }

                    effects.RevealInExplorer(target.FilePath);
                    return true;

                case GenesisRecordingFileContextCommand.CopyPath:
                    effects.CopyPath(target.FilePath);
                    return true;

                case GenesisRecordingFileContextCommand.DeleteFile:
                    if (!effects.FileExists(target.FilePath))
                    {
                        effects.ReportError(
                            "録画ファイル操作",
                            $"録画ファイルが見つかりません:\n{target.FilePath}");
                        return false;
                    }

                    if (!effects.ConfirmDelete(target.DisplayName, target.FilePath))
                    {
                        return false;
                    }

                    effects.DeleteFile(target.FilePath);
                    if (refreshAfterDelete is not null)
                    {
                        try
                        {
                            await refreshAfterDelete().ConfigureAwait(false);
                        }
                        catch
                        {
                            // ファイル削除自体は完了しているため、一覧更新の例外は握りつぶす。
                        }
                    }

                    return true;

                default:
                    return false;
            }
        }
        catch (Exception exception)
        {
            effects.ReportError(
                "録画ファイル操作",
                $"録画ファイルの操作に失敗しました:\n{exception.Message}");
            return false;
        }
    }
}

internal sealed class SystemGenesisRecordingFileContextEffects(nint owner)
    : IGenesisRecordingFileContextEffects
{
    private const uint MbOk = 0x00000000;
    private const uint MbYesNo = 0x00000004;
    private const uint MbIconError = 0x00000010;
    private const uint MbIconWarning = 0x00000030;
    private const uint MbDefButton2 = 0x00000100;
    private const int IdYes = 6;

    public bool FileExists(string filePath) => File.Exists(filePath);

    public void PlayFile(string filePath) =>
        _ = Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });

    public void RevealInExplorer(string filePath)
    {
        if (!filePath.Contains('"', StringComparison.Ordinal))
        {
            _ = Process.Start(
                new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"")
                {
                    UseShellExecute = true,
                });
            return;
        }

        var parentDirectory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(parentDirectory))
        {
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            start.ArgumentList.Add(parentDirectory);
            _ = Process.Start(start);
        }
    }

    public void CopyPath(string filePath) =>
        _ = GenesisChannelContextMenuNative.CopySourceToClipboard(owner, filePath);

    public bool ConfirmDelete(string displayName, string filePath) =>
        MessageBoxW(
            owner,
            $"次の録画ファイルを削除しますか？\n\n{displayName}\n{filePath}\n\nこの操作は元に戻せません。",
            "録画ファイルの削除",
            MbYesNo | MbIconWarning | MbDefButton2) == IdYes;

    public void DeleteFile(string filePath) => File.Delete(filePath);

    public void ReportError(string title, string message) =>
        _ = MessageBoxW(owner, message, title, MbOk | MbIconError);
}
