using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackEncodedRandomAccessClassification
{
    NotApplicable,
    IndependentlyDecodable,
    NotIndependentlyDecodable,
    Indeterminate,
}

public enum PlaybackEncodedDiscontinuity
{
    None,
    Global,
}

public readonly record struct PlaybackEncodedPayloadSlice
{
    internal PlaybackEncodedPayloadSlice(int offset, int length)
    {
        Offset = offset;
        Length = length;
    }

    public int Offset { get; }

    public int Length { get; }

    internal static bool TryCreate(
        int offset,
        int length,
        out PlaybackEncodedPayloadSlice slice)
    {
        slice = default;
        if (offset < 0 || length <= 0)
        {
            return false;
        }

        try
        {
            _ = checked(offset + length);
        }
        catch (OverflowException)
        {
            return false;
        }

        slice = new(offset, length);
        return true;
    }
}

public sealed class PlaybackEncodedSample
{
    internal PlaybackEncodedSample(
        uint trackId,
        int sampleOrdinal,
        long dtsTicks,
        long ptsTicks,
        long durationTicks,
        PlaybackEncodedPayloadSlice payloadSlice,
        PlaybackEncodedRandomAccessClassification randomAccessClassification)
    {
        if (trackId == 0 || sampleOrdinal < 0 || durationTicks <= 0 ||
            payloadSlice.Offset < 0 || payloadSlice.Length <= 0)
        {
            throw new ArgumentException("Encoded sample is invalid.");
        }

        TrackId = trackId;
        SampleOrdinal = sampleOrdinal;
        DtsTicks = dtsTicks;
        PtsTicks = ptsTicks;
        DurationTicks = durationTicks;
        PayloadSlice = payloadSlice;
        RandomAccessClassification = randomAccessClassification;
    }

    public uint TrackId { get; }

    public int SampleOrdinal { get; }

    public long DtsTicks { get; }

    public long PtsTicks { get; }

    public long DurationTicks { get; }

    public PlaybackEncodedPayloadSlice PayloadSlice { get; }

    public PlaybackEncodedRandomAccessClassification
        RandomAccessClassification
    { get; }
}

public sealed class PlaybackEncodedSampleBatch : IDisposable
{
    private readonly object _gate = new();
    private readonly PlaybackEncodedSample[] _sampleArray;
    private readonly ReadOnlyCollection<PlaybackEncodedSample> _samples;
    private byte[]? _payloadStorage;

    internal PlaybackEncodedSampleBatch(
        PlaybackEncodedStreamDescription streamDescription,
        ulong epochId,
        uint fragmentSequence,
        PlaybackEncodedDiscontinuity discontinuity,
        PlaybackEncodedSample[] samples,
        byte[] payloadStorage)
    {
        ArgumentNullException.ThrowIfNull(streamDescription);
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(payloadStorage);
        if (samples.Length == 0 || payloadStorage.Length == 0 ||
            !Enum.IsDefined(discontinuity))
        {
            throw new ArgumentException("Encoded sample batch is invalid.");
        }

        var sampleIdentities = new HashSet<(uint TrackId, int Ordinal)>();
        foreach (var sample in samples)
        {
            if (sample is null ||
                streamDescription.FindTrack(sample.TrackId) is null ||
                sample.PayloadSlice.Offset >
                    payloadStorage.Length - sample.PayloadSlice.Length ||
                !sampleIdentities.Add((sample.TrackId, sample.SampleOrdinal)))
            {
                throw new ArgumentException("Encoded sample batch is invalid.");
            }
        }

        StreamDescription = streamDescription;
        EpochId = epochId;
        FragmentSequence = fragmentSequence;
        Discontinuity = discontinuity;
        _sampleArray = samples.ToArray();
        _samples = Array.AsReadOnly(_sampleArray);
        PayloadByteLength = payloadStorage.Length;
        _payloadStorage = payloadStorage;
    }

    public Guid StreamGenerationId => StreamDescription.StreamGenerationId;

    public PlaybackEncodedStreamDescription StreamDescription { get; }

    public ulong EpochId { get; }

    public uint FragmentSequence { get; }

    public PlaybackEncodedDiscontinuity Discontinuity { get; }

    public IReadOnlyList<PlaybackEncodedSample> Samples => _samples;

    public int PayloadByteLength { get; }

    public bool IsDisposed => Volatile.Read(ref _payloadStorage) is null;

    public void CopyPayloadTo(
        PlaybackEncodedSample sample,
        Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(sample);
        lock (_gate)
        {
            var storage = _payloadStorage;
            if (storage is null)
            {
                ObjectDisposedException.ThrowIf(
                    true,
                    nameof(PlaybackEncodedSampleBatch));
            }

            if (Array.IndexOf(_sampleArray, sample) < 0)
            {
                throw new ArgumentException(
                    "Sample does not belong to this batch.",
                    nameof(sample));
            }

            var slice = sample.PayloadSlice;
            if (destination.Length < slice.Length)
            {
                throw new ArgumentException(
                    "Destination is shorter than the sample payload.",
                    nameof(destination));
            }

            storage.AsSpan(slice.Offset, slice.Length).CopyTo(destination);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            var storage = _payloadStorage;
            if (storage is null)
            {
                return;
            }

            Volatile.Write(ref _payloadStorage, null);
            Array.Clear(storage, 0, storage.Length);
        }
    }
}
