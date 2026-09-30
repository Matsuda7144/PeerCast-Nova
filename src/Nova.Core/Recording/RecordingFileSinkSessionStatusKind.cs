namespace PeerCast.Nova.Core.Recording;

public enum RecordingFileSinkSessionStatusKind
{
    Starting,
    Writing,
    Completed,
    NoDecodableVideoStart,
    Faulted,
}
