namespace PeerCast.Nova.App;

/// <summary>Reference identity for the sources used to render a native tab.</summary>
internal readonly record struct GenesisGuiNextNativeRowRebuildIdentity
{
    private GenesisGuiNextNativeRowRebuildIdentity(
        object sourceRows,
        GenesisGuiNextTab tab,
        GenesisGuiNextRecordingPageMode recordingPageMode,
        object? suppressionRows)
    {
        SourceRows = sourceRows;
        Tab = tab;
        RecordingPageMode = recordingPageMode;
        SuppressionRows = tab == GenesisGuiNextTab.Recording &&
            recordingPageMode == GenesisGuiNextRecordingPageMode.ActiveRecordings
                ? suppressionRows
                : null;
    }

    private object SourceRows { get; }
    private GenesisGuiNextTab Tab { get; }
    private GenesisGuiNextRecordingPageMode RecordingPageMode { get; }
    private object? SuppressionRows { get; }

    public static GenesisGuiNextNativeRowRebuildIdentity Create(
        object sourceRows,
        GenesisGuiNextTab tab,
        GenesisGuiNextRecordingPageMode recordingPageMode,
        object? suppressionRows) =>
        new(sourceRows, tab, recordingPageMode, suppressionRows);

    public bool RequiresRebuild(GenesisGuiNextNativeRowRebuildIdentity? rendered) =>
        rendered is not { } previous ||
        !ReferenceEquals(SourceRows, previous.SourceRows) ||
        Tab != previous.Tab ||
        RecordingPageMode != previous.RecordingPageMode ||
        !ReferenceEquals(SuppressionRows, previous.SuppressionRows);
}
