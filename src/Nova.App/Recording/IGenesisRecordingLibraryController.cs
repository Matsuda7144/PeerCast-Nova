namespace PeerCast.Nova.App;

/// <summary>Read-only application boundary; no root, store, or file action ownership.</summary>
internal interface IGenesisRecordingLibraryController
{
    GenesisRecordingLibrarySnapshot GetSnapshot();
    Task<GenesisRecordingLibraryRefreshResult> RefreshAsync(CancellationToken cancellationToken);
}
