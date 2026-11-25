using System.IO.Pipelines;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Pcp;

public sealed partial class PcpDownstreamRelayServer
{
    private ReadOnlyMemory<byte> ResolveInitialMetadata() =>
        !_initialMetadata.IsEmpty
            ? _initialMetadata
            : _initialMetadataProvider?.Invoke() ?? default;
    // The shared listener lends its existing reader/transport. This channel's
    // original server still owns child admission, peers and recovery accounting.
    internal async ValueTask<PcpDownstreamChildResult> RunRoutedAsync(TcpTransportConnection connection,
        PipeReader reader, PcpDownstreamRelayRequest request, PcpWanIngressPolicy.Lease? ingress,
        CancellationToken cancellationToken)
    {
        if (request.ChannelId != _channelId.Value) throw new InvalidOperationException("Routed channel mismatch.");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entry = new SessionEntry(connection) { Task = completion.Task, Shared = true };
        lock (_gate)
        {
            if (_state != PcpDownstreamRelayServerState.Running || _childAdmission.IsClosed ||
                _stopSource.IsCancellationRequested || _activeConnectionCount >= _listener.MaxConcurrentConnections)
                throw new OperationCanceledException("Channel owner admission is closed.");
            _sessions.Add(entry);
            _acceptedConnectionCount = checked(_acceptedConnectionCount + 1);
            _activeConnectionCount = checked(_activeConnectionCount + 1);
        }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopSource.Token);
        PcpDownstreamChildSession? session = null;
        PcpDownstreamChildResult? result = null;
        var failed = false;
        try
        {
            session = PcpDownstreamChildSession.CreateRouted(connection, reader, request, _fanout, _localSessionId,
                _childOptions, packetWriter: PacketWriterFactory?.Invoke(connection),
                admission: _admission, childAdmission: _childAdmission, peers: Peers,
                wanIngress: ingress, initialMetadata: ResolveInitialMetadata(), traffic: TrafficScope);
            lock (_gate) entry.Session = session;
            result = await session.RunAsync(lifetime.Token).ConfigureAwait(false);
            return result;
        }
        catch { failed = true; throw; }
        finally
        {
            try { if (session is not null) await session.DisposeAsync().ConfigureAwait(false); }
            catch { failed = true; }
            if (session is not null) TrackAdmissionCleanup(session.AdmissionCleanupCompletion);
            // Return to the lender before waiting for its cleanup; otherwise
            // shared listener -> handler -> transport cleanup would deadlock.
            _ = FinishBorrowedAsync();
            async Task FinishBorrowedAsync()
            {
                try
                {
                    await connection.CleanupCompletion.ConfigureAwait(false);
                    if (session is not null && !await session.AdmissionCleanupCompletion.ConfigureAwait(false)) failed = true;
                }
                catch { failed = true; }
                finally
                {
                    CompleteSessionEntry(entry, result, failed);
                    completion.TrySetResult();
                }
            }
        }
    }

    private void CompleteSessionEntry(SessionEntry entry, PcpDownstreamChildResult? result, bool failed)
    {
        lock (_gate)
        {
            entry.CleanupConfirmed = !failed && result?.CleanupCompleted == true;
            _sessions.Remove(entry);
            _activeConnectionCount--;
            _completedConnectionCount = checked(_completedConnectionCount + 1);
            if (failed) _internalFailureCount = checked(_internalFailureCount + 1);
            else if (result is not null) RecordResultLocked(result);
        }
    }
}
