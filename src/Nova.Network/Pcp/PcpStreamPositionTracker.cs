namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Expands PCP's 32-bit packet position into a monotonic 64-bit position.
/// One tracker belongs to one serialized channel-session control path.
/// </summary>
public sealed class PcpStreamPositionTracker
{
    private const uint HalfRange = 0x80000000U;

    private bool _hasPosition;
    private uint _previousWirePosition;
    private long _absolutePosition;

    public PcpStreamPositionObservation Observe(uint wirePosition)
    {
        if (!_hasPosition)
        {
            _hasPosition = true;
            _previousWirePosition = wirePosition;
            _absolutePosition = wirePosition;

            return new PcpStreamPositionObservation(
                PcpStreamPositionObservationKind.Initial,
                wirePosition,
                PreviousWirePosition: null,
                _absolutePosition);
        }

        if (wirePosition == _previousWirePosition)
        {
            return new PcpStreamPositionObservation(
                PcpStreamPositionObservationKind.Duplicate,
                wirePosition,
                _previousWirePosition,
                _absolutePosition);
        }

        var delta = unchecked(wirePosition - _previousWirePosition);

        if (delta >= HalfRange)
        {
            return new PcpStreamPositionObservation(
                PcpStreamPositionObservationKind.BackwardDiscontinuity,
                wirePosition,
                _previousWirePosition,
                _absolutePosition);
        }

        var kind = wirePosition < _previousWirePosition
            ? PcpStreamPositionObservationKind.Wrapped
            : PcpStreamPositionObservationKind.Advanced;

        var previousWirePosition = _previousWirePosition;
        _absolutePosition = checked(_absolutePosition + delta);
        _previousWirePosition = wirePosition;

        return new PcpStreamPositionObservation(
            kind,
            wirePosition,
            previousWirePosition,
            _absolutePosition);
    }
}
