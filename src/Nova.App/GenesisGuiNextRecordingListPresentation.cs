namespace PeerCast.Nova.App;

/// <summary>Mode-specific native schema. All transformations remain I/O-free.</summary>
internal static class GenesisGuiNextRecordingListPresentation
{
    private static readonly IReadOnlyList<GenesisNativeListColumnPresentation> ChannelColumns =
        Array.AsReadOnly(GenesisChannelListPresentation.Columns.Select(static column =>
            new GenesisNativeListColumnPresentation(column.Header, column.PreferredWidth, column.Alignment)).ToArray());

    public static IReadOnlyList<GenesisNativeListColumnPresentation> Columns(
        GenesisGuiNextTab tab, GenesisGuiNextRecordingPageMode mode) =>
        tab == GenesisGuiNextTab.Publishing ? GenesisGuiNextPublishingPresentation.Columns :
        tab == GenesisGuiNextTab.Logs ? GenesisGuiNextLogProjection.Columns
        : tab != GenesisGuiNextTab.Recording ? ChannelColumns : mode switch
        {
            GenesisGuiNextRecordingPageMode.History => GenesisRecordingHistoryListPresentation.Columns,
            GenesisGuiNextRecordingPageMode.Library => GenesisRecordingLibraryListPresentation.Columns,
            GenesisGuiNextRecordingPageMode.Rules => GenesisRecordingRulesListPresentation.Columns,
            _ => ChannelColumns,
        };

    public static string[] Values(GenesisGuiNextNativeTabRow row) =>
        row.Publishing is { } publishing ? GenesisGuiNextPublishingPresentation.Values(publishing) :
        row.Log is { } log ? GenesisGuiNextLogProjection.Values(log)
        : row.RecordingHistory is { } history ? GenesisRecordingHistoryListPresentation.CreateRowValues(history)
        : row.RecordingLibrary is { } library ? GenesisRecordingLibraryListPresentation.CreateRowValues(library)
        : row.RecordingRule is { } rule ? GenesisRecordingRulesListPresentation.CreateRowValues(rule)
        : GenesisChannelListPresentation.CreateRowValues(row.DisplayChannel);

    // Remove rows before changing their schema. On failure, erase partial columns
    // and leave an inert empty list rather than pairing old payloads with new cells.
    public static bool TryReplaceColumns(Func<bool> clearRows, Func<bool> replaceColumns, Action clearColumns)
    {
        if (!clearRows()) return false;
        if (replaceColumns()) return true;
        clearColumns();
        return false;
    }
}
