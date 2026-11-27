using System.Runtime.InteropServices;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

using static PeerCast.Nova.App.GenesisGuiNextAppShellBridge;
using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

/// <summary>
/// リレーツリータブのネイティブコントロール生成・状態同期・下流切断操作を担うコントローラー。
/// </summary>
internal sealed class GenesisRelayTreeTabController
{
    private readonly IGenesisGuiNextShellHost _host;
    private nint _relayTreeBody;
    private nint _relayTreeTitle;
    private nint _relayTreeChannels;
    private nint _relayTreeRefresh;
    private nint _relayTreeMask;
    private nint _relayTreeSummary;
    private nint _relayTreeDetails;
    private bool _treeUpdating;
    private bool _treeListHidden;
    private bool _maskTreeAddresses = true;
    private GenesisChannelItem[] _treeChannels = [];
    private ChannelId? _treeChannelId;
    private GenesisRelayTreeSnapshot _treeSnapshot = GenesisRelayTreeSnapshot.Empty();
    private GenesisRelayTreeSelectionKey? _treeSelected;
    private Task? _treeDisconnectTask;
    private TreeDisconnectStatus? _treeDisconnectStatus;
    private readonly Dictionary<GenesisRelayTreeSelectionKey, (nint Handle, string Text, string? Parent)> _treeItems = new();
    private (ChannelId?, ChannelSessionId?) _treeDisplayKey;

    private sealed record TreeDisconnectStatus(GenesisRelayTreeSelectionKey Key, PcpDownstreamDisconnectOutcome? Outcome);

    internal GenesisRelayTreeTabController(IGenesisGuiNextShellHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    internal nint ChannelsComboWindow => _relayTreeChannels;

    internal nint[] VisualControls =>
    [
        _relayTreeBody,
        _relayTreeTitle,
        _relayTreeChannels,
        _relayTreeRefresh,
        _relayTreeMask,
        _relayTreeSummary,
        _relayTreeDetails,
    ];

    internal bool IsPageShown =>
        GenesisGuiNextRelayTreeLayout.Applies(_host.IsModern, _host.Tabs.SelectedTab) && !_host.IsSettingsPageOpen;

    internal void Create()
    {
        if (!_host.IsModern)
        {
            return;
        }

        var init = new Win32InitCommonControlsEx { Size = 8, Classes = 2 };
        _ = InitCommonControlsEx(ref init);
        var instance = GetModuleHandleW(null);
        nint Create(string type, string text, uint style, int id) => CreateWindowExW(
            0,
            type,
            text,
            WindowStyleChild | style,
            0,
            0,
            1,
            1,
            GetParent(_host.ListView),
            (nint)id,
            instance,
            0);

        _relayTreeTitle = Create("STATIC", "リレーツリー", StaticStyleNoPrefix, 0);
        _relayTreeSummary = Create("STATIC", "", StaticStyleNoPrefix, 0);
        _relayTreeDetails = Create("STATIC", "", StaticStyleNoPrefix, 0);
        _relayTreeChannels = Create("COMBOBOX", "", WindowStyleTabStop | 0x00200000 | 0x0213, RelayTreeChannelsControlId);
        _ = PeerCast.Nova.Windows.Win32AccessibilityAnnotation.SetName(_relayTreeChannels, "リレーツリー対象チャンネル");
        _relayTreeRefresh = Create("BUTTON", "更新", WindowStyleTabStop | ButtonStyleOwnerDraw, RelayTreeRefreshControlId);
        _relayTreeMask = Create("BUTTON", "IP: 非表示", WindowStyleTabStop | ButtonStyleOwnerDraw, RelayTreeMaskControlId);
        _relayTreeBody = Create("SysTreeView32", "", WindowStyleTabStop | 1 | 2 | 4 | 0x20 | 0x200, RelayTreeBodyControlId);
        _ = PeerCast.Nova.Windows.Win32AccessibilityAnnotation.SetNameAndDescription(
            _relayTreeBody,
            "リレーツリー",
            help: "矢印キーでノードを選択し、メニューキーまたは右クリックで操作します");
        foreach (var control in VisualControls)
        {
            _host.ApplyDefaultFont(control);
        }

        _ = SendMessageW(_relayTreeChannels, 0x0153, nuint.MaxValue, _host.Scale(22));
        _ = SendMessageW(_relayTreeChannels, 0x0153, 0, _host.Scale(22));
        _ = SetWindowTheme(_relayTreeBody, " ", " ");
        _ = SendMessageW(_relayTreeBody, 0x111D, 0, GenesisGuiNextDesign.Background);
        _ = SendMessageW(_relayTreeBody, 0x111E, 0, GenesisGuiNextDesign.Text);
        _ = SendMessageW(_relayTreeBody, 0x1128, 0, GenesisGuiNextDesign.Border);
    }

    internal void Detach()
    {
        _ = PeerCast.Nova.Windows.Win32AccessibilityAnnotation.Clear(_relayTreeChannels);
        _ = PeerCast.Nova.Windows.Win32AccessibilityAnnotation.Clear(_relayTreeBody);
    }

    internal void CaptureSelectedChannel()
    {
        var index = SendMessageW(_host.ListView, ListViewMessageGetNextItem, nuint.MaxValue, (nint)ListViewNextItemSelected).ToInt32();
        if (GenesisGuiNextRelayActivation.TryResolve(
                GenesisChannelListRenderedChannels.FindRow(_host.ListView, index)?.DisplayChannel,
                out var id))
        {
            _treeChannelId = id;
        }
    }

    internal bool ProcessCommand(int id, nint control, int notification)
    {
        if (!IsPageShown || _treeUpdating)
        {
            return false;
        }

        if (id == RelayTreeChannelsControlId && control == _relayTreeChannels && notification == 1)
        {
            var index = SendMessageW(control, 0x0147, 0, 0).ToInt32();
            _treeChannelId = index >= 0 &&
                index < _treeChannels.Length &&
                GenesisGuiNextRelayActivation.TryResolve(_treeChannels[index], out var channel)
                ? channel
                : null;
        }
        else if (id == RelayTreeMaskControlId && control == _relayTreeMask && notification == 0)
        {
            _maskTreeAddresses = !_maskTreeAddresses;
        }
        else if (id != RelayTreeRefreshControlId || control != _relayTreeRefresh || notification != 0)
        {
            return false;
        }

        _host.SynchronizeNativeSelection();
        return true;
    }

    internal bool SynchronizeVisibility()
    {
        var shown = IsPageShown && _relayTreeBody != 0;
        foreach (var control in VisualControls)
        {
            if (control != 0)
            {
                _ = _host.PresentVisible(control, shown ? ShowWindowShowNoActivate : ShowWindowHide);
            }
        }

        if (shown || (_treeListHidden && !_host.IsSettingsPageOpen))
        {
            _treeListHidden = shown;
            _host.SynchronizeMainPaneVisibility();
        }

        return shown;
    }

    internal bool DrawControl(nint pointer)
    {
        if (!IsPageShown || pointer == 0)
        {
            return false;
        }

        var draw = Marshal.PtrToStructure<Win32DrawItem>(pointer);
        using var font = GenesisWindowUiFont.SelectForDrawing(draw.DeviceContext, draw.ControlWindow);
        if (draw.ControlWindow == _relayTreeChannels && draw.ControlType == 3)
        {
            return _host.DrawYellowPageItem(draw);
        }

        if (draw.ControlWindow != _relayTreeRefresh && draw.ControlWindow != _relayTreeMask)
        {
            return false;
        }

        return _host.DrawRecordingModeButton(
            draw,
            _host.ReadControlText(draw.ControlWindow),
            draw.ControlWindow == _relayTreeMask && !_maskTreeAddresses);
    }

    private GenesisChannelItem? SynchronizeChannels()
    {
        var channels = _host.Tabs.Channels
            .Where(c => GenesisGuiNextRelayActivation.TryResolve(c, out _))
            .DistinctBy(c =>
            {
                GenesisGuiNextRelayActivation.TryResolve(c, out var id);
                return id;
            })
            .ToArray();
        if (!_treeChannels.SequenceEqual(channels))
        {
            _treeUpdating = true;
            try
            {
                _ = SendMessageW(_relayTreeChannels, 0x014B, 0, 0);
                foreach (var channel in channels)
                {
                    _ = SendMessageTextW(_relayTreeChannels, 0x0143, 0, channel.Name);
                }

                _treeChannels = channels;
            }
            finally
            {
                _treeUpdating = false;
            }
        }

        var selected = Array.FindIndex(
            channels,
            c => GenesisGuiNextRelayActivation.TryResolve(c, out var id) && id == _treeChannelId);
        if (selected < 0 && channels.Length > 0)
        {
            selected = 0;
        }

        _treeUpdating = true;
        try
        {
            _ = SendMessageW(_relayTreeChannels, 0x014E, (nuint)selected, 0);
        }
        finally
        {
            _treeUpdating = false;
        }

        if (selected < 0)
        {
            _treeChannelId = null;
            return null;
        }

        GenesisGuiNextRelayActivation.TryResolve(channels[selected], out _treeChannelId);
        return channels[selected];
    }

    private void PlacePage()
    {
        if (!GetWindowRect(_host.ListView, out Win32Rect list) ||
            !GetWindowRect(_host.DetailsPane, out Win32Rect details))
        {
            return;
        }

        var point = new Win32Point { X = list.Left, Y = list.Top };
        if (!ScreenToClient(GetParent(_host.ListView), ref point))
        {
            return;
        }

        var x = _host.LogicalWidth(point.X);
        var y = _host.LogicalWidth(point.Y);
        var width = _host.LogicalWidth(list.Right - list.Left);
        var height = _host.LogicalWidth(details.Bottom - list.Top);
        var layout = new GenesisGuiNextRelayTreeLayout(width, height);
        void Place(nint control, int left, int top, int w, int h) =>
            _ = _host.PresentBounds(control, x + left, y + top, w, h, true);
        Place(_relayTreeTitle, 12, 4, 120, 28);
        Place(_relayTreeChannels, 140, 4, Math.Max(120, width - 400), 240);
        Place(_relayTreeRefresh, width - 240, 4, 68, 28);
        Place(_relayTreeMask, width - 160, 4, 148, 28);
        Place(_relayTreeSummary, 12, 38, width - 24, 22);
        Place(_relayTreeBody, layout.Body.Left, layout.Body.Top, layout.Body.Width, layout.Body.Height);
        Place(_relayTreeDetails, 12, layout.Details.Top, width - 24, layout.Details.Height);
        _ = _host.PresentText(_relayTreeMask, _maskTreeAddresses ? "IP: 非表示" : "IP: 表示中");
    }

    internal void Present()
    {
        var snapshot = _host.ViewModel.ReadRelayTree(SynchronizeChannels());
        _treeSnapshot = snapshot;
        var scope = (snapshot.ChannelId, snapshot.OwnerId);
        _treeUpdating = true;
        try
        {
            var nodes = snapshot.Nodes.Take(GenesisRelayTreeTopology.MaximumNodes).ToArray();
            var desired = nodes.Select(n => GenesisRelayTreeSelection.Key(snapshot, n)).ToHashSet();
            var sameScope = scope == _treeDisplayKey;
            var expanded = _treeItems.ToDictionary(
                p => p.Key,
                p => (SendMessageW(_relayTreeBody, 0x1127, (nuint)p.Value.Handle, 0x20).ToInt64() & 0x20) != 0);
            var firstHandle = SendMessageW(_relayTreeBody, 0x110A, 5, 0);
            var firstKey = _treeItems.Where(p => p.Value.Handle == firstHandle).Select(p => p.Key).FirstOrDefault();
            var rebuild = !sameScope || !_treeItems.Keys.ToHashSet().SetEquals(desired) ||
                nodes.Any(n => _treeItems.TryGetValue(GenesisRelayTreeSelection.Key(snapshot, n), out var item) && item.Parent != n.ParentId);
            if (rebuild)
            {
                _ = SendMessageW(_relayTreeBody, 0x1101, 0, (nint)(-65536)); // TVI_ROOT
                _treeItems.Clear();
                if (!sameScope)
                {
                    _treeSelected = null;
                }

                _treeDisplayKey = scope;
            }

            // レート・状態更新時も既存ノードのハンドルを維持し、展開状態・キャレット・スクロール位置を保つ。
            foreach (var node in nodes)
            {
                var key = GenesisRelayTreeSelection.Key(snapshot, node);
                var text = GenesisRelayTreeNodePresentation.Row(_treeSnapshot, node, _maskTreeAddresses);
                if (!_treeItems.TryGetValue(key, out var old))
                {
                    var parent = node.ParentId is { } pid ? nodes.FirstOrDefault(n => n.Id == pid) : null;
                    var parentHandle = parent is not null &&
                        _treeItems.TryGetValue(GenesisRelayTreeSelection.Key(snapshot, parent), out var p)
                        ? p.Handle
                        : (nint)(-65536);
                    var handle = WriteTreeItem(0, parentHandle, text);
                    if (handle != 0)
                    {
                        _treeItems[key] = (handle, text, node.ParentId);
                    }
                }
                else if (old.Text != text)
                {
                    _ = WriteTreeItem(old.Handle, 0, text);
                    _treeItems[key] = (old.Handle, text, node.ParentId);
                }
            }

            if (GenesisRelayTreeSelection.Resolve(snapshot, _treeSelected) is null)
            {
                _treeSelected = null;
            }

            if (rebuild)
            {
                foreach (var pair in _treeItems)
                {
                    if (!sameScope || !expanded.TryGetValue(pair.Key, out var wasExpanded) || wasExpanded)
                    {
                        _ = SendMessageW(_relayTreeBody, 0x1102, 2, pair.Value.Handle);
                    }
                }

                if (_treeSelected is { } selected && _treeItems.TryGetValue(selected, out var caret))
                {
                    _ = SendMessageW(_relayTreeBody, 0x110B, 9, caret.Handle);
                }

                if (sameScope && firstKey is not null && _treeItems.TryGetValue(firstKey, out var first))
                {
                    _ = SendMessageW(_relayTreeBody, 0x110B, 5, first.Handle);
                }
            }
        }
        finally
        {
            _treeUpdating = false;
        }

        PlacePage();
        var status = Volatile.Read(ref _treeDisconnectStatus);
        var prefix = status is not null && status.Key.ChannelId == snapshot.ChannelId && status.Key.OwnerId == snapshot.OwnerId
            ? GenesisRelayTreeDisconnect.Status(status.Outcome) + " / "
            : "";
        _ = _host.PresentText(_relayTreeSummary, prefix + GenesisRelayTreeNodePresentation.Summary(snapshot));
        PresentSelection();
    }

    private void PresentSelection() => _ = _host.PresentText(
        _relayTreeDetails,
        GenesisRelayTreeNodePresentation.Details(
            _treeSnapshot,
            GenesisRelayTreeSelection.Resolve(_treeSnapshot, _treeSelected),
            _maskTreeAddresses));

    internal bool HandleNotify(nint notification)
    {
        if (notification == 0 || _treeUpdating)
        {
            return false;
        }

        var header = Marshal.PtrToStructure<Win32NotifyHeader>(notification);
        if (header.WindowFrom != _relayTreeBody)
        {
            return false;
        }

        // TreeView は親へ NM_RCLICK を送るため、HitTest ベースの独自メニュー表示後に既定処理を抑止する。
        if (header.Code == -5 && GetCursorPos(out var cursor))
        {
            return HandleContextMenu(_relayTreeBody, 0x007B, (nint)((cursor.Y << 16) | (cursor.X & 0xffff)));
        }

        if (header.Code != -451) // TVN_SELCHANGEDW
        {
            return false;
        }

        var caret = SendMessageW(_relayTreeBody, 0x110A, 9, 0);
        _treeSelected = _treeItems.Where(p => p.Value.Handle == caret).Select(p => p.Key).FirstOrDefault();
        _host.SynchronizeNativeSelection();
        return true;
    }

    internal bool HandleContextMenu(nint window, uint message, nint position)
    {
        if (window != _relayTreeBody || message != 0x007B || !IsPageShown)
        {
            return false;
        }

        var x = (short)((long)position & 0xffff);
        var y = (short)(((long)position >> 16) & 0xffff);
        var keyboard = x == -1 && y == -1;
        nint item;
        var point = new Win32Point { X = x, Y = y };
        if (keyboard)
        {
            item = SendMessageW(window, 0x110A, 9, 0);
            _ = GetWindowRect(window, out Win32Rect rect);
            x = (short)(rect.Left + 24);
            y = (short)(rect.Top + 24);
        }
        else
        {
            if (!ScreenToClient(window, ref point))
            {
                return true;
            }

            var hit = new Win32TreeHit { X = point.X, Y = point.Y };
            var memory = Marshal.AllocHGlobal(Marshal.SizeOf<Win32TreeHit>());
            try
            {
                Marshal.StructureToPtr(hit, memory, false);
                item = SendMessageW(window, 0x1111, 0, memory);
                // 行のインデントや右余白のクリックはノード選択とみなさない。
                if ((Marshal.PtrToStructure<Win32TreeHit>(memory).Flags & 0x46) == 0)
                {
                    item = 0;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(memory);
            }
        }

        _treeSelected = _treeItems.Where(p => p.Value.Handle == item).Select(p => p.Key).FirstOrDefault();
        _ = SendMessageW(window, 0x110B, 9, item);
        _host.SynchronizeNativeSelection();
        if (GenesisRelayTreeSelection.Resolve(_treeSnapshot, _treeSelected) is null)
        {
            return true;
        }

        var key = _treeSelected;
        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            return true;
        }

        try
        {
            _ = AppendMenuW(menu, 0, 1, "ノードの詳細を表示");
            _ = AppendMenuW(menu, 0, 2, "表示IDをコピー");
            if (_treeDisconnectTask?.IsCompleted != false &&
                GenesisRelayTreeSelection.HasDownstreamIdentity(_treeSnapshot, key))
            {
                _ = AppendMenuW(menu, 0, 3, "下流を切断...");
            }

            var command = TrackPopupMenu(menu, 0x0100 | 0x0002, x, y, 0, _host.Owner, 0);
            // モーダルメニュー閉鎖後の最新状態で対象ノードを再解決する。
            _host.SynchronizeNativeSelection();
            var node = IsPageShown ? GenesisRelayTreeSelection.Resolve(_treeSnapshot, key) : null;
            if (node is not null && command == 2)
            {
                _ = GenesisChannelContextMenuNative.CopySourceToClipboard(
                    _host.Owner,
                    node.Id,
                    () => GenesisRelayTreeSelection.Resolve(_treeSnapshot, key) is not null);
            }

            if (node is not null && command == 1)
            {
                _treeSelected = key;
                _host.SynchronizeNativeSelection();
                _ = SetFocus(window);
            }

            if (node is not null && command == 3 && key is not null && _treeDisconnectTask?.IsCompleted != false)
            {
                ConfirmDisconnect(key);
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
        }

        return true;
    }

    private void ConfirmDisconnect(GenesisRelayTreeSelectionKey key)
    {
        var target = GenesisRelayTreeDisconnect.Confirm(
            key,
            () =>
            {
                _host.SynchronizeNativeSelection();
                return _treeSnapshot;
            },
            () => !_host.IsDetached && IsPageShown,
            id => MessageBoxW(
                _host.Owner,
                "選択した下流Peerとの接続を切断しますか？\n\nこのPeerを経由している視聴者にも影響する可能性があります。\n\n切断対象: " + id,
                "下流の切断",
                0x134) == 6); // 既定 No の Yes/No ダイアログ（マスク済み表示 ID のみ提示）。
        if (target is null)
        {
            return;
        }

        Volatile.Write(ref _treeDisconnectStatus, new(key, null));
        _treeDisconnectTask = Task.Run(async () =>
        {
            if (_host.IsDetached)
            {
                return;
            }

            PcpDownstreamDisconnectOutcome outcome;
            try
            {
                var receipt = _host.ViewModel.RelayActivation?.RequestDisconnect(target)
                    ?? new(PcpDownstreamDisconnectOutcome.NotOwnedDownstream);
                Volatile.Write(ref _treeDisconnectStatus, new(key, receipt.Outcome));
                outcome = receipt.Completion is { } completion
                    ? await completion.ConfigureAwait(false)
                    : receipt.Outcome;
            }
            catch (Exception)
            {
                outcome = PcpDownstreamDisconnectOutcome.Failed;
            }

            Volatile.Write(ref _treeDisconnectStatus, new(key, outcome));
        });
        _host.SynchronizeNativeSelection();
    }

    private nint WriteTreeItem(nint handle, nint parent, string text)
    {
        var label = Marshal.StringToHGlobalUni(text);
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<Win32TreeInsert>());
        try
        {
            var value = new Win32TreeItem { Mask = 1, Handle = handle, Text = label };
            if (handle == 0)
            {
                Marshal.StructureToPtr(new Win32TreeInsert { Parent = parent, After = (nint)(-65534), Item = value }, memory, false);
                return SendMessageW(_relayTreeBody, 0x1132, 0, memory);
            }

            Marshal.StructureToPtr(value, memory, false);
            return SendMessageW(_relayTreeBody, 0x113F, 0, memory);
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
            Marshal.FreeHGlobal(label);
        }
    }

    internal bool IsTreeControl(nint control) => control != 0 && control == _relayTreeBody;
    internal nint PrepareControl(nuint context) => _host.TopBrush;
}
