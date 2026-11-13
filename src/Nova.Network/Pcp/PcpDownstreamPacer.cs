namespace PeerCast.Nova.Network.Pcp;

/// <summary>Application IPv4 media pacing, not NIC shaping or admission accounting.</summary>
public sealed class PcpDownstreamPacer
{
    public const int ControlBytesPerSecond = 1000;
    public const int ControlBurstBytes = 8192;
    public const int MaxWaitingRequests = 128;
    public static TimeSpan PacketDeadline { get; } = TimeSpan.FromSeconds(30);
    private readonly object _gate = new();
    private readonly LinkedList<TaskCompletionSource> _waiting = new();
    private readonly double _bytesPerSecond;
    private double _mediaTokens, _controlTokens = ControlBurstBytes;
    private long _last;

    public PcpDownstreamPacer(int? mediaKbps, TimeProvider? clock = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(mediaKbps ?? 0);
        MediaKbps = mediaKbps;
        Clock = clock ?? TimeProvider.System;
        _bytesPerSecond = (double)(mediaKbps ?? 0) * 125;
        BurstBytes = (int)Math.Clamp(Math.Ceiling(_bytesPerSecond / 4), 1, 16384);
        _mediaTokens = BurstBytes;
        _last = Clock.GetTimestamp();
    }

    public int? MediaKbps { get; }
    public int BurstBytes { get; }
    public int WaitingRequests { get { lock (_gate) return _waiting.Count; } }
    internal TimeProvider Clock { get; }

    // Control has a separate finite allowance; it never queues behind media.
    // Exhaustion closes the connection instead of blocking a handshake or
    // claiming an unlimited control exemption. Failed writes never refund credit.
    internal void ConsumeControl(int bytes)
    {
        if (MediaKbps is null) return;
        lock (_gate)
        {
            Refill();
            if (bytes > _controlTokens) throw new PcpPacingLimitException();
            _controlTokens -= bytes;
        }
    }

    internal ValueTask AcquireMediaAsync(int bytes, CancellationToken token) => AcquireMediaAsync(bytes, null, token);

    internal async ValueTask AcquireMediaAsync(int bytes, PcpPacingWaitState? state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (MediaKbps is null) return;
        if (MediaKbps == 0 || bytes <= 0 || bytes > BurstBytes) throw new PcpPacingLimitException();
        LinkedListNode<TaskCompletionSource> node;
        lock (_gate)
        {
            if (_waiting.Count >= MaxWaitingRequests) throw new PcpPacingLimitException();
            node = _waiting.AddLast(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            state?.Set(true);
            if (node == _waiting.First) node.Value.TrySetResult();
        }
        try
        {
            await node.Value.Task.WaitAsync(token).ConfigureAwait(false);
            while (true)
            {
                TimeSpan delay;
                lock (_gate)
                {
                    token.ThrowIfCancellationRequested();
                    Refill();
                    if (_mediaTokens >= bytes)
                    {
                        _mediaTokens -= bytes;
                        state?.Set(false);
                        return;
                    }
                    state?.Set(true);
                    // Task.Delay rounds sub-millisecond durations down. Always
                    // round up to a full millisecond to avoid a synchronous spin.
                    delay = TimeSpan.FromMilliseconds(Math.Max(1,
                        Math.Ceiling((bytes - _mediaTokens) / _bytesPerSecond * 1000)));
                }
                // Only the head has a timer; all other bounded waiters await
                // their turn. No lock is held across an await.
                await Task.Delay(delay, Clock, token).ConfigureAwait(false);
            }
        }
        finally
        {
            lock (_gate)
            {
                state?.Set(false);
                _waiting.Remove(node);
                _waiting.First?.Value.TrySetResult();
            }
        }
    }

    private void Refill()
    {
        var now = Clock.GetTimestamp();
        var elapsed = Clock.GetElapsedTime(_last, now).TotalSeconds;
        if (elapsed < 0) throw new PcpPacingLimitException();
        _last = now;
        _mediaTokens = Math.Min(BurstBytes, _mediaTokens + elapsed * _bytesPerSecond);
        _controlTokens = Math.Min(ControlBurstBytes, _controlTokens + elapsed * ControlBytesPerSecond);
    }
}

internal sealed class PcpPacingLimitException : IOException;

internal sealed class PcpPacingWaitState
{
    private int _waiting;
    internal bool Waiting => Volatile.Read(ref _waiting) != 0;
    internal void Set(bool value) => Volatile.Write(ref _waiting, value ? 1 : 0);
}

/// <summary>One startup-owned IPv4 budget shared across routes and generations.</summary>
public interface IPcpDownstreamRelayPacing
{
    PcpDownstreamPacer? Pacing { get; }
}

internal interface IPcpRelayPacingBackpressure
{
    bool WaitingForPacing { get; }
}
