using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "Performance",
    "CA1822:Mark members as static",
    Justification = "The operation-observation helper remains instance-scoped with the parser accounting boundary.",
    Scope = "member",
    Target = "~M:PeerCast.Nova.Playback.PlaybackBoundedFlvParser.GetObservedByteCount(System.Int32,System.Int32)")]
