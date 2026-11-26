using PeerCast.Nova.Foundation.Configuration;

namespace PeerCast.Nova.App;

// A draft for General only. Persistence remains owned by GenesisAppearanceSettings.
internal sealed class GenesisGeneralSettingsPage(GenesisAppearanceSettings settings)
{
    private bool _disposed;
    private NovaGuiMode _observedMode = settings.ConfiguredMode;
    internal bool IsOpen { get; private set; }
    internal NovaGuiMode Draft { get; private set; } = settings.ConfiguredMode;
    internal NovaGuiMode RuntimeMode => settings.CurrentRuntimeMode;
    internal NovaGuiMode ConfiguredMode => settings.ConfiguredMode;
    internal bool IsDirty => Draft != ConfiguredMode;
    internal bool IsError { get; private set; }
    internal string Message { get; private set; } = string.Empty;
    internal static bool UseIntegrated(bool modern, bool available) => modern && available;
    internal static string Label(NovaGuiMode mode) => mode == NovaGuiMode.Modern ? "Modern" : "Classic";

    internal bool Open()
    {
        if (_disposed) return false;
        if (IsOpen) return true;
        IsOpen = true;
        Draft = _observedMode = ConfiguredMode;
        Message = settings.Error ?? (settings.HasLiveSwitchHandler
            ? "保存すると再起動なしで表示モードを切り替えます。"
            : "保存しても、現在の表示モードは切り替わりません。");
        IsError = settings.Error is not null;
        return true;
    }

    internal void Refresh()
    {
        if (!IsOpen || _disposed || _observedMode == ConfiguredMode) return;
        if (Draft == _observedMode) Draft = ConfiguredMode;
        _observedMode = ConfiguredMode;
        Message = IsDirty ? "保存済みの表示モードが更新されました。未保存の変更があります。" : "保存済みの表示モードが更新されました。";
        IsError = false;
    }

    internal void Select(NovaGuiMode mode)
    {
        if (!IsOpen || _disposed || !Enum.IsDefined(mode)) return;
        Refresh();
        Draft = mode;
        IsError = false;
        Message = IsDirty
            ? (settings.HasLiveSwitchHandler
                ? "未保存の変更があります。保存すると即時に切り替わります。"
                : "未保存の変更があります。保存すると次回起動時から適用されます。")
            : "保存済みの表示モードを選択しています。";
    }

    internal bool Save()
    {
        if (!IsOpen || _disposed) return false;
        Refresh();
        if (!IsDirty) { Message = "保存する変更はありません。"; return !IsError; }
        var saved = settings.Save(Draft);
        if (saved) _observedMode = ConfiguredMode;
        IsError = !saved;
        Message = saved
            ? (settings.HasLiveSwitchHandler ? "保存しました。表示モードを切り替えます。" : "保存しました。次回起動時から適用されます。")
            : settings.Error!;
        return saved;
    }

    internal void Cancel()
    {
        if (!IsOpen || _disposed) return;
        Draft = _observedMode = ConfiguredMode;
        IsError = false;
        Message = "未保存の変更を破棄しました。";
    }

    internal void Close()
    {
        if (!IsOpen) return;
        Cancel();
        IsOpen = false;
    }

    internal void Release() { Close(); _disposed = true; }
}
