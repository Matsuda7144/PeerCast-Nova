using System.Buffers.Binary;

namespace PeerCast.Nova.Network.Pcp;

public sealed record PcpFlvBootstrapStatusSnapshot(
    bool HeaderRetained,
    bool MetadataRetained,
    bool AudioConfigurationRetained,
    bool VideoConfigurationRetained,
    int HeaderByteCount,
    int MetadataByteCount,
    int AudioConfigurationByteCount,
    int VideoConfigurationByteCount,
    int RetainedByteCount)
{
    public static PcpFlvBootstrapStatusSnapshot Empty =>
        new(false, false, false, false, 0, 0, 0, 0, 0);
}

internal sealed record PcpFlvBootstrapPayloadSnapshot(
    ReadOnlyMemory<byte> Header,
    ReadOnlyMemory<byte> Metadata,
    ReadOnlyMemory<byte> AudioConfiguration,
    ReadOnlyMemory<byte> VideoConfiguration);

/// <summary>
/// Incrementally observes raw FLV bytes and retains only a bounded header,
/// latest metadata tag, and AAC/AVC sequence headers. It does not decode or
/// rewrite the stream.
/// </summary>
internal sealed class PcpFlvBootstrapRetention
{
    private const int FlvHeaderLength = 13;
    private const int TagHeaderLength = 11;
    private const int PreviousTagSizeLength = 4;
    private const int MinimumVideoSequenceTagLength =
        TagHeaderLength + 6 + PreviousTagSizeLength;

    private readonly object _gate = new();
    private readonly PcpFlvBootstrapRetentionOptions _options;
    private readonly byte[] _headerProbe = new byte[FlvHeaderLength];
    private readonly byte[] _tagHeader = new byte[TagHeaderLength];
    private readonly byte[] _currentTagPrefix = new byte[5];
    private readonly byte[] _previousTagSizeProbe = new byte[PreviousTagSizeLength];

    private byte[]? _header;
    private byte[]? _metadata;
    private byte[]? _audioConfiguration;
    private byte[]? _videoConfiguration;
    private byte[]? _currentTag;
    private int _headerProbeLength;
    private int _tagHeaderLength;
    private int _currentTagOffset;
    private int _currentTagRemaining;
    private byte _currentTagType;
    private int _currentDataSize;
    private int _currentTagBytesObserved;
    private int _currentTagPrefixLength;
    private int _previousTagSizeProbeLength;
    private int _retainedByteCount;
    private bool _headerResolved;
    private bool _disabled;

    public PcpFlvBootstrapRetention(
        PcpFlvBootstrapRetentionOptions? options = null)
    {
        _options = options is null
            ? new PcpFlvBootstrapRetentionOptions()
            : options with { };
        _options.Validate();
    }

    public PcpFlvBootstrapStatusSnapshot StatusSnapshot
    {
        get
        {
            lock (_gate)
            {
                return new(
                    _header is not null,
                    _metadata is not null,
                    _audioConfiguration is not null,
                    _videoConfiguration is not null,
                    _header?.Length ?? 0,
                    _metadata?.Length ?? 0,
                    _audioConfiguration?.Length ?? 0,
                    _videoConfiguration?.Length ?? 0,
                    _retainedByteCount);
            }
        }
    }

    public PcpFlvBootstrapPayloadSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new(
                AsMemory(_header),
                AsMemory(_metadata),
                AsMemory(_audioConfiguration),
                AsMemory(_videoConfiguration));
        }
    }

    public void Observe(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return;
        }

        lock (_gate)
        {
            if (_disabled)
            {
                return;
            }

            var span = payload.Span;
            var offset = 0;

            if (!_headerResolved)
            {
                var copyLength = Math.Min(
                    FlvHeaderLength - _headerProbeLength,
                    span.Length);
                span[..copyLength].CopyTo(
                    _headerProbe.AsSpan(_headerProbeLength));
                _headerProbeLength += copyLength;
                offset += copyLength;

                if (_headerProbeLength < FlvHeaderLength)
                {
                    return;
                }

                if (IsValidHeader(_headerProbe))
                {
                    Retain(ref _header, _headerProbe.ToArray());
                }
                else
                {
                    ProcessTagBytes(_headerProbe);
                }

                _headerResolved = true;
                _headerProbeLength = 0;
                if (_disabled)
                {
                    return;
                }
            }

            ProcessTagBytes(span[offset..]);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            ClearArray(ref _header);
            ClearArray(ref _metadata);
            ClearArray(ref _audioConfiguration);
            ClearArray(ref _videoConfiguration);
            ClearArray(ref _currentTag);
            Array.Clear(_headerProbe);
            Array.Clear(_tagHeader);
            Array.Clear(_currentTagPrefix);
            Array.Clear(_previousTagSizeProbe);
            _headerProbeLength = 0;
            _tagHeaderLength = 0;
            _currentTagOffset = 0;
            _currentTagRemaining = 0;
            _currentTagType = 0;
            _currentDataSize = 0;
            _currentTagBytesObserved = 0;
            _currentTagPrefixLength = 0;
            _previousTagSizeProbeLength = 0;
            _retainedByteCount = 0;
            _headerResolved = false;
            _disabled = false;
        }
    }

    private void ProcessTagBytes(ReadOnlySpan<byte> bytes)
    {
        var offset = 0;
        while (offset < bytes.Length && !_disabled)
        {
            if (_currentTag is not null || _currentTagRemaining > 0)
            {
                var copyLength = Math.Min(
                    _currentTagRemaining,
                    bytes.Length - offset);
                ObserveCurrentTagBytes(bytes.Slice(offset, copyLength), copyLength);
                if (_currentTag is not null && copyLength > 0)
                {
                    bytes.Slice(offset, copyLength).CopyTo(
                        _currentTag.AsSpan(_currentTagOffset));
                    _currentTagOffset += copyLength;
                }

                _currentTagRemaining -= copyLength;
                offset += copyLength;
                if (_currentTagRemaining != 0)
                {
                    return;
                }

                CompleteCurrentTag();
                continue;
            }

            var headerBytes = Math.Min(
                TagHeaderLength - _tagHeaderLength,
                bytes.Length - offset);
            bytes.Slice(offset, headerBytes).CopyTo(
                _tagHeader.AsSpan(_tagHeaderLength));
            _tagHeaderLength += headerBytes;
            offset += headerBytes;

            if (_tagHeaderLength < TagHeaderLength)
            {
                return;
            }

            if (!StartCurrentTag())
            {
                _disabled = true;
                return;
            }
        }
    }

    private bool StartCurrentTag()
    {
        _currentTagType = _tagHeader[0];
        if (_currentTagType is not 8 and not 9 and not 18)
        {
            return false;
        }

        _currentDataSize = ReadUInt24(_tagHeader.AsSpan(1, 3));
        _currentTagBytesObserved = 0;
        _currentTagPrefixLength = 0;
        _previousTagSizeProbeLength = 0;
        Array.Clear(_currentTagPrefix);
        Array.Clear(_previousTagSizeProbe);
        var totalLength = checked(
            TagHeaderLength + _currentDataSize + PreviousTagSizeLength);
        var maximumCaptureBytes = _currentTagType == 18
            ? _options.MaximumMetadataTagBytes
            : _options.MaximumConfigurationTagBytes;

        _currentTag = _currentDataSize <= maximumCaptureBytes
            ? new byte[totalLength]
            : null;
        if (_currentTag is not null)
        {
            _tagHeader.CopyTo(_currentTag, 0);
            _currentTagOffset = TagHeaderLength;
        }
        else
        {
            _currentTagOffset = 0;
        }

        _currentTagRemaining = totalLength - TagHeaderLength;
        return true;
    }

    private void CompleteCurrentTag()
    {
        var previousTagSizeValid = _previousTagSizeProbeLength ==
                PreviousTagSizeLength &&
            BinaryPrimitives.ReadUInt32BigEndian(_previousTagSizeProbe) ==
                checked((uint)(TagHeaderLength + _currentDataSize));
        var completeTagValid = _currentTag is not null
            ? IsCompleteTag(_currentTag, _currentDataSize)
            : previousTagSizeValid;

        if (_currentTag is not null && completeTagValid)
        {
            switch (_currentTagType)
            {
                case 18:
                    Retain(ref _metadata, _currentTag);
                    break;
                case 8 when IsAacSequenceHeader(
                    _currentTag,
                    _currentDataSize):
                    Retain(ref _audioConfiguration, _currentTag);
                    break;
                case 9 when IsAvcSequenceHeader(
                    _currentTag,
                    _currentDataSize):
                    Retain(ref _videoConfiguration, _currentTag);
                    break;
            }
        }

        _currentTag = null;
        _currentTagOffset = 0;
        _currentTagRemaining = 0;
        _currentTagType = 0;
        _currentDataSize = 0;
        _currentTagBytesObserved = 0;
        _currentTagPrefixLength = 0;
        _previousTagSizeProbeLength = 0;
        _tagHeaderLength = 0;
    }

    private void ObserveCurrentTagBytes(ReadOnlySpan<byte> bytes, int length)
    {
        if (length <= 0)
        {
            return;
        }

        var segmentStart = _currentTagBytesObserved;
        var segmentEnd = checked(segmentStart + length);
        var dataStart = Math.Min(segmentStart, _currentDataSize);
        var dataEnd = Math.Min(segmentEnd, _currentDataSize);
        if (dataEnd > dataStart && _currentTagPrefixLength < _currentTagPrefix.Length)
        {
            var prefixStart = Math.Max(dataStart, _currentTagPrefixLength);
            var prefixEnd = Math.Min(dataEnd, _currentTagPrefix.Length);
            if (prefixEnd > prefixStart)
            {
                bytes.Slice(
                        prefixStart - segmentStart,
                        prefixEnd - prefixStart)
                    .CopyTo(_currentTagPrefix.AsSpan(prefixStart));
                _currentTagPrefixLength = prefixEnd;
            }
        }

        var previousStart = Math.Max(segmentStart, _currentDataSize);
        var previousEnd = Math.Min(
            segmentEnd,
            _currentDataSize + PreviousTagSizeLength);
        if (previousEnd > previousStart)
        {
            var destinationOffset = previousStart - _currentDataSize;
            var copyLength = previousEnd - previousStart;
            bytes.Slice(
                    previousStart - segmentStart,
                    copyLength)
                .CopyTo(_previousTagSizeProbe.AsSpan(destinationOffset));
            _previousTagSizeProbeLength = Math.Max(
                _previousTagSizeProbeLength,
                destinationOffset + copyLength);
        }

        _currentTagBytesObserved = segmentEnd;
    }

    private bool IsLiveAudioTag()
    {
        if (_currentTagType != 8 ||
            _currentDataSize == 0 ||
            _currentTagPrefixLength == 0)
        {
            return false;
        }

        return (_currentTagPrefix[0] >> 4) != 10 ||
            (_currentTagPrefixLength >= 2 && _currentTagPrefix[1] == 1);
    }

    private bool IsLiveVideoTag()
    {
        if (_currentTagType != 9 ||
            _currentDataSize == 0 ||
            _currentTagPrefixLength == 0)
        {
            return false;
        }

        return (_currentTagPrefix[0] & 0x0f) != 7 ||
            (_currentTagPrefixLength >= 2 && _currentTagPrefix[1] == 1);
    }

    private void Retain(ref byte[]? slot, byte[] value)
    {
        var replacedLength = slot?.Length ?? 0;
        var nextTotal = checked(_retainedByteCount - replacedLength + value.Length);
        if (nextTotal > _options.MaximumTotalRetainedBytes)
        {
            return;
        }

        var old = slot;
        slot = value;
        _retainedByteCount = nextTotal;
        if (old is not null && !ReferenceEquals(old, value))
        {
            Array.Clear(old);
        }
    }

    private static bool IsValidHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length != FlvHeaderLength ||
            header[0] != (byte)'F' ||
            header[1] != (byte)'L' ||
            header[2] != (byte)'V')
        {
            return false;
        }

        var flags = header[4];
        return header[3] == 1 &&
            flags != 0 &&
            (flags & ~0x05) == 0 &&
            BinaryPrimitives.ReadUInt32BigEndian(header[5..9]) == 9 &&
            BinaryPrimitives.ReadUInt32BigEndian(header[9..13]) == 0;
    }

    private static bool IsCompleteTag(ReadOnlySpan<byte> tag, int dataSize) =>
        tag.Length == TagHeaderLength + dataSize + PreviousTagSizeLength &&
        BinaryPrimitives.ReadUInt32BigEndian(tag[^PreviousTagSizeLength..]) ==
            checked((uint)(TagHeaderLength + dataSize));

    private static bool IsAacSequenceHeader(ReadOnlySpan<byte> tag, int dataSize) =>
        dataSize >= 3 &&
        (tag[TagHeaderLength] >> 4) == 10 &&
        tag[TagHeaderLength + 1] == 0;

    private static bool IsAvcSequenceHeader(ReadOnlySpan<byte> tag, int dataSize) =>
        dataSize >= 6 &&
        (tag[TagHeaderLength] & 0x0f) == 7 &&
        (tag[TagHeaderLength] >> 4) == 1 &&
        tag[TagHeaderLength + 1] == 0 &&
        tag[TagHeaderLength + 2] == 0 &&
        tag[TagHeaderLength + 3] == 0 &&
        tag[TagHeaderLength + 4] == 0 &&
        tag.Length >= MinimumVideoSequenceTagLength;

    private static int ReadUInt24(ReadOnlySpan<byte> value) =>
        (value[0] << 16) | (value[1] << 8) | value[2];

    private static ReadOnlyMemory<byte> AsMemory(byte[]? value) =>
        value is null ? ReadOnlyMemory<byte>.Empty : value.ToArray();

    private static void ClearArray(ref byte[]? value)
    {
        if (value is not null)
        {
            Array.Clear(value);
            value = null;
        }
    }
}
