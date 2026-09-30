namespace PeerCast.Nova.Core.Recording;

public enum RecordingSinkSessionLifecycleKind
{
    Active,
    Completed,
    NoDecodableVideoStart,
    Faulted,
}
