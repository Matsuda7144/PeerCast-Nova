using System.Buffers.Binary;

namespace PeerCast.Nova.Playback;

public sealed record PlaybackPcpFmp4SegmentAssemblerOptions
{
    public int MaximumSegmentBytes { get; init; } = 16 * 1024 * 1024;

    internal bool IsValid =>
        MaximumSegmentBytes is > 0 &&
        MaximumSegmentBytes <=
            PlaybackBoundedDemuxOptions.MaximumInputBytesPerReadHardLimit;
}
/// <summary>
/// Reassembles only the accepted top-level fMP4 units carried across PCP
/// callbacks. The supplied segment memory is valid only until its callback
/// completes. Incomplete bytes are copied into a bounded, owned carry buffer.
/// </summary>
public sealed class PlaybackPcpFmp4SegmentAssembler : IDisposable
{
    private const uint HeadPacketType = 0x64616568;
    private const uint DataPacketType = 0x61746164;
    private const uint FileTypeBoxType = 0x66747970;
    private const uint MovieBoxType = 0x6d6f6f76;
    private const uint MovieFragmentBoxType = 0x6d6f6f66;
    private const uint MediaDataBoxType = 0x6d646174;
    private const uint UuidBoxType = 0x75756964;

    private readonly int _maximumSegmentBytes;
    private byte[] _buffer = Array.Empty<byte>();
    private int _length;
    private int _boxStart;
    private int? _boxLength;
    private PlaybackFmp4LiveSegmentKind? _segmentKind;
    private int _boxCount;
    private bool _disposed;
    private long _completedInitializationCount;
    private long _completedMediaSegmentCount;

    public PlaybackPcpFmp4SegmentAssembler(
        PlaybackPcpFmp4SegmentAssemblerOptions? options = null)
    {
        options ??= new PlaybackPcpFmp4SegmentAssemblerOptions();
        if (!options.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        _maximumSegmentBytes = options.MaximumSegmentBytes;
        _buffer = new byte[Math.Min(8, _maximumSegmentBytes)];
    }

    public int CarryByteCount => _length;

    public bool HasPartialSegment => _length != 0;

    public PlaybackFmp4LiveSegmentKind? PartialSegmentKind => _segmentKind;

    public long CompletedInitializationCount => _completedInitializationCount;

    public long CompletedMediaSegmentCount => _completedMediaSegmentCount;

    public async ValueTask AppendAsync(
        uint packetType,
        ReadOnlyMemory<byte> payload,
        Func<PlaybackFmp4LiveSegmentKind, ReadOnlyMemory<byte>, CancellationToken, ValueTask>
            onSegment,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(onSegment);
        cancellationToken.ThrowIfCancellationRequested();

        var kind = packetType switch
        {
            HeadPacketType => PlaybackFmp4LiveSegmentKind.Initialization,
            DataPacketType => PlaybackFmp4LiveSegmentKind.Media,
            _ => throw new InvalidDataException(
                "Only PCP HEAD and DATA packets carry fMP4 segment bytes."),
        };

        if (payload.IsEmpty)
        {
            return;
        }

        if (_segmentKind is { } currentKind && currentKind != kind)
        {
            throw new InvalidDataException(
                "A partial fMP4 segment cannot cross PCP HEAD/DATA packet types.");
        }

        var offset = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _segmentKind ??= kind;

            if (_boxLength is null)
            {
                var headerBytes = _length - _boxStart;
                var requiredHeaderBytes = GetRequiredHeaderBytes();
                if (requiredHeaderBytes is null)
                {
                    if (offset >= payload.Length)
                    {
                        break;
                    }

                    var copyCount = Math.Min(
                        8 - headerBytes,
                        payload.Length - offset);
                    AppendBytes(payload.Span.Slice(offset, copyCount));
                    offset += copyCount;
                    continue;
                }

                if (headerBytes < requiredHeaderBytes.Value)
                {
                    if (offset >= payload.Length)
                    {
                        break;
                    }

                    var copyCount = Math.Min(
                        requiredHeaderBytes.Value - headerBytes,
                        payload.Length - offset);
                    AppendBytes(payload.Span.Slice(offset, copyCount));
                    offset += copyCount;
                    continue;
                }

                var parseResult = PlaybackIsoBmffBoxHeaderParser.Parse(
                    _buffer.AsSpan(_boxStart, headerBytes),
                    out var header);
                if (parseResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
                    header is null || header.BoxSize is null)
                {
                    throw new InvalidDataException(
                        "The fMP4 box header is malformed or extends to the end of the stream.");
                }

                if (header.BoxSize.Value > (ulong)_maximumSegmentBytes ||
                    header.BoxSize.Value > int.MaxValue ||
                    header.BoxSize.Value > (ulong)(_maximumSegmentBytes - _boxStart))
                {
                    throw new InvalidDataException(
                        "The declared fMP4 box exceeds the configured segment limit.");
                }

                _boxLength = (int)header.BoxSize.Value;
                ValidateBoxType(header.Type);
            }

            var currentBoxBytes = _length - _boxStart;
            var remainingBoxBytes = _boxLength.Value - currentBoxBytes;
            if (remainingBoxBytes > 0)
            {
                if (offset >= payload.Length)
                {
                    break;
                }

                var copyCount = Math.Min(
                    remainingBoxBytes,
                    payload.Length - offset);
                AppendBytes(payload.Span.Slice(offset, copyCount));
                offset += copyCount;
                if (_length - _boxStart < _boxLength.Value)
                {
                    continue;
                }
            }

            CompleteCurrentBox();
            if (_boxCount == 2)
            {
                var completedKind = _segmentKind!.Value;
                var completedLength = _length;
                try
                {
                    await onSegment(
                        completedKind,
                        _buffer.AsMemory(0, completedLength),
                        cancellationToken).ConfigureAwait(false);
                    if (completedKind == PlaybackFmp4LiveSegmentKind.Initialization)
                    {
                        _completedInitializationCount = checked(
                            _completedInitializationCount + 1);
                    }
                    else
                    {
                        _completedMediaSegmentCount = checked(
                            _completedMediaSegmentCount + 1);
                    }
                }
                finally
                {
                    ResetSegment();
                }

                if (offset >= payload.Length)
                {
                    break;
                }
            }
        }
    }

    public void Complete()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (HasPartialSegment)
        {
            throw new InvalidDataException(
                "The final PCP input ended with an incomplete fMP4 segment.");
        }
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ResetSegment();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ResetSegment();
        _buffer = Array.Empty<byte>();
        _disposed = true;
    }

    private int? GetRequiredHeaderBytes()
    {
        var headerBytes = _length - _boxStart;
        if (headerBytes < 8)
        {
            return null;
        }

        var boxHeader = _buffer.AsSpan(_boxStart, headerBytes);
        var sizeMarker = BinaryPrimitives.ReadUInt32BigEndian(boxHeader[..4]);
        var type = BinaryPrimitives.ReadUInt32BigEndian(boxHeader.Slice(4, 4));
        var baseHeaderBytes = sizeMarker == 1 ? 16 : 8;
        return baseHeaderBytes + (type == UuidBoxType ? 16 : 0);
    }

    private void ValidateBoxType(uint type)
    {
        var expectedType = (_segmentKind, _boxCount) switch
        {
            (PlaybackFmp4LiveSegmentKind.Initialization, 0) => FileTypeBoxType,
            (PlaybackFmp4LiveSegmentKind.Initialization, 1) => MovieBoxType,
            (PlaybackFmp4LiveSegmentKind.Media, 0) => MovieFragmentBoxType,
            (PlaybackFmp4LiveSegmentKind.Media, 1) => MediaDataBoxType,
            _ => 0U,
        };
        if (expectedType == 0 || type != expectedType)
        {
            throw new InvalidDataException(
                "The fMP4 boxes do not match the accepted initialization or media segment shape.");
        }
    }

    private void CompleteCurrentBox()
    {
        _boxCount = checked(_boxCount + 1);
        _boxStart = _length;
        _boxLength = null;
    }

    private void AppendBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return;
        }

        var requiredLength = checked(_length + bytes.Length);
        if (requiredLength > _maximumSegmentBytes)
        {
            throw new InvalidDataException(
                "The fMP4 carry exceeds the configured segment limit.");
        }

        EnsureCapacity(requiredLength);
        bytes.CopyTo(_buffer.AsSpan(_length));
        _length = requiredLength;
    }

    private void EnsureCapacity(int requiredLength)
    {
        if (_buffer.Length >= requiredLength)
        {
            return;
        }

        var doubled = (long)_buffer.Length * 2;
        var nextLength = (int)Math.Min(
            _maximumSegmentBytes,
            Math.Max(requiredLength, Math.Max(256L, doubled)));
        var replacement = new byte[nextLength];
        _buffer.AsSpan(0, _length).CopyTo(replacement);
        _buffer.AsSpan(0, _length).Clear();
        _buffer = replacement;
    }

    private void ResetSegment()
    {
        if (_length != 0)
        {
            _buffer.AsSpan(0, _length).Clear();
        }

        _length = 0;
        _boxStart = 0;
        _boxLength = null;
        _segmentKind = null;
        _boxCount = 0;
    }
}
