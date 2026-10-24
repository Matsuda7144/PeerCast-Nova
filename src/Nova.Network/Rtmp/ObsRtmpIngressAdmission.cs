namespace PeerCast.Nova.Network.Rtmp;

// Internal deterministic test observations. Production supplies no observer.
// No callbacks execute inside the admission gate; stream ownership stays with
// the accepted client, including when a test observes its pending reads.
internal sealed class ObsRtmpIngressObservation
{
    internal Action? AcceptWaiting { get; init; }
    internal Action? ForcedStopRequested { get; init; }
    internal Func<Stream, Stream>? ObserveStream { get; init; }
    internal Action<RtmpMessage>? MessageDecoded { get; init; }
    internal Action<RtmpMessage>? MessageAdmitted { get; init; }
}

/// <summary>Linearizes complete-message admission against a local close request.</summary>
internal sealed class ObsRtmpIngressAdmission : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _readStop = new();
    private bool _closed;

    public CancellationToken ReadStop => _readStop.Token;

    public bool TryAdmit()
    {
        lock (_gate) { return !_closed; }
    }

    public void Close()
    {
        lock (_gate)
        {
            if (_closed) return;
            _closed = true;
        }
        // Only internal reads/handshake operations use this token. Admitted
        // message processing continues with the separate forced-stop token.
        _readStop.Cancel();
    }

    public void Dispose() => _readStop.Dispose();
}
