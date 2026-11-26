namespace PeerCast.Nova.App;

/// <summary>Independent memory queries only. No persistence, scan, or command ownership.</summary>
internal interface IGenesisRecordingHistoryController
{
    GenesisRecordingHistorySnapshot GetRecordingHistorySnapshot();
    GenesisRecordingHistoryDurabilitySnapshot? GetRecordingHistoryDurabilitySnapshot();
    GenesisRecordingLibrarySnapshot GetRecordingLibrarySnapshot();
}
