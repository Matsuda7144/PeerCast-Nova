using System.Runtime.InteropServices;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

/// <summary>
/// Folder-only shell picker for the Modern recording-path draft. This does not
/// persist settings or create the selected directory.
/// </summary>
internal static class GenesisRecordingDirectoryPicker
{
    internal readonly record struct Result(string? SelectedPath, bool Failed);

    internal static Result Browse(nint owner)
    {
        if (!OperatingSystem.IsWindows() || owner == 0) return new(null, true);
        nint displayName = 0;
        nint selected = 0;
        try
        {
            displayName = Marshal.AllocCoTaskMem(260 * sizeof(char));
            var info = new Win32BrowseInfo
            {
                Owner = owner,
                DisplayName = displayName,
                Title = "録画保存先フォルダーを選択してください",
                Flags = 0x0001, // BIF_RETURNONLYFSDIRS; avoids STA-only BIF_NEWDIALOGSTYLE on the host UI thread.
            };
            selected = SHBrowseForFolderW(ref info);
            if (selected == 0) return new(null, false); // User cancellation is not an error.
            var path = new char[32768];
            if (!SHGetPathFromIDListEx(selected, path, (uint)path.Length, 0))
                return new(null, true);
            var end = Array.IndexOf(path, '\0');
            if (end <= 0 || !GenesisRecordingOutputDirectorySettings.TryNormalize(new string(path, 0, end), out var normalized))
                return new(null, true);
            return new(normalized, false);
        }
        catch (Exception exception) when (exception is ExternalException or ArgumentException or NotSupportedException)
        {
            return new(null, true);
        }
        finally
        {
            if (selected != 0) Marshal.FreeCoTaskMem(selected);
            if (displayName != 0) Marshal.FreeCoTaskMem(displayName);
        }
    }
}
