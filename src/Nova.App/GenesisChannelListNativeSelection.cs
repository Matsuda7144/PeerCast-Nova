using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

internal static class GenesisChannelListRenderedChannels
{
    private static readonly ConcurrentDictionary<
        nint,
        IReadOnlyList<GenesisGuiNextNativeTabRow>> RowsByListView = new();
    private static readonly ConcurrentDictionary<nint, string> SelectionKeysByListView = new();

    public static void Set(
        nint listView,
        IReadOnlyList<GenesisChannelItem> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (listView == 0)
        {
            return;
        }

        var rows = new GenesisGuiNextNativeTabRow[channels.Count];
        for (var index = 0; index < channels.Count; index++)
        {
            rows[index] = new(channels[index], channels[index]);
        }

        SetRows(listView, Array.AsReadOnly(rows));
    }

    private static readonly ConcurrentDictionary<nint, string> HistorySelectionKeys = new();
    private static readonly ConcurrentDictionary<nint, bool> HistoryModes = new();

    public static void SetRows(
        nint listView,
        IReadOnlyList<GenesisGuiNextNativeTabRow> rows, bool? historyMode = null) =>
        SetRows(
            listView,
            rows,
            static (window, row) => GenesisChannelListNativeSelection.RestoreRow(
                window,
                row,
                static () => { }), historyMode);

    internal static void SetRows(
        nint listView,
        IReadOnlyList<GenesisGuiNextNativeTabRow> rows,
        Func<nint, int, bool> restoreRow, bool? historyMode = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(restoreRow);
        if (listView == 0)
        {
            return;
        }

        var snapshot = Array.AsReadOnly(rows.ToArray());
        HistoryModes[listView] = historyMode ?? rows.Any(x => x.RecordingHistory is not null);
        RowsByListView[listView] = snapshot;
        var restoreIndex = FindAnchoredRow(listView, snapshot);
        if (restoreIndex >= 0)
        {
            _ = restoreRow(listView, restoreIndex);
        }
    }

    public static void CaptureSelectedRow(nint listView, int row)
    {
        if (listView == 0)
        {
            return;
        }

        if (row < 0 ||
            !RowsByListView.TryGetValue(listView, out var rows) ||
            row >= rows.Count)
        {
            _ = SelectionMap(listView).TryRemove(listView, out _);
            return;
        }

        SelectionMap(listView)[listView] = GetSelectionKey(rows[row]);
    }

    public static void AnchorRecordingRule(nint listView, Guid ruleId) =>
        SelectionKeysByListView[listView] = $"recording-rule:{ruleId:N}";

    internal static int FindAnchoredRow(
        nint listView,
        IReadOnlyList<GenesisGuiNextNativeTabRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (listView == 0 ||
            !SelectionMap(listView).TryGetValue(listView, out var stableKey))
        {
            return -1;
        }

        var found = -1;
        for (var index = 0; index < rows.Count; index++)
        {
            var candidateKey = GetSelectionKey(rows[index]);
            if (rows[index].RecordingLibrary is not null
                    ? GenesisRecordingLibraryKnownRootRegistry.PathComparer.Equals(candidateKey, stableKey)
                    : string.Equals(candidateKey, stableKey, StringComparison.Ordinal))
            {
                if (!HistoryModes.TryGetValue(listView, out var historyMode) || !historyMode) return index;
                if (found >= 0)
                {
                    _ = SelectionMap(listView).TryRemove(listView, out _);
                    return -1;
                }
                found = index;
            }
        }

        if (found < 0) _ = SelectionMap(listView).TryRemove(listView, out _);
        return found;
    }

    private static ConcurrentDictionary<nint, string> SelectionMap(nint listView) =>
        HistoryModes.TryGetValue(listView, out var history) && history ? HistorySelectionKeys : SelectionKeysByListView;

    private static string GetSelectionKey(GenesisGuiNextNativeTabRow row)
    {
        if (row.Publishing is { } publishing) return $"publishing:{publishing.SessionId:N}";
        if (row.Log is { } log) return $"log:{log.Timestamp:O}:{log.Kind}";
        if (row.RecordingHistory is { } history) return $"recording-history:{history.SessionId:N}";
        if (row.RecordingLibrary is { } library)
        {
            return "recording-library:" + library.Path;
        }
        if (row.Suppression is { } suppressed)
        {
            return $"recording-suppression:{suppressed.ChannelId.Value:N}:{suppressed.RuleId:N}";
        }
        if (row.RecordingRule is { } rule)
        {
            return $"recording-rule:{rule.RuleId:N}";
        }

        if (row.ActiveRecording is { } recording)
        {
            return $"recording:{recording.ChannelId.Value:N}:" +
                $"{recording.SessionId.Value:N}:{(int)recording.Origin}";
        }

        return row.StableSelectionKey ??
            GenesisChannelIdentity.GetStableKey(row.DisplayChannel);
    }

    public static GenesisGuiNextNativeTabRow? FindRow(nint listView, int row)
    {
        if (listView == 0 ||
            row < 0 ||
            !RowsByListView.TryGetValue(listView, out var rows) ||
            row >= rows.Count)
        {
            return null;
        }

        return rows[row];
    }

    public static GenesisChannelItem? Find(nint listView, int row) =>
        FindRow(listView, row)?.InteractiveChannel;

    public static void Remove(nint listView)
    {
        if (listView != 0)
        {
            _ = RowsByListView.TryRemove(listView, out _);
            _ = SelectionKeysByListView.TryRemove(listView, out _);
            _ = HistorySelectionKeys.TryRemove(listView, out _);
            _ = HistoryModes.TryRemove(listView, out _);
        }
    }
}

internal static class GenesisChannelListNativeSelection
{
    private const uint ListViewMessageFirst = 0x1000;
    private const uint ListViewMessageEnsureVisible = ListViewMessageFirst + 19;
    private const uint ListViewMessageSetItemState = ListViewMessageFirst + 43;
    private const uint ListViewItemStateFocused = 0x0001;
    private const uint ListViewItemStateSelected = 0x0002;

    public static bool SelectRow(nint listView, int row) =>
        SelectRow(listView, row, SetSelectedState, EnsureVisible);

    internal static bool RestoreRow(
        nint listView,
        int row,
        Action clearManagedSelection) =>
        RestoreRow(
            listView,
            row,
            SetSelectedState,
            EnsureVisible,
            clearManagedSelection);

    internal static bool RestoreRow(
        nint listView,
        int row,
        Func<nint, int, bool> selectRow,
        Func<nint, int, bool> ensureVisible,
        Action clearManagedSelection)
    {
        ArgumentNullException.ThrowIfNull(selectRow);
        ArgumentNullException.ThrowIfNull(ensureVisible);
        ArgumentNullException.ThrowIfNull(clearManagedSelection);
        if (listView == 0 || row < 0)
        {
            return false;
        }

        if (!selectRow(listView, row))
        {
            clearManagedSelection();
            return false;
        }

        return ensureVisible(listView, row);
    }

    internal static bool SelectRowWithoutEnsureVisible(
        nint listView,
        int row,
        Func<int, bool> selectManagedRow) =>
        SelectRowWithoutEnsureVisible(
            listView,
            row,
            SetSelectedState,
            ClearSelectedState,
            selectManagedRow);

    internal static bool SelectRowWithoutEnsureVisible(
        nint listView,
        int row,
        Func<nint, int, bool> selectRow,
        Func<int, bool> selectManagedRow) =>
        SelectRowWithoutEnsureVisible(
            listView,
            row,
            selectRow,
            ClearSelectedState,
            selectManagedRow);

    internal static bool SelectRowWithoutEnsureVisible(
        nint listView,
        int row,
        Func<nint, int, bool> selectRow,
        Func<nint, int, bool> clearRow,
        Func<int, bool> selectManagedRow)
    {
        ArgumentNullException.ThrowIfNull(selectRow);
        ArgumentNullException.ThrowIfNull(clearRow);
        ArgumentNullException.ThrowIfNull(selectManagedRow);
        if (listView == 0 || row < 0)
        {
            return false;
        }

        if (!selectRow(listView, row))
        {
            return false;
        }

        if (selectManagedRow(row))
        {
            return true;
        }

        _ = clearRow(listView, row);
        return false;
    }

    internal static bool SynchronizeManagedSelection(
        int selectedIndex,
        bool selectionChangesSuppressed,
        Func<int, bool> selectManagedRow,
        Action clearManagedSelection)
    {
        ArgumentNullException.ThrowIfNull(selectManagedRow);
        ArgumentNullException.ThrowIfNull(clearManagedSelection);
        if (selectionChangesSuppressed)
        {
            return false;
        }

        if (selectedIndex < 0)
        {
            clearManagedSelection();
            return true;
        }

        return selectManagedRow(selectedIndex);
    }

    internal static bool SelectRow(
        nint listView,
        int row,
        Func<nint, int, bool> selectRow,
        Func<nint, int, bool> ensureVisible)
    {
        ArgumentNullException.ThrowIfNull(selectRow);
        ArgumentNullException.ThrowIfNull(ensureVisible);
        if (listView == 0 || row < 0)
        {
            return false;
        }

        return selectRow(listView, row) && ensureVisible(listView, row);
    }

    private static bool SetSelectedState(nint listView, int row)
    {
        var selectedState = ListViewItemStateFocused | ListViewItemStateSelected;
        var item = new ListViewItem
        {
            State = selectedState,
            StateMask = selectedState,
        };
        return SendMessageItem(
            listView,
            ListViewMessageSetItemState,
            (nuint)row,
            ref item) != 0;
    }

    private static bool ClearSelectedState(nint listView, int row)
    {
        var selectedState = ListViewItemStateFocused | ListViewItemStateSelected;
        var item = new ListViewItem
        {
            State = 0,
            StateMask = selectedState,
        };
        return SendMessageItem(
            listView,
            ListViewMessageSetItemState,
            (nuint)row,
            ref item) != 0;
    }

    private static bool EnsureVisible(nint listView, int row) =>
        SendMessageW(
            listView,
            ListViewMessageEnsureVisible,
            (nuint)row,
            0) != 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ListViewItem
    {
        public uint Mask;
        public int Item;
        public int SubItem;
        public uint State;
        public uint StateMask;
        public nint Text;
        public int MaximumTextCharacters;
        public int Image;
        public nint Parameter;
        public int Indent;
        public int GroupId;
        public uint Columns;
        public nint ColumnIndices;
        public nint ColumnFormats;
        public int Group;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageW(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern int SendMessageItem(
        nint window,
        uint message,
        nuint wParam,
        ref ListViewItem item);
}
