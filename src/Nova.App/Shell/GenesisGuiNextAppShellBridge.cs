using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using PeerCast.Nova.Foundation.Logging;
using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge : IAsyncDisposable
{
    internal const int FavoriteButtonWidth = 132;
    internal const int FavoriteButtonGap = 8;
    internal const int RefreshButtonWidth = 72;
    internal const int RefreshButtonGap = 8;
    internal const int MinimumStatusTextWidth = 300;
    private const int NavigationControlIdBase = 1100;
    private const int RelayTreeNavigationControlId = 1142;
    internal static int NavigationControlIdFor(int index) => index == (int)GenesisGuiNextTab.RelayTree ? RelayTreeNavigationControlId : NavigationControlIdBase + index;
    internal static int NavigationIndexFor(int id) => id == RelayTreeNavigationControlId ? (int)GenesisGuiNextTab.RelayTree :
        id >= NavigationControlIdBase && id < NavigationControlIdBase + 10 ? id - NavigationControlIdBase : -1;
    private const int FavoriteControlId = 1110;
    internal const int RefreshControlId = 1111;
    internal const int PlayControlId = 1112;
    internal const int StopControlId = 1113;
    private const int DetailsCopyControlId = 1114;
    private const int DetailsRecordControlId = 1115;
    private const int DetailsStopRecordingControlId = 1116;
    private const int DetailsRecordingStatusControlId = 1117;
    private const int ActiveRecordingsModeControlId = 1118;
    private const int RecordingRulesModeControlId = 1119;
    private const int CreateRuleControlId = 1120;
    private const int EditRuleControlId = 1121;
    private const int ToggleRuleControlId = 1122;
    private const int DeleteRuleControlId = 1123;
    private const int MoveRuleUpControlId = 1124;
    private const int MoveRuleDownControlId = 1125;
    private const int RecordingLibraryModeControlId = 1126;
    private const int RecordingLibraryRefreshControlId = 1127;
    internal const int RecordingHistoryModeControlId = 1128;
    internal const int RecordingHistoryRefreshControlId = 1129;
    internal const int RecordingHistoryDetailsControlId = 1130;
    internal const int RelayStartControlId = 1131;
    internal const int RelayStopControlId = 1132;
    internal const int RelayStatusControlId = 1133;
    internal const int PublishingStartControlId = 1134;
    internal const int PublishingInterruptControlId = 1135;
    internal const int PublishingFinishControlId = 1136;
    internal const int RelayControlsTop = 148;
    internal const int RelayStatusTop = 180;
    internal static int RelayStatusWidth(int paneWidth) => Math.Max(1, paneWidth - 24);
    internal const uint RecordingHistoryDetailsStyle = 0x0800 | 0x0004 | 0x0040 | 0x0080 | 0x00200000 | 0x00100000;
    internal static (int Top, int Height) HistoryDetailsLayout => (40, 70);
    private const int DetailsCopyButtonWidth = 112;
    private const int DetailsCopyButtonHeight = 28;
    private const int DetailsRecordButtonWidth = 68;
    private const int DetailsStopRecordingButtonWidth = 82;
    private const int DetailsRecordingButtonGap = 6;
    private const int RecordingModeButtonWidth = 78;
    private const int RecordingModeButtonGap = 6;
    private const int ShowWindowHide = 0;
    private const int ShowWindowShowNoActivate = 4;
    private const int NavigationItemTop = 8;
    private const int NavigationItemHeight = 36;
    private const int NavigationItemCount = 10;
    private const int PlaybackButtonHorizontalInset = 6;
    private const int PlaybackButtonGap = 8;
    private const int PlaybackButtonHeight = 30;
    private const int PlaybackButtonBottomInset = 10;
    private const int MinimumPlaybackButtonTop =
        NavigationItemTop + (NavigationItemCount * NavigationItemHeight) + 8;
    internal const int MinimumSidebarHeight = MinimumPlaybackButtonTop + PlaybackButtonHeight + PlaybackButtonBottomInset;
    private const int ButtonStyleOwnerDraw = 0x0000000B;
    private const uint WindowStyleChild = 0x40000000;
    private const uint WindowStyleVisible = 0x10000000;
    private const uint WindowStyleTabStop = 0x00010000;
    private const uint WindowStyleClipSiblings = 0x04000000;
    private const uint WindowStyleClipChildren = 0x02000000;
    private const uint StaticStyleNoPrefix = 0x00000080;
    private const uint StaticStyleOwnerDraw = 0x0000000D;
    private const uint WindowMessageCommand = 0x0111;
    private const uint WindowMessageNcDestroy = 0x0082;
    private const uint WindowMessagePaint = 0x000F;
    private const uint WindowMessageDrawItem = 0x002B;
    private const uint WindowMessageNotify = 0x004E;
    private const uint WindowMessageControlColorEdit = 0x0133;
    private const uint WindowMessageControlColorListBox = 0x0134;
    private const uint WindowMessageControlColorStatic = 0x0138;
    private const uint WindowMessageApplication = 0x8000;
    private const uint WindowMessageSynchronizeSelection = WindowMessageApplication + 0x716;
    private const uint WindowMessagePublishingDispatch = WindowMessageApplication + 0x717;
    internal const uint WindowMessageSynchronizeYellowPageFilter = WindowMessageApplication + 0x719;
    private const int ButtonNotificationClicked = 0;
    private const int NotifyCustomDraw = -12;
    private const uint CustomDrawStagePrePaint = 0x00000001;
    private const uint CustomDrawStageItemPrePaint = 0x00010001;
    private const uint CustomDrawStageItemPostPaint = 0x00010002;
    private const uint CustomDrawStateSelected = 0x0001;
    private const int CustomDrawNotifyPostPaint = 0x0010;
    private const int CustomDrawNotifyItemDraw = 0x0020;
    private const uint ListViewMessageFirst = 0x1000;
    private const uint ListViewMessageSetBackgroundColor = ListViewMessageFirst + 1;
    private const uint ListViewMessageGetNextItem = ListViewMessageFirst + 12;
    private const uint ListViewMessageGetItemRectangle = ListViewMessageFirst + 14;
    private const uint ListViewMessageSetTextColor = ListViewMessageFirst + 36;
    private const uint ListViewMessageSetTextBackgroundColor = ListViewMessageFirst + 38;
    private const int ListViewNextItemSelected = 0x0002;
    private const uint WindowMessageLeftButtonDown = 0x0201;
    private const uint WindowMessageLeftButtonUp = 0x0202;
    private const uint WindowMessageRightButtonDown = 0x0204;
    private const uint WindowMessageRightButtonUp = 0x0205;
    private const uint WindowMessageKeyDown = 0x0100;
    private const uint WindowMessageKeyUp = 0x0101;
    private const uint WindowMessageListViewSetItemState = ListViewMessageFirst + 43;
    // Win32 COLORREF stores channels in BGR order.
    private const int BackgroundColor = 0x00412917;
    private const int UnselectedColor = 0x00412917;
    private const int SelectedColor = 0x006A4629;
    private const int AccentColor = 0xFF8C3A;
    private const int TextColor = 0x00F7F4F2;
    private const int DisabledTextColor = 0x009D9188;
    private const int TransparentBackgroundMode = 1;
    private const uint DrawTextSingleLine = 0x0020;
    private const uint DrawTextVerticalCenter = 0x0004;
    private const uint DrawTextNoPrefix = 0x0800;
    private const uint DrawItemDisabled = 0x0004;
    private const uint DrawItemFocused = 0x0010;
    private const uint DrawItemTypeStatic = 5; // ODT_STATIC in WinUser.h (ODT_LISTBOX is 2).
    private const int AccentWidth = 5;
    private const int PlaybackAccentColor = 0x00B87832;
    private const int StopAccentColor = 0x003C43D8;
    private const int RecordingAccentColor = 0x000030E0;
    private const int WarningTextColor = 0x000055FF;
    private static readonly ConcurrentDictionary<nint, AppShellState> StatesByContainer = new();
    private static readonly ConcurrentDictionary<nint, AppShellState> StatesByListView = new();
    private static readonly WindowProcedure WindowProcedureDelegate = ProcessWindowMessage;
    private static readonly Win32SubclassProcedure ListViewSubclassProcedureDelegate =
        ProcessListViewMessage;
    private readonly nint _listView;
    private readonly GenesisGuiNextAppShellPlacement.WindowRectangle _listRectangle;
    private readonly LegacyControlVisibility[] _legacyControls;
    private readonly nint _sidebar;
    private readonly nint _statusBar;
    private readonly nint _detailsPane;
    private readonly int _detailsTextWidth;
    private readonly GenesisChannelSearchBoxBridge _searchBox;
    private readonly string _className;
    private readonly nint _instance;
    private readonly nint _backgroundBrush;
    private int _disposed;

    internal static bool IsOwnerDrawButton(uint controlType) =>
        controlType == DrawItemTypeButton;

    internal static bool IsOwnerDrawStatic(uint controlType) =>
        controlType == DrawItemTypeStatic;

    internal static bool IsRecordingStatusDraw(uint controlType, uint controlId) =>
        IsOwnerDrawStatic(controlType) && controlId == DetailsRecordingStatusControlId;

    internal static bool IsRecordingStatusVisible(bool isRulesPage) => !isRulesPage;

    internal static bool IsHistoryNavigationCommand(int id) =>
        NavigationIndexFor(id) >= 0 ||
        id is RefreshControlId or ActiveRecordingsModeControlId or RecordingRulesModeControlId or
            RecordingLibraryModeControlId or RecordingHistoryModeControlId or RecordingHistoryRefreshControlId;

    internal static bool IsPublishingNavigationCommand(int id) =>
        NavigationIndexFor(id) >= 0 ||
        id == RefreshControlId;

    internal static (int Top, int Height) RecordingStatusLayout(bool recordingPage, bool libraryPage) =>
        libraryPage ? (114, 32) : recordingPage ? (132, 14) : (122, 18);

    internal static string RecordingStatusText(bool libraryPage, string libraryText, string recordingText) =>
        libraryPage ? libraryText : recordingText;

    private GenesisGuiNextAppShellBridge(
        nint listView,
        GenesisGuiNextAppShellPlacement.WindowRectangle listRectangle,
        LegacyControlVisibility[] legacyControls,
        nint sidebar,
        nint statusBar,
        nint detailsPane,
        int detailsTextWidth,
        GenesisChannelSearchBoxBridge searchBox,
        string className,
        nint instance,
        nint backgroundBrush)
    {
        _listView = listView;
        _listRectangle = listRectangle;
        _legacyControls = legacyControls;
        _sidebar = sidebar;
        _statusBar = statusBar;
        _detailsPane = detailsPane;
        _detailsTextWidth = detailsTextWidth;
        _searchBox = searchBox;
        _className = className;
        _instance = instance;
        _backgroundBrush = backgroundBrush;
    }

    internal static bool TryResolveSelectedRowAccent(
        bool selected,
        int left,
        int top,
        int right,
        int bottom,
        out GenesisGuiNextListViewAccentRectangle accent)
    {
        if (!selected || right <= left || bottom <= top)
        {
            accent = default;
            return false;
        }

        accent = new(
            left,
            top,
            Math.Min(right, left + AccentWidth),
            bottom);
        return true;
    }

    internal static bool TryResolvePlaybackButtonLayout(
        int sidebarWidth,
        int sidebarHeight,
        out int playLeft,
        out int stopLeft,
        out int top,
        out int buttonWidth,
        out int buttonHeight)
    {
        playLeft = PlaybackButtonHorizontalInset;
        buttonWidth = (sidebarWidth - (PlaybackButtonHorizontalInset * 2) -
            PlaybackButtonGap) / 2;
        stopLeft = playLeft + buttonWidth + PlaybackButtonGap;
        top = sidebarHeight - PlaybackButtonBottomInset - PlaybackButtonHeight;
        buttonHeight = PlaybackButtonHeight;
        if (buttonWidth < 48 || top < MinimumPlaybackButtonTop)
        {
            playLeft = 0;
            stopLeft = 0;
            top = 0;
            buttonWidth = 0;
            buttonHeight = 0;
            return false;
        }

        return true;
    }

    internal static void ApplyListViewColors(nint listView, bool modernAppearance = false)
    {
        _ = SetWindowTheme(listView, " ", " ");
        _ = SendMessageW(
            listView,
            ListViewMessageSetBackgroundColor,
            0,
            (nint)(modernAppearance ? GenesisGuiNextDesign.List : BackgroundColor));
        _ = SendMessageW(
            listView,
            ListViewMessageSetTextBackgroundColor,
            0,
            (nint)(modernAppearance ? GenesisGuiNextDesign.List : UnselectedColor));
        _ = SendMessageW(
            listView,
            ListViewMessageSetTextColor,
            0,
            (nint)TextColor);
        _ = InvalidateRect(listView, 0, true);
    }

    public static GenesisGuiNextAppShellBridge? TryCreate(
        nint owner,
        nint parent,
        nint listView,
        GenesisGuiNextAppShellPlacement.WindowRectangle listRectangle,
        GenesisGuiNextAppShellPlacement.WindowRectangle sidebarRectangle,
        GenesisGuiNextAppShellPlacement.WindowRectangle statusRectangle,
        GenesisGuiNextAppShellPlacement.WindowRectangle detailsRectangle,
        GenesisGuiNextTabsViewModel tabs,
        GenesisMainViewModel viewModel,
        GenesisMainWindowInteraction interaction,
        LegacyControlVisibility[] legacyControls)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(legacyControls);
        var instance = GetModuleHandleW(null);
        if (parent == 0 ||
            listView == 0 ||
            instance == 0 ||
            !TryResolvePlaybackButtonLayout(
                sidebarRectangle.Width,
                sidebarRectangle.Height,
                out var playButtonLeft,
                out var stopButtonLeft,
                out var playbackButtonTop,
                out var playbackButtonWidth,
                out var playbackButtonHeight) ||
            !TryResolveControlLayout(
                statusRectangle.Width,
                out var statusTextWidth,
                out var searchLeft,
                out var searchWidth,
                out var refreshButtonLeft,
                out var favoriteButtonLeft, viewModel.UsesModernAppearance))
        {
            return null;
        }

        var className = $"PeerCastNovaGenesisShell-{Guid.NewGuid():N}";
        var backgroundBrush = CreateSolidBrush((uint)BackgroundColor);
        if (backgroundBrush == 0)
        {
            return null;
        }

        var windowClass = new WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedureDelegate),
            Instance = instance,
            BackgroundBrush = backgroundBrush,
            ClassName = className,
        };
        if (RegisterClassExW(ref windowClass) == 0)
        {
            _ = DeleteObject(backgroundBrush);
            return null;
        }

        var sidebar = CreateWindowExW(
            0,
            className,
            string.Empty,
            WindowStyleChild | WindowStyleVisible | WindowStyleClipChildren | WindowStyleClipSiblings,
            sidebarRectangle.Left,
            sidebarRectangle.Top,
            sidebarRectangle.Width,
            sidebarRectangle.Height,
            parent,
            0,
            instance,
            0);
        var statusBar = CreateWindowExW(
            0,
            className,
            string.Empty,
            WindowStyleChild | WindowStyleVisible | WindowStyleClipChildren | WindowStyleClipSiblings,
            statusRectangle.Left,
            statusRectangle.Top,
            statusRectangle.Width,
            statusRectangle.Height,
            parent,
            0,
            instance,
            0);
        var detailsPane = CreateWindowExW(
            0,
            className,
            string.Empty,
            WindowStyleChild | WindowStyleVisible | WindowStyleClipChildren | WindowStyleClipSiblings,
            detailsRectangle.Left,
            detailsRectangle.Top,
            detailsRectangle.Width,
            detailsRectangle.Height,
            parent,
            0,
            instance,
            0);
        if (sidebar == 0 || statusBar == 0 || detailsPane == 0)
        {
            DestroyWindowIfPresent(detailsPane);
            DestroyWindowIfPresent(statusBar);
            DestroyWindowIfPresent(sidebar);
            _ = UnregisterClassW(className, instance);
            _ = DeleteObject(backgroundBrush);
            return null;
        }

        var navigationItems = GenesisGuiNextNavigationPresentation.ItemsFor(viewModel.UsesModernAppearance);
        var navigationRowHeight = GenesisGuiNextNavigationPresentation.RowHeight(viewModel.UsesModernAppearance);
        var navigationButtons = new nint[navigationItems.Count];
        for (var index = 0; index < navigationButtons.Length; index++)
        {
            navigationButtons[index] = CreateWindowExW(
                0,
                "BUTTON",
                navigationItems[index],
                WindowStyleChild |
                    WindowStyleVisible |
                    WindowStyleTabStop |
                    ButtonStyleOwnerDraw,
                0,
                NavigationItemTop + GenesisGuiNextNavigationPresentation.Row(index, viewModel.UsesModernAppearance) * navigationRowHeight,
                sidebarRectangle.Width,
                navigationRowHeight - 2,
                sidebar,
                (nint)NavigationControlIdFor(index),
                instance,
                0);
            if (navigationButtons[index] == 0)
            {
                DestroyWindowIfPresent(detailsPane);
                DestroyWindowIfPresent(statusBar);
                DestroyWindowIfPresent(sidebar);
                _ = UnregisterClassW(className, instance);
                _ = DeleteObject(backgroundBrush);
                return null;
            }

            ApplyDefaultFont(navigationButtons[index]);
            _ = EnableWindow(
                navigationButtons[index],
                GenesisGuiNextNavigationPresentation.IsImplemented(index));
        }

        var playButton = CreatePlaybackButton(
            "再生",
            sidebar,
            PlayControlId,
            playButtonLeft,
            playbackButtonTop,
            playbackButtonWidth,
            playbackButtonHeight,
            instance);
        var stopButton = CreatePlaybackButton(
            "停止",
            sidebar,
            StopControlId,
            stopButtonLeft,
            playbackButtonTop,
            playbackButtonWidth,
            playbackButtonHeight,
            instance);
        if (playButton == 0 || stopButton == 0)
        {
            DestroyWindowIfPresent(detailsPane);
            DestroyWindowIfPresent(statusBar);
            DestroyWindowIfPresent(sidebar);
            _ = UnregisterClassW(className, instance);
            _ = DeleteObject(backgroundBrush);
            return null;
        }

        var statusLabel = CreateWindowExW(
            0,
            "STATIC",
            GenesisGuiNextTopStatusPresentation.FormatUnavailable(),
            WindowStyleChild | WindowStyleVisible,
            8,
            0,
            statusTextWidth,
            statusRectangle.Height,
            statusBar,
            0,
            instance,
            0);
        var searchBox = statusLabel == 0
            ? null
            : GenesisChannelSearchBoxBridge.TryCreate(
                statusBar,
                searchLeft,
                searchWidth,
                statusRectangle.Height,
                tabs, viewModel.UsesModernAppearance);
        var favoriteButton = CreateWindowExW(
            0,
            "BUTTON",
            "Add Favorite",
            WindowStyleChild | WindowStyleVisible | WindowStyleTabStop |
                (viewModel.UsesModernAppearance ? ButtonStyleOwnerDraw : 0u),
            favoriteButtonLeft,
            2,
            FavoriteButtonWidth,
            Math.Max(1, statusRectangle.Height - 4),
            statusBar,
            (nint)FavoriteControlId,
            instance,
            0);
        var refreshButton = CreateWindowExW(
            0,
            "BUTTON",
            "更新",
            WindowStyleChild |
                WindowStyleVisible |
                WindowStyleTabStop |
                ButtonStyleOwnerDraw,
            refreshButtonLeft,
            2,
            RefreshButtonWidth,
            Math.Max(1, statusRectangle.Height - 4),
            statusBar,
            (nint)RefreshControlId,
            instance,
            0);
        var (detailsRecordButtonLeft, detailsStopRecordingButtonLeft, detailsCopyButtonLeft, detailsTextWidth) =
            ResolveDetailsActions(detailsRectangle.Width);
        var detailsTitle = CreateDetailsTextControl(
            detailsPane,
            left: 12,
            top: 8,
            width: detailsTextWidth,
            height: 24,
            instance);
        var activeRecordingsModeButton = CreateRecordingModeButton(
            GenesisGuiNextRecordingPageModePresentation.ActiveRecordingsLabel,
            detailsPane,
            ActiveRecordingsModeControlId,
            left: RecordingToolbarLeft(0),
            instance);
        var recordingRulesModeButton = CreateRecordingModeButton(
            GenesisGuiNextRecordingPageModePresentation.RulesLabel,
            detailsPane,
            RecordingRulesModeControlId,
            left: RecordingToolbarLeft(2),
            instance);
        var recordingLibraryModeButton = CreateRecordingModeButton(
            GenesisGuiNextRecordingPageModePresentation.LibraryLabel,
            detailsPane, RecordingLibraryModeControlId,
            left: RecordingToolbarLeft(1), instance);
        var recordingLibraryRefreshButton = CreateRecordingModeButton(
            "更新", detailsPane, RecordingLibraryRefreshControlId,
            left: RecordingToolbarLeft(4), instance);
        var detailsDescription = CreateDetailsTextControl(
            detailsPane,
            left: 12,
            top: 36,
            width: Math.Max(1, detailsRectangle.Width - 24),
            height: 32,
            instance);
        var detailsMetadata = CreateDetailsTextControl(
            detailsPane,
            left: 12,
            top: 74,
            width: Math.Max(1, detailsRectangle.Width - 24),
            height: 22,
            instance);
        var detailsIdentity = CreateDetailsTextControl(
            detailsPane,
            left: 12,
            top: 100,
            width: Math.Max(1, detailsRectangle.Width - 24),
            height: 18,
            instance);
        var detailsRecordingStatus = CreateWindowExW(
            0,
            "STATIC",
            string.Empty,
            WindowStyleChild | WindowStyleVisible | StaticStyleOwnerDraw,
            12,
            122,
            Math.Max(1, detailsRectangle.Width - 24),
            18,
            detailsPane,
            (nint)DetailsRecordingStatusControlId,
            instance,
            0);
        var detailsRecordButton = CreateWindowExW(
            0,
            "BUTTON",
            "録画",
            WindowStyleChild |
                WindowStyleVisible |
                WindowStyleTabStop |
                ButtonStyleOwnerDraw,
            detailsRecordButtonLeft,
            8,
            DetailsRecordButtonWidth,
            DetailsCopyButtonHeight,
            detailsPane,
            (nint)DetailsRecordControlId,
            instance,
            0);
        var detailsStopRecordingButton = CreateWindowExW(
            0,
            "BUTTON",
            "録画停止",
            WindowStyleChild |
                WindowStyleVisible |
                WindowStyleTabStop |
                ButtonStyleOwnerDraw,
            detailsStopRecordingButtonLeft,
            8,
            DetailsStopRecordingButtonWidth,
            DetailsCopyButtonHeight,
            detailsPane,
            (nint)DetailsStopRecordingControlId,
            instance,
            0);
        var detailsCopyButton = CreateWindowExW(
            0,
            "BUTTON",
            "情報をコピー",
            WindowStyleChild |
                WindowStyleVisible |
                WindowStyleTabStop |
                ButtonStyleOwnerDraw,
            detailsCopyButtonLeft,
            8,
            DetailsCopyButtonWidth,
            DetailsCopyButtonHeight,
            detailsPane,
            (nint)DetailsCopyControlId,
            instance,
            0);
        if (statusLabel == 0 || searchBox is null || favoriteButton == 0 || refreshButton == 0 ||
            detailsTitle == 0 || activeRecordingsModeButton == 0 ||
            recordingRulesModeButton == 0 || recordingLibraryModeButton == 0 ||
            recordingLibraryRefreshButton == 0 || detailsDescription == 0 || detailsMetadata == 0 ||
            detailsIdentity == 0 || detailsRecordingStatus == 0 ||
            detailsRecordButton == 0 || detailsStopRecordingButton == 0 ||
            detailsCopyButton == 0)
        {
            if (searchBox is not null)
            {
                searchBox.Dispose();
            }

            DestroyWindowIfPresent(detailsPane);
            DestroyWindowIfPresent(statusBar);
            DestroyWindowIfPresent(sidebar);
            _ = UnregisterClassW(className, instance);
            _ = DeleteObject(backgroundBrush);
            return null;
        }

        ApplyDefaultFont(statusLabel);
        ApplyDefaultFont(favoriteButton);
        ApplyDefaultFont(refreshButton);
        ApplyDefaultFont(detailsTitle);
        ApplyDefaultFont(activeRecordingsModeButton);
        ApplyDefaultFont(recordingRulesModeButton);
        ApplyDefaultFont(recordingLibraryModeButton);
        ApplyDefaultFont(recordingLibraryRefreshButton);
        ApplyDefaultFont(detailsDescription);
        ApplyDefaultFont(detailsMetadata);
        ApplyDefaultFont(detailsIdentity);
        ApplyDefaultFont(detailsRecordingStatus);
        ApplyDefaultFont(detailsRecordButton);
        ApplyDefaultFont(detailsStopRecordingButton);
        ApplyDefaultFont(detailsCopyButton);
        _ = EnableWindow(
            refreshButton,
            GenesisChannelRefreshButtonBridge.CanRefreshForOwner(owner));
        var state = new AppShellState(
            owner,
            sidebar,
            statusBar,
            statusLabel,
            detailsPane,
            detailsTextWidth,
            searchBox,
            favoriteButton,
            refreshButton,
            navigationButtons,
            playButton,
            stopButton,
            detailsTitle,
            activeRecordingsModeButton,
            recordingRulesModeButton,
            recordingLibraryModeButton,
            recordingLibraryRefreshButton,
            detailsDescription,
            detailsMetadata,
            detailsIdentity,
            detailsRecordingStatus,
            detailsRecordButton,
            detailsStopRecordingButton,
            detailsCopyButton,
            listView,
            tabs,
            viewModel,
            interaction,
            backgroundBrush);
        state.CreatePaneSplitter(instance);
        state.CreateIntegratedSettings(className, instance);
        if (!state.RuleButtonsAvailable || !StatesByContainer.TryAdd(sidebar, state) ||
            !StatesByContainer.TryAdd(statusBar, state) ||
            !StatesByContainer.TryAdd(detailsPane, state) ||
            !StatesByListView.TryAdd(listView, state) ||
            !SetWindowSubclass(
                listView,
                ListViewSubclassProcedureDelegate,
                ListViewSubclassIdentifier,
                0))
        {
            state.DestroyPaneSplitter();
            state.DestroyIntegratedSettings();
            _ = StatesByContainer.TryRemove(sidebar, out _);
            _ = StatesByContainer.TryRemove(statusBar, out _);
            _ = StatesByContainer.TryRemove(detailsPane, out _);
            _ = StatesByListView.TryRemove(listView, out _);
            searchBox.Dispose();
            DestroyWindowIfPresent(detailsPane);
            DestroyWindowIfPresent(statusBar);
            DestroyWindowIfPresent(sidebar);
            _ = UnregisterClassW(className, instance);
            _ = DeleteObject(backgroundBrush);
            return null;
        }

        state.Attach();
        StatesByOwner[owner] = state;
        if (viewModel.UsesModernAppearance) state.AttachVisuals();
        state.ApplyClientLayout();
        state.SynchronizeNativeSelection();
        return new(
            listView,
            listRectangle,
            legacyControls,
            sidebar,
            statusBar,
            detailsPane,
            detailsTextWidth,
            searchBox,
            className,
            instance,
            backgroundBrush);
    }

    internal readonly record struct LegacyControlVisibility(
        nint Window,
        bool WasVisible);

    internal void HideLegacyControls()
    {
        foreach (var control in _legacyControls)
        {
            if (control.Window != 0 && IsWindow(control.Window))
            {
                _ = ShowWindow(control.Window, ShowWindowHide);
            }
        }
    }

    private void RestoreLegacyControls()
    {
        foreach (var control in _legacyControls)
        {
            if (control.Window != 0 && IsWindow(control.Window))
            {
                _ = ShowWindow(
                    control.Window,
                    control.WasVisible ? ShowWindowShowNoActivate : ShowWindowHide);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            if (StatesByContainer.TryRemove(_sidebar, out var state))
            {
                state.Detach();
                state.DestroyIntegratedSettings();
                await state.DrainAutomaticCommandsAsync().ConfigureAwait(false);
            }

            _ = StatesByContainer.TryRemove(_statusBar, out _);
            _ = StatesByContainer.TryRemove(_detailsPane, out _);
            _ = StatesByListView.TryRemove(_listView, out _);
            if (_listView != 0 && IsWindow(_listView))
            {
                _ = RemoveWindowSubclass(
                    _listView,
                    ListViewSubclassProcedureDelegate,
                    ListViewSubclassIdentifier);
            }

            await _searchBox.DisposeAsync().ConfigureAwait(false);
            DestroyWindowIfPresent(_detailsPane);
            DestroyWindowIfPresent(_statusBar);
            DestroyWindowIfPresent(_sidebar);
            _ = UnregisterClassW(_className, _instance);
            _ = DeleteObject(_backgroundBrush);
            RestoreLegacyControls();
            if (_listView != 0 && IsWindow(_listView))
            {
                _ = MoveWindow(
                    _listView,
                    _listRectangle.Left,
                    _listRectangle.Top,
                    _listRectangle.Width,
                    _listRectangle.Height,
                    true);
            }
        }

    }

    internal static bool TryResolveControlLayout(
        int totalWidth,
        out int statusTextWidth,
        out int searchLeft,
        out int searchWidth,
        out int refreshButtonLeft,
        out int favoriteButtonLeft,
        bool modernAppearance = false)
    {
        if (modernAppearance)
            return GenesisGuiNextTopBarLayout.TryResolve(totalWidth, out statusTextWidth, out searchLeft,
                out searchWidth, out refreshButtonLeft, out favoriteButtonLeft);
        const int statusTextLeft = 8;
        const int statusSearchGap = 8;
        favoriteButtonLeft = totalWidth - FavoriteButtonWidth;
        statusTextWidth = MinimumStatusTextWidth;
        searchLeft = statusTextLeft + statusTextWidth + statusSearchGap;
        refreshButtonLeft = favoriteButtonLeft - FavoriteButtonGap - RefreshButtonWidth;
        searchWidth = refreshButtonLeft - RefreshButtonGap - searchLeft;
        if (totalWidth <= 0 ||
            !GenesisChannelSearchBoxBridge.TryResolveControlLayout(
                searchWidth,
                out _,
                out _))
        {
            statusTextWidth = 0;
            searchLeft = 0;
            searchWidth = 0;
            refreshButtonLeft = 0;
            favoriteButtonLeft = 0;
            return false;
        }

        return true;
    }

    internal static void SynchronizeRenderedList(nint listView,
        IReadOnlyList<GenesisNativeListColumnPresentation>? columns, IReadOnlyList<string[]> values)
    {
        if (!StatesByListView.TryGetValue(listView, out var state)) return;
        state.UpdateListPaintData(columns, values);
        if (state.IsActiveRecordingsPage())
            state.SynchronizeNativeSelection();
    }

    internal static string? ResolveRecordingCompletionMessage(
        GenesisGuiNextTab selectedTab,
        GenesisGuiNextRecordingPageMode pageMode,
        GenesisGuiNextNativeTabRow? row,
        string commandMessage) =>
        selectedTab == GenesisGuiNextTab.Recording &&
        pageMode == GenesisGuiNextRecordingPageMode.ActiveRecordings &&
        row is null
            ? commandMessage
            : null;

    internal static bool TryFocusSearchForOwner(nint owner) =>
        StatesByOwner.TryGetValue(owner, out var state) && state.FocusSearch();

    private static string AccessibleListTitle(GenesisGuiNextTab tab) =>
        tab switch
        {
            GenesisGuiNextTab.New => "新着チャンネル一覧",
            GenesisGuiNextTab.Favorites => "お気に入りチャンネル一覧",
            GenesisGuiNextTab.Playing => "再生中チャンネル一覧",
            GenesisGuiNextTab.Recording => "録画一覧",
            GenesisGuiNextTab.Publishing => "配信設定",
            GenesisGuiNextTab.Logs => "ログ一覧",
            GenesisGuiNextTab.RelayTree => "リレーツリー",
            _ => "チャンネル一覧",
        };

    internal static bool RequiresFavoriteButtonSynchronization(uint message) =>
        message == WindowMessageLeftButtonDown ||
        message == WindowMessageLeftButtonUp ||
        message == WindowMessageRightButtonDown ||
        message == WindowMessageRightButtonUp ||
        message == WindowMessageKeyDown ||
        message == WindowMessageKeyUp ||
        message == WindowMessageListViewSetItemState;

    private static void ApplyDefaultFont(nint control) => _ = GenesisWindowUiFont.Apply(control);

    private static nint CreatePlaybackButton(
        string label,
        nint sidebar,
        int controlId,
        int left,
        int top,
        int width,
        int height,
        nint instance)
    {
        var button = CreateWindowExW(
            0,
            "BUTTON",
            label,
            WindowStyleChild |
                WindowStyleVisible |
                WindowStyleTabStop |
                ButtonStyleOwnerDraw,
            left,
            top,
            width,
            height,
            sidebar,
            (nint)controlId,
            instance,
            0);
        ApplyDefaultFont(button);
        return button;
    }

    private static nint CreateDetailsTextControl(
        nint parent,
        int left,
        int top,
        int width,
        int height,
        nint instance) =>
        CreateWindowExW(
            0,
            "STATIC",
            string.Empty,
            WindowStyleChild | WindowStyleVisible | StaticStyleNoPrefix,
            left,
            top,
            width,
            height,
            parent,
            0,
            instance,
            0);

    private static nint CreateRecordingModeButton(
        string text,
        nint parent,
        int controlId,
        int left,
        nint instance) =>
        CreateWindowExW(
            0,
            "BUTTON",
            text,
            WindowStyleChild |
                WindowStyleVisible |
                WindowStyleTabStop |
                ButtonStyleOwnerDraw,
            left,
            8,
            RecordingModeButtonWidth,
            26,
            parent,
            (nint)controlId,
            instance,
            0);

    private static nint ProcessWindowMessage(
        nint window,
        uint message,
        nuint wParam,
        nint lParam)
    {
        if (message == WindowMessageNcDestroy)
        {
            if (StatesByContainer.TryRemove(window, out var removedState))
            {
                removedState.Detach();
            }

            return DefWindowProcW(window, message, wParam, lParam);
        }

        if (!StatesByContainer.TryGetValue(window, out var state))
        {
            return DefWindowProcW(window, message, wParam, lParam);
        }

        if (message == NetworkSettingsSaveCompleted)
        {
            state.CompleteNetworkSettingsSave(wParam);
            return 0;
        }
        if (message == NatSettingsSaveCompleted)
        {
            state.CompleteNatSettingsSave(wParam, (long)lParam);
            return 0;
        }
        if (message == LivePublicSettingsApplied)
        {
            state.CompleteLivePublicSettingsApply(wParam);
            return 0;
        }
        if (message == PublishingExternalProbeCompleted)
        {
            state.CompletePublishingExternalProbe(wParam);
            return 0;
        }
        if (message == WindowMessageCommand)
        {
            state.ProcessCommand(wParam, lParam);
        }
        else if (message == 0x0014 && state.PaintPaneBackground(window, (nint)wParam))
        {
            return 1;
        }
        else if (message == WindowMessageDrawItem &&
            state.DrawNavigationItem(lParam))
        {
            return 1;
        }
        else if (message == WindowMessageNotify &&
            state.HandleListViewCustomDraw(lParam, out var customDrawResult))
        {
            return customDrawResult;
        }
        else if (message == 0x0135 && state.IsSettingsPane(window)) // WM_CTLCOLORBTN for native notification checkboxes.
        {
            return state.PrepareStaticControl(wParam, window);
        }
        else if (message == WindowMessageControlColorStatic)
        {
            if (state.IsRelayTreeControl(lParam)) return state.PrepareRelayTreeControl(wParam);
            // Win32 read-only EDIT controls send WM_CTLCOLORSTATIC too. Their
            // text and scroll repaints need the same opaque fill as other edits.
            if (state.IsReadOnlyDetailsEdit(window, lParam)) return state.PrepareDarkControl(wParam, window);
            return state.PrepareStaticControl(wParam, window);
        }
        else if (message == WindowMessageControlColorEdit ||
            message == WindowMessageControlColorListBox)
        {
            return state.PrepareDarkControl(wParam, window);
        }
        else if (message == WindowMessageSynchronizeYellowPageFilter)
        {
            state.SynchronizeYellowPageFilter();
            return 0;
        }
        else if (message == WindowMessagePublishingDispatch)
        {
            state.DrainPublishingUi();
            return 0;
        }
        else if (message == WindowMessageSynchronizeSelection)
        {
            state.SynchronizeNativeSelection();
            return 0;
        }
        else if ((message is 0x0005 or 0x02E3) && state.IsDetailsPane(window) && !state.LayoutInProgress) // WM_SIZE / WM_DPICHANGED_AFTERPARENT
        {
            state.ResetDetailsPresentation();
            state.SynchronizeNativeSelection();
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    private static nint ProcessListViewMessage(
        nint window,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassIdentifier,
        nuint referenceData)
    {
        _ = subclassIdentifier;
        _ = referenceData;
        // Release the row image list before the native list's WM_DESTROY owns cleanup.
        if (message == 0x0002 && StatesByListView.TryGetValue(window, out var destroyingState))
            destroyingState.DetachVisuals();
        if (message == WindowMessageNotify && StatesByListView.TryGetValue(window, out var headerState) &&
            headerState.DrawListHeader(lParam, out var headerResult)) return headerResult;
        if (message == WindowMessageNcDestroy)
        {
            if (StatesByListView.TryRemove(window, out var removedState))
            {
                removedState.Detach();
            }

            _ = RemoveWindowSubclass(
                window,
                ListViewSubclassProcedureDelegate,
                ListViewSubclassIdentifier);
            return DefSubclassProc(window, message, wParam, lParam);
        }

        var result = DefSubclassProc(window, message, wParam, lParam);
        if (StatesByListView.TryGetValue(window, out var state))
        {
            if (state.IsModern && message is (0x0200 or 0x02A3)) state.TrackListHover(message, lParam);
            if (message == WindowMessagePaint)
            {
                state.PaintSelectedRowAccent();
            }

            if (RequiresFavoriteButtonSynchronization(message))
            {
                state.SynchronizeFavoriteButtonPresentation();
            }
        }

        return result;
    }

    private static void DestroyWindowIfPresent(nint window)
    {
        if (window != 0 && IsWindow(window))
        {
            _ = DestroyWindow(window);
        }
    }

    private sealed partial class AppShellState
    {
        private readonly nint _owner;
        private readonly nint _sidebar;
        private readonly nint _statusBar;
        private readonly nint _statusLabel;
        private readonly nint _detailsPane;
        private int _detailsTextWidth;
        private readonly GenesisChannelSearchBoxBridge _searchBox;
        private readonly nint _favoriteButton;
        private readonly GenesisNativePresentationUpdates _favoriteUpdates = new();
        private readonly GenesisNativePresentationUpdates _detailsUpdates = new();
        private readonly nint _refreshButton;
        private readonly nint[] _navigationButtons;
        private readonly nint _playButton;
        private readonly nint _stopButton;
        private readonly nint _detailsTitle;
        private readonly nint _relayStart;
        private readonly nint _relayStop;
        private readonly nint _relayStatus;
        private readonly nint _activeRecordingsModeButton;
        private readonly nint _recordingRulesModeButton;
        private readonly nint _recordingLibraryModeButton;
        private readonly nint _recordingLibraryRefreshButton;
        private readonly nint _recordingHistoryModeButton;
        private readonly nint _recordingHistoryRefreshButton;
        private readonly nint _recordingHistoryDetails;
        private GenesisRecordingHistoryPresentation _historyStatusPresentation = new(string.Empty, false);
        private string _historyDetailsText = string.Empty;
        private readonly nint _createRuleButton;
        private readonly nint _editRuleButton;
        private readonly nint _toggleRuleButton;
        private readonly nint _deleteRuleButton;
        private readonly nint _moveRuleUpButton;
        private readonly nint _moveRuleDownButton;
        private string _toggleRuleLabel = "有効化";
        private readonly nint _detailsDescription;
        private readonly nint _detailsMetadata;
        private readonly nint _detailsIdentity;
        private readonly nint _detailsRecordingStatus;
        private readonly nint _detailsRecordButton;
        private readonly nint _detailsStopRecordingButton;
        private readonly nint _detailsCopyButton;
        private readonly nint _publishingStartButton;
        private readonly nint _publishingInterruptButton;
        private readonly nint _publishingFinishButton;
        private readonly ConcurrentQueue<Action> _publishingUi = new();
        private readonly nint _listView;
        private readonly GenesisGuiNextTabsViewModel _tabs;
        private readonly GenesisMainViewModel _viewModel;
        private readonly GenesisMainWindowInteraction _interaction;
        private readonly nint _backgroundBrush;
        private readonly GenesisGuiNextUtilityNavigationSelection _utilityNavigationSelection = new();
        private int _playCommandInFlight;
        private int _stopCommandInFlight;
        private int _detached;

        public AppShellState(
            nint owner,
            nint sidebar,
            nint statusBar,
            nint statusLabel,
            nint detailsPane,
            int detailsTextWidth,
            GenesisChannelSearchBoxBridge searchBox,
            nint favoriteButton,
            nint refreshButton,
            nint[] navigationButtons,
            nint playButton,
            nint stopButton,
            nint detailsTitle,
            nint activeRecordingsModeButton,
            nint recordingRulesModeButton,
            nint recordingLibraryModeButton,
            nint recordingLibraryRefreshButton,
            nint detailsDescription,
            nint detailsMetadata,
            nint detailsIdentity,
            nint detailsRecordingStatus,
            nint detailsRecordButton,
            nint detailsStopRecordingButton,
            nint detailsCopyButton,
            nint listView,
            GenesisGuiNextTabsViewModel tabs,
            GenesisMainViewModel viewModel,
            GenesisMainWindowInteraction interaction,
            nint backgroundBrush)
        {
            _owner = owner;
            _modernAppearance = viewModel.UsesModernAppearance;
            _sidebar = sidebar;
            _statusBar = statusBar;
            _statusLabel = statusLabel;
            _detailsPane = detailsPane;
            _detailsTextWidth = detailsTextWidth;
            _favoriteButton = favoriteButton;
            _refreshButton = refreshButton;
            _searchBox = searchBox;
            _navigationButtons = navigationButtons;
            _playButton = playButton;
            _stopButton = stopButton;
            _detailsTitle = detailsTitle;
            _activeRecordingsModeButton = activeRecordingsModeButton;
            _recordingRulesModeButton = recordingRulesModeButton;
            _recordingLibraryModeButton = recordingLibraryModeButton;
            _recordingLibraryRefreshButton = recordingLibraryRefreshButton;
            _recordingHistoryModeButton = CreateRecordingModeButton(
                GenesisGuiNextRecordingPageModePresentation.HistoryLabel, detailsPane,
                RecordingHistoryModeControlId, RecordingToolbarLeft(3), GetModuleHandleW(null));
            _recordingHistoryRefreshButton = CreateRecordingModeButton(
                "更新", detailsPane, RecordingHistoryRefreshControlId, RecordingToolbarLeft(4), GetModuleHandleW(null));
            _recordingHistoryDetails = CreateWindowExW(0, "EDIT", string.Empty,
                WindowStyleChild | WindowStyleTabStop | RecordingHistoryDetailsStyle,
                12, HistoryDetailsLayout.Top, Math.Max(1, detailsTextWidth), HistoryDetailsLayout.Height,
                detailsPane, (nint)RecordingHistoryDetailsControlId, GetModuleHandleW(null), 0);
            _ = PeerCast.Nova.Windows.Win32AccessibilityAnnotation.SetName(_recordingHistoryDetails, "録画履歴の詳細");
            ApplyDefaultFont(_recordingHistoryDetails);
            ApplyDefaultFont(_recordingHistoryModeButton);
            ApplyDefaultFont(_recordingHistoryRefreshButton);
            _createRuleButton = CreateRecordingModeButton("新規", detailsPane,
                CreateRuleControlId, RecordingToolbarLeft(4), GetModuleHandleW(null));
            _editRuleButton = CreateRecordingModeButton("編集", detailsPane,
                EditRuleControlId, RecordingToolbarLeft(5), GetModuleHandleW(null));
            _toggleRuleButton = CreateRecordingModeButton("有効化", detailsPane,
                ToggleRuleControlId, RecordingToolbarLeft(6), GetModuleHandleW(null));
            _deleteRuleButton = CreateRecordingModeButton("削除", detailsPane,
                DeleteRuleControlId, RecordingToolbarLeft(7), GetModuleHandleW(null));
            _moveRuleUpButton = CreateRecordingModeButton("上へ", detailsPane,
                MoveRuleUpControlId, RecordingToolbarLeft(8), GetModuleHandleW(null));
            _moveRuleDownButton = CreateRecordingModeButton("下へ", detailsPane,
                MoveRuleDownControlId, RecordingToolbarLeft(9), GetModuleHandleW(null));
            ApplyDefaultFont(_createRuleButton);
            ApplyDefaultFont(_editRuleButton);
            ApplyDefaultFont(_toggleRuleButton);
            ApplyDefaultFont(_deleteRuleButton);
            ApplyDefaultFont(_moveRuleUpButton);
            ApplyDefaultFont(_moveRuleDownButton);
            _detailsDescription = detailsDescription;
            _detailsMetadata = detailsMetadata;
            _detailsIdentity = detailsIdentity;
            _detailsRecordingStatus = detailsRecordingStatus;
            _detailsRecordButton = detailsRecordButton;
            _detailsStopRecordingButton = detailsStopRecordingButton;
            _detailsCopyButton = detailsCopyButton;
            _publishingStartButton = CreateRecordingModeButton(
                GenesisGuiNextPublishingPresentation.StartLabel, detailsPane,
                PublishingStartControlId, detailsTextWidth + 20, GetModuleHandleW(null));
            _publishingInterruptButton = CreateRecordingModeButton(
                GenesisGuiNextPublishingPresentation.InterruptLabel, detailsPane,
                PublishingInterruptControlId, detailsTextWidth + 188, GetModuleHandleW(null));
            _publishingFinishButton = CreateRecordingModeButton(
                GenesisGuiNextPublishingPresentation.FinishLabel, detailsPane,
                PublishingFinishControlId, detailsTextWidth + 104, GetModuleHandleW(null));
            ApplyDefaultFont(_publishingFinishButton);
            _ = ShowWindow(_publishingFinishButton, ShowWindowHide);
            _ = EnableWindow(_publishingFinishButton, false);
            ApplyDefaultFont(_publishingStartButton);
            ApplyDefaultFont(_publishingInterruptButton);
            _ = ShowWindow(_publishingStartButton, ShowWindowHide);
            _ = ShowWindow(_publishingInterruptButton, ShowWindowHide);
            _ = EnableWindow(_publishingStartButton, false);
            _ = EnableWindow(_publishingInterruptButton, false);
            CreatePublishingGuide(GetModuleHandleW(null));
            _listView = listView;
            _ = PeerCast.Nova.Windows.Win32AccessibilityAnnotation.SetName(_listView, "チャンネル一覧");
            _tabs = tabs;
            _viewModel = viewModel;
            _interaction = interaction;
            _backgroundBrush = backgroundBrush;
            _relayStart = CreateRecordingModeButton("Relay開始", detailsPane, RelayStartControlId, 12, GetModuleHandleW(null));
            _relayStop = CreateRecordingModeButton("Relay停止", detailsPane, RelayStopControlId, 102, GetModuleHandleW(null));
            _ = MoveWindow(_relayStart, 12, RelayControlsTop, 84, 26, true);
            _ = MoveWindow(_relayStop, 102, RelayControlsTop, 84, 26, true);
            _relayStatus = CreateWindowExW(0, "STATIC", string.Empty,
                WindowStyleChild | WindowStyleVisible | StaticStyleOwnerDraw, 12, RelayStatusTop,
                Math.Max(1, detailsTextWidth), 40, detailsPane, (nint)RelayStatusControlId, GetModuleHandleW(null), 0);
            ApplyDefaultFont(_relayStart);
            ApplyDefaultFont(_relayStop);
            ApplyDefaultFont(_relayStatus);
            CreateRelayTree();
        }

        public void Attach()
        {
            if (IsModern && _tabs.PublishingSettings is { } settings) _publishingEditor = new(settings);
            _tabs.SetPublishingDispatcher(PostPublishingUi);
            _tabs.PropertyChanged += OnTabsPropertyChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        public bool RuleButtonsAvailable => PublishingGuideAvailable && _publishingStartButton != 0 && _publishingInterruptButton != 0 && _publishingFinishButton != 0 && _relayStart != 0 && _relayStop != 0 && _relayStatus != 0 && _recordingHistoryDetails != 0 && _recordingHistoryModeButton != 0 && _recordingHistoryRefreshButton != 0 && _createRuleButton != 0 && _editRuleButton != 0 &&
            _toggleRuleButton != 0 && _deleteRuleButton != 0 && _moveRuleUpButton != 0 && _moveRuleDownButton != 0;

        public void Detach()
        {
            if (Interlocked.Exchange(ref _detached, 1) == 0)
            {
                if (_detailsHeight is { } savedDetailsHeight)
                {
                    GenesisMainWindowResizeGuard.SaveDetailsHeight(_owner, savedDetailsHeight);
                }

                _ = PeerCast.Nova.Windows.Win32AccessibilityAnnotation.Clear(_listView);
                _ = PeerCast.Nova.Windows.Win32AccessibilityAnnotation.Clear(_recordingHistoryDetails);
                _ = PeerCast.Nova.Windows.Win32AccessibilityAnnotation.Clear(_relayTreeChannels);
                _ = PeerCast.Nova.Windows.Win32AccessibilityAnnotation.Clear(_relayTreeBody);
                DestroyPaneSplitter();
                _ = StatesByOwner.TryRemove(_owner, out _);
                _publishingEditor?.Dispose();
                if (IsWindow(_publishingEditPane)) _ = RemoveWindowSubclass(_publishingEditPane, PublishingEditorProcedure, PublishingEditorSubclassId);
                if (IsWindow(_publishingAdvancedPane)) _ = RemoveWindowSubclass(_publishingAdvancedPane, PublishingEditorProcedure, PublishingEditorSubclassId);
                DetachVisuals();
                _generalSettings?.Release();
                _pathSettings?.Release();
                CloseNatSettingsPage();
                CloseNetworkSettingsPages();
                _settingsCategory = null;
                _detailsUpdates.Close();
                _favoriteUpdates.Close();
                _tabs.SetPublishingDispatcher(null);
                _publishingSettingsWindow?.Dispose();
                _publishingSettingsWindow = null;
                while (_publishingUi.TryDequeue(out _)) { }
                _tabs.PropertyChanged -= OnTabsPropertyChanged;
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }
        }

        private void PostPublishingUi(Action action)
        {
            if (Volatile.Read(ref _detached) != 0) return;
            _publishingUi.Enqueue(action);
            _ = PostMessageW(_detailsPane, WindowMessagePublishingDispatch, 0, 0);
        }

        internal void DrainPublishingUi()
        {
            while (_publishingUi.TryDequeue(out var action))
                if (Volatile.Read(ref _detached) == 0) action();
        }

        private async Task ObserveQuickRuleMutationAsync(Guid? selectedId, GenesisRecordingRuleQuickAction action)
        {
            _ = await _tabs.QuickMutateRecordingRuleAsync(selectedId, action, (confirmation, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (Volatile.Read(ref _detached) != 0) return ValueTask.FromResult(false);
                SynchronizeNativeSelection();
                var accepted = MessageBoxW(_owner, confirmation.Message, "録画ルール", 0x24) == 6;
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult(accepted);
            }).ConfigureAwait(false);
        }

        public void ProcessCommand(nuint wParam, nint lParam)
        {
            _searchBox.ProcessCommand(wParam, lParam);
            var commandId = (int)(wParam & 0xffff);
            var notificationCode = (int)((wParam >> 16) & 0xffff);
            if (Volatile.Read(ref _detached) == 0 && ProcessRelayTreeCommand(commandId, lParam, notificationCode)) return;
            if (Volatile.Read(ref _detached) == 0 && ProcessPublishingAdvancedInput(commandId, notificationCode, lParam)) return;
            if (Volatile.Read(ref _detached) == 0 && ProcessPublishingInlineInput(commandId, notificationCode, lParam)) return;
            if (Volatile.Read(ref _detached) == 0 && ProcessSettingsPathInput(commandId, notificationCode, lParam)) return;
            if (Volatile.Read(ref _detached) == 0 && ProcessNetworkSettingsInput(commandId, notificationCode, lParam)) return;
            if (notificationCode != ButtonNotificationClicked ||
                Volatile.Read(ref _detached) != 0)
            {
                return;
            }

            if (ProcessIntegratedSettingsCommand(commandId, lParam)) return;
            if (SettingsPageOpen && commandId == FavoriteControlId) return;
            if (_tabs.SelectedTab == GenesisGuiNextTab.Publishing &&
                lParam == _publishingStartButton && commandId == PublishingStartControlId)
            {
                _ = _tabs.RequestPublishingStartAsync();
                return;
            }
            if (_tabs.SelectedTab == GenesisGuiNextTab.Publishing &&
                lParam == _publishingInterruptButton && commandId == PublishingInterruptControlId)
            {
                _ = _tabs.RequestPublishingInterruptAsync();
                return;
            }
            if (_tabs.SelectedTab == GenesisGuiNextTab.Publishing &&
                lParam == _publishingFinishButton && commandId == PublishingFinishControlId)
            {
                _ = _tabs.RequestPublishingFinishAsync();
                return;
            }
            if (ProcessPublishingGuideCommand(commandId, lParam)) return;
            if (_tabs.SelectedTab == GenesisGuiNextTab.Publishing &&
                !IsPublishingNavigationCommand(commandId)) return;
            if (lParam == _relayStop && commandId == RelayStopControlId)
            {
                _ = GenesisGuiNextRelayCommandDispatch.StopAsync(_viewModel);
                return;
            }
            if (lParam == _relayStart && commandId == RelayStartControlId)
            {
                SynchronizeNativeSelection();
                var index = SendMessageW(_listView, ListViewMessageGetNextItem, nuint.MaxValue, (nint)ListViewNextItemSelected).ToInt32();
                _ = GenesisGuiNextRelayCommandDispatch.StartAsync(_viewModel, GenesisChannelListRenderedChannels.FindRow(_listView, index));
                return;
            }
            if (_tabs.IsRecordingHistoryPage && !IsHistoryNavigationCommand(commandId)) return;

            if ((IsRulesPageActive() || _tabs.IsRecordingLibraryPage || _tabs.IsRecordingHistoryPage) &&
                (lParam == _detailsRecordButton ||
                 lParam == _detailsStopRecordingButton ||
                 lParam == _detailsCopyButton))
            {
                return;
            }

            if (lParam == _playButton && commandId == PlayControlId)
            {
                StartSelectedChannelPlayback();
                return;
            }

            if (lParam == _stopButton && commandId == StopControlId)
            {
                StartStopPlayback();
                return;
            }

            if (lParam == _detailsRecordButton && commandId == DetailsRecordControlId)
            {
                StartSelectedChannelRecording();
                return;
            }

            if (lParam == _detailsStopRecordingButton &&
                commandId == DetailsStopRecordingControlId)
            {
                StopSelectedChannelRecording();
                return;
            }

            if (lParam == _detailsCopyButton && commandId == DetailsCopyControlId)
            {
                var details = GetCurrentDetailsPresentation();
                if (!details.IsEmpty)
                {
                    _ = GenesisChannelContextMenuNative.CopySourceToClipboard(
                        _owner,
                        details.CopyText);
                }

                return;
            }

            if (lParam == _activeRecordingsModeButton &&
                commandId == ActiveRecordingsModeControlId)
            {
                if (_tabs.SelectedTab != GenesisGuiNextTab.Recording) return;
                _ = ObserveRecordingPageModeAsync(
                    GenesisGuiNextRecordingPageMode.ActiveRecordings);
                return;
            }

            if (lParam == _recordingRulesModeButton &&
                commandId == RecordingRulesModeControlId)
            {
                if (_tabs.SelectedTab != GenesisGuiNextTab.Recording) return;
                _ = ObserveRecordingPageModeAsync(
                    GenesisGuiNextRecordingPageMode.Rules);
                return;
            }

            if (lParam == _recordingHistoryModeButton && commandId == RecordingHistoryModeControlId)
            {
                if (_tabs.SelectedTab == GenesisGuiNextTab.Recording)
                    _ = ObserveRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.History);
                return;
            }
            if (lParam == _recordingHistoryRefreshButton && commandId == RecordingHistoryRefreshControlId)
            {
                if (_tabs.CanRefreshRecordingHistory) _ = _tabs.RefreshRecordingHistoryAsync();
                return;
            }
            if (lParam == _recordingLibraryModeButton && commandId == RecordingLibraryModeControlId)
            {
                if (_tabs.SelectedTab == GenesisGuiNextTab.Recording)
                    _ = ObserveRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library);
                return;
            }
            if (lParam == _recordingLibraryRefreshButton && commandId == RecordingLibraryRefreshControlId)
            {
                if (_tabs.CanRefreshRecordingLibrary) _ = _tabs.RefreshRecordingLibraryAsync();
                return;
            }

            if ((lParam == _createRuleButton && commandId == CreateRuleControlId) ||
                (lParam == _editRuleButton && commandId == EditRuleControlId))
            {
                var rowIndex = SendMessageW(_listView, ListViewMessageGetNextItem,
                    nuint.MaxValue, (nint)ListViewNextItemSelected).ToInt32();
                var selectedId = GenesisChannelListRenderedChannels.FindRow(_listView, rowIndex)?.RecordingRule?.RuleId;
                var draft = commandId == CreateRuleControlId
                    ? _tabs.CreateRecordingRule() : _tabs.EditRecordingRule(selectedId);
                if (draft is not null && !GenesisRecordingRuleEditorWindow.ShowModal(_owner, _tabs.RuleEditor, draft))
                {
                    _detailsUpdates.Reset();
                    _ = SetWindowTextW(_detailsDescription, "録画ルールの編集画面を開けませんでした。");
                }
                return;
            }

            if ((lParam == _toggleRuleButton && commandId == ToggleRuleControlId) ||
                (lParam == _deleteRuleButton && commandId == DeleteRuleControlId) ||
                (lParam == _moveRuleUpButton && commandId == MoveRuleUpControlId) ||
                (lParam == _moveRuleDownButton && commandId == MoveRuleDownControlId))
            {
                var rowIndex = SendMessageW(_listView, ListViewMessageGetNextItem,
                    nuint.MaxValue, (nint)ListViewNextItemSelected).ToInt32();
                var selectedId = GenesisChannelListRenderedChannels.FindRow(_listView, rowIndex)?.RecordingRule?.RuleId;
                var rule = _tabs.RecordingRulesSnapshot?.Rules?.Rules.FirstOrDefault(r => r.Id == selectedId);
                var action = commandId switch
                {
                    DeleteRuleControlId => GenesisRecordingRuleQuickAction.Delete,
                    MoveRuleUpControlId => GenesisRecordingRuleQuickAction.MoveUp,
                    MoveRuleDownControlId => GenesisRecordingRuleQuickAction.MoveDown,
                    _ => rule?.IsEnabled == true ? GenesisRecordingRuleQuickAction.Disable : GenesisRecordingRuleQuickAction.Enable,
                };
                _ = ObserveQuickRuleMutationAsync(selectedId, action);
                return;
            }

            if (lParam == _favoriteButton && commandId == FavoriteControlId)
            {
                var selectedRow = SendMessageW(
                    _listView,
                    ListViewMessageGetNextItem,
                    nuint.MaxValue,
                    (nint)ListViewNextItemSelected).ToInt32();
                _ = GenesisGuiNextFavoriteToggle.Apply(
                    _tabs,
                    GenesisChannelListRenderedChannels.FindRow(_listView, selectedRow));
                return;
            }

            if (lParam == _refreshButton && commandId == RefreshControlId)
            {
                _ = GenesisChannelRefreshButtonBridge.TryRefreshForOwner(_owner);
                return;
            }

            var itemIndex = NavigationIndexFor(commandId);
            if (itemIndex == (int)GenesisGuiNextTab.RelayTree && !IsModern) return;
            if (itemIndex >= 0 && itemIndex < _navigationButtons.Length &&
                !(GenesisGuiNextNavigationPresentation.TryResolveExistingView(itemIndex, out var targetTab) && targetTab == GenesisGuiNextTab.Publishing) &&
                !CanLeavePublishingEditor()) return;
            if (GenesisGuiNextNavigationPresentation.TryResolveExistingView(
                    itemIndex,
                    out var selectedTab))
            {
                CloseIntegratedSettings();
                if (selectedTab == GenesisGuiNextTab.RelayTree) CaptureTreeChannel();
                _utilityNavigationSelection.SelectView(_tabs, selectedTab);
                _ = InvalidateRect(_sidebar, 0, true);
                if (selectedTab == GenesisGuiNextTab.Recording)
                {
                    _ = RefreshActiveRecordingsAsync();
                    if (_tabs.RecordingPageMode ==
                        GenesisGuiNextRecordingPageMode.Rules)
                    {
                        _ = _tabs.ReloadRecordingRulesAsync();
                    }
                }

                return;
            }

            if (GenesisGuiNextNavigationPresentation.TryResolvePrimaryCommand(
                    itemIndex,
                    out var primaryCommand))
            {
                if (primaryCommand == GenesisGuiNextPrimaryCommand.Search)
                {
                    CloseIntegratedSettings();
                    _ = _utilityNavigationSelection.ActivateSearch(
                        _tabs,
                        _searchBox.Focus);
                    _ = InvalidateRect(_sidebar, 0, true);
                }
                else
                {
                    if (primaryCommand != GenesisGuiNextPrimaryCommand.Settings) CloseIntegratedSettings();
                    _utilityNavigationSelection.SelectUtility(itemIndex);
                    _ = InvalidateRect(_sidebar, 0, true);
                    if (primaryCommand == GenesisGuiNextPrimaryCommand.Settings)
                    {
                        if (!OpenIntegratedSettings())
                            _ = GenesisChannelNotificationSettingsButtonBridge.TryOpenForOwner(
                                _owner,
                                _navigationButtons[itemIndex]);
                    }
                    else if (primaryCommand == GenesisGuiNextPrimaryCommand.About)
                    {
                        _ = GenesisAboutButtonBridge.TryShowForOwner(_owner);
                    }
                }
            }
        }

        public bool DrawNavigationItem(nint drawItemPointer)
        {
            if (drawItemPointer == 0)
            {
                return false;
            }

            var drawItem = Marshal.PtrToStructure<DrawItem>(drawItemPointer);
            using var fontSelection = GenesisWindowUiFont.SelectForDrawing(
                drawItem.DeviceContext,
                drawItem.ControlWindow);
            if (DrawSettingsYpList(drawItem)) return true;
            if (DrawSettingsButton(drawItem)) return true;
            if (DrawPublishingPanelItem(drawItem)) return true;
            if (DrawRecordingPanelItem(drawItem)) return true;
            if (DrawChannelCardItem(drawItem)) return true;
            if (IsModern && drawItem.ControlType == 3 && _searchBox.IsYellowPageFilter(drawItem.ControlWindow))
                return DrawYellowPageItem(drawItem);
            if (IsOwnerDrawButton(drawItem.ControlType) &&
                drawItem.ControlId is CreateRuleControlId or EditRuleControlId)
            {
                return DrawRecordingModeButton(drawItem,
                    drawItem.ControlId == CreateRuleControlId ? "新規" : "編集", false);
            }
            if (IsOwnerDrawButton(drawItem.ControlType) &&
                (drawItem.ControlId == RefreshControlId || IsModern && drawItem.ControlId == FavoriteControlId))
            {
                return IsModern ? DrawTopButton(drawItem) : DrawClassicRefreshButton(drawItem);
            }

            if (IsOwnerDrawButton(drawItem.ControlType) &&
                drawItem.ControlId == DetailsCopyControlId)
            {
                return DrawDetailsCopyButton(drawItem);
            }

            if (IsOwnerDrawButton(drawItem.ControlType) &&
                drawItem.ControlId == ActiveRecordingsModeControlId)
            {
                return DrawRecordingModeButton(
                    drawItem,
                    GenesisGuiNextRecordingPageModePresentation.ActiveRecordingsLabel,
                    _tabs.RecordingPageMode ==
                        GenesisGuiNextRecordingPageMode.ActiveRecordings);
            }

            if (IsOwnerDrawButton(drawItem.ControlType) &&
                drawItem.ControlId == RecordingRulesModeControlId)
            {
                return DrawRecordingModeButton(
                    drawItem,
                    GenesisGuiNextRecordingPageModePresentation.RulesLabel,
                    _tabs.RecordingPageMode == GenesisGuiNextRecordingPageMode.Rules);
            }

            if (IsOwnerDrawButton(drawItem.ControlType) &&
                drawItem.ControlId == RecordingHistoryModeControlId)
                return DrawRecordingModeButton(drawItem,
                    GenesisGuiNextRecordingPageModePresentation.HistoryLabel,
                    _tabs.RecordingPageMode == GenesisGuiNextRecordingPageMode.History);
            if (IsOwnerDrawButton(drawItem.ControlType) &&
                drawItem.ControlId == RecordingLibraryModeControlId)
                return DrawRecordingModeButton(drawItem,
                    GenesisGuiNextRecordingPageModePresentation.LibraryLabel,
                    _tabs.RecordingPageMode == GenesisGuiNextRecordingPageMode.Library);
            if (IsOwnerDrawButton(drawItem.ControlType) &&
                drawItem.ControlId is RecordingLibraryRefreshControlId or RecordingHistoryRefreshControlId)
                return DrawRecordingModeButton(drawItem, "更新", false);

            if (IsOwnerDrawButton(drawItem.ControlType) &&
                drawItem.ControlId is CreateRuleControlId or EditRuleControlId or ToggleRuleControlId or DeleteRuleControlId or MoveRuleUpControlId or MoveRuleDownControlId)
            {
                var label = drawItem.ControlId switch
                {
                    CreateRuleControlId => "新規",
                    EditRuleControlId => "編集",
                    ToggleRuleControlId => _toggleRuleLabel,
                    MoveRuleUpControlId => "上へ",
                    MoveRuleDownControlId => "下へ",
                    _ => "削除",
                };
                return DrawRecordingModeButton(drawItem, label, false);
            }

            if (IsOwnerDrawButton(drawItem.ControlType) &&
                drawItem.ControlId is DetailsRecordControlId or DetailsStopRecordingControlId)
            {
                return DrawManualRecordingButton(drawItem);
            }

            if (IsOwnerDrawButton(drawItem.ControlType) &&
                drawItem.ControlId is PublishingStartControlId or PublishingInterruptControlId or PublishingFinishControlId)
                return DrawRecordingModeButton(drawItem,
                    drawItem.ControlId == PublishingStartControlId
                        ? GenesisGuiNextPublishingPresentation.StartLabel : drawItem.ControlId == PublishingFinishControlId
                        ? GenesisGuiNextPublishingPresentation.FinishLabel : GenesisGuiNextPublishingPresentation.InterruptLabel,
                    false, drawItem.ControlId == PublishingInterruptControlId);

            if (DrawPublishingGuide(drawItem)) return true;
            if (IsOwnerDrawButton(drawItem.ControlType) && drawItem.ControlId is RelayStartControlId or RelayStopControlId)
                return DrawRecordingModeButton(drawItem, drawItem.ControlId == RelayStartControlId ? "Relay開始" : "Relay停止", false);
            if (IsOwnerDrawStatic(drawItem.ControlType) && drawItem.ControlId == RelayStatusControlId)
            {
                FillRectangle(drawItem.DeviceContext, drawItem.ItemRectangle, BackgroundColor);
                _ = SetBkMode(drawItem.DeviceContext, TransparentBackgroundMode);
                var snapshot = _viewModel.RelaySnapshot;
                _ = SetTextColor(drawItem.DeviceContext, snapshot.Error == GenesisRelayActivationError.None ? TextColor : WarningTextColor);
                var rectangle = drawItem.ItemRectangle;
                var text = _viewModel.RelayActivation is null ? "ローカルRelay: 利用できません" : snapshot.Text;
                _ = DrawTextW(drawItem.DeviceContext, text, -1, ref rectangle, DrawTextNoPrefix | 0x0010);
                return true;
            }
            if (IsRecordingStatusDraw(drawItem.ControlType, drawItem.ControlId))
            {
                return DrawManualRecordingStatus(drawItem);
            }

            if (IsOwnerDrawButton(drawItem.ControlType) &&
                drawItem.ControlId is PlayControlId or StopControlId)
            {
                return DrawPlaybackButton(drawItem);
            }

            var itemIndex = NavigationIndexFor((int)drawItem.ControlId);
            if (!IsOwnerDrawButton(drawItem.ControlType) ||
                itemIndex < 0 ||
                itemIndex >= _navigationButtons.Length)
            {
                return false;
            }

            if (!IsModern) return DrawClassicNavigation(drawItem, itemIndex);
            var selected = _utilityNavigationSelection.IsSelected(itemIndex, _tabs.SelectedTab);
            var disabled = (drawItem.ItemState & DrawItemDisabled) != 0;
            FillRectangle(
                drawItem.DeviceContext,
                drawItem.ItemRectangle,
                GenesisGuiNextDesign.NavigationBackground(selected, _hoverControl == drawItem.ControlWindow));
            if (selected)
            {
                var accent = drawItem.ItemRectangle;
                accent.Right = Math.Min(accent.Right, accent.Left + Scale(4));
                FillRectangle(drawItem.DeviceContext, accent, GenesisGuiNextDesign.Accent);
            }

            _ = SetBkMode(drawItem.DeviceContext, TransparentBackgroundMode);
            _ = SetTextColor(
                drawItem.DeviceContext,
                disabled ? DisabledTextColor : TextColor);
            var labelRectangle = drawItem.ItemRectangle;
            DrawNavigationIcon(drawItem.DeviceContext, itemIndex, drawItem.ItemRectangle, disabled);
            labelRectangle.Left += Scale(38);
            labelRectangle.Right -= Scale(6);
            _ = DrawTextW(
                drawItem.DeviceContext,
                GenesisGuiNextNavigationPresentation.ItemsFor(IsModern)[itemIndex],
                -1,
                ref labelRectangle,
                DrawTextSingleLine |
                    DrawTextVerticalCenter |
                    DrawTextNoPrefix | 0x8000);
            if ((drawItem.ItemState & DrawItemFocused) != 0)
            {
                _ = DrawFocusRect(drawItem.DeviceContext, ref drawItem.ItemRectangle);
            }

            return true;
        }

        private static bool DrawRecordingModeButton(
            DrawItem drawItem,
            string label,
            bool selected,
            bool dangerous = false)
        {
            var pressed = (drawItem.ItemState & 0x0001) != 0;
            var disabled = (drawItem.ItemState & DrawItemDisabled) != 0;
            FillRectangle(
                drawItem.DeviceContext,
                drawItem.ItemRectangle,
                selected || pressed ? SelectedColor : UnselectedColor);
            _ = SetBkMode(drawItem.DeviceContext, TransparentBackgroundMode);
            _ = SetTextColor(
                drawItem.DeviceContext,
                disabled ? DisabledTextColor : dangerous ? StopAccentColor : TextColor);
            var labelRectangle = drawItem.ItemRectangle;
            labelRectangle.Left += 3;
            labelRectangle.Right -= 3;
            _ = DrawTextW(
                drawItem.DeviceContext,
                label,
                -1,
                ref labelRectangle,
                DrawTextSingleLine |
                    DrawTextVerticalCenter |
                    DrawTextNoPrefix);
            if ((drawItem.ItemState & DrawItemFocused) != 0)
            {
                _ = DrawFocusRect(drawItem.DeviceContext, ref drawItem.ItemRectangle);
            }

            return true;
        }

        private static bool DrawDetailsCopyButton(DrawItem drawItem)
        {
            var pressed = (drawItem.ItemState & 0x0001) != 0;
            var disabled = (drawItem.ItemState & DrawItemDisabled) != 0;
            FillRectangle(
                drawItem.DeviceContext,
                drawItem.ItemRectangle,
                disabled
                    ? BackgroundColor
                    : pressed
                        ? SelectedColor
                        : UnselectedColor);
            _ = SetBkMode(drawItem.DeviceContext, TransparentBackgroundMode);
            _ = SetTextColor(
                drawItem.DeviceContext,
                disabled ? DisabledTextColor : TextColor);
            var labelRectangle = drawItem.ItemRectangle;
            labelRectangle.Left += 4;
            labelRectangle.Right -= 4;
            _ = DrawTextW(
                drawItem.DeviceContext,
                "情報をコピー",
                -1,
                ref labelRectangle,
                DrawTextSingleLine |
                    DrawTextVerticalCenter |
                    DrawTextNoPrefix);
            if ((drawItem.ItemState & DrawItemFocused) != 0)
            {
                _ = DrawFocusRect(drawItem.DeviceContext, ref drawItem.ItemRectangle);
            }

            return true;
        }

        private bool DrawManualRecordingButton(DrawItem drawItem)
        {
            var isRecord = drawItem.ControlId == DetailsRecordControlId;
            var pressed = (drawItem.ItemState & 0x0001) != 0;
            var disabled = (drawItem.ItemState & DrawItemDisabled) != 0;
            FillRectangle(
                drawItem.DeviceContext,
                drawItem.ItemRectangle,
                disabled
                    ? BackgroundColor
                    : pressed
                        ? SelectedColor
                        : RecordingAccentColor);
            _ = SetBkMode(drawItem.DeviceContext, TransparentBackgroundMode);
            _ = SetTextColor(
                drawItem.DeviceContext,
                disabled ? DisabledTextColor : TextColor);
            var labelRectangle = drawItem.ItemRectangle;
            labelRectangle.Left += 3;
            labelRectangle.Right -= 3;
            _ = DrawTextW(
                drawItem.DeviceContext,
                isRecord && _allowAutomaticShown ? GenesisGuiNextRecordingCommandDispatch.AllowAutomaticLabel : isRecord ? "録画" : "録画停止",
                -1,
                ref labelRectangle,
                DrawTextSingleLine |
                    DrawTextVerticalCenter |
                    DrawTextNoPrefix);
            if ((drawItem.ItemState & DrawItemFocused) != 0)
            {
                _ = DrawFocusRect(drawItem.DeviceContext, ref drawItem.ItemRectangle);
            }

            return true;
        }

        private bool DrawManualRecordingStatus(DrawItem drawItem)
        {
            if (_tabs.IsRecordingHistoryPage)
            {
                FillRectangle(drawItem.DeviceContext, drawItem.ItemRectangle, BackgroundColor);
                _ = SetBkMode(drawItem.DeviceContext, TransparentBackgroundMode);
                _ = SetTextColor(drawItem.DeviceContext, _historyStatusPresentation.IsWarning ? WarningTextColor : TextColor);
                var rectangle = drawItem.ItemRectangle;
                _ = DrawTextW(drawItem.DeviceContext, _historyStatusPresentation.Text, -1, ref rectangle, DrawTextNoPrefix | 0x0010); // DT_WORDBREAK
                return true;
            }
            if (_tabs.IsRecordingLibraryPage)
            {
                FillRectangle(drawItem.DeviceContext, drawItem.ItemRectangle, BackgroundColor);
                _ = SetBkMode(drawItem.DeviceContext, TransparentBackgroundMode);
                _ = SetTextColor(drawItem.DeviceContext, TextColor);
                var rectangle = drawItem.ItemRectangle;
                _ = DrawTextW(drawItem.DeviceContext,
                    RecordingStatusText(true, _tabs.RecordingLibraryStatusText, string.Empty), -1,
                    ref rectangle, DrawTextNoPrefix);
                return true;
            }
            var presentation = GetCurrentRecordingPresentation();
            var text = RecordingStatusText(false, _tabs.RecordingLibraryStatusText, presentation.DisplayText);
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            _ = SetBkMode(drawItem.DeviceContext, TransparentBackgroundMode);
            _ = SetTextColor(
                drawItem.DeviceContext,
                presentation.IsFailure
                    ? WarningTextColor
                    : presentation.IsRecording || presentation.IsTransitioning
                        ? RecordingAccentColor
                        : TextColor);
            var labelRectangle = drawItem.ItemRectangle;
            labelRectangle.Left += 2;
            labelRectangle.Right -= 2;
            _ = DrawTextW(
                drawItem.DeviceContext,
                text,
                -1,
                ref labelRectangle,
                DrawTextSingleLine |
                    DrawTextVerticalCenter |
                    DrawTextNoPrefix);
            return true;
        }

        private static bool DrawPlaybackButton(DrawItem drawItem)
        {
            var isPlay = drawItem.ControlId == PlayControlId;
            var pressed = (drawItem.ItemState & 0x0001) != 0;
            var disabled = (drawItem.ItemState & DrawItemDisabled) != 0;
            FillRectangle(
                drawItem.DeviceContext,
                drawItem.ItemRectangle,
                disabled
                    ? BackgroundColor
                    : pressed
                        ? SelectedColor
                        : isPlay
                            ? PlaybackAccentColor
                            : StopAccentColor);
            _ = SetBkMode(drawItem.DeviceContext, TransparentBackgroundMode);
            _ = SetTextColor(
                drawItem.DeviceContext,
                disabled ? DisabledTextColor : TextColor);
            var labelRectangle = drawItem.ItemRectangle;
            labelRectangle.Left += 4;
            labelRectangle.Right -= 4;
            _ = DrawTextW(
                drawItem.DeviceContext,
                isPlay ? "再生" : "停止",
                -1,
                ref labelRectangle,
                DrawTextSingleLine |
                    DrawTextVerticalCenter |
                    DrawTextNoPrefix);
            if ((drawItem.ItemState & DrawItemFocused) != 0)
            {
                _ = DrawFocusRect(drawItem.DeviceContext, ref drawItem.ItemRectangle);
            }

            return true;
        }

        public bool HandleListViewCustomDraw(
            nint notificationPointer,
            out nint result)
        {
            if (!IsModern) return HandleClassicListViewCustomDraw(notificationPointer, out result);
            result = 0;
            if (notificationPointer == 0 ||
                Volatile.Read(ref _detached) != 0)
            {
                return false;
            }

            var header = Marshal.PtrToStructure<NotifyHeader>(notificationPointer);
            if (header.WindowFrom != _listView || header.Code != NotifyCustomDraw)
            {
                return false;
            }

            if (DrawChannelListCell(notificationPointer, out result)) return true;
            var draw = Marshal.PtrToStructure<CustomDraw>(notificationPointer);
            if (draw.DrawStage == CustomDrawStagePrePaint)
            {
                _paintSelectedRow = SendMessageW(_listView, ListViewMessageGetNextItem, nuint.MaxValue,
                    (nint)ListViewNextItemSelected).ToInt32();
                result = (nint)CustomDrawNotifyItemDraw;
                return true;
            }

            if (draw.DrawStage == CustomDrawStageItemPrePaint)
            {
                var selected = draw.ItemSpec == (nuint)_paintSelectedRow;
                Marshal.WriteInt32(notificationPointer,
                    Marshal.OffsetOf<ListViewCustomDraw>(nameof(ListViewCustomDraw.TextColor)).ToInt32(),
                    GenesisGuiNextDesign.Text);
                Marshal.WriteInt32(notificationPointer,
                    Marshal.OffsetOf<ListViewCustomDraw>(nameof(ListViewCustomDraw.TextBackgroundColor)).ToInt32(),
                    GenesisGuiNextDesign.RowBackground(selected, draw.ItemSpec == (nuint)_hoverRow));
                // Suppress only native selection painting, never the actual list selection.
                draw.ItemState &= ~(CustomDrawStateSelected | 0x0010u);
                Marshal.StructureToPtr(draw, notificationPointer, false);
                result = (nint)(0x00000002 | CustomDrawNotifyPostPaint | (UsesChannelListStyle ? 0x00000020u : 0u));
                return true;
            }

            if (draw.DrawStage == CustomDrawStageItemPostPaint)
            {
                var bounds = new Rectangle();
                if (SendHeaderRectangle(_listView, ListViewMessageGetItemRectangle, draw.ItemSpec, ref bounds) != 0)
                    draw.ItemRectangle = bounds;
                if (GetClientRect(_listView, out var client))
                {
                    if (draw.ItemRectangle.Right < client.Right)
                    {
                        var tail = draw.ItemRectangle;
                        tail.Left = tail.Right; tail.Right = client.Right;
                        FillRectangle(draw.DeviceContext, tail, GenesisGuiNextDesign.RowBackground(
                            draw.ItemSpec == (nuint)_paintSelectedRow, draw.ItemSpec == (nuint)_hoverRow));
                    }
                    draw.ItemRectangle.Right = client.Right;
                }
                PaintRowSeparator(draw);
                if (draw.ItemSpec == (nuint)_paintSelectedRow)
                {
                    var accent = draw.ItemRectangle;
                    if (UsesChannelListStyle) accent.Left = 0;
                    accent.Right = Math.Min(accent.Right, accent.Left + (UsesChannelListStyle ? Scale(3) : AccentWidth));
                    FillRectangle(draw.DeviceContext, accent, GenesisGuiNextDesign.Accent);
                    if (GetFocus() == _listView)
                    {
                        var focus = draw.ItemRectangle;
                        focus.Left += Scale(4);
                        _ = DrawFocusRect(draw.DeviceContext, ref focus);
                    }
                }

                return true;
            }

            return false;
        }

        internal bool IsReadOnlyDetailsEdit(nint pane, nint control) =>
            pane == _detailsPane && control != 0 &&
            ((UsesRecordingPanel && control == _recordingHistoryDetails) ||
             (UsesPublishingPanel && (control == _publishingInstructions || control == _publishingBasic ||
                control == _publishingOptions || control == _publishingDiagnostic)));

        public nint PrepareStaticControl(nuint deviceContext, nint pane)
        {
            _ = SetBkMode((nint)deviceContext, TransparentBackgroundMode);
            if (IsSettingsPane(pane))
            {
                _ = SetBkColor((nint)deviceContext, GenesisGuiNextDesign.Panel);
                _ = SetTextColor((nint)deviceContext, GenesisGuiNextDesign.Text);
                return _topBrush;
            }
            _ = SetTextColor((nint)deviceContext, IsModern && pane == _statusBar ? GenesisGuiNextDesign.SecondaryText : TextColor);
            if ((UsesRecordingPanel || UsesPublishingPanel) && pane == _detailsPane) return _topBrush;
            return IsModern && pane == _statusBar ? _topBrush : _backgroundBrush;
        }

        public nint PrepareDarkControl(nuint deviceContext, nint pane)
        {
            _ = SetBkMode((nint)deviceContext, 2);
            if (IsSettingsPane(pane) || (pane == _publishingEditPane || pane == _publishingAdvancedPane) && IsModern)
            {
                _ = SetBkColor((nint)deviceContext, GenesisGuiNextDesign.Input);
                _ = SetTextColor((nint)deviceContext, GenesisGuiNextDesign.Text);
                return _inputBrush;
            }
            if ((UsesRecordingPanel || UsesPublishingPanel) && pane == _detailsPane)
            {
                _ = SetBkColor((nint)deviceContext, GenesisGuiNextDesign.Input);
                _ = SetTextColor((nint)deviceContext, GenesisGuiNextDesign.Text);
                return _inputBrush;
            }
            _ = SetBkColor((nint)deviceContext, IsModern && pane == _statusBar ? GenesisGuiNextDesign.Input : BackgroundColor);
            _ = SetTextColor((nint)deviceContext, TextColor);
            return IsModern && pane == _statusBar ? _inputBrush : _backgroundBrush;
        }

        public void SynchronizeYellowPageFilter() =>
            _searchBox.SynchronizeYellowPageFilter();

        public bool IsActiveRecordingsPage() =>
            _tabs.SelectedTab == GenesisGuiNextTab.Recording &&
            _tabs.RecordingPageMode == GenesisGuiNextRecordingPageMode.ActiveRecordings;

        public void SynchronizeNativeSelection()
        {
            if (Volatile.Read(ref _detached) != 0)
            {
                return;
            }

            _ = EnableWindow(
                _refreshButton,
                GenesisChannelRefreshButtonBridge.CanRefreshForOwner(_owner));

            foreach (var button in _navigationButtons)
            {
                if (button != 0 && IsWindow(button))
                {
                    _ = InvalidateRect(button, 0, true);
                }
            }

            SynchronizePublishingPageBounds();
            SynchronizeChannelListStyle();
            SynchronizeFavoriteButtonPresentation();
            SynchronizePlaybackButtonPresentation();
            _ = PeerCast.Nova.Windows.Win32AccessibilityAnnotation.SetName(
                _listView,
                AccessibleListTitle(_tabs.SelectedTab));
        }

        internal bool FocusSearch()
        {
            if (Volatile.Read(ref _detached) != 0)
            {
                return false;
            }

            CloseIntegratedSettings();
            var focused = _utilityNavigationSelection.ActivateSearch(_tabs, _searchBox.Focus);
            _ = InvalidateRect(_sidebar, 0, true);
            return focused;
        }

        public void SynchronizeFavoriteButtonPresentation()
        {
            if (Volatile.Read(ref _detached) != 0)
            {
                return;
            }

            var selectedRow = SendMessageW(
                _listView,
                ListViewMessageGetNextItem,
                nuint.MaxValue,
                (nint)ListViewNextItemSelected).ToInt32();
            GenesisChannelListRenderedChannels.CaptureSelectedRow(
                _listView,
                selectedRow);
            var renderedRow = GenesisChannelListRenderedChannels.FindRow(
                _listView,
                selectedRow);
            var presentation = GenesisGuiNextFavoriteButtonPresentationResolver.Resolve(
                _tabs,
                renderedRow, IsModern);
            var favoriteEnabled = presentation.IsEnabled && !SettingsPageOpen;
            using (var update = _favoriteUpdates.Begin(0, _ => { }))
            {
                _favoriteUpdates.Set(_favoriteButton, "text", presentation.Label,
                    () => IsWindow(_favoriteButton) && SetWindowTextW(_favoriteButton, presentation.Label));
                _favoriteUpdates.Set(_favoriteButton, "enabled", favoriteEnabled, () =>
                {
                    if (!IsWindow(_favoriteButton)) return false;
                    _ = EnableWindow(_favoriteButton, favoriteEnabled);
                    return true;
                });
            }
            SynchronizeDetailsPresentation(renderedRow);
            SynchronizePlaybackButtonPresentation();
        }

        public void ResetDetailsPresentation() => _detailsUpdates.Reset();

        private void SynchronizeDetailsPresentation(GenesisGuiNextNativeTabRow? row)
        {
            if (Volatile.Read(ref _detached) != 0)
            {
                return;
            }
            using var update = _detailsUpdates.Begin(
                (_tabs.SelectedTab, _tabs.RecordingPageMode, _viewModel.RelaySnapshot,
                    GetCurrentRecordingPresentation(), _tabs.RecordingHistoryStatus,
                    _tabs.PublishingConnection.StateText, _tabs.PublishingConnection.Tone, _tabs.PublishingKeyRevealed),
                control => { if (IsWindow(control)) _ = InvalidateRect(control, 0, true); });
            var relayControls = GenesisGuiNextRelayControlsPresentation.Resolve(IsModern, _tabs.SelectedTab,
                _viewModel.RelaySnapshot, _viewModel.RelayActivation?.CanStop == true);
            _ = PresentVisible(_relayStart, relayControls.Start ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = PresentVisible(_relayStop, relayControls.Stop ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = PresentVisible(_relayStatus, relayControls.Status ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = PresentEnabled(_relayStart, relayControls.Start && GenesisGuiNextRelayCommandDispatch.CanStart(_viewModel, row));
            _ = PresentEnabled(_relayStop, relayControls.Stop && _viewModel.RelayActivation?.CanStop == true);
            if (GetClientRect(_detailsPane, out var relayPane))
                _ = PresentBounds(_relayStatus, 12, RelayStatusTop, RelayStatusWidth(LogicalWidth(relayPane.Right - relayPane.Left)), 40, true);
            _ = PresentInvalidation(_relayStatus, 0, true);
            var treeShown = SynchronizeRelayTree();
            if (treeShown) { PresentRelayTree(); return; }

            var isRecordingPage = _tabs.SelectedTab == GenesisGuiNextTab.Recording;
            var isRulesPage = IsRulesPageActive();
            var isLibraryPage = _tabs.IsRecordingLibraryPage;
            var isHistoryPage = _tabs.IsRecordingHistoryPage;
            var isLogPage = _tabs.SelectedTab == GenesisGuiNextTab.Logs;
            var isPublishingPage = _tabs.SelectedTab == GenesisGuiNextTab.Publishing;
            _ = PresentVisible(_publishingFinishButton, isPublishingPage ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = PresentEnabled(_publishingFinishButton, isPublishingPage && _tabs.PublishingCommands.CanFinish);
            _ = PresentInvalidation(_publishingFinishButton, 0, true);
            _ = PresentVisible(_publishingStartButton, isPublishingPage ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = PresentVisible(_publishingInterruptButton, isPublishingPage ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = PresentEnabled(_publishingStartButton, isPublishingPage && _tabs.PublishingCommands.CanStart);
            _ = PresentEnabled(_publishingInterruptButton, isPublishingPage && _tabs.PublishingCommands.CanInterrupt);
            _ = PresentInvalidation(_publishingStartButton, 0, true);
            _ = PresentInvalidation(_publishingInterruptButton, 0, true);
            var hideRecordingActions = isRulesPage || isLibraryPage || isHistoryPage || isLogPage || isPublishingPage;
            _allowAutomaticShown = row?.Suppression is not null;
            _ = PresentBounds(_detailsRecordButton, _detailsTextWidth + 20, 8,
                _allowAutomaticShown ? DetailsRecordButtonWidth + DetailsRecordingButtonGap + DetailsStopRecordingButtonWidth : DetailsRecordButtonWidth,
                DetailsCopyButtonHeight, true);
            _ = PresentText(_detailsRecordButton, _allowAutomaticShown ? GenesisGuiNextRecordingCommandDispatch.AllowAutomaticLabel : UsesChannelCard ? "録画開始" : "録画");
            _ = PresentVisible(_createRuleButton, isRulesPage ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = PresentVisible(_editRuleButton, isRulesPage ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = PresentVisible(_toggleRuleButton, isRulesPage ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = PresentVisible(_deleteRuleButton, isRulesPage ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = PresentVisible(_moveRuleUpButton, isRulesPage ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = PresentVisible(_moveRuleDownButton, isRulesPage ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = PresentEnabled(_createRuleButton, _tabs.CanCreateRecordingRule);
            _ = PresentEnabled(_editRuleButton, _tabs.CanEditRecordingRule(row?.RecordingRule?.RuleId));
            _toggleRuleLabel = _tabs.RecordingRuleToggleLabel(row?.RecordingRule?.RuleId);
            _ = PresentText(_toggleRuleButton, _toggleRuleLabel);
            _ = PresentEnabled(_toggleRuleButton, _tabs.CanQuickMutateRecordingRule(row?.RecordingRule?.RuleId));
            _ = PresentEnabled(_deleteRuleButton, _tabs.CanQuickMutateRecordingRule(row?.RecordingRule?.RuleId));
            _ = PresentEnabled(_moveRuleUpButton, _tabs.CanMoveRecordingRule(row?.RecordingRule?.RuleId, GenesisRecordingRuleQuickAction.MoveUp));
            _ = PresentEnabled(_moveRuleDownButton, _tabs.CanMoveRecordingRule(row?.RecordingRule?.RuleId, GenesisRecordingRuleQuickAction.MoveDown));
            _ = PresentInvalidation(_createRuleButton, 0, true);
            _ = PresentInvalidation(_editRuleButton, 0, true);
            _ = PresentInvalidation(_toggleRuleButton, 0, true);
            _ = PresentInvalidation(_deleteRuleButton, 0, true);
            _ = PresentInvalidation(_moveRuleUpButton, 0, true);
            _ = PresentInvalidation(_moveRuleDownButton, 0, true);
            _ = PresentVisible(
                _activeRecordingsModeButton,
                isRecordingPage ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = PresentVisible(
                _recordingRulesModeButton,
                isRecordingPage ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = PresentVisible(_recordingLibraryModeButton,
                isRecordingPage ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = PresentVisible(_recordingLibraryRefreshButton,
                isLibraryPage ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = PresentEnabled(_recordingLibraryRefreshButton, _tabs.CanRefreshRecordingLibrary);
            _ = PresentInvalidation(_recordingLibraryRefreshButton, 0, true);
            _ = PresentVisible(_recordingHistoryModeButton, isRecordingPage ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = PresentVisible(_recordingHistoryRefreshButton, isHistoryPage ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = PresentEnabled(_recordingHistoryRefreshButton, _tabs.CanRefreshRecordingHistory);
            _ = PresentInvalidation(_recordingHistoryModeButton, 0, true);
            _ = PresentInvalidation(_recordingHistoryRefreshButton, 0, true);
            _ = PresentVisible(_recordingHistoryDetails, isHistoryPage ? ShowWindowShowNoActivate : ShowWindowHide);
            foreach (var textControl in new[] { _detailsTitle, _detailsDescription, _detailsMetadata, _detailsIdentity })
                _ = PresentVisible(textControl, isHistoryPage || isLogPage && UsesChannelCard && textControl == _detailsIdentity
                    ? ShowWindowHide : ShowWindowShowNoActivate);
            MoveDetailsTextControls(isRecordingPage, isLibraryPage || isHistoryPage);
            _ = PresentVisible(
                _detailsRecordButton,
                hideRecordingActions ? ShowWindowHide : ShowWindowShowNoActivate);
            _ = PresentVisible(
                _detailsStopRecordingButton,
                hideRecordingActions || _allowAutomaticShown ? ShowWindowHide : ShowWindowShowNoActivate);
            _ = PresentVisible(
                _detailsCopyButton,
                hideRecordingActions || IsModern ? ShowWindowHide : ShowWindowShowNoActivate);
            _ = PresentVisible(
                _detailsRecordingStatus,
                !isLogPage && !isPublishingPage && IsRecordingStatusVisible(isRulesPage) ? ShowWindowShowNoActivate : ShowWindowHide);

            SynchronizePublishingGuide(isPublishingPage);
            if (isPublishingPage)
            {
                var publishing = row?.Publishing ?? (_tabs.PublishingRows.Count > 0 ? _tabs.PublishingRows[0] : null);
                _ = PresentText(_detailsTitle, UsesPublishingPanel ? "配信" : "OBS 接続設定");
                _ = PresentText(_detailsDescription, publishing is null ? "配信状態を確認できません。" :
                    publishing.StateText + (string.IsNullOrEmpty(publishing.DiagnosticText) ? string.Empty : " · " + publishing.DiagnosticText));
                _ = PresentText(_detailsMetadata, NovaObsPublishingConnectionPresentation.Instructions);
                _ = PresentText(_detailsIdentity, publishing is null ? string.Empty :
                    "エラー分類 / 終了理由 · " + GenesisGuiNextPublishingPresentation.ErrorText(publishing) +
                    (string.IsNullOrEmpty(_tabs.PublishingCommands.DiagnosticText) ? string.Empty :
                        " · " + _tabs.PublishingCommands.DiagnosticText) +
                    (string.IsNullOrEmpty(_tabs.PublishingConnectionFeedback) ? string.Empty : " · " + _tabs.PublishingConnectionFeedback));
                _ = PresentText(_detailsRecordingStatus, string.Empty);
                _ = PresentInvalidation(_detailsPane, 0, true);
                return;
            }
            if (isLogPage)
            {
                _ = PresentText(_detailsTitle, "ログ");
                _ = PresentText(_detailsDescription, _tabs.LogRows.Count == 0
                    ? GenesisGuiNextLogProjection.EmptyText : row?.Log?.Content ?? "ログを選択してください。");
                _ = PresentText(_detailsMetadata, GenesisGuiNextLogProjection.ScopeText);
                _ = PresentText(_detailsIdentity, string.Empty);
                _ = PresentText(_detailsRecordingStatus, string.Empty);
                _ = PresentInvalidation(_detailsPane, 0, true);
                return;
            }

            if (isRecordingPage)
            {
                _ = PresentInvalidation(_activeRecordingsModeButton, 0, true);
                _ = PresentInvalidation(_recordingRulesModeButton, 0, true);
                _ = PresentInvalidation(_recordingLibraryModeButton, 0, true);
            }

            if (isHistoryPage)
            {
                _historyStatusPresentation = _tabs.RecordingHistoryStatus;
                _ = PresentText(_detailsRecordingStatus, _historyStatusPresentation.Text);
                var detailsText = _tabs.RecordingHistoryDetails(row?.RecordingHistory);
                if (!string.Equals(detailsText, _historyDetailsText, StringComparison.Ordinal))
                {
                    // Identical observations must not reset the read-only edit's scroll/caret.
                    _historyDetailsText = detailsText;
                    _ = PresentText(_recordingHistoryDetails, detailsText);
                }
                _ = PresentEnabled(_detailsCopyButton, false);
                _ = PresentEnabled(_detailsRecordButton, false);
                _ = PresentEnabled(_detailsStopRecordingButton, false);
                _ = PresentInvalidation(_detailsRecordingStatus, 0, true);
                _ = PresentInvalidation(_detailsPane, 0, true);
                return;
            }
            if (isLibraryPage)
            {
                var libraryDetails = row?.RecordingLibrary?.Details;
                _ = PresentText(_detailsTitle, libraryDetails?.Title ?? "録画ライブラリ");
                _ = PresentText(_detailsDescription, libraryDetails?.Description ?? string.Empty);
                _ = PresentText(_detailsMetadata, libraryDetails?.Metadata ?? string.Empty);
                _ = PresentText(_detailsIdentity, libraryDetails?.Identity ?? string.Empty);
                _ = PresentText(_detailsRecordingStatus, _tabs.RecordingLibraryStatusText);
                _ = PresentEnabled(_detailsCopyButton, false);
                _ = PresentEnabled(_detailsRecordButton, false);
                _ = PresentEnabled(_detailsStopRecordingButton, false);
                _ = PresentInvalidation(_detailsRecordingStatus, 0, true);
                _ = PresentInvalidation(_detailsPane, 0, true);
                return;
            }

            if (isRulesPage)
            {
                _ = PresentText(_detailsTitle, "録画ルール");
                _ = PresentText(_detailsDescription, _tabs.RecordingRulesStatusText);
                _ = PresentText(_detailsMetadata, string.Empty);
                if (row?.RecordingRule is { } ruleRow &&
                    _tabs.RecordingRulesSnapshot?.Rules?.Rules.FirstOrDefault(rule => rule.Id == ruleRow.RuleId) is { } storedRule &&
                    !GenesisRecordingRuleEditor.CanEdit(storedRule))
                    _ = PresentText(_detailsMetadata, GenesisRecordingRuleEditor.UnsupportedMessage);
                _ = PresentText(_detailsIdentity, GenesisRecordingRuleMutationController.PersistedOrderHelp);
                _ = PresentText(_detailsRecordingStatus, string.Empty);
                _ = PresentEnabled(_detailsCopyButton, false);
                _ = PresentEnabled(_detailsRecordButton, false);
                _ = PresentEnabled(_detailsStopRecordingButton, false);
                _ = PresentInvalidation(_detailsPane, 0, true);
                return;
            }

            if (ResolveRecordingCompletionMessage(
                    _tabs.SelectedTab,
                    _tabs.RecordingPageMode,
                    row,
                    _viewModel.AutomaticRecordingCommandMessage) is { } completionMessage)
            {
                _ = PresentText(
                    _detailsTitle,
                    GenesisGuiNextNativeTabProjection.GetRecordingEmptyStateText(
                        _tabs.ActiveRecordingsAvailable,
                        _tabs.ActiveRecordingCount));
                _ = PresentText(_detailsDescription, completionMessage);
                _ = PresentText(_detailsMetadata, string.Empty);
                _ = PresentText(_detailsIdentity, string.Empty);
                _ = PresentText(_detailsRecordingStatus, string.Empty);
                _ = PresentEnabled(_detailsCopyButton, false);
                _ = PresentEnabled(_detailsRecordButton, false);
                _ = PresentEnabled(_detailsStopRecordingButton, false);
                _ = PresentInvalidation(_detailsRecordingStatus, 0, true);
                _ = PresentInvalidation(_detailsPane, 0, true);
                return;
            }

            _ = PresentVisible(_detailsStopRecordingButton, _allowAutomaticShown ? ShowWindowHide : ShowWindowShowNoActivate);
            if (row?.Suppression is { } suppressed)
            {
                _ = PresentText(_detailsTitle, "自動録画を抑止中");
                _ = PresentText(_detailsDescription, suppressed.StatusText);
                _ = PresentText(_detailsMetadata, "同じルールによる自動録画を、この起動中だけ抑止します。手動録画や別ルールによる録画は対象外です。");
                _ = PresentText(_detailsIdentity, $"ChannelId: {suppressed.ChannelId.Value:N} / 停止時の対象ルール: {suppressed.RuleId:D}");
                _ = PresentText(_detailsRecordingStatus, "自動録画を抑止中");
                _ = PresentEnabled(_detailsCopyButton, false);
                _ = PresentEnabled(_detailsRecordButton, (_allowAutomaticOperation is null || _allowAutomaticOperation.IsCompleted) && _viewModel.CanAllowAutomatic(suppressed));
                _ = PresentEnabled(_detailsStopRecordingButton, false);
                _ = PresentInvalidation(_detailsPane, 0, true);
                return;
            }

            var details = GenesisGuiNextChannelDetailsProjection.FromRow(row);
            _ = PresentText(
                _detailsTitle,
                details.IsEmpty ? details.EmptyStateText : details.Name);
            _ = PresentText(
                _detailsDescription,
                string.IsNullOrWhiteSpace(details.Description)
                    ? string.Empty
                    : $"詳細: {details.Description}");
            _ = PresentText(_detailsMetadata, UsesChannelCard ? details.MetadataLines : details.MetadataText);
            _ = PresentText(_detailsIdentity, UsesChannelCard ? details.IdentityLines : details.IdentityText);
            _ = PresentEnabled(_detailsCopyButton, !details.IsEmpty);
            var recording = row?.ActiveRecording is { } activeRecording
                ? _viewModel.GetActiveRecordingPresentation(activeRecording)
                : _viewModel.GetManualRecordingPresentation(row?.InteractiveChannel);
            _ = PresentText(_detailsRecordingStatus, recording.DisplayText);
            _ = PresentEnabled(_detailsRecordButton, recording.CanRecord);
            _ = PresentEnabled(_detailsStopRecordingButton, recording.CanStop);
            _ = PresentInvalidation(_detailsRecordingStatus, 0, true);
            _ = PresentInvalidation(_detailsPane, 0, true);
        }

        private void MoveDetailsTextControls(bool recordingPage, bool libraryPage)
        {
            if (UsesPublishingPanel) return; // SynchronizePublishingGuide places these shared controls.
            if (UsesRecordingPanel)
            {
                PlaceRecordingPanel();
                return;
            }
            if (UsesChannelCard)
            {
                PlaceChannelCard();
                return;
            }
            if (IsModern && GetClientRect(_detailsPane, out var resetClient))
            {
                var actions = ResolveDetailsActions(LogicalWidth(resetClient.Right));
                _ = PresentBounds(_detailsStopRecordingButton, actions.StopLeft, 8, DetailsStopRecordingButtonWidth, DetailsCopyButtonHeight, true);
                _ = PresentBounds(_detailsCopyButton, actions.CopyLeft, 8, DetailsCopyButtonWidth, DetailsCopyButtonHeight, true);
                _ = PresentBounds(_relayStart, 12, RelayControlsTop, 84, 26, true);
                _ = PresentBounds(_relayStop, 102, RelayControlsTop, 84, 26, true);
            }
            var titleTop = recordingPage ? 40 : 8;
            var titleHeight = recordingPage ? 20 : 24;
            var descriptionTop = recordingPage ? 64 : 36;
            var descriptionHeight = recordingPage ? 28 : 32;
            var metadataTop = recordingPage ? 96 : 74;
            var metadataHeight = recordingPage ? 18 : 22;
            var identityTop = recordingPage ? 116 : 100;
            var identityHeight = recordingPage ? 16 : 18;
            var (statusTop, statusHeight) = RecordingStatusLayout(recordingPage, libraryPage);
            if (libraryPage)
            {
                // Reserve two wrapped lines for coverage even when a row is selected.
                titleHeight = 18;
                descriptionTop = 60;
                descriptionHeight = 18;
                metadataTop = 78;
                identityTop = 96;
            }
            var fullWidth = GetClientRect(_detailsPane, out var clientRectangle)
                ? Math.Max(1, LogicalWidth(clientRectangle.Right - clientRectangle.Left) - 24)
                : Math.Max(1, _detailsTextWidth);
            _ = PresentBounds(
                _detailsTitle,
                12,
                titleTop,
                libraryPage ? fullWidth : _detailsTextWidth,
                titleHeight,
                true);
            _ = PresentBounds(_detailsDescription, 12, descriptionTop, fullWidth, descriptionHeight, true);
            _ = PresentBounds(_detailsMetadata, 12, metadataTop, fullWidth, metadataHeight, true);
            _ = PresentBounds(_detailsIdentity, 12, identityTop, fullWidth, identityHeight, true);
            _ = PresentBounds(_detailsRecordingStatus, 12, statusTop, fullWidth, statusHeight, true);
            _ = PresentBounds(_recordingHistoryDetails, 12, HistoryDetailsLayout.Top, fullWidth, HistoryDetailsLayout.Height, true);
        }

        private bool IsRulesPageActive() =>
            _tabs.SelectedTab == GenesisGuiNextTab.Recording &&
            _tabs.RecordingPageMode == GenesisGuiNextRecordingPageMode.Rules;

        private async Task ObserveRecordingPageModeAsync(
            GenesisGuiNextRecordingPageMode mode)
        {
            try
            {
                await _tabs.SelectRecordingPageModeAsync(mode).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to switch recording page mode.", exception);
            }
        }

        private GenesisManualRecordingPresentation GetCurrentRecordingPresentation()
        {
            var selectedRow = SendMessageW(
                _listView,
                ListViewMessageGetNextItem,
                nuint.MaxValue,
                (nint)ListViewNextItemSelected).ToInt32();
            var row = GenesisChannelListRenderedChannels.FindRow(_listView, selectedRow);
            return row?.ActiveRecording is { } activeRecording
                ? _viewModel.GetActiveRecordingPresentation(activeRecording)
                : _viewModel.GetManualRecordingPresentation(row?.InteractiveChannel);
        }

        private GenesisChannelDetailsPresentation GetCurrentDetailsPresentation()
        {
            var selectedRow = SendMessageW(
                _listView,
                ListViewMessageGetNextItem,
                nuint.MaxValue,
                (nint)ListViewNextItemSelected).ToInt32();
            var row = GenesisChannelListRenderedChannels.FindRow(_listView, selectedRow);
            return GenesisGuiNextChannelDetailsProjection.FromRow(row);
        }

        private void SynchronizePlaybackButtonPresentation()
        {
            if (Volatile.Read(ref _detached) != 0)
            {
                return;
            }

            var selectedRowIndex = SendMessageW(
                _listView,
                ListViewMessageGetNextItem,
                nuint.MaxValue,
                (nint)ListViewNextItemSelected).ToInt32();
            var selectedRow = GenesisChannelListRenderedChannels.FindRow(
                _listView,
                selectedRowIndex);
            var presentation = GenesisGuiNextPlaybackCommandPresentation.Resolve(
                _viewModel,
                _tabs.SelectedTab,
                selectedRow,
                _tabs.RecordingPageMode);
            _ = EnableWindow(
                _playButton,
                presentation.IsPlayEnabled && Volatile.Read(ref _playCommandInFlight) == 0);
            _ = EnableWindow(
                _stopButton,
                presentation.IsStopEnabled && Volatile.Read(ref _stopCommandInFlight) == 0);
        }

        private void StartSelectedChannelPlayback()
        {
            if (Interlocked.CompareExchange(ref _playCommandInFlight, 1, 0) != 0)
            {
                return;
            }

            var selectedRowIndex = SendMessageW(
                _listView,
                ListViewMessageGetNextItem,
                nuint.MaxValue,
                (nint)ListViewNextItemSelected).ToInt32();
            var selectedRow = GenesisChannelListRenderedChannels.FindRow(
                _listView,
                selectedRowIndex);
            var presentation = GenesisGuiNextPlaybackCommandPresentation.Resolve(
                _viewModel,
                _tabs.SelectedTab,
                selectedRow,
                _tabs.RecordingPageMode);
            if (!presentation.IsPlayEnabled || selectedRow is null)
            {
                Interlocked.Exchange(ref _playCommandInFlight, 0);
                SynchronizePlaybackButtonPresentation();
                return;
            }

            SynchronizePlaybackButtonPresentation();
            _ = ObserveSelectedChannelPlaybackAsync(selectedRow);
        }

        private async Task ObserveSelectedChannelPlaybackAsync(
            GenesisGuiNextNativeTabRow selectedRow)
        {
            try
            {
                _ = await GenesisGuiNextPlaybackCommandDispatch
                    .ActivateSelectedChannelAsync(
                        _viewModel,
                        _interaction,
                        _tabs.SelectedTab,
                        selectedRow)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to activate selected channel playback.", exception);
            }
            finally
            {
                Interlocked.Exchange(ref _playCommandInFlight, 0);
                _ = PostMessageW(
                    _sidebar,
                    WindowMessageSynchronizeSelection,
                    0,
                    0);
            }
        }

        private void StartSelectedChannelRecording()
        {
            var selectedRowIndex = SendMessageW(
                _listView,
                ListViewMessageGetNextItem,
                nuint.MaxValue,
                (nint)ListViewNextItemSelected).ToInt32();
            var selectedRow = GenesisChannelListRenderedChannels.FindRow(
                _listView,
                selectedRowIndex);
            if (selectedRow?.Suppression is not null)
            {
                if (_allowAutomaticOperation is null || _allowAutomaticOperation.IsCompleted)
                {
                    _allowAutomaticOperation = ObserveAllowAutomaticAsync(selectedRow);
                    SynchronizeDetailsPresentation(selectedRow);
                }
                return;
            }
            var presentation = GenesisGuiNextRecordingCommandPresentation.Resolve(
                _viewModel,
                _tabs.SelectedTab,
                selectedRow);
            if (!presentation.IsRecordEnabled)
            {
                SynchronizeDetailsPresentation(selectedRow);
                return;
            }

            _ = ObserveSelectedChannelRecordingAsync(selectedRow!, stop: false);
        }

        private void StopSelectedChannelRecording()
        {
            var selectedRowIndex = SendMessageW(
                _listView,
                ListViewMessageGetNextItem,
                nuint.MaxValue,
                (nint)ListViewNextItemSelected).ToInt32();
            var selectedRow = GenesisChannelListRenderedChannels.FindRow(
                _listView,
                selectedRowIndex);
            if (selectedRow?.ActiveRecording is { } activeRecording)
            {
                if (_viewModel.GetActiveRecordingPresentation(activeRecording).CanStop)
                {
                    if (activeRecording.Origin == PeerCast.Nova.Core.Recording.RecordingSessionOrigin.AutomaticRule)
                    {
                        _automaticRowStopOperations.RemoveAll(t => t.IsCompleted);
                        if (_automaticRowStopOperations.Count < GenesisAutomaticRecordingSuppression.Capacity)
                            _automaticRowStopOperations.Add(ObserveRecordingRowStopAsync(selectedRow));
                    }
                    else
                    {
                        _ = ObserveRecordingRowStopAsync(selectedRow);
                    }
                }

                return;
            }

            var presentation = GenesisGuiNextRecordingCommandPresentation.Resolve(
                _viewModel,
                _tabs.SelectedTab,
                selectedRow);
            if (!presentation.IsStopRecordingEnabled)
            {
                SynchronizeDetailsPresentation(selectedRow);
                return;
            }

            _ = ObserveSelectedChannelRecordingAsync(selectedRow!, stop: true);
        }

        private async Task ObserveSelectedChannelRecordingAsync(
            GenesisGuiNextNativeTabRow selectedRow,
            bool stop)
        {
            try
            {
                if (stop && selectedRow.ActiveRecording is { } activeRecording)
                {
                    if (activeRecording.Origin ==
                        PeerCast.Nova.Core.Recording.RecordingSessionOrigin.Manual)
                    {
                        await _viewModel.StopManualRecordingAsync(
                                activeRecording.ChannelId,
                                activeRecording.SessionId)
                            .ConfigureAwait(false);
                    }
                }
                else if (stop)
                {
                    _ = await GenesisGuiNextRecordingCommandDispatch
                        .StopSelectedAsync(
                            _viewModel,
                            _tabs.SelectedTab,
                            selectedRow)
                        .ConfigureAwait(false);
                }
                else
                {
                    _ = await GenesisGuiNextRecordingCommandDispatch
                        .StartSelectedAsync(
                            _viewModel,
                            _tabs.SelectedTab,
                            selectedRow)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to execute manual recording command.", exception);
            }
            finally
            {
                _ = PostMessageW(
                    _detailsPane,
                    WindowMessageSynchronizeSelection,
                    0,
                    0);
            }
        }

        private async Task ObserveRecordingRowStopAsync(
            GenesisGuiNextNativeTabRow selectedRow)
        {
            try
            {
                _ = await GenesisGuiNextRecordingCommandDispatch
                    .StopRecordingRowAsync(_viewModel, selectedRow)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to stop active recording row.", exception);
            }
            finally
            {
                if (Volatile.Read(ref _detached) == 0) _ = PostMessageW(
                    _detailsPane,
                    WindowMessageSynchronizeSelection,
                    0,
                    0);
            }
        }

        private bool _allowAutomaticShown;
        private Task? _allowAutomaticOperation;
        private readonly List<Task> _automaticRowStopOperations = [];

        public Task DrainAutomaticCommandsAsync() => Task.WhenAll(_automaticRowStopOperations.Concat(
            _allowAutomaticOperation is { } allow ? [allow] : Array.Empty<Task>()));

        private async Task ObserveAllowAutomaticAsync(GenesisGuiNextNativeTabRow row)
        {
            try { await GenesisGuiNextRecordingCommandDispatch.AllowRecordingRowAsync(_viewModel, row).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to re-allow automatic recording.", exception);
            }
            finally
            {
                if (Volatile.Read(ref _detached) == 0)
                    _ = PostMessageW(_detailsPane, WindowMessageSynchronizeSelection, 0, 0);
            }
        }

        private async Task RefreshActiveRecordingsAsync()
        {
            try
            {
                await _viewModel.RefreshActiveRecordingsAsync()
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to refresh active recordings.", exception);
            }
        }

        private void StartStopPlayback()
        {
            if (_tabs.IsRecordingHistoryPage || !_viewModel.CanStop ||
                Interlocked.CompareExchange(ref _stopCommandInFlight, 1, 0) != 0)
            {
                SynchronizePlaybackButtonPresentation();
                return;
            }

            SynchronizePlaybackButtonPresentation();
            _ = ObserveStopPlaybackAsync();
        }

        private async Task ObserveStopPlaybackAsync()
        {
            try
            {
                await GenesisGuiNextPlaybackCommandDispatch
                    .StopAsync(_viewModel, _interaction)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to stop playback.", exception);
            }
            finally
            {
                Interlocked.Exchange(ref _stopCommandInFlight, 0);
                _ = PostMessageW(
                    _sidebar,
                    WindowMessageSynchronizeSelection,
                    0,
                    0);
            }
        }

        public void PaintSelectedRowAccent()
        {
            if (Volatile.Read(ref _detached) != 0)
            {
                return;
            }

            var selectedRow = SendMessageW(
                _listView,
                ListViewMessageGetNextItem,
                nuint.MaxValue,
                (nint)ListViewNextItemSelected);
            if (selectedRow < 0)
            {
                return;
            }

            var rectanglePointer = Marshal.AllocHGlobal(Marshal.SizeOf<Rectangle>());
            try
            {
                Marshal.StructureToPtr(new Rectangle(), rectanglePointer, fDeleteOld: false);
                var rectangleRead = SendMessageW(
                    _listView,
                    ListViewMessageGetItemRectangle,
                    (nuint)selectedRow,
                    rectanglePointer);
                if (rectangleRead == 0)
                {
                    return;
                }

                var itemRectangle = Marshal.PtrToStructure<Rectangle>(rectanglePointer);
                if (!TryResolveSelectedRowAccent(
                    selected: true,
                    itemRectangle.Left,
                    itemRectangle.Top,
                    itemRectangle.Right,
                    itemRectangle.Bottom,
                    out var accent))
                {
                    return;
                }

                var deviceContext = GetDC(_listView);
                if (deviceContext != 0)
                {
                    try
                    {
                        FillRectangle(
                            deviceContext,
                            new Rectangle
                            {
                                Left = accent.Left,
                                Top = accent.Top,
                                Right = accent.Right,
                                Bottom = accent.Bottom,
                            },
                            IsModern ? GenesisGuiNextDesign.Accent : AccentColor);
                    }
                    finally
                    {
                        _ = ReleaseDC(_listView, deviceContext);
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(rectanglePointer);
            }
        }

        private void OnTabsPropertyChanged(
            object? sender,
            PropertyChangedEventArgs eventArgs)
        {
            _ = sender;
            if (Volatile.Read(ref _detached) == 0)
            {
                if (eventArgs.PropertyName == nameof(GenesisGuiNextTabsViewModel.SelectedTab))
                {
                    CloseIntegratedSettings();
                    _utilityNavigationSelection.OnSelectedTabChanged();
                    _ = InvalidateRect(_sidebar, 0, true);
                }

                _ = PostMessageW(
                    _sidebar,
                    WindowMessageSynchronizeSelection,
                    0,
                    0);
                _ = PostMessageW(
                    _statusBar,
                    WindowMessageSynchronizeSelection,
                    0,
                    0);
                _ = PostMessageW(
                    _detailsPane,
                    WindowMessageSynchronizeSelection,
                    0,
                    0);
            }
        }

        private void OnViewModelPropertyChanged(
            object? sender,
            PropertyChangedEventArgs eventArgs)
        {
            _ = sender;
            if (Volatile.Read(ref _detached) == 0 &&
                eventArgs.PropertyName is nameof(GenesisMainViewModel.RecordingPresentationRevision) or
                    nameof(GenesisMainViewModel.RelaySnapshot))
            {
                _ = PostMessageW(
                    _detailsPane,
                    WindowMessageSynchronizeSelection,
                    0,
                    0);
                return;
            }

            if (Volatile.Read(ref _detached) == 0 &&
                eventArgs.PropertyName is nameof(GenesisMainViewModel.CanPlay) or
                    nameof(GenesisMainViewModel.CanStop) or
                    nameof(GenesisMainViewModel.PlaybackState))
            {
                _ = PostMessageW(
                    _sidebar,
                    WindowMessageSynchronizeSelection,
                    0,
                    0);
            }
        }
    }

    private static void FillRectangle(
        nint deviceContext,
        Rectangle rectangle,
        int color)
    {
        var brush = CreateSolidBrush((uint)color);
        if (brush != 0)
        {
            _ = FillRect(deviceContext, ref rectangle, brush);
            _ = DeleteObject(brush);
        }
    }
}
