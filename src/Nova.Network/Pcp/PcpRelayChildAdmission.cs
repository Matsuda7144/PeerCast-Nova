using System.Diagnostics.CodeAnalysis;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>A server-scoped registration fence; never closes the borrowed fan-out.</summary>
internal sealed class PcpRelayChildAdmission
{
    private readonly object _gate = new();
    private readonly HashSet<PcpRelayChildRegistration> _children = [];
    private bool _closed;
    private bool _deliveryFailed;

    public bool IsClosed { get { lock (_gate) { return _closed; } } }

    public bool TryAddChild(PcpRelayFanout fanout, IPcpRelayChildSink sink,
        [NotNullWhen(true)] out PcpRelayChildRegistration? registration)
    {
        lock (_gate)
        {
            PruneCompleted();
            if (_closed) { registration = null; return false; }
            if (!fanout.TryAddChild(sink, out registration)) return false;
            _children.Add(registration);
            return true;
        }
    }

    public void Close() { lock (_gate) { _closed = true; } }

    public (PcpRelayChildRegistration[] Children, bool DeliveryFailed) CaptureClosed()
    {
        lock (_gate)
        {
            if (!_closed) throw new InvalidOperationException("Relay admission is still open.");
            PruneCompleted();
            return ([.. _children], _deliveryFailed);
        }
    }

    internal static bool DeliveryFailed(PcpRelayChildStatusSnapshot snapshot) =>
        snapshot.DiscardedPacketCount != 0 || snapshot.EndReason is
            PcpRelayChildEndReason.SinkFailed or PcpRelayChildEndReason.SlowConsumer or PcpRelayChildEndReason.FanoutStopped;

    private void PruneCompleted()
    {
        foreach (var child in _children.Where(child => child.Completion.IsCompleted).ToArray())
        {
            _deliveryFailed |= DeliveryFailed(child.Snapshot);
            _children.Remove(child);
        }
    }
}

// Internal observation boundaries only. Production supplies no observer.
internal enum PcpRelayQuiescencePoint
{
    BeforeReserve, AfterReserve, BeforeClosedResponse, BeforeAccept,
    BeforeListenerStop, AfterInitialFailureEvaluation
}

internal sealed class PcpRelayQuiescenceObserver(Func<PcpRelayQuiescencePoint, ValueTask> observe)
{
    public ValueTask ObserveAsync(PcpRelayQuiescencePoint point) => observe(point);
}
