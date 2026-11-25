using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge
{
    private sealed partial class AppShellState
    {
        private nint _publishingSubtitle, _publishingObsTitle, _publishingBasicTitle, _publishingOptionsTitle;
        private nint _publishingBasic, _publishingOptions, _publishingDiagnostic;
        private (bool Expanded, int Width, int Height, uint Dpi)? _publishingPanePlacement;
        private (bool Expanded, bool Settings)? _publishingPaneVisibility;
        private nint[] PublishingPageTitles => IsModern ? [_publishingSubtitle, _publishingObsTitle, _publishingBasicTitle, _publishingOptionsTitle] : [];
        private nint[] PublishingPageValues => IsModern ? [_publishingBasic, _publishingOptions, _publishingDiagnostic] : [];
        private nint[] PublishingPageControls => [.. PublishingPageTitles, .. PublishingPageValues, .. PublishingEditorControls];

        private void CreatePublishingPage(nint instance)
        {
            if (!IsModern) return;
            nint Text(string text) => CreateWindowExW(0, "STATIC", text, WindowStyleChild,
                0, 0, 1, 1, _detailsPane, 0, instance, 0);
            nint Value() => CreateWindowExW(0, "EDIT", string.Empty,
                WindowStyleChild | WindowStyleTabStop | 0x00200000 | 0x0800 | 0x0004 | 0x0040,
                0, 0, 1, 1, _detailsPane, 0, instance, 0); // native read-only multiline, keyboard-selectable and scrollable
            _publishingSubtitle = Text("OBS入力・接続情報・配信操作を確認します。公開範囲は配信設定に従います。");
            _publishingObsTitle = Text("OBS接続情報");
            _publishingBasicTitle = Text("基本設定（保存済み）");
            _publishingOptionsTitle = Text("詳細設定・適用状況");
            _publishingBasic = Value(); _publishingOptions = Value(); _publishingDiagnostic = Value();
            CreatePublishingInlineSettings(instance);
            foreach (var h in PublishingPageControls) ApplyDefaultFont(h);
        }

        private void SynchronizePublishingPageBounds()
        {
            if (!IsModern || !GetClientRect(_owner, out var client) ||
                !GenesisMainWindowLayout.TryResolve(client.Right, client.Bottom, _layoutDpi, out _, out _, out var list, out var details)) return;
            var expanded = UsesPublishingPanel;
            var placement = (expanded, client.Right, client.Bottom, _layoutDpi);
            if (_publishingPanePlacement != placement)
            {
                _publishingPanePlacement = placement;
                var bounds = expanded ? new GenesisGuiNextAppShellPlacement.WindowRectangle(list.Left, list.Top, list.Right, details.Bottom) : details;
                var wasInProgress = LayoutInProgress;
                LayoutInProgress = true;
                try
                {
                    _ = MoveWindow(_detailsPane, Scale(bounds.Left), Scale(bounds.Top), Scale(bounds.Width), Scale(bounds.Height), true);
                    _detailsUpdates.Reset();
                }
                finally { LayoutInProgress = wasInProgress; }
            }
            if (_publishingPaneVisibility != (expanded, SettingsPageOpen))
            {
                _publishingPaneVisibility = (expanded, SettingsPageOpen);
                _ = ShowWindow(_listView, expanded || SettingsPageOpen ? ShowWindowHide : ShowWindowShowNoActivate);
                _ = ShowWindow(_detailsPane, SettingsPageOpen ? ShowWindowHide : ShowWindowShowNoActivate);
            }
        }

        private void SynchronizePublishingPage(bool visible)
        {
            foreach (var h in PublishingPageTitles.Concat(PublishingPageValues.Where(h => h != _publishingBasic && h != _publishingOptions)))
                _ = PresentVisible(h, visible && IsModern ? ShowWindowShowNoActivate : ShowWindowHide);
            if (IsModern) SynchronizePublishingInline(visible);
            if (!visible || !IsModern) return;
            var settings = _tabs.PublishingSettings;
            var summary = GenesisGuiNextPublishingPageSummary.Create(settings?.Saved, settings?.Applied, settings?.LoadFailed == true,
                _tabs.PublishingRows.Count > 0 ? _tabs.PublishingRows[0] : null, _tabs.PublishingCommands.DiagnosticText, _tabs.PublishingConnectionFeedback);
            _ = PresentText(_publishingBasic, summary.Basic);
            _ = PresentText(_publishingOptions, summary.Options);
            _ = PresentText(_publishingDiagnostic, (_publishingEditor is { } editor ? editor.Result.Message +
                PublishingInlineErrorLabel(editor.Result.Field) + "\r\n" : "") + summary.Diagnostic);
            _ = PresentVisible(_detailsIdentity, ShowWindowHide);
        }
    }
}
