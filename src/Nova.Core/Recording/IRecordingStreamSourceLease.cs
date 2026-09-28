namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// One recording-scoped reader lease from an existing channel owner.
/// </summary>
/// <remarks>
/// The returned reader remains valid until this lease is disposed. Callers must
/// not dispose <see cref="ReadStream"/> directly; the lease decides whether it
/// is closing a private reader or only releasing a view of shared channel
/// ownership.
/// </remarks>
public interface IRecordingStreamSourceLease : IAsyncDisposable
{
    Stream ReadStream { get; }
}
