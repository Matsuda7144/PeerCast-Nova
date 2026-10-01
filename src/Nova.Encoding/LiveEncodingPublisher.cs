using System.Security.Cryptography;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Media;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.MediaEncoding;

public sealed record LiveEncodingPublisherOptions
{
    public int MaximumPendingSamples { get; init; } = 128;
    public int MaximumPendingPayloadBytes { get; init; } = 1024 * 1024;
}

public sealed record LiveEncodingTrackExtent(
    int Samples, long FirstDts, long LastDts, long FirstPts, long LastPts,
    Fmp4MuxRandomAccessKind FirstRandomAccess);

public sealed record LiveEncodingFragmentSnapshot(
    uint Sequence, LiveEncodingTrackExtent? Video, LiveEncodingTrackExtent? Audio,
    int ByteLength, string Sha256, int SourceVideoFrames, int SourceAudioBlocks);

public sealed record LiveEncodingPublisherResult(
    bool Completed, bool Cancelled, PacedSyntheticMediaSourceSnapshot Source,
    MediaEncoderSnapshot Video, MediaEncoderSnapshot Audio, int InitialPaddingSamples,
    long Fragments, int FirstPublishVideoFrames, int FirstPublishAudioBlocks,
    int SourceRemainingAtFirstPublish, int PeakPendingSamples, long PeakPendingPayloadBytes,
    PcpFmp4PublishingBoundarySnapshot Publisher);

/// <summary>
/// Owns one source, encoder/mux/publisher generation. Borrows the runtime and
/// consumer, which must outlive RunAsync. Each awaited consumer callback is the
/// downstream capacity boundary; it must observe cancellation promptly.
/// Normal finite completion flushes all packets. Cancellation aborts without
/// flushing an already terminally cancelled publishing generation.
/// </summary>
public sealed class LiveEncodingPublisher
{
    // 24 video + at most 39 AAC samples fit the existing receiver's
    // 64-sample batch limit. No receiver limit changes are needed.
    private const long FragmentTicks = 38_400;
    private readonly PacedSyntheticMediaSource _source;
    private readonly ChannelId _channel;
    private readonly ChannelSessionId _session;
    private readonly IPcpChannelPacketConsumer _consumer;
    private readonly LiveEncodingPublisherOptions _options;
    private readonly Action<LiveEncodingFragmentSnapshot>? _fragmentPublished;
    private int _started;

    public LiveEncodingPublisher(PacedSyntheticMediaSource source, ChannelId channel,
        ChannelSessionId session, IPcpChannelPacketConsumer consumer,
        LiveEncodingPublisherOptions? options = null,
        Action<LiveEncodingFragmentSnapshot>? fragmentPublished = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(consumer);
        options ??= new();
        if (options.MaximumPendingSamples is < 2 or > 256 ||
            options.MaximumPendingPayloadBytes is < 1024 or > 4 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
        _source = source; _channel = channel; _session = session; _consumer = consumer;
        _options = options with { }; _fragmentPublished = fragmentPublished;
    }

    public Task<LiveEncodingPublisherResult> RunAsync(VerifiedPlaybackDecoderRuntimeLease runtime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return RunCoreAsync(() => new NativeLiveEncodingSession(runtime), cancellationToken);
    }

    internal async Task<LiveEncodingPublisherResult> RunCoreAsync(
        Func<ILiveEncodingSession> createEncoders, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("A live publisher is single-use.");
        }
        await using var source = _source;
        cancellationToken.ThrowIfCancellationRequested();
        using var encoders = createEncoders();
        if (encoders.InitialPaddingSamples < 0 || encoders.InitialPaddingSamples > 48_000)
        {
            throw new InvalidOperationException("Invalid live AAC priming extent.");
        }
        var created = Fmp4Muxer.Create(encoders.Tracks, out var muxer, new()
        {
            MaximumSamplesPerFragment = _options.MaximumPendingSamples,
            MaximumFragmentPayloadBytes = _options.MaximumPendingPayloadBytes,
            MaximumMediaSegmentBytes = _options.MaximumPendingPayloadBytes + 64 * 1024,
            MaximumSampleBytes = _options.MaximumPendingPayloadBytes,
        });
        if (created != Fmp4MuxerCreateResult.Created || muxer is null)
        {
            throw new InvalidOperationException($"Live mux creation failed: {created}.");
        }
        using var ownedMuxer = muxer;
        var publisherCreated = PcpFmp4PublishingBoundary.Create(_channel, _session, _consumer,
            out var publisher, new()
            {
                MaximumInitializationBytes = 64 * 1024,
                MaximumPacketPayloadBytes = 64 * 1024,
                MaximumMediaSegmentBytes = _options.MaximumPendingPayloadBytes + 64 * 1024,
            });
        if (publisherCreated != PcpFmp4PublishingBoundaryCreateResult.Created || publisher is null)
        {
            throw new InvalidOperationException($"Live publisher creation failed: {publisherCreated}.");
        }
        await using var ownedPublisher = publisher;
        var pending = new List<Fmp4MuxEncodedSample>();
        var nextDts = new Dictionary<uint, long> { [1] = encoders.InitialPaddingSamples, [2] = 0 };
        var lastPts = new Dictionary<uint, long>();
        var latestDts = new Dictionary<uint, long>();
        long pendingBytes = 0, peakBytes = 0, fragments = 0, cutoff = FragmentTicks;
        var peakSamples = 0;
        var firstVideo = -1; var firstAudio = -1; var firstRemaining = -1;
        var completed = false; var cancelled = false;
        try
        {
            RequireMux(ownedMuxer.GenerateInitializationSegment(out var initialization));
            await PublishAsync(initialization!, PcpFmp4PublishingSegmentRole.Initialization).ConfigureAwait(false);
            await foreach (var input in source.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Append(encoders.Encode(input));
                await EmitReadyAsync().ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            Append(encoders.Complete());
            await EmitReadyAsync().ConfigureAwait(false);
            if (pending.Count != 0) { await EmitAsync(long.MaxValue).ConfigureAwait(false); }
            completed = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Abort is deliberately not encoder EOS; a partially published box
            // cannot be resumed on the existing publisher after cancellation.
            cancelled = true;
        }
        await source.DisposeAsync().ConfigureAwait(false);
        return new(completed, cancelled, source.Snapshot, encoders.VideoSnapshot,
            encoders.AudioSnapshot, encoders.InitialPaddingSamples, fragments, firstVideo, firstAudio,
            firstRemaining, peakSamples, peakBytes, publisher.Snapshot);

        void Append(IReadOnlyList<Fmp4MuxEncodedSample> samples)
        {
            foreach (var sample in samples)
            {
                if (!nextDts.TryGetValue(sample.TrackId, out var expectedDts) ||
                    sample.DtsTicks != expectedDts || sample.DurationTicks <= 0 || sample.PtsTicks < 0 ||
                    (lastPts.TryGetValue(sample.TrackId, out var previousPts) && sample.PtsTicks <= previousPts))
                {
                    throw new InvalidOperationException("The live encoded timeline is not contiguous and monotonic.");
                }
                if (pending.Count >= _options.MaximumPendingSamples ||
                    sample.Payload.Length > _options.MaximumPendingPayloadBytes - pendingBytes)
                {
                    throw new InvalidOperationException("The live pending sample bound was exceeded.");
                }
                // Detach even a test/injected encoder's borrowed storage before the next Encode call.
                pending.Add(sample with { Payload = sample.Payload.ToArray() });
                pendingBytes += sample.Payload.Length;
                nextDts[sample.TrackId] = checked(sample.DtsTicks + sample.DurationTicks);
                lastPts[sample.TrackId] = sample.PtsTicks;
                latestDts[sample.TrackId] = sample.DtsTicks;
                peakSamples = Math.Max(peakSamples, pending.Count);
                peakBytes = Math.Max(peakBytes, pendingBytes);
            }
        }

        async Task EmitReadyAsync()
        {
            while (latestDts.GetValueOrDefault(1U, -1L) >= cutoff && latestDts.GetValueOrDefault(2U, -1L) >= cutoff)
            {
                await EmitAsync(cutoff).ConfigureAwait(false);
                cutoff = checked(cutoff + FragmentTicks);
            }
        }

        async Task EmitAsync(long exclusiveEnd)
        {
            var samples = pending.Where(sample => sample.DtsTicks < exclusiveEnd)
                .OrderBy(static sample => sample.DtsTicks).ThenBy(static sample => sample.TrackId).ToArray();
            if (samples.Length == 0) { throw new InvalidOperationException("A live fragment cannot be empty."); }
            RequireMux(ownedMuxer.MuxFragment(samples, out var media));
            var segment = media!;
            var progress = source.Snapshot;
            await PublishAsync(segment.Bytes, PcpFmp4PublishingSegmentRole.Media).ConfigureAwait(false);
            if (firstVideo < 0)
            {
                firstVideo = progress.VideoFrames; firstAudio = progress.AudioBlocks;
                firstRemaining = source.VideoFrameCount - firstVideo + source.AudioBlockCount - firstAudio;
            }
            fragments++;
            _fragmentPublished?.Invoke(new(segment.SequenceNumber, Extent(1), Extent(2),
                segment.Bytes.Length, Convert.ToHexString(SHA256.HashData(segment.Bytes)),
                progress.VideoFrames, progress.AudioBlocks));
            pending.RemoveAll(sample => sample.DtsTicks < exclusiveEnd);
            pendingBytes -= samples.Sum(static sample => (long)sample.Payload.Length);

            LiveEncodingTrackExtent? Extent(uint track)
            {
                var items = samples.Where(sample => sample.TrackId == track).ToArray();
                return items.Length == 0 ? null : new(items.Length, items[0].DtsTicks, items[^1].DtsTicks,
                    items[0].PtsTicks, items[^1].PtsTicks, items[0].RandomAccessKind);
            }
        }

        async Task PublishAsync(ReadOnlyMemory<byte> bytes, PcpFmp4PublishingSegmentRole role)
        {
            var result = await publisher.PublishSegmentAsync(bytes, role, cancellationToken).ConfigureAwait(false);
            if (result == PcpFmp4PublishingResult.Cancelled && cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            if (result != PcpFmp4PublishingResult.Published)
            {
                throw new InvalidOperationException($"Live PCP publication failed: {result}.");
            }
        }
    }

    private static void RequireMux(Fmp4MuxerResult result)
    {
        if (result != Fmp4MuxerResult.Produced)
        {
            throw new InvalidOperationException($"Live mux failed: {result}.");
        }
    }
}
