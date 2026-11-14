using System.Runtime.InteropServices;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge
{
    private const int RelayTreeBodyControlId = 1138, RelayTreeChannelsControlId = 1139,
        RelayTreeRefreshControlId = 1140, RelayTreeMaskControlId = 1141;

    private sealed partial class AppShellState
    {
        private nint _relayTreeBody, _relayTreeTitle, _relayTreeChannels, _relayTreeRefresh,
            _relayTreeMask, _relayTreeSummary, _relayTreeDetails;
        private bool _treeUpdating, _treeListHidden, _maskTreeAddresses = true;
        private GenesisChannelItem[] _treeChannels = [];
        private ChannelId? _treeChannelId;
        private GenesisRelayTreeSnapshot _treeSnapshot = GenesisRelayTreeSnapshot.Empty();
        private GenesisRelayTreeSelectionKey? _treeSelected;
        private Task? _treeDisconnectTask;
        private sealed record TreeDisconnectStatus(GenesisRelayTreeSelectionKey Key, PcpDownstreamDisconnectOutcome? Outcome);
        private TreeDisconnectStatus? _treeDisconnectStatus;
        private readonly Dictionary<GenesisRelayTreeSelectionKey, (nint Handle, string Text, string? Parent)> _treeItems = new();
        private (ChannelId?, ChannelSessionId?) _treeDisplayKey;
        private nint[] RelayTreeVisualControls => [_relayTreeBody, _relayTreeTitle, _relayTreeChannels,
            _relayTreeRefresh, _relayTreeMask, _relayTreeSummary, _relayTreeDetails];

        private bool TreePageShown => GenesisGuiNextRelayTreeLayout.Applies(IsModern, _tabs.SelectedTab) && !SettingsPageOpen;
        private void CreateRelayTree()
        {
            if (!IsModern) return;
            var init = new TreeInit { Size = 8, Classes = 2 };
            _ = TreeInitialize(ref init);
            var instance = GetModuleHandleW(null);
            nint Create(string type, string text, uint style, int id) => CreateWindowExW(0, type, text,
                WindowStyleChild | style, 0, 0, 1, 1, GetParent(_listView), (nint)id, instance, 0);
            _relayTreeTitle = Create("STATIC", "リレーツリー", StaticStyleNoPrefix, 0);
            _relayTreeSummary = Create("STATIC", "", StaticStyleNoPrefix, 0);
            _relayTreeDetails = Create("STATIC", "", StaticStyleNoPrefix, 0);
            _relayTreeChannels = Create("COMBOBOX", "", WindowStyleTabStop | 0x00200000 | 0x0213, RelayTreeChannelsControlId);
            _relayTreeRefresh = Create("BUTTON", "更新", WindowStyleTabStop | ButtonStyleOwnerDraw, RelayTreeRefreshControlId);
            _relayTreeMask = Create("BUTTON", "IP: 非表示", WindowStyleTabStop | ButtonStyleOwnerDraw, RelayTreeMaskControlId);
            _relayTreeBody = Create("SysTreeView32", "", WindowStyleTabStop | 1 | 2 | 4 | 0x20 | 0x200, RelayTreeBodyControlId);
            foreach (var control in RelayTreeVisualControls) ApplyDefaultFont(control);
            _ = SendMessageW(_relayTreeChannels, 0x0153, nuint.MaxValue, Scale(22));
            _ = SendMessageW(_relayTreeChannels, 0x0153, 0, Scale(22));
            _ = SetWindowTheme(_relayTreeBody, " ", " ");
            _ = SendMessageW(_relayTreeBody, 0x111D, 0, GenesisGuiNextDesign.Background);
            _ = SendMessageW(_relayTreeBody, 0x111E, 0, GenesisGuiNextDesign.Text);
            _ = SendMessageW(_relayTreeBody, 0x1128, 0, GenesisGuiNextDesign.Border);
        }

        private void CaptureTreeChannel()
        {
            var index = SendMessageW(_listView, ListViewMessageGetNextItem, nuint.MaxValue, (nint)ListViewNextItemSelected).ToInt32();
            if (GenesisGuiNextRelayActivation.TryResolve(GenesisChannelListRenderedChannels.FindRow(_listView, index)?.DisplayChannel, out var id))
                _treeChannelId = id;
        }

        public bool ProcessRelayTreeCommand(int id, nint control, int notification)
        {
            if (!TreePageShown || _treeUpdating) return false;
            if (id == RelayTreeChannelsControlId && control == _relayTreeChannels && notification == 1)
            {
                var index = SendMessageW(control, 0x0147, 0, 0).ToInt32();
                _treeChannelId = index >= 0 && index < _treeChannels.Length && GenesisGuiNextRelayActivation.TryResolve(_treeChannels[index], out var channel) ? channel : null;
            }
            else if (id == RelayTreeMaskControlId && control == _relayTreeMask && notification == 0)
                _maskTreeAddresses = !_maskTreeAddresses;
            else if (id != RelayTreeRefreshControlId || control != _relayTreeRefresh || notification != 0) return false;
            SynchronizeNativeSelection();
            return true;
        }

        private bool SynchronizeRelayTree()
        {
            var shown = TreePageShown && _relayTreeBody != 0;
            foreach (var control in RelayTreeVisualControls)
                if (control != 0) _ = PresentVisible(control, shown ? ShowWindowShowNoActivate : ShowWindowHide);
            if (shown || _treeListHidden && !SettingsPageOpen)
            {
                _ = PresentVisible(_listView, shown ? ShowWindowHide : ShowWindowShowNoActivate);
                _ = PresentVisible(_detailsPane, shown ? ShowWindowHide : ShowWindowShowNoActivate);
                _treeListHidden = shown;
            }
            return shown;
        }

        public bool DrawRelayTreeControl(nint pointer)
        {
            if (!TreePageShown || pointer == 0) return false;
            var draw = Marshal.PtrToStructure<DrawItem>(pointer);
            using var font = GenesisWindowUiFont.SelectForDrawing(draw.DeviceContext, draw.ControlWindow);
            if (draw.ControlWindow == _relayTreeChannels && draw.ControlType == 3) return DrawYellowPageItem(draw);
            if (draw.ControlWindow != _relayTreeRefresh && draw.ControlWindow != _relayTreeMask) return false;
            return DrawRecordingModeButton(draw, ReadCardControlText(draw.ControlWindow),
                draw.ControlWindow == _relayTreeMask && !_maskTreeAddresses);
        }

        private GenesisChannelItem? SynchronizeTreeChannels()
        {
            var channels = _tabs.Channels.Where(c => GenesisGuiNextRelayActivation.TryResolve(c, out _))
                .DistinctBy(c => { GenesisGuiNextRelayActivation.TryResolve(c, out var id); return id; }).ToArray();
            if (!_treeChannels.SequenceEqual(channels))
            {
                _treeUpdating = true;
                try
                {
                    _ = SendMessageW(_relayTreeChannels, 0x014B, 0, 0);
                    foreach (var channel in channels) _ = TreeSendText(_relayTreeChannels, 0x0143, 0, channel.Name);
                    _treeChannels = channels;
                }
                finally { _treeUpdating = false; }
            }
            var selected = Array.FindIndex(channels, c => GenesisGuiNextRelayActivation.TryResolve(c, out var id) && id == _treeChannelId);
            if (selected < 0 && channels.Length > 0) selected = 0;
            _treeUpdating = true;
            try { _ = SendMessageW(_relayTreeChannels, 0x014E, (nuint)selected, 0); }
            finally { _treeUpdating = false; }
            if (selected < 0) { _treeChannelId = null; return null; }
            GenesisGuiNextRelayActivation.TryResolve(channels[selected], out _treeChannelId);
            return channels[selected];
        }

        private void PlaceTreePage()
        {
            if (!GetWindowRect(_listView, out Rectangle list) || !GetWindowRect(_detailsPane, out Rectangle details)) return;
            var point = new Point { X = list.Left, Y = list.Top };
            if (!ScreenToClient(GetParent(_listView), ref point)) return;
            var x = LogicalWidth(point.X); var y = LogicalWidth(point.Y);
            var width = LogicalWidth(list.Right - list.Left); var height = LogicalWidth(details.Bottom - list.Top);
            var layout = new GenesisGuiNextRelayTreeLayout(width, height);
            void Place(nint control, int left, int top, int w, int h) => _ = PresentBounds(control, x + left, y + top, w, h, true);
            Place(_relayTreeTitle, 12, 4, 120, 28);
            Place(_relayTreeChannels, 140, 4, Math.Max(120, width - 400), 240);
            Place(_relayTreeRefresh, width - 240, 4, 68, 28);
            Place(_relayTreeMask, width - 160, 4, 148, 28);
            Place(_relayTreeSummary, 12, 38, width - 24, 22);
            Place(_relayTreeBody, layout.Body.Left, layout.Body.Top, layout.Body.Width, layout.Body.Height);
            Place(_relayTreeDetails, 12, layout.Details.Top, width - 24, layout.Details.Height);
            _ = PresentText(_relayTreeMask, _maskTreeAddresses ? "IP: 非表示" : "IP: 表示中");
        }

        private void PresentRelayTree()
        {
            var snapshot = _viewModel.ReadRelayTree(SynchronizeTreeChannels());
            _treeSnapshot = snapshot;
            var scope = (snapshot.ChannelId, snapshot.OwnerId);
            _treeUpdating = true;
            try
            {
                var nodes = snapshot.Nodes.Take(GenesisRelayTreeTopology.MaximumNodes).ToArray();
                var desired = nodes.Select(n => GenesisRelayTreeSelection.Key(snapshot, n)).ToHashSet();
                var sameScope = scope == _treeDisplayKey;
                var expanded = _treeItems.ToDictionary(p => p.Key,
                    p => (SendMessageW(_relayTreeBody, 0x1127, (nuint)p.Value.Handle, 0x20).ToInt64() & 0x20) != 0);
                var firstHandle = SendMessageW(_relayTreeBody, 0x110A, 5, 0);
                var firstKey = _treeItems.Where(p => p.Value.Handle == firstHandle).Select(p => p.Key).FirstOrDefault();
                var rebuild = !sameScope || !_treeItems.Keys.ToHashSet().SetEquals(desired) ||
                    nodes.Any(n => _treeItems.TryGetValue(GenesisRelayTreeSelection.Key(snapshot, n), out var item) && item.Parent != n.ParentId);
                if (rebuild)
                {
                    _ = SendMessageW(_relayTreeBody, 0x1101, 0, (nint)(-65536)); // TVI_ROOT
                    _treeItems.Clear();
                    if (!sameScope) _treeSelected = null;
                    _treeDisplayKey = scope;
                }
                // Existing item handles stay intact across rate/state updates;
                // native expansion, caret and scrolling are therefore retained.
                foreach (var node in nodes)
                {
                    var key = GenesisRelayTreeSelection.Key(snapshot, node);
                    var text = NodeText(node);
                    if (!_treeItems.TryGetValue(key, out var old))
                    {
                        var parent = node.ParentId is { } pid ? nodes.FirstOrDefault(n => n.Id == pid) : null;
                        var parentHandle = parent is not null && _treeItems.TryGetValue(GenesisRelayTreeSelection.Key(snapshot, parent), out var p)
                            ? p.Handle : (nint)(-65536);
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
                if (GenesisRelayTreeSelection.Resolve(snapshot, _treeSelected) is null) _treeSelected = null;
                if (rebuild)
                {
                    foreach (var pair in _treeItems)
                        if (!sameScope || !expanded.TryGetValue(pair.Key, out var wasExpanded) || wasExpanded)
                            _ = SendMessageW(_relayTreeBody, 0x1102, 2, pair.Value.Handle);
                    if (_treeSelected is { } selected && _treeItems.TryGetValue(selected, out var caret))
                        _ = SendMessageW(_relayTreeBody, 0x110B, 9, caret.Handle);
                    if (sameScope && firstKey is not null && _treeItems.TryGetValue(firstKey, out var first))
                        _ = SendMessageW(_relayTreeBody, 0x110B, 5, first.Handle);
                }
            }
            finally { _treeUpdating = false; }
            PlaceTreePage();
            var status = Volatile.Read(ref _treeDisconnectStatus);
            var prefix = status is not null && status.Key.ChannelId == snapshot.ChannelId && status.Key.OwnerId == snapshot.OwnerId
                ? GenesisRelayTreeDisconnect.Status(status.Outcome) + " / " : "";
            _ = PresentText(_relayTreeSummary, prefix + GenesisRelayTreeNodePresentation.Summary(snapshot));
            PresentTreeSelection();
        }

        private string NodeText(GenesisRelayTreeNode node) =>
            GenesisRelayTreeNodePresentation.Row(_treeSnapshot, node, _maskTreeAddresses);

        private void PresentTreeSelection() => _ = PresentText(_relayTreeDetails,
            GenesisRelayTreeNodePresentation.Details(_treeSnapshot,
                GenesisRelayTreeSelection.Resolve(_treeSnapshot, _treeSelected), _maskTreeAddresses));

        public bool HandleRelayTreeNotify(nint notification)
        {
            if (notification == 0 || _treeUpdating) return false;
            var header = Marshal.PtrToStructure<TreeHeader>(notification);
            if (header.Window != _relayTreeBody) return false;
            // TreeView reports NM_RCLICK to its actual parent. Suppress its default
            // processing after opening our HitTest-based menu.
            if (header.Code == -5 && TreeGetCursor(out var cursor))
                return RelayTreeContext(_relayTreeBody, 0x007B, (nint)((cursor.Y << 16) | (cursor.X & 0xffff)));
            if (header.Code != -451) return false; // TVN_SELCHANGEDW
            var caret = SendMessageW(_relayTreeBody, 0x110A, 9, 0);
            _treeSelected = _treeItems.Where(p => p.Value.Handle == caret).Select(p => p.Key).FirstOrDefault();
            SynchronizeNativeSelection();
            return true;
        }

        public bool RelayTreeContext(nint window, uint message, nint position)
        {
            if (window != _relayTreeBody || message != 0x007B || !TreePageShown) return false;
            var x = (short)((long)position & 0xffff); var y = (short)(((long)position >> 16) & 0xffff);
            var keyboard = x == -1 && y == -1;
            nint item;
            var point = new Point { X = x, Y = y };
            if (keyboard)
            {
                item = SendMessageW(window, 0x110A, 9, 0);
                _ = GetWindowRect(window, out Rectangle rect); x = (short)(rect.Left + 24); y = (short)(rect.Top + 24);
            }
            else
            {
                if (!ScreenToClient(window, ref point)) return true;
                var hit = new TreeHit { X = point.X, Y = point.Y };
                var memory = Marshal.AllocHGlobal(Marshal.SizeOf<TreeHit>());
                try
                {
                    Marshal.StructureToPtr(hit, memory, false);
                    item = SendMessageW(window, 0x1111, 0, memory);
                    // A row's indent/right-hand whitespace is not a node hit.
                    if ((Marshal.PtrToStructure<TreeHit>(memory).Flags & 0x46) == 0) item = 0;
                }
                finally { Marshal.FreeHGlobal(memory); }
            }
            _treeSelected = _treeItems.Where(p => p.Value.Handle == item).Select(p => p.Key).FirstOrDefault();
            _ = SendMessageW(window, 0x110B, 9, item);
            SynchronizeNativeSelection();
            if (GenesisRelayTreeSelection.Resolve(_treeSnapshot, _treeSelected) is null) return true;
            var key = _treeSelected;
            var menu = TreeCreateMenu();
            if (menu == 0) return true;
            try
            {
                _ = TreeAppendMenu(menu, 0, 1, "ノードの詳細を表示");
                _ = TreeAppendMenu(menu, 0, 2, "表示IDをコピー");
                if (_treeDisconnectTask?.IsCompleted != false && GenesisRelayTreeSelection.HasDownstreamIdentity(_treeSnapshot, key))
                    _ = TreeAppendMenu(menu, 0, 3, "下流を切断...");
                var command = TreeTrackMenu(menu, 0x0100 | 0x0002, x, y, 0, _owner, 0);
                // Resolve against a fresh observation after the modal menu.
                SynchronizeNativeSelection();
                var node = TreePageShown ? GenesisRelayTreeSelection.Resolve(_treeSnapshot, key) : null;
                if (node is not null && command == 2)
                    _ = GenesisChannelContextMenuNative.CopySourceToClipboard(_owner, node.Id,
                        () => GenesisRelayTreeSelection.Resolve(_treeSnapshot, key) is not null);
                if (node is not null && command == 1)
                {
                    _treeSelected = key;
                    SynchronizeNativeSelection();
                    _ = SetFocus(window);
                }
                if (node is not null && command == 3 && key is not null && _treeDisconnectTask?.IsCompleted != false)
                    ConfirmTreeDisconnect(key);
            }
            finally { _ = TreeDestroyMenu(menu); }
            return true;
        }

        private void ConfirmTreeDisconnect(GenesisRelayTreeSelectionKey key)
        {
            var target = GenesisRelayTreeDisconnect.Confirm(key,
                () => { SynchronizeNativeSelection(); return _treeSnapshot; },
                () => Volatile.Read(ref _detached) == 0 && TreePageShown,
                id => MessageBoxW(_owner,
                    "選択した下流Peerとの接続を切断しますか？\n\nこのPeerを経由している視聴者にも影響する可能性があります。\n\n切断対象: " + id,
                    "下流の切断", 0x134) == 6); // Yes/No, default No. Display ID never exposes a masked address.
            if (target is null) return;
            Volatile.Write(ref _treeDisconnectStatus, new(key, null));
            _treeDisconnectTask = Task.Run(async () =>
            {
                if (Volatile.Read(ref _detached) != 0) return;
                PcpDownstreamDisconnectOutcome outcome;
                try
                {
                    var receipt = _viewModel.RelayActivation?.RequestDisconnect(target)
                        ?? new(PcpDownstreamDisconnectOutcome.NotOwnedDownstream);
                    Volatile.Write(ref _treeDisconnectStatus, new(key, receipt.Outcome));
                    outcome = receipt.Completion is { } completion ? await completion.ConfigureAwait(false) : receipt.Outcome;
                }
                catch (Exception) { outcome = PcpDownstreamDisconnectOutcome.Failed; }
                Volatile.Write(ref _treeDisconnectStatus, new(key, outcome));
                // Existing one-second refresh presents scoped status. No native UI call
                // from this worker, including after the window has been destroyed.
            });
            SynchronizeNativeSelection();
        }

        private nint WriteTreeItem(nint handle, nint parent, string text)
        {
            var label = Marshal.StringToHGlobalUni(text);
            var memory = Marshal.AllocHGlobal(Marshal.SizeOf<TreeInsert>());
            try
            {
                var value = new TreeItem { Mask = 1, Handle = handle, Text = label };
                if (handle == 0)
                {
                    Marshal.StructureToPtr(new TreeInsert { Parent = parent, After = (nint)(-65534), Item = value }, memory, false);
                    return SendMessageW(_relayTreeBody, 0x1132, 0, memory);
                }
                Marshal.StructureToPtr(value, memory, false);
                return SendMessageW(_relayTreeBody, 0x113F, 0, memory);
            }
            finally { Marshal.FreeHGlobal(memory); Marshal.FreeHGlobal(label); }
        }

        public bool IsRelayTreeControl(nint control) => control != 0 && control == _relayTreeBody;
        public nint PrepareRelayTreeControl(nuint context) => _topBrush;
    }

    [StructLayout(LayoutKind.Sequential)] private struct TreeInit { public int Size, Classes; }
    [StructLayout(LayoutKind.Sequential)] private struct TreeHeader { public nint Window; public nuint Id; public int Code; }
    [StructLayout(LayoutKind.Sequential)] private struct TreeHit { public int X, Y; public uint Flags; public nint Item; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TreeItem
    { public uint Mask; public nint Handle; public uint State, StateMask; public nint Text; public int TextMax, Image, SelectedImage, Children; public nint Data; }
    [StructLayout(LayoutKind.Sequential)] private struct TreeInsert { public nint Parent, After; public TreeItem Item; }
    [DllImport("comctl32.dll", EntryPoint = "InitCommonControlsEx")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool TreeInitialize(ref TreeInit init);
    [DllImport("user32.dll", EntryPoint = "CreatePopupMenu")] private static extern nint TreeCreateMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool TreeAppendMenu(nint menu, uint flags, nuint id, string label);
    [DllImport("user32.dll", EntryPoint = "TrackPopupMenu")] private static extern int TreeTrackMenu(nint menu, uint flags, int x, int y, int reserved, nint owner, nint rect);
    [DllImport("user32.dll", EntryPoint = "DestroyMenu")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool TreeDestroyMenu(nint menu);
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)] private static extern nint TreeSendText(nint window, uint message, nuint value, string text);
    [DllImport("user32.dll", EntryPoint = "GetCursorPos")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool TreeGetCursor(out Point point);
}
