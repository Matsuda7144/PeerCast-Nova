namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Resolves the final file path for one concrete recording session.
/// </summary>
public interface IRecordingFilePathProvider
{
    string GetPath(
        RecordingSinkStartRequest request,
        RecordingSessionId sessionId);
}
