using System.Threading.Channels;

namespace PeerCast.Nova.Playback;

internal readonly record struct PlaybackFmp4HttpFragment(
    byte[]? Initialization,
    byte[] Media)
{
    public int ByteCount => (Initialization?.Length ?? 0) + Media.Length;
}

// The host serializes enqueue, completion and accounting under its gate. The
// reader releases a reservation only after writing both parts to the socket.
internal sealed class PlaybackFmp4HttpClientBuffer(CancellationToken lifetime) : IDisposable
{
    internal const int MaximumPendingFragments = 8;
    internal const int MaximumPendingBytes = 4 * 1024 * 1024;

    private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
    private readonly Channel<PlaybackFmp4HttpFragment> _fragments =
        Channel.CreateBounded<PlaybackFmp4HttpFragment>(new BoundedChannelOptions(MaximumPendingFragments)
        {
            SingleReader = true,
            SingleWriter = true,
        });

    private int _pendingFragments;
    private int _pendingBytes;

    public CancellationToken Token => _stop.Token;
    public ChannelReader<PlaybackFmp4HttpFragment> Reader => _fragments.Reader;
    public bool Started { get; private set; }
    public TaskCompletionSource<bool> Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool TryEnqueue(byte[]? initialization, ReadOnlySpan<byte> media)
    {
        var byteCount = (initialization?.Length ?? 0) + media.Length;
        if (Token.IsCancellationRequested ||
            _pendingFragments == MaximumPendingFragments ||
            byteCount > MaximumPendingBytes - _pendingBytes ||
            !_fragments.Writer.TryWrite(new(initialization, media.ToArray())))
        {
            return false;
        }

        _pendingFragments++;
        _pendingBytes += byteCount;
        Started = true;
        return true;
    }

    public void AcknowledgeWrite(PlaybackFmp4HttpFragment fragment)
    {
        _pendingFragments--;
        _pendingBytes -= fragment.ByteCount;
    }

    public void Complete() => _fragments.Writer.TryComplete();

    public void Abort() => _stop.Cancel();

    public void Dispose() => _stop.Dispose();
}
