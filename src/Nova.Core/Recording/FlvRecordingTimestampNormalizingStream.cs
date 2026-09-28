using System.Buffers.Binary;

namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Validates and rebases actual FLV audio/video media-tag timestamps onto one
/// shared recording timeline. It owns and disposes the wrapped source.
/// </summary>
internal sealed class FlvRecordingTimestampNormalizingStream : Stream
{
    private const byte AudioTagType = 8;
    private const byte VideoTagType = 9;
    private const byte ScriptTagType = 18;
    private const byte AvcCodecId = 7;
    private const byte AacSoundFormat = 10;
    private const byte AacSequenceHeader = 0;
    private const byte AacRaw = 1;
    private const byte AvcSequenceHeader = 0;
    private const byte AvcNalu = 1;
    private const byte AvcEndOfSequence = 2;
    private const int FlvFixedHeaderLength = 9;
    private const int TagHeaderLength = 11;
    private const int PreviousTagSizeLength = 4;
    private const int MaximumFlvHeaderLength = 64 * 1024;
    private const int MaximumTagDataLength = 0x00FF_FFFF;

    private readonly Stream source;
    private byte[]? pendingOutput;
    private int pendingOutputOffset;
    private uint? baselineTimestamp;
    private uint? lastMediaTimestamp;
    private bool headerRead;
    private bool sourceEnded;
    private bool isDisposed;

    public FlvRecordingTimestampNormalizingStream(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException(
                "The FLV recording source stream must be readable.",
                nameof(source));
        }

        this.source = source;
    }

    public override bool CanRead => !isDisposed && source.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadAsync(
                buffer.AsMemory(offset, count),
                CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (buffer.Length == 0)
        {
            return 0;
        }

        if (pendingOutput is null)
        {
            if (sourceEnded)
            {
                return 0;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!headerRead)
            {
                pendingOutput = await ReadHeaderAsync(
                    cancellationToken).ConfigureAwait(false);
                headerRead = true;
            }
            else
            {
                pendingOutput = await ReadNextTagAsync(
                    cancellationToken).ConfigureAwait(false);

                if (pendingOutput is null)
                {
                    sourceEnded = true;
                    return 0;
                }
            }

            pendingOutputOffset = 0;
        }

        var count = Math.Min(
            buffer.Length,
            pendingOutput.Length - pendingOutputOffset);
        pendingOutput.AsMemory(
                pendingOutputOffset,
                count)
            .CopyTo(buffer);
        pendingOutputOffset += count;

        if (pendingOutputOffset == pendingOutput.Length)
        {
            pendingOutput = null;
            pendingOutputOffset = 0;
        }

        return count;
    }

    public override void Flush() => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !isDisposed)
        {
            isDisposed = true;
            source.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            if (!isDisposed)
            {
                isDisposed = true;
                await source.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await base.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }

    private async ValueTask<byte[]> ReadHeaderAsync(
        CancellationToken cancellationToken)
    {
        var fixedHeader = new byte[FlvFixedHeaderLength];
        _ = await ReadExactlyAsync(
            fixedHeader,
            allowCleanEnd: false,
            "FLV header",
            cancellationToken).ConfigureAwait(false);

        if (fixedHeader[0] != (byte)'F' ||
            fixedHeader[1] != (byte)'L' ||
            fixedHeader[2] != (byte)'V')
        {
            throw InvalidFlv("The source does not have an FLV signature.");
        }

        if (fixedHeader[3] != 1 || (fixedHeader[4] & ~0x05) != 0)
        {
            throw InvalidFlv("The FLV version or flags are invalid.");
        }

        var dataOffset = BinaryPrimitives.ReadUInt32BigEndian(
            fixedHeader.AsSpan(5, 4));
        if (dataOffset < FlvFixedHeaderLength ||
            dataOffset > MaximumFlvHeaderLength - PreviousTagSizeLength)
        {
            throw InvalidFlv("The FLV header offset is outside supported bounds.");
        }

        var fullHeaderLength = checked((int)dataOffset + PreviousTagSizeLength);
        var header = new byte[fullHeaderLength];
        fixedHeader.CopyTo(header, 0);
        _ = await ReadExactlyAsync(
            header.AsMemory(FlvFixedHeaderLength),
            allowCleanEnd: false,
            "FLV header extension",
            cancellationToken).ConfigureAwait(false);

        var previousTagSizeOffset = checked((int)dataOffset);
        if (BinaryPrimitives.ReadUInt32BigEndian(
                header.AsSpan(previousTagSizeOffset, PreviousTagSizeLength)) != 0)
        {
            throw InvalidFlv("The FLV initial PreviousTagSize is not zero.");
        }

        return header;
    }

    private async ValueTask<byte[]?> ReadNextTagAsync(
        CancellationToken cancellationToken)
    {
        var tagHeader = new byte[TagHeaderLength];
        if (!await ReadExactlyAsync(
                tagHeader,
                allowCleanEnd: true,
                "FLV tag header",
                cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var dataSize = ReadUInt24(tagHeader.AsSpan(1, 3));
        if (dataSize > MaximumTagDataLength)
        {
            throw InvalidFlv("The FLV tag data size is invalid.");
        }

        var dataLength = checked((int)dataSize);
        var tagLength = checked(
            TagHeaderLength + dataLength + PreviousTagSizeLength);
        var tag = new byte[tagLength];
        tagHeader.CopyTo(tag, 0);
        _ = await ReadExactlyAsync(
            tag.AsMemory(TagHeaderLength),
            allowCleanEnd: false,
            "FLV tag data and PreviousTagSize",
            cancellationToken).ConfigureAwait(false);

        if (ReadUInt24(tag.AsSpan(8, 3)) != 0)
        {
            throw InvalidFlv("The FLV tag StreamID is not zero.");
        }

        var previousTagSizeOffset = TagHeaderLength + dataLength;
        var expectedPreviousTagSize = checked((uint)(TagHeaderLength + dataLength));
        var previousTagSize = BinaryPrimitives.ReadUInt32BigEndian(
            tag.AsSpan(previousTagSizeOffset, PreviousTagSizeLength));
        if (previousTagSize != expectedPreviousTagSize)
        {
            throw InvalidFlv("The FLV tag PreviousTagSize is inconsistent.");
        }

        var body = tag.AsSpan(TagHeaderLength, dataLength);
        var isMediaTag = tag[0] switch
        {
            AudioTagType => IsAudioMediaTag(body),
            VideoTagType => IsVideoMediaTag(body),
            ScriptTagType => false,
            _ => throw InvalidFlv("The FLV tag type is unsupported."),
        };

        if (isMediaTag)
        {
            var timestamp = ReadTimestamp(tag);
            if (lastMediaTimestamp is { } previousTimestamp &&
                timestamp < previousTimestamp)
            {
                throw InvalidFlv("The FLV media timestamp moved backwards.");
            }

            baselineTimestamp ??= timestamp;
            lastMediaTimestamp = timestamp;
            WriteTimestamp(tag, timestamp - baselineTimestamp.Value);
        }

        return tag;
    }

    private async ValueTask<bool> ReadExactlyAsync(
        Memory<byte> destination,
        bool allowCleanEnd,
        string description,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < destination.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await source.ReadAsync(
                destination[totalRead..],
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (allowCleanEnd && totalRead == 0)
                {
                    return false;
                }

                throw InvalidFlv($"The {description} is truncated.");
            }

            totalRead += read;
        }

        return true;
    }

    private static bool IsAudioMediaTag(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
        {
            throw InvalidFlv("The FLV audio tag has no audio header.");
        }

        var soundFormat = (byte)(body[0] >> 4);
        if (soundFormat != AacSoundFormat)
        {
            return true;
        }

        if (body.Length < 2)
        {
            throw InvalidFlv("The FLV AAC tag is missing its packet type.");
        }

        return body[1] switch
        {
            AacSequenceHeader => false,
            AacRaw => true,
            _ => throw InvalidFlv("The FLV AAC packet type is unsupported."),
        };
    }

    private static bool IsVideoMediaTag(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
        {
            throw InvalidFlv("The FLV video tag has no video header.");
        }

        var codecId = (byte)(body[0] & 0x0F);
        if (codecId != AvcCodecId)
        {
            return true;
        }

        if (body.Length < 5)
        {
            throw InvalidFlv("The FLV AVC tag is missing its packet header.");
        }

        return body[1] switch
        {
            AvcSequenceHeader => false,
            AvcNalu => true,
            AvcEndOfSequence => false,
            _ => throw InvalidFlv("The FLV AVC packet type is unsupported."),
        };
    }

    private static uint ReadTimestamp(ReadOnlySpan<byte> tag)
    {
        return ((uint)tag[7] << 24) |
               ((uint)tag[4] << 16) |
               ((uint)tag[5] << 8) |
               tag[6];
    }

    private static void WriteTimestamp(Span<byte> tag, uint timestamp)
    {
        tag[4] = (byte)(timestamp >> 16);
        tag[5] = (byte)(timestamp >> 8);
        tag[6] = (byte)timestamp;
        tag[7] = (byte)(timestamp >> 24);
    }

    private static uint ReadUInt24(ReadOnlySpan<byte> value)
    {
        return ((uint)value[0] << 16) |
               ((uint)value[1] << 8) |
               value[2];
    }

    private static RecordingStreamTransformException InvalidFlv(string message)
    {
        return new RecordingStreamTransformException(message);
    }
}
