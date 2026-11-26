using System.Collections.Concurrent;
using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal static class GenesisMainWindowControlIds
{
    internal const int SourceLabel = 1001;
    internal const int SourceEdit = 1002;
    internal const int PlayButton = 1003;
    internal const int StopButton = 1004;
    internal const int ChannelList = 1005;
    internal const int StatusText = 1006;
}

internal readonly record struct GenesisMainWindowControlHandles(
    nint Owner,
    nint SourceLabel,
    nint SourceEdit,
    nint PlayButton,
    nint StopButton,
    nint ChannelList,
    nint StatusText);

/// <summary>
/// メインウィンドウ上のネイティブ子コントロール HWND をテキスト検索ではなく
/// 明示登録・コントロール ID ベースで解決するレジストリ。
/// </summary>
internal static class GenesisMainWindowControlRegistry
{
    private static readonly ConcurrentDictionary<nint, GenesisMainWindowControlHandles> HandlesByOwner = new();

    internal static void Register(GenesisMainWindowControlHandles handles)
    {
        if (handles.Owner != 0)
        {
            HandlesByOwner[handles.Owner] = handles;
        }
    }

    internal static void Unregister(nint owner)
    {
        if (owner != 0)
        {
            _ = HandlesByOwner.TryRemove(owner, out _);
        }
    }

    internal static bool TryResolve(nint owner, out GenesisMainWindowControlHandles handles)
    {
        if (owner == 0)
        {
            handles = default;
            return false;
        }

        if (HandlesByOwner.TryGetValue(owner, out var registered))
        {
            if (IsRegisteredEntryValid(owner, registered))
            {
                handles = registered;
                return true;
            }

            _ = HandlesByOwner.TryRemove(owner, out _);
        }

        if (!OperatingSystem.IsWindows())
        {
            handles = default;
            return false;
        }

        var sourceLabel = GetDlgItem(owner, GenesisMainWindowControlIds.SourceLabel);
        var sourceEdit = GetDlgItem(owner, GenesisMainWindowControlIds.SourceEdit);
        var playButton = GetDlgItem(owner, GenesisMainWindowControlIds.PlayButton);
        var stopButton = GetDlgItem(owner, GenesisMainWindowControlIds.StopButton);
        var channelList = GetDlgItem(owner, GenesisMainWindowControlIds.ChannelList);
        var statusText = GetDlgItem(owner, GenesisMainWindowControlIds.StatusText);

        if (sourceLabel == 0 &&
            sourceEdit == 0 &&
            playButton == 0 &&
            stopButton == 0 &&
            channelList == 0 &&
            statusText == 0)
        {
            // コントロール ID 未付与の従来ウィンドウ／テストモック互換フォールバック。
            sourceLabel = FindWindowExW(owner, 0, "STATIC", "Source");
            sourceEdit = FindWindowExW(owner, 0, "EDIT", null);
            playButton = FindWindowExW(owner, 0, "BUTTON", "Play");
            stopButton = FindWindowExW(owner, 0, "BUTTON", "Stop");
            channelList = FindWindowExW(owner, 0, "SysListView32", null);
            statusText = sourceLabel == 0 ? 0 : FindWindowExW(owner, sourceLabel, "STATIC", null);
        }

        handles = new(
            owner,
            sourceLabel,
            sourceEdit,
            playButton,
            stopButton,
            channelList,
            statusText);
        return handles.SourceLabel != 0 ||
            handles.SourceEdit != 0 ||
            handles.PlayButton != 0 ||
            handles.StopButton != 0 ||
            handles.ChannelList != 0 ||
            handles.StatusText != 0;
    }

    internal static nint ResolveChannelList(nint owner)
    {
        if (owner == 0)
        {
            return 0;
        }

        if (TryResolve(owner, out var handles) && handles.ChannelList != 0)
        {
            return handles.ChannelList;
        }

        return OperatingSystem.IsWindows() ? FindWindowExW(owner, 0, "SysListView32", null) : 0;
    }

    internal static nint ResolveSourceEdit(nint owner)
    {
        if (owner == 0)
        {
            return 0;
        }

        if (TryResolve(owner, out var handles) && handles.SourceEdit != 0)
        {
            return handles.SourceEdit;
        }

        return OperatingSystem.IsWindows() ? FindWindowExW(owner, 0, "EDIT", null) : 0;
    }

    internal static nint ResolveStatusText(nint owner)
    {
        if (owner == 0)
        {
            return 0;
        }

        if (TryResolve(owner, out var handles) && handles.StatusText != 0)
        {
            return handles.StatusText;
        }

        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        var sourceLabel = FindWindowExW(owner, 0, "STATIC", "Source");
        return sourceLabel == 0 ? 0 : FindWindowExW(owner, sourceLabel, "STATIC", null);
    }

    private static bool IsRegisteredEntryValid(nint owner, GenesisMainWindowControlHandles registered)
    {
        if (!OperatingSystem.IsWindows() || !IsWindow(owner))
        {
            return true;
        }

        return registered.ChannelList == 0 || IsWindow(registered.ChannelList);
    }
}
