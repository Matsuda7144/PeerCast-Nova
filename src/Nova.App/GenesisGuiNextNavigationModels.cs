namespace PeerCast.Nova.App;

internal static class GenesisGuiNextNavigationPresentation
{
    public const int SearchNavigationItemIndex = 5;

    private static readonly string[] Labels =
    [
        "チャンネル一覧",
        "新着",
        "お気に入り",
        "視聴中",
        "録画",
        "検索",
        "配信",
        "設定",
        "ログ",
        "について",
    ];

    public static IReadOnlyList<string> Items { get; } =
        Array.AsReadOnly(Labels);

    internal static IReadOnlyList<string> ItemsFor(bool modern) => modern ? ModernItems : Items;
    private static readonly IReadOnlyList<string> ModernItems = Array.AsReadOnly<string>([.. Labels, "リレーツリー"]);
    internal static int Row(int index, bool modern) => !modern ? index : index == 10 ? 4 : index >= 4 ? index + 1 : index;
    internal static int RowHeight(bool modern) => modern ? 32 : 36;

    public static bool IsImplemented(int itemIndex) =>
        (itemIndex >= (int)GenesisGuiNextTab.Channels &&
            itemIndex <= (int)GenesisGuiNextTab.Recording) || itemIndex is (int)GenesisGuiNextTab.Logs or (int)GenesisGuiNextTab.Publishing or (int)GenesisGuiNextTab.RelayTree ||
        TryResolvePrimaryCommand(itemIndex, out _);

    public static bool TryResolveExistingView(
        int itemIndex,
        out GenesisGuiNextTab selectedTab)
    {
        if ((itemIndex >= (int)GenesisGuiNextTab.Channels &&
            itemIndex <= (int)GenesisGuiNextTab.Recording) || itemIndex is (int)GenesisGuiNextTab.Logs or (int)GenesisGuiNextTab.Publishing or (int)GenesisGuiNextTab.RelayTree)
        {
            selectedTab = (GenesisGuiNextTab)itemIndex;
            return true;
        }

        selectedTab = default;
        return false;
    }

    public static bool TryResolvePrimaryCommand(
        int itemIndex,
        out GenesisGuiNextPrimaryCommand command)
    {
        command = itemIndex switch
        {
            SearchNavigationItemIndex => GenesisGuiNextPrimaryCommand.Search,
            7 => GenesisGuiNextPrimaryCommand.Settings,
            9 => GenesisGuiNextPrimaryCommand.About,
            _ => GenesisGuiNextPrimaryCommand.None,
        };
        return command != GenesisGuiNextPrimaryCommand.None;
    }
}

internal enum GenesisGuiNextPrimaryCommand
{
    None,
    Search,
    Settings,
    About,
}

internal sealed class GenesisGuiNextUtilityNavigationSelection
{
    public int SelectedItemIndex { get; private set; } = -1;

    public void SelectUtility(int itemIndex) =>
        SelectedItemIndex = itemIndex;

    public void Clear() =>
        SelectedItemIndex = -1;

    public void OnSelectedTabChanged() => Clear();

    public bool IsSelected(int itemIndex, GenesisGuiNextTab selectedTab) =>
        SelectedItemIndex >= 0
            ? itemIndex == SelectedItemIndex
            : itemIndex == (int)selectedTab;

    public void SelectView(
        GenesisGuiNextTabsViewModel tabs,
        GenesisGuiNextTab selectedTab)
    {
        ArgumentNullException.ThrowIfNull(tabs);

        Clear();
        tabs.SelectedTab = selectedTab;
    }

    public bool ActivateSearch(
        GenesisGuiNextTabsViewModel tabs,
        Func<bool> focusSearchEdit)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(focusSearchEdit);

        // SelectedTab changes synchronously notify the shell, which clears
        // utility selection. Commit Search afterward so it remains selected.
        tabs.SelectedTab = GenesisGuiNextTab.Channels;
        SelectUtility(GenesisGuiNextNavigationPresentation.SearchNavigationItemIndex);
        return focusSearchEdit();
    }
}

internal sealed record GenesisGuiNextFavoriteButtonPresentation(
    string Label,
    bool IsEnabled);

internal static class GenesisGuiNextFavoriteButtonPresentationResolver
{
    private const string AddLabel = "Add Favorite";
    private const string RemoveLabel = "Remove Favorite";

    public static GenesisGuiNextFavoriteButtonPresentation Resolve(
        GenesisGuiNextTabsViewModel tabs,
        GenesisGuiNextNativeTabRow? row, bool modern = false)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        var add = modern ? "お気に入り追加" : AddLabel;
        var remove = modern ? "お気に入り削除" : RemoveLabel;
        if (row is null || tabs.SelectedTab is GenesisGuiNextTab.Playing or
            GenesisGuiNextTab.Recording or GenesisGuiNextTab.Logs or GenesisGuiNextTab.Publishing)
        {
            return new(add, IsEnabled: false);
        }

        if (tabs.IsFavorite(row.DisplayChannel))
        {
            return new(remove, IsEnabled: true);
        }

        return new(add, row.InteractiveChannel is not null);
    }
}

internal readonly record struct GenesisGuiNextListViewAccentRectangle(
    int Left,
    int Top,
    int Right,
    int Bottom);
