using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge
{
    internal const int PublishingServerCopyControlId = 1170;
    internal const int PublishingKeyRevealControlId = 1171;
    internal const int PublishingKeyCopyControlId = 1172;
    private const int PublishingConnectionStateControlId = 1173;
    internal const int PublishingSettingsControlId = 1174;

    private sealed partial class AppShellState
    {
        private nint _publishingServerLabel, _publishingServerValue, _publishingKeyLabel, _publishingKeyValue;
        private nint _publishingServerCopy, _publishingKeyReveal, _publishingKeyCopy, _publishingConnectionState, _publishingInstructions;
        private nint _publishingSettingsButton;
        private GenesisGuiNextPublishingSettingsWindowHost? _publishingSettingsWindow;

        private bool PublishingGuideAvailable => new[] { _publishingServerLabel, _publishingServerValue,
            _publishingKeyLabel, _publishingKeyValue, _publishingServerCopy, _publishingKeyReveal,
            _publishingKeyCopy, _publishingConnectionState, _publishingInstructions, _publishingSettingsButton }.All(handle => handle != 0);

        private void CreatePublishingGuide(nint instance)
        {
            nint Text(string text) => CreateWindowExW(0, "STATIC", text, WindowStyleChild,
                0, 0, 1, 1, _detailsPane, 0, instance, 0);
            // STATIC values have no native Edit copy/context menu that could
            // bypass identity validation. Buttons copy the full, unelided value.
            nint Value() => CreateWindowExW(0, "STATIC", string.Empty,
                WindowStyleChild | 0x000C | 0x4000, // no-wrap, end ellipsis
                0, 0, 1, 1, _detailsPane, 0, instance, 0);
            _publishingServerLabel = Text("サーバー");
            _publishingServerValue = Value();
            _publishingServerCopy = CreateRecordingModeButton("コピー", _detailsPane, PublishingServerCopyControlId, 0, instance);
            _publishingKeyLabel = Text("ストリームキー");
            _publishingKeyValue = Value();
            _publishingConnectionState = CreateWindowExW(0, "STATIC", string.Empty,
                WindowStyleChild | StaticStyleOwnerDraw, 0, 0, 1, 1, _detailsPane,
                (nint)PublishingConnectionStateControlId, instance, 0);
            _publishingKeyReveal = CreateRecordingModeButton("表示", _detailsPane, PublishingKeyRevealControlId, 0, instance);
            _publishingKeyCopy = CreateRecordingModeButton("コピー", _detailsPane, PublishingKeyCopyControlId, 0, instance);
            _publishingSettingsButton = CreateRecordingModeButton("配信設定…", _detailsPane, PublishingSettingsControlId, 0, instance);
            // Keep wrapped instructions readable at narrow widths without growing
            // into lifecycle feedback. This read-only field never contains a key.
            _publishingInstructions = CreateWindowExW(0, "EDIT", NovaObsPublishingConnectionPresentation.Instructions,
                WindowStyleChild | 0x00200000 | 0x0800 | 0x0004 | 0x0040,
                0, 0, 1, 1, _detailsPane, 0, instance, 0);
            foreach (var control in PublishingGuideControls())
            {
                ApplyDefaultFont(control);
                _ = ShowWindow(control, ShowWindowHide);
            }
        }

        private nint[] PublishingGuideControls() => [_publishingServerLabel, _publishingServerValue,
            _publishingKeyLabel, _publishingKeyValue, _publishingServerCopy, _publishingKeyReveal,
            _publishingKeyCopy, _publishingConnectionState, _publishingInstructions, _publishingSettingsButton];

        private bool ProcessPublishingGuideCommand(int commandId, nint control)
        {
            if (_tabs.SelectedTab != GenesisGuiNextTab.Publishing) return false;
            if (commandId == PublishingSettingsControlId && control == _publishingSettingsButton)
            {
                if (_tabs.PublishingSettings is { } settings)
                {
                    _publishingSettingsWindow ??= new(_owner, settings, () => _tabs.PublishingCommands.CanInterrupt || _tabs.PublishingCommands.CanFinish);
                    SystemGenesisGuiNextSettingsWindowFeedback.ReportIfFailed(_owner, _publishingSettingsWindow.Show(), GenesisGuiNextSettingsWindowKind.Publishing);
                }
                return true;
            }
            if (commandId == PublishingKeyRevealControlId && control == _publishingKeyReveal)
            { _ = _tabs.TogglePublishingKeyAsync(); return true; }
            var key = commandId == PublishingKeyCopyControlId && control == _publishingKeyCopy;
            if (!key && !(commandId == PublishingServerCopyControlId && control == _publishingServerCopy)) return false;
            _ = _tabs.CopyPublishingConnectionAsync(key, (value, current) =>
                Volatile.Read(ref _detached) == 0 &&
                GenesisChannelContextMenuNative.CopySourceToClipboard(_detailsPane, value, current));
            return true;
        }

        private void SynchronizePublishingGuide(bool visible)
        {
            foreach (var control in PublishingGuideControls())
                _ = PresentVisible(control, visible ? ShowWindowShowNoActivate : ShowWindowHide);
            if (!visible)
            {
                _ = PresentText(_publishingKeyValue, NovaObsPublishingConnectionPresentation.Mask);
                _ = PresentText(_publishingServerValue, string.Empty);
                return;
            }
            var width = GetClientRect(_detailsPane, out var rectangle) ? rectangle.Right - rectangle.Left : _detailsTextWidth;
            var layout = GenesisGuiNextPublishingGuideLayout.ForWidth(width);
            void Place(nint control, GenesisGuiNextPublishingGuideRectangle bounds) =>
                _ = PresentBounds(control, bounds.Left, bounds.Top, bounds.Width, bounds.Height, true);
            Place(_detailsTitle, layout.Title);
            Place(_publishingStartButton, layout.Start);
            Place(_publishingFinishButton, layout.Finish);
            Place(_publishingInterruptButton, layout.Interrupt);
            Place(_publishingConnectionState, layout.State);
            Place(_publishingSettingsButton, layout.Settings);
            _ = PresentEnabled(_publishingSettingsButton, _tabs.PublishingSettings is not null);
            Place(_publishingServerLabel, GenesisGuiNextPublishingGuideLayout.ServerLabel);
            Place(_publishingServerValue, layout.Server);
            Place(_publishingServerCopy, layout.ServerCopy);
            Place(_publishingKeyLabel, GenesisGuiNextPublishingGuideLayout.KeyLabel);
            Place(_publishingKeyValue, layout.Key);
            Place(_publishingKeyReveal, layout.Reveal);
            Place(_publishingKeyCopy, layout.KeyCopy);
            Place(_publishingInstructions, layout.Instructions);
            _ = PresentVisible(_detailsDescription, ShowWindowHide);
            _ = PresentVisible(_detailsMetadata, ShowWindowHide);
            Place(_detailsIdentity, layout.Feedback);
            var presentation = _tabs.PublishingConnection;
            _ = PresentText(_publishingServerValue, presentation.ServerText);
            _ = PresentText(_publishingKeyValue, _tabs.PublishingKeyDisplay);
            _ = PresentText(_publishingKeyReveal, _tabs.PublishingKeyRevealed ? "隠す" : "表示");
            _ = PresentEnabled(_publishingServerCopy, presentation.CanCopyServer);
            _ = PresentEnabled(_publishingKeyReveal, presentation.CanUseKey);
            _ = PresentEnabled(_publishingKeyCopy, presentation.CanUseKey);
            foreach (var control in new[] { _publishingConnectionState, _publishingServerCopy, _publishingKeyReveal, _publishingKeyCopy })
                _ = PresentInvalidation(control, 0, true);
        }

        private bool DrawPublishingGuide(DrawItem drawItem)
        {
            if (IsOwnerDrawButton(drawItem.ControlType) && drawItem.ControlId is
                PublishingServerCopyControlId or PublishingKeyCopyControlId or PublishingKeyRevealControlId or PublishingSettingsControlId)
                return DrawRecordingModeButton(drawItem,
                    drawItem.ControlId == PublishingSettingsControlId ? "配信設定…" :
                    drawItem.ControlId == PublishingKeyRevealControlId ? _tabs.PublishingKeyRevealed ? "隠す" : "表示" : "コピー", false);
            if (!IsOwnerDrawStatic(drawItem.ControlType) || drawItem.ControlId != PublishingConnectionStateControlId) return false;
            FillRectangle(drawItem.DeviceContext, drawItem.ItemRectangle, BackgroundColor);
            _ = SetBkMode(drawItem.DeviceContext, TransparentBackgroundMode);
            _ = SetTextColor(drawItem.DeviceContext, _tabs.PublishingConnection.Tone switch
            {
                NovaObsPublishingTone.Active => 0x008BC935,
                NovaObsPublishingTone.Warning => WarningTextColor,
                NovaObsPublishingTone.Special => 0x00FF8DAD,
                _ => AccentColor,
            });
            var rectangle = drawItem.ItemRectangle;
            _ = DrawTextW(drawItem.DeviceContext, _tabs.PublishingConnection.StateText, -1, ref rectangle,
                DrawTextNoPrefix | DrawTextSingleLine | DrawTextVerticalCenter | 0x8000);
            return true;
        }
    }
}

internal readonly record struct GenesisGuiNextPublishingGuideRectangle(int Left, int Top, int Width, int Height)
{
    internal int Right => Left + Width;
    internal int Bottom => Top + Height;
}

/// <summary>Logical coordinates in the existing fixed-size, DPI-virtualized shell.</summary>
internal readonly record struct GenesisGuiNextPublishingGuideLayout(int ContentWidth, int ServerWidth, int KeyWidth, int CopyLeft, int RevealLeft)
{
    internal GenesisGuiNextPublishingGuideRectangle Title => new(12, 8, Math.Max(1, ContentWidth - 264), 24);
    internal GenesisGuiNextPublishingGuideRectangle Start => new(ContentWidth - 240, 8, 78, 26);
    internal GenesisGuiNextPublishingGuideRectangle Finish => new(ContentWidth - 156, 8, 78, 26);
    internal GenesisGuiNextPublishingGuideRectangle Interrupt => new(ContentWidth - 72, 8, 78, 26);
    internal GenesisGuiNextPublishingGuideRectangle State => new(12, 36, Math.Max(1, ContentWidth - 120), 22);
    internal GenesisGuiNextPublishingGuideRectangle Settings => new(ContentWidth - 100, 36, 112, 22);
    internal static GenesisGuiNextPublishingGuideRectangle ServerLabel => new(12, 62, 76, 24);
    internal GenesisGuiNextPublishingGuideRectangle Server => new(94, 60, ServerWidth, 24);
    internal GenesisGuiNextPublishingGuideRectangle ServerCopy => new(CopyLeft, 60, 72, 26);
    internal static GenesisGuiNextPublishingGuideRectangle KeyLabel => new(12, 94, 82, 24);
    internal GenesisGuiNextPublishingGuideRectangle Key => new(94, 92, KeyWidth, 24);
    internal GenesisGuiNextPublishingGuideRectangle Reveal => new(RevealLeft, 92, 64, 26);
    internal GenesisGuiNextPublishingGuideRectangle KeyCopy => new(CopyLeft, 92, 72, 26);
    internal GenesisGuiNextPublishingGuideRectangle Instructions => new(12, 124, ContentWidth, 60);
    internal GenesisGuiNextPublishingGuideRectangle Feedback => new(12, 188, ContentWidth, 34);

    internal static bool ShowRelayControls(GenesisGuiNextTab tab) => tab != GenesisGuiNextTab.Publishing;

    internal static GenesisGuiNextPublishingGuideLayout ForWidth(int width) =>
        new(Math.Max(1, width - 24), Math.Max(1, width - 186), Math.Max(1, width - 258),
            Math.Max(94, width - 84), Math.Max(94, width - 156));
}
