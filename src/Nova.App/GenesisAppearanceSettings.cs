using PeerCast.Nova.Foundation.Configuration;

namespace PeerCast.Nova.App;

internal sealed class GenesisAppearanceSettings
{
    internal const string NextStartNotice = "画面のデザインを選択します。変更は次回起動時から適用されます。";
    private readonly NovaSettingsStore _store;

    internal GenesisAppearanceSettings(NovaSettingsStore store)
    {
        _store = store;
        try
        {
            ConfiguredMode = Normalize(store.Load().GuiMode);
        }
        catch (Exception exception) when (IsSettingsFailure(exception))
        {
            Error = "設定を読み込めませんでした。今回はクラシック表示を使用します。設定ファイルを確認してください。";
        }
        CurrentRuntimeMode = ConfiguredMode;
    }

    internal NovaGuiMode CurrentRuntimeMode { get; }
    internal NovaGuiMode ConfiguredMode { get; private set; }
    internal string? Error { get; private set; }
    internal static NovaGuiMode Normalize(NovaGuiMode mode) =>
        mode == NovaGuiMode.Modern ? NovaGuiMode.Modern : NovaGuiMode.Classic;

    internal bool Save(NovaGuiMode mode)
    {
        if (!Enum.IsDefined(mode)) return false;
        try
        {
            _store.UpdateAsync(current => current with { GuiMode = mode }).GetAwaiter().GetResult();
            ConfiguredMode = mode;
            Error = null;
            return true;
        }
        catch (Exception exception) when (IsSettingsFailure(exception))
        {
            Error = "設定を保存できませんでした。表示モードの設定は変更されていません。";
            return false;
        }
    }

    private static bool IsSettingsFailure(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or
            NotSupportedException or ArgumentException;
}
