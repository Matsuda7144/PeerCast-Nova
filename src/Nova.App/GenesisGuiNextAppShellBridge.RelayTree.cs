using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge
{
    internal const int RelayTreeToggleControlId = 1137;
    private const int RelayTreeBodyControlId = 1138;

    private sealed partial class AppShellState
    {
        private nint _relayTreeToggle, _relayTreeBody;
        private bool _relayTreeOpen;
        private (ChannelId?, ChannelSessionId?) _treeDisplayKey;

        private void CreateRelayTree()
        {
            if (!IsModern) return;
            var instance = GetModuleHandleW(null);
            _relayTreeToggle = CreateRecordingModeButton("リレーツリー", _detailsPane, RelayTreeToggleControlId, 390, instance);
            // Native read-only multiline edit: bounded text, keyboard selection,
            // wrapping and vertical scrolling. No peer action is dispatched.
            _relayTreeBody = CreateWindowExW(0, "EDIT", string.Empty,
                WindowStyleChild | WindowStyleTabStop | 0x0800 | 0x0004 | 0x0040 | 0x00200000,
                22, 50, 1, 70, _detailsPane, (nint)RelayTreeBodyControlId, instance, 0);
            ApplyDefaultFont(_relayTreeToggle);
            ApplyDefaultFont(_relayTreeBody);
            _ = ShowWindow(_relayTreeToggle, ShowWindowHide);
        }

        private bool ProcessRelayTreeCommand(int id, nint control)
        {
            if (id != RelayTreeToggleControlId || control != _relayTreeToggle ||
                !GenesisGuiNextRelayTreeLayout.Applies(IsModern, _tabs.SelectedTab)) return false;
            _relayTreeOpen = !_relayTreeOpen;
            SynchronizeNativeSelection(); // Always resolves the current selection, never a queued old snapshot.
            return true;
        }

        private bool SynchronizeRelayTree()
        {
            var available = GenesisGuiNextRelayTreeLayout.Applies(IsModern, _tabs.SelectedTab) &&
                _relayTreeToggle != 0 && _relayTreeBody != 0;
            if (_relayTreeToggle != 0)
            {
                _ = PresentVisible(_relayTreeToggle, available ? ShowWindowShowNoActivate : ShowWindowHide);
                var toggle = GenesisGuiNextRelayTreeLayout.Toggle;
                _ = PresentBounds(_relayTreeToggle, toggle.Left, toggle.Top, toggle.Width, toggle.Height, true);
                _ = PresentInvalidation(_relayTreeToggle, 0, true);
            }
            if (_relayTreeBody != 0)
                _ = PresentVisible(_relayTreeBody, available && _relayTreeOpen ? ShowWindowShowNoActivate : ShowWindowHide);
            return available && _relayTreeOpen;
        }

        private void PresentRelayTree(GenesisGuiNextNativeTabRow? row)
        {
            var snapshot = _viewModel.ReadRelayTree(row?.DisplayChannel);
            var presentation = GenesisGuiNextRelayTreePresentation.From(snapshot);
            _ = PresentText(_detailsMetadata, presentation.Summary);
            if (GetClientRect(_detailsPane, out var client))
            {
                var body = new GenesisGuiNextRelayTreeLayout(LogicalWidth(client.Right)).Body;
                _ = PresentBounds(_relayTreeBody, body.Left, body.Top, body.Width, body.Height, true);
            }
            var key = (snapshot.ChannelId, snapshot.OwnerId);
            _detailsUpdates.Set(_relayTreeBody, "tree", (key, presentation.Tree), () =>
            {
                if (!IsWindow(_relayTreeBody)) return false;
                var first = key == _treeDisplayKey ? SendMessageW(_relayTreeBody, 0x00CE, 0, 0).ToInt32() : 0; // EM_GETFIRSTVISIBLELINE
                if (!SetWindowTextW(_relayTreeBody, presentation.Tree)) return false;
                _treeDisplayKey = key;
                if (first > 0) _ = SendMessageW(_relayTreeBody, 0x00B6, 0, first); // EM_LINESCROLL
                return true;
            });
        }

        public bool IsRelayTreeControl(nint control) => control != 0 && control == _relayTreeBody;

        public nint PrepareRelayTreeControl(nuint context)
        {
            _ = SetBkMode((nint)context, 2);
            _ = SetBkColor((nint)context, GenesisGuiNextDesign.Panel);
            _ = SetTextColor((nint)context, GenesisGuiNextDesign.Text);
            return _topBrush;
        }
    }
}
