using System.Buffers;
using System.Buffers.Binary;

namespace PeerCast.Nova.Playback;

/// <summary>
/// Classifies a bounded transient media prefix into fixed container values.
/// It never returns signature bytes and clears retained prefix data on every
/// reset, terminal result, and disposal.
/// </summary>
public sealed class PlaybackMediaClassifier : IDisposable
{
    private const int TransportStreamPacketSize = 188;
    private const int TransportStreamSyncCount = 3;

    private static ReadOnlySpan<byte> AsfHeaderObject =>
    [
        0x30, 0x26, 0xb2, 0x75,
        0x8e, 0x66,
        0xcf, 0x11,
        0xa6, 0xd9,
        0x00, 0xaa, 0x00, 0x62, 0xce, 0x6c,
    ];

    private static ReadOnlySpan<byte> EbmlHeaderIdentifier =>
        [0x1a, 0x45, 0xdf, 0xa3];

    private static ReadOnlySpan<byte> EbmlDocumentTypeIdentifier =>
        [0x42, 0x82];

    private readonly PlaybackMediaClassificationOptions _options;
    private byte[]? _prefix;
    private int _prefixLength;
    private PlaybackMediaClassificationState _state =
        PlaybackMediaClassificationState.Collecting;
    private PlaybackMediaContainer _container =
        PlaybackMediaContainer.Unknown;
    private PlaybackMediaClassificationBasis? _basis;
    private PlaybackMediaClassificationLimit? _limit;
    private long _observedPacketCount;
    private long _examinedByteCount;
    private int _discontinuityCount;

    public PlaybackMediaClassifier(
        PlaybackMediaClassificationOptions? options = null)
    {
        _options = options is null
            ? new PlaybackMediaClassificationOptions()
            : options with { };
        _options.Validate();
        _prefix = ArrayPool<byte>.Shared.Rent(_options.MaxProbeBytes);
    }

    public PlaybackMediaClassificationSnapshot Snapshot => new(
        _state,
        _container,
        _basis,
        _limit,
        _observedPacketCount,
        _examinedByteCount,
        _discontinuityCount,
        _prefixLength);

    public PlaybackMediaClassificationSnapshot Observe(
        ReadOnlySpan<byte> payload,
        PlaybackDiscontinuityKind discontinuity =
            PlaybackDiscontinuityKind.None)
    {
        ThrowIfDisposed();

        if (_state != PlaybackMediaClassificationState.Collecting)
        {
            throw new InvalidOperationException(
                "Media classification has already reached a terminal state.");
        }

        _observedPacketCount = checked(_observedPacketCount + 1);

        if (discontinuity != PlaybackDiscontinuityKind.None)
        {
            _discontinuityCount = checked(_discontinuityCount + 1);
            ClearPrefix();

            if (_discontinuityCount > _options.MaxDiscontinuities)
            {
                SetLimit(PlaybackMediaClassificationLimit.Discontinuities);
                return Snapshot;
            }
        }

        var remaining = _options.MaxProbeBytes - _prefixLength;
        var copyLength = Math.Min(payload.Length, remaining);

        if (copyLength > 0)
        {
            payload[..copyLength].CopyTo(
                _prefix.AsSpan(_prefixLength, copyLength));
            _prefixLength += copyLength;
            _examinedByteCount = checked(
                _examinedByteCount + copyLength);
        }

        if (TryClassify(
            _prefix.AsSpan(0, _prefixLength),
            out var container,
            out var basis))
        {
            _state = PlaybackMediaClassificationState.Recognized;
            _container = container;
            _basis = basis;
            ClearPrefix();
            return Snapshot;
        }

        if (copyLength < payload.Length ||
            _prefixLength == _options.MaxProbeBytes)
        {
            SetLimit(PlaybackMediaClassificationLimit.PrefixBytes);
        }

        return Snapshot;
    }

    public PlaybackMediaClassificationSnapshot Complete()
    {
        ThrowIfDisposed();

        if (_state == PlaybackMediaClassificationState.Collecting)
        {
            _state = PlaybackMediaClassificationState.Ended;
            ClearPrefix();
        }

        return Snapshot;
    }

    public void Dispose()
    {
        var prefix = Interlocked.Exchange(ref _prefix, null);

        if (prefix is null)
        {
            return;
        }

        _prefixLength = 0;
        ArrayPool<byte>.Shared.Return(prefix, clearArray: true);
    }

    private static bool TryClassify(
        ReadOnlySpan<byte> prefix,
        out PlaybackMediaContainer container,
        out PlaybackMediaClassificationBasis basis)
    {
        if (IsFlashVideo(prefix))
        {
            container = PlaybackMediaContainer.FlashVideo;
            basis = PlaybackMediaClassificationBasis.FileHeader;
            return true;
        }

        if (prefix.Length >= AsfHeaderObject.Length &&
            prefix[..AsfHeaderObject.Length].SequenceEqual(AsfHeaderObject))
        {
            container = PlaybackMediaContainer.AdvancedSystemsFormat;
            basis = PlaybackMediaClassificationBasis.FileHeader;
            return true;
        }

        if (TryClassifyEbml(prefix, out container))
        {
            basis = PlaybackMediaClassificationBasis.EbmlDocumentType;
            return true;
        }

        if (ContainsOggCapturePattern(prefix))
        {
            container = PlaybackMediaContainer.Ogg;
            basis = PlaybackMediaClassificationBasis.CapturePattern;
            return true;
        }

        if (ContainsTransportStreamSync(prefix))
        {
            container = PlaybackMediaContainer.MpegTransportStream;
            basis = PlaybackMediaClassificationBasis.RepeatedPacketSync;
            return true;
        }

        container = PlaybackMediaContainer.Unknown;
        basis = default;
        return false;
    }

    private static bool IsFlashVideo(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length < 9 ||
            prefix[0] != (byte)'F' ||
            prefix[1] != (byte)'L' ||
            prefix[2] != (byte)'V' ||
            prefix[3] != 1)
        {
            return false;
        }

        const byte allowedTypeFlags = 0x05;
        var typeFlagsAreValid =
            (prefix[4] & ~allowedTypeFlags) == 0;
        var dataOffset = BinaryPrimitives.ReadUInt32BigEndian(
            prefix[5..9]);
        return typeFlagsAreValid && dataOffset >= 9;
    }

    private static bool ContainsOggCapturePattern(ReadOnlySpan<byte> prefix)
    {
        for (var index = 0; index <= prefix.Length - 5; index++)
        {
            if (prefix[index] == (byte)'O' &&
                prefix[index + 1] == (byte)'g' &&
                prefix[index + 2] == (byte)'g' &&
                prefix[index + 3] == (byte)'S' &&
                prefix[index + 4] == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsTransportStreamSync(
        ReadOnlySpan<byte> prefix)
    {
        var requiredLength =
            ((TransportStreamSyncCount - 1) * TransportStreamPacketSize) + 1;
        var maximumOffset = Math.Min(
            TransportStreamPacketSize - 1,
            prefix.Length - requiredLength);

        for (var offset = 0; offset <= maximumOffset; offset++)
        {
            var matched = true;

            for (var packet = 0;
                packet < TransportStreamSyncCount;
                packet++)
            {
                if (prefix[offset +
                    (packet * TransportStreamPacketSize)] != 0x47)
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryClassifyEbml(
        ReadOnlySpan<byte> prefix,
        out PlaybackMediaContainer container)
    {
        container = PlaybackMediaContainer.Unknown;

        if (prefix.Length < 5 ||
            !prefix[..4].SequenceEqual(EbmlHeaderIdentifier))
        {
            return false;
        }

        if (!TryReadVariableInteger(
            prefix,
            offset: 4,
            maximumLength: 8,
            out var sizeLength,
            out var headerSize))
        {
            return false;
        }

        var cursor = 4 + sizeLength;

        if (headerSize > int.MaxValue ||
            headerSize > (ulong)(prefix.Length - cursor))
        {
            return false;
        }

        var headerEnd = cursor + (int)headerSize;

        while (cursor < headerEnd)
        {
            if (!TryGetVariableIntegerLength(
                prefix[cursor..headerEnd],
                maximumLength: 4,
                out var identifierLength) ||
                cursor + identifierLength > headerEnd)
            {
                return false;
            }

            var identifier =
                prefix.Slice(cursor, identifierLength);
            cursor += identifierLength;

            if (!TryReadVariableInteger(
                prefix[..headerEnd],
                cursor,
                maximumLength: 8,
                out var valueSizeLength,
                out var valueSize))
            {
                return false;
            }

            cursor += valueSizeLength;

            if (valueSize > int.MaxValue ||
                valueSize > (ulong)(headerEnd - cursor))
            {
                return false;
            }

            var value = prefix.Slice(cursor, (int)valueSize);

            if (identifier.SequenceEqual(EbmlDocumentTypeIdentifier))
            {
                if (value.SequenceEqual("webm"u8))
                {
                    container = PlaybackMediaContainer.WebM;
                    return true;
                }

                if (value.SequenceEqual("matroska"u8))
                {
                    container = PlaybackMediaContainer.Matroska;
                    return true;
                }

                return false;
            }

            cursor += (int)valueSize;
        }

        return false;
    }

    private static bool TryReadVariableInteger(
        ReadOnlySpan<byte> source,
        int offset,
        int maximumLength,
        out int length,
        out ulong value)
    {
        length = 0;
        value = 0;

        if (offset < 0 || offset >= source.Length ||
            !TryGetVariableIntegerLength(
                source[offset..],
                maximumLength,
                out length) ||
            offset + length > source.Length)
        {
            return false;
        }

        var marker = (byte)(0x80 >> (length - 1));
        value = (ulong)(source[offset] & (marker - 1));

        for (var index = 1; index < length; index++)
        {
            value = (value << 8) | source[offset + index];
        }

        return true;
    }

    private static bool TryGetVariableIntegerLength(
        ReadOnlySpan<byte> source,
        int maximumLength,
        out int length)
    {
        length = 0;

        if (source.IsEmpty || source[0] == 0)
        {
            return false;
        }

        var marker = 0x80;

        while ((source[0] & marker) == 0)
        {
            length++;
            marker >>= 1;
        }

        length++;
        return length <= maximumLength;
    }

    private void SetLimit(PlaybackMediaClassificationLimit limit)
    {
        _state = PlaybackMediaClassificationState.LimitReached;
        _limit = limit;
        ClearPrefix();
    }

    private void ClearPrefix()
    {
        if (_prefixLength == 0)
        {
            return;
        }

        _prefix.AsSpan(0, _prefixLength).Clear();
        _prefixLength = 0;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_prefix is null, this);
    }
}
