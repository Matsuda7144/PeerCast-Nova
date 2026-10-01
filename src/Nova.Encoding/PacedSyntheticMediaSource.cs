using System.Runtime.CompilerServices;

namespace PeerCast.Nova.MediaEncoding;

public sealed record LiveMediaInput(RawYuv420VideoFrame? Video, RawPcmAudioFrame? Audio);

public readonly record struct PacedSyntheticMediaSourceSnapshot(
    int VideoFrames, int AudioBlocks, bool Completed, bool Stopped);

/// <summary>
/// One bounded 64x48/30fps YUV420P and 48kHz stereo PCM generation. Buffers are
/// created only when due; media timestamps use counters, never wall-clock time.
/// </summary>
public sealed class PacedSyntheticMediaSource : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ILiveMediaClock _clock;
    private int _videoFrames;
    private int _audioBlocks;
    private int _completed;
    private int _stopped;
    private bool _started;
    private bool _disposed;

    public PacedSyntheticMediaSource(int videoFrameCount = 192)
        : this(videoFrameCount, new MonotonicLiveMediaClock(TimeProvider.System)) { }

    internal PacedSyntheticMediaSource(int videoFrameCount, ILiveMediaClock clock)
    {
        // 16 video frames and 25 AAC input blocks have the same duration.
        if (videoFrameCount is < 16 or > 288 || videoFrameCount % 16 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(videoFrameCount));
        }
        ArgumentNullException.ThrowIfNull(clock);
        VideoFrameCount = videoFrameCount;
        AudioBlockCount = checked(videoFrameCount * 25 / 16);
        _clock = clock;
    }

    public int VideoFrameCount { get; }
    public int AudioBlockCount { get; }
    public PacedSyntheticMediaSourceSnapshot Snapshot => new(
        Volatile.Read(ref _videoFrames), Volatile.Read(ref _audioBlocks),
        Volatile.Read(ref _completed) != 0, Volatile.Read(ref _stopped) != 0);

    public async IAsyncEnumerable<LiveMediaInput> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        CancellationTokenSource linked;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) { throw new InvalidOperationException("A live source is single-use."); }
            _started = true;
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        }
        using (linked)
        {
            _clock.Start();
            try
            {
                while (_videoFrames < VideoFrameCount || _audioBlocks < AudioBlockCount)
                {
                    var videoDue = _videoFrames < VideoFrameCount ? (long)_videoFrames * 1600 : long.MaxValue;
                    var audioDue = _audioBlocks < AudioBlockCount ? (long)_audioBlocks * 1024 : long.MaxValue;
                    var video = videoDue <= audioDue;
                    await _clock.WaitUntilAsync(Math.Min(videoDue, audioDue), linked.Token).ConfigureAwait(false);
                    LiveMediaInput input;
                    lock (_gate)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        ObjectDisposedException.ThrowIf(_disposed, this);
                        input = video
                            ? new(CreateVideo(_videoFrames++), null)
                            : new(null, CreateAudio(_audioBlocks++));
                    }
                    linked.Token.ThrowIfCancellationRequested();
                    yield return input;
                }
                Volatile.Write(ref _completed, 1);
            }
            finally { Volatile.Write(ref _stopped, 1); }
        }
    }

    private static RawYuv420VideoFrame CreateVideo(int index)
    {
        var bytes = new byte[64 * 48 * 3 / 2];
        for (var y = 0; y < 48; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                bytes[y * 64 + x] = Math.Abs(x - index % 64) < 2 ? (byte)235 :
                    (((x + index) / 8 + y / 8) % 2 == 0 ? (byte)40 : (byte)190);
            }
        }
        bytes.AsSpan(64 * 48).Fill(128);
        return new(64, 48, index, bytes);
    }

    private static RawPcmAudioFrame CreateAudio(int index)
    {
        var samples = new float[2048];
        for (var sample = 0; sample < 1024; sample++)
        {
            var value = (float)(0.25 * Math.Sin(2 * Math.PI * 440 * ((long)index * 1024 + sample) / 48_000));
            samples[sample * 2] = samples[sample * 2 + 1] = value;
        }
        return new((long)index * 1024, samples);
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) { return ValueTask.CompletedTask; }
            _disposed = true;
            Volatile.Write(ref _stopped, 1);
            _stop.Cancel();
            _stop.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}

internal interface ILiveMediaClock
{
    void Start();
    ValueTask WaitUntilAsync(long mediaTicks, CancellationToken cancellationToken);
}

internal sealed class MonotonicLiveMediaClock(TimeProvider provider) : ILiveMediaClock
{
    private long _epoch;
    public void Start() => _epoch = provider.GetTimestamp();
    public async ValueTask WaitUntilAsync(long mediaTicks, CancellationToken cancellationToken)
    {
        // Convert each absolute target once; no per-event rounded-delay accumulation.
        var target = TimeSpan.FromTicks(checked(mediaTicks * TimeSpan.TicksPerSecond / 48_000));
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var delay = target - provider.GetElapsedTime(_epoch);
            if (delay <= TimeSpan.Zero) { return; }
            await Task.Delay(delay, provider, cancellationToken).ConfigureAwait(false);
        }
    }
}
