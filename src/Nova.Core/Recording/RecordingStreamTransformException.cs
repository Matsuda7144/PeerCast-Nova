namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Identifies a malformed or unsupported input rejected by a recording
/// stream transform.
/// </summary>
internal sealed class RecordingStreamTransformException : IOException
{
    public RecordingStreamTransformException(string message)
        : this(message, RecordingTransformThrowSite.Unknown)
    {
    }

    internal RecordingStreamTransformException(string message, RecordingTransformThrowSite site)
        : base(message)
    {
        Diagnostic = RecordingTransformFailure.FromSite(site);
    }

    internal RecordingTransformFailure Diagnostic { get; }
}
