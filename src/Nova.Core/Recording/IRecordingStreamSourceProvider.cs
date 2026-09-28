namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Supplies one readable media stream for a concrete recording session.
/// </summary>
public interface IRecordingStreamSourceProvider
{
    ValueTask<Stream> OpenReadAsync(
        RecordingSinkStartRequest request,
        CancellationToken cancellationToken);
}
