namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge
{
    // GuiFinishAdmissionSafetyTests および GuiNextPublishingCommandTests が
    // GenesisGuiNextAppShellBridge 上の *ControlId 定数をリフレクション検証するため、
    // 分離されたコントローラーのコントロール ID もここに集約する。
    internal const int RelayTreeBodyControlId = 1138;
    internal const int RelayTreeChannelsControlId = 1139;
    internal const int RelayTreeRefreshControlId = 1140;
    internal const int RelayTreeMaskControlId = 1141;

    // 統合設定コントローラー用 ID・メッセージ定数
    internal const int GeneralCategoryId = 1180;
    internal const int LegacySettingsId = 1181;
    internal const int ClassicChoiceId = 1182;
    internal const int ModernChoiceId = 1183;
    internal const int GeneralSaveId = 1184;
    internal const int GeneralCancelId = 1185;
    internal const int GeneralBackId = 1186;
    internal const int CheckUpdatesId = 1187;
    internal const int OpenReleasePageId = 1188;
    internal const int PlaybackCategoryId = 1190;
    internal const int RecordingCategoryId = 1191;
    internal const int NotificationsCategoryId = 1192;
    internal const int NotifyNewId = 1193;
    internal const int NotifyFavoriteId = 1194;
    internal const int NotifySoundId = 1195;
    internal const int TestSoundId = 1196;
    internal const int SettingsPathId = 1197;
    internal const int SettingsBrowseId = 1198;
    internal const int SettingsResetId = 1199;
    internal const int ConnectionCategoryId = 1200;
    internal const int YellowPagesCategoryId = 1201;
    internal const int ConnectionIpv6Id = 1202;
    internal const int ConnectionInputId = 1210;
    internal const int ConnectionAutoMappingId = 1216;
    internal const int ConnectionWanId = 1217;
    internal const int YpInputId = 1220;
    internal const int YpListId = 1230;
    internal const int YpEnabledId = 1231;
    internal const int YpNewId = 1232;
    internal const int YpRemoveId = 1233;
    internal const int YpToggleId = 1234;
    internal const int InternalPlayerChoiceId = 1235;
    internal const int ExternalPlayerChoiceId = 1236;
    internal const int LowLatencyPlaybackId = 1237;
    internal const int SetupLibMpvId = 1238;
    internal const int ExternalPlayerArgsId = 1239;
    internal const uint NetworkSettingsSaveCompleted = 0x8730;
    internal const uint NatSettingsSaveCompleted = 0x8731;
    internal const uint LivePublicSettingsApplied = 0x8732;

    internal static nint CreateConnectionBodyWindow(nint parent, nint instance, nint owner) =>
        GenesisIntegratedSettingsController.CreateConnectionBodyWindow(parent, instance, owner);

    // 配信タブコントローラー用 ID・メッセージ定数
    internal const int PublishingServerCopyControlId = 1170;
    internal const int PublishingKeyRevealControlId = 1171;
    internal const int PublishingKeyCopyControlId = 1172;
    internal const int PublishingConnectionStateControlId = 1173;
    internal const int PublishingSettingsControlId = 1174;
    internal const int PublishingEditId = 1240;
    internal const int PublishingSaveId = 1241;
    internal const int PublishingCancelId = 1242;
    internal const int PublishingInputId = 1243;
    internal const int AdvancedToggleId = 1250;
    internal const int AdvancedEditId = 1251;
    internal const int AdvancedSaveId = 1252;
    internal const int AdvancedCancelId = 1253;
    internal const int AdvancedInputId = 1254;
    internal const int AdvancedFillNatId = 1265;
    internal const int AdvancedPickYpId = 1266;
    internal const uint PublishingExternalProbeCompleted = 0x8733;

    internal static bool TryMovePublishingInlineTab(nint owner, nint focus, bool reverse) =>
        StatesByOwner.TryGetValue(owner, out var state) && state.Publishing.MoveInlineTab(focus, reverse);

    internal static Task RequestSettingsSoundAsync(GenesisChannelNotificationSettings settings) =>
        GenesisIntegratedSettingsController.RequestSettingsSoundAsync(settings);
}
