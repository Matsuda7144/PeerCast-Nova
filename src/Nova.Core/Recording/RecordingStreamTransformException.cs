namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Identifies a malformed or unsupported input rejected by a recording
/// stream transform.
/// </summary>
internal sealed class RecordingStreamTransformException : IOException
{
    public RecordingStreamTransformException(string message)
        : base(message)
    {
    }
}
