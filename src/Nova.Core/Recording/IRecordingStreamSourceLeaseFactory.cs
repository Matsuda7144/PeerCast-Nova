namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Acquires a recording-scoped reader from an already-owned channel stream.
/// </summary>
/// <remarks>
/// Implementations do not transfer ownership of the channel owner or its
/// shared stream. Callers own and must dispose each returned lease.
/// </remarks>
public interface IRecordingStreamSourceLeaseFactory
{
    ValueTask<IRecordingStreamSourceLease?> AcquireAsync(
        RecordingSinkStartRequest request,
        CancellationToken cancellationToken);
}
