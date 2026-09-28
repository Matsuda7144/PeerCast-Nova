namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Replaceable recorder boundary. Implementations own media/file I/O.
/// </summary>
public interface IRecordingSink
{
    ValueTask<RecordingSinkStartResult> StartAsync(
        RecordingSinkStartRequest request,
        CancellationToken cancellationToken);

    ValueTask<RecordingSinkStopResult> StopAsync(
        RecordingSinkStopRequest request,
        CancellationToken cancellationToken);
}
