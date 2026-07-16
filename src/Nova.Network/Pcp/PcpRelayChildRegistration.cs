namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Controls one local relay child without exposing a remote identifier or
/// endpoint through the fan-out status boundary.
/// </summary>
public sealed class PcpRelayChildRegistration : IAsyncDisposable
{
    private readonly PcpRelayChildWorker _state;

    internal PcpRelayChildRegistration(PcpRelayChildWorker state)
    {
        _state = state;
    }

    public PcpRelayChildStatusSnapshot Snapshot => _state.Snapshot;

    /// <summary>
    /// Completes only after the child callback has really returned and all
    /// bounded pending payload references have been released.
    /// </summary>
    public Task Completion => _state.Completion;

    public async ValueTask<bool> StopAsync(
        CancellationToken cancellationToken = default)
    {
        _state.RequestStop(PcpRelayChildEndReason.OwnerRemoved);
        return await _state.WaitForCompletionAsync(
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
