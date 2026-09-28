namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Bounded stage classification for a failed recording session.
/// </summary>
public enum RecordingFailureStage
{
    Unknown,
    SourceRead,
    FileInitialize,
    FileWrite,
    FileFinalize,
    StreamTransform,
}
