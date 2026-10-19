using System.Buffers.Binary;

namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// For AVC FLV, retains only a bounded bootstrap while waiting for an IDR.
/// Audio-only and non-AVC inputs fall back to the original FLV byte stream.
/// This stream owns and disposes its source.
/// </summary>
internal sealed class FlvRecordingDecodableStartStream : Stream,
    IRecordingFileWriteOutcomeSource
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
    private const int MaximumFlvTagDataLength = 0x00FF_FFFF;
    private const int MaximumProbeBytes = 1024 * 1024;
    private const int MaximumBootstrapBytes = 1024 * 1024;
    private const int MaximumAvcConfigurationBytes = 1024 * 1024;
    private const int MaximumNalUnitsPerTag = 4096;

    private readonly Stream source;
    private readonly List<byte[]> probeTags = [];
    private readonly Queue<byte[]> fallbackTags = [];
    private readonly List<BootstrapTag> bootstrapTags = [];
    private byte[]? flvHeader;
    private byte[]? pendingOutput;
    private int pendingOutputOffset;
    private int probeBytes;
    private int bootstrapBytes;
    private int? nalLengthSize;
    private GateState gateState;
    private bool headerRead;
    private bool sourceEnded;
    private bool isDisposed;

    public FlvRecordingDecodableStartStream(Stream source)
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

    public bool NoDecodableVideoStart =>
        gateState is GateState.WaitingForIdr or
            GateState.NoDecodableVideoStart;

    public bool HasPendingDecodableStartOutput =>
        gateState == GateState.StartupPrefixPending;

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

        while (true)
        {
            if (pendingOutput is not null)
            {
                return CopyPendingOutput(buffer);
            }

            if (fallbackTags.Count > 0)
            {
                pendingOutput = fallbackTags.Dequeue();
                pendingOutputOffset = 0;
                continue;
            }

            if (gateState is GateState.PassThrough or GateState.Fallback)
            {
                if (sourceEnded)
                {
                    return 0;
                }

                return await source.ReadAsync(
                    buffer,
                    cancellationToken).ConfigureAwait(false);
            }

            if (sourceEnded)
            {
                return 0;
            }

            if (!headerRead)
            {
                cancellationToken.ThrowIfCancellationRequested();
                flvHeader = await ReadHeaderAsync(
                    cancellationToken).ConfigureAwait(false);
                headerRead = true;

                if ((flvHeader[4] & 0x01) == 0)
                {
                    gateState = GateState.PassThrough;
                    pendingOutput = flvHeader;
                    pendingOutputOffset = 0;
                }
                else
                {
                    gateState = GateState.Probing;
                }

                continue;
            }

            byte[]? tag;
            try
            {
                tag = await ReadTagAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested &&
                      gateState == GateState.Probing)
            {
                BeginFallback(includeTag: null);
                continue;
            }

            if (tag is null)
            {
                sourceEnded = true;
                if (gateState == GateState.Probing)
                {
                    BeginFallback(includeTag: null);
                    continue;
                }

                if (gateState == GateState.WaitingForIdr)
                {
                    BeginNoDecodableVideoStart();
                    continue;
                }

                return 0;
            }

            if (gateState == GateState.Probing)
            {
                ProcessProbeTag(tag);
            }
            else if (gateState == GateState.WaitingForIdr)
            {
                ProcessWaitingForIdrTag(tag);
            }
        }
    }

    public override void Flush() => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

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
            throw InvalidFlv("The source does not have an FLV signature.", RecordingTransformThrowSite.DecodableStartSignature);
        }

        if (fixedHeader[3] != 1 || (fixedHeader[4] & ~0x05) != 0)
        {
            throw InvalidFlv("The FLV version or flags are invalid.", RecordingTransformThrowSite.DecodableStartVersionOrFlags);
        }

        var dataOffset = BinaryPrimitives.ReadUInt32BigEndian(
            fixedHeader.AsSpan(5, 4));
        if (dataOffset < FlvFixedHeaderLength ||
            dataOffset > MaximumFlvHeaderLength - PreviousTagSizeLength)
        {
            throw InvalidFlv("The FLV header offset is outside supported bounds.", RecordingTransformThrowSite.DecodableStartHeaderOffset);
        }

        var fullHeaderLength = checked((int)dataOffset + PreviousTagSizeLength);
        var header = new byte[fullHeaderLength];
        fixedHeader.CopyTo(header, 0);
        _ = await ReadExactlyAsync(
            header.AsMemory(FlvFixedHeaderLength),
            allowCleanEnd: false,
            "FLV header extension",
            cancellationToken).ConfigureAwait(false);

        if (BinaryPrimitives.ReadUInt32BigEndian(
                header.AsSpan((int)dataOffset, PreviousTagSizeLength)) != 0)
        {
            throw InvalidFlv("The FLV initial PreviousTagSize is not zero.", RecordingTransformThrowSite.DecodableStartInitialPreviousTagSize);
        }

        return header;
    }

    private async ValueTask<byte[]?> ReadTagAsync(
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
        if (dataSize > MaximumFlvTagDataLength)
        {
            throw InvalidFlv("The FLV tag data size is invalid.", RecordingTransformThrowSite.DecodableStartTagDataSize);
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
            throw InvalidFlv("The FLV tag StreamID is not zero.", RecordingTransformThrowSite.DecodableStartTagStreamId);
        }

        var expectedPreviousTagSize = checked((uint)(TagHeaderLength + dataLength));
        var previousTagSize = BinaryPrimitives.ReadUInt32BigEndian(
            tag.AsSpan(TagHeaderLength + dataLength, PreviousTagSizeLength));
        if (previousTagSize != expectedPreviousTagSize)
        {
            throw InvalidFlv("The FLV tag PreviousTagSize is inconsistent.", RecordingTransformThrowSite.DecodableStartPreviousTagSize);
        }

        _ = GetMediaClassification(tag);
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

                throw InvalidFlv($"The {description} is truncated.", RecordingTransformThrowSite.DecodableStartReadExactlyTruncated);
            }

            totalRead += read;
        }

        return true;
    }

    private void ProcessProbeTag(byte[] tag)
    {
        var type = tag[0];
        var body = tag.AsSpan(TagHeaderLength, checked((int)ReadUInt24(tag.AsSpan(1, 3))));

        if (type == VideoTagType)
        {
            if ((body[0] & 0x0F) == AvcCodecId)
            {
                BeginAvcGate(tag);
                return;
            }

            BeginFallback(tag);
            return;
        }

        if ((long)probeBytes + tag.Length > MaximumProbeBytes)
        {
            BeginFallback(tag);
            return;
        }

        probeTags.Add(tag);
        probeBytes += tag.Length;
    }

    private void BeginAvcGate(byte[] firstAvcTag)
    {
        gateState = GateState.WaitingForIdr;

        foreach (var tag in probeTags)
        {
            if (tag[0] == ScriptTagType)
            {
                AddBootstrapTag(BootstrapTagKind.ScriptData, tag);
            }
            else if (tag[0] == AudioTagType &&
                     !GetMediaClassification(tag))
            {
                AddBootstrapTag(BootstrapTagKind.AacSequenceHeader, tag);
            }
        }

        probeTags.Clear();
        probeBytes = 0;
        ProcessWaitingForIdrTag(firstAvcTag);
    }

    private void ProcessWaitingForIdrTag(byte[] tag)
    {
        switch (tag[0])
        {
            case ScriptTagType:
                AddBootstrapTag(BootstrapTagKind.ScriptData, tag);
                return;

            case AudioTagType:
                if (!GetMediaClassification(tag))
                {
                    AddBootstrapTag(BootstrapTagKind.AacSequenceHeader, tag);
                }

                return;

            case VideoTagType:
                ProcessAvcVideoTag(tag);
                return;

            default:
                throw InvalidFlv("The FLV tag type is unsupported.", RecordingTransformThrowSite.DecodableStartWaitingTagType);
        }
    }

    private void ProcessAvcVideoTag(byte[] tag)
    {
        var body = tag.AsSpan(TagHeaderLength, checked((int)ReadUInt24(tag.AsSpan(1, 3))));
        if (body.Length < 5 || (body[0] & 0x0F) != AvcCodecId)
        {
            return;
        }

        switch (body[1])
        {
            case AvcSequenceHeader:
                if (!TryReadAvcConfiguration(
                        body[5..],
                        out var currentNalLengthSize))
                {
                    throw InvalidFlv("The AVCDecoderConfigurationRecord is invalid or unsupported.", RecordingTransformThrowSite.DecodableStartAvcConfiguration);
                }

                if (body.Length - 5 > MaximumAvcConfigurationBytes)
                {
                    throw InvalidFlv("The AVCDecoderConfigurationRecord exceeds supported bounds.", RecordingTransformThrowSite.DecodableStartAvcConfigurationBounds);
                }

                nalLengthSize = currentNalLengthSize;
                AddBootstrapTag(BootstrapTagKind.AvcSequenceHeader, tag);
                return;

            case AvcNalu:
                if (nalLengthSize is not { } lengthSize)
                {
                    return;
                }

                if (ContainsIdrNalUnit(body[5..], lengthSize))
                {
                    BeginRecordingAtIdr(tag);
                }

                return;

            case AvcEndOfSequence:
                return;

            default:
                throw InvalidFlv("The FLV AVC packet type is unsupported.", RecordingTransformThrowSite.DecodableStartGateAvcPacketType);
        }
    }

    private void BeginRecordingAtIdr(byte[] idrTag)
    {
        pendingOutput = BuildBootstrapPrefix(idrTag);
        pendingOutputOffset = 0;
        gateState = GateState.StartupPrefixPending;
        bootstrapTags.Clear();
        bootstrapBytes = 0;
    }

    private void BeginNoDecodableVideoStart()
    {
        gateState = GateState.NoDecodableVideoStart;
        pendingOutput = BuildBootstrapPrefix(idrTag: null);
        pendingOutputOffset = 0;
        bootstrapTags.Clear();
        bootstrapBytes = 0;
    }

    private byte[] BuildBootstrapPrefix(byte[]? idrTag)
    {
        var totalLength = checked(
            flvHeader!.Length +
            bootstrapTags.Sum(static item => item.Bytes.Length) +
            (idrTag?.Length ?? 0));
        var startPrefix = new byte[totalLength];
        var offset = 0;
        CopyTo(startPrefix, ref offset, flvHeader);
        foreach (var bootstrapTag in bootstrapTags)
        {
            CopyTo(startPrefix, ref offset, bootstrapTag.Bytes);
        }

        if (idrTag is not null)
        {
            CopyTo(startPrefix, ref offset, idrTag);
        }

        return startPrefix;
    }

    private void AddBootstrapTag(
        BootstrapTagKind kind,
        byte[] tag)
    {
        if (kind is BootstrapTagKind.AvcSequenceHeader or
            BootstrapTagKind.AacSequenceHeader)
        {
            var index = bootstrapTags.FindIndex(item => item.Kind == kind);
            var priorLength = index >= 0
                ? bootstrapTags[index].Bytes.Length
                : 0;
            var projectedBytes = checked(
                (long)bootstrapBytes - priorLength + tag.Length);
            if (projectedBytes > MaximumBootstrapBytes)
            {
                throw InvalidFlv("The retained FLV bootstrap exceeds supported bounds.", RecordingTransformThrowSite.DecodableStartReplacementBootstrapBounds);
            }

            if (index >= 0)
            {
                bootstrapTags[index] = new BootstrapTag(kind, tag);
            }
            else
            {
                bootstrapTags.Add(new BootstrapTag(kind, tag));
            }

            bootstrapBytes = (int)projectedBytes;
            return;
        }

        var totalBytes = checked((long)bootstrapBytes + tag.Length);
        if (totalBytes > MaximumBootstrapBytes)
        {
            throw InvalidFlv("The retained FLV bootstrap exceeds supported bounds.", RecordingTransformThrowSite.DecodableStartAdditionalBootstrapBounds);
        }

        bootstrapTags.Add(new BootstrapTag(kind, tag));
        bootstrapBytes = (int)totalBytes;
    }

    private void BeginFallback(byte[]? includeTag)
    {
        gateState = GateState.Fallback;
        fallbackTags.Clear();

        foreach (var tag in probeTags)
        {
            fallbackTags.Enqueue(tag);
        }

        if (includeTag is not null)
        {
            fallbackTags.Enqueue(includeTag);
        }

        probeTags.Clear();
        probeBytes = 0;
        pendingOutput = flvHeader;
        pendingOutputOffset = 0;
    }

    private int CopyPendingOutput(Memory<byte> destination)
    {
        var output = pendingOutput!;
        var count = Math.Min(
            destination.Length,
            output.Length - pendingOutputOffset);
        output.AsMemory(pendingOutputOffset, count).CopyTo(destination);
        pendingOutputOffset += count;

        if (pendingOutputOffset == output.Length)
        {
            pendingOutput = null;
            pendingOutputOffset = 0;
            if (gateState == GateState.StartupPrefixPending)
            {
                gateState = GateState.PassThrough;
            }
        }

        return count;
    }

    private static bool GetMediaClassification(byte[] tag)
    {
        var dataLength = checked((int)ReadUInt24(tag.AsSpan(1, 3)));
        var body = tag.AsSpan(TagHeaderLength, dataLength);

        return tag[0] switch
        {
            AudioTagType => IsAudioMediaTag(body),
            VideoTagType => IsVideoMediaTag(body),
            ScriptTagType => false,
            _ => throw InvalidFlv("The FLV tag type is unsupported.", RecordingTransformThrowSite.DecodableStartMediaTagType),
        };
    }

    private static bool IsAudioMediaTag(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
        {
            throw InvalidFlv("The FLV audio tag has no audio header.", RecordingTransformThrowSite.DecodableStartAudioHeader);
        }

        if ((body[0] >> 4) != AacSoundFormat)
        {
            return true;
        }

        if (body.Length < 2)
        {
            throw InvalidFlv("The FLV AAC tag is missing its packet type.", RecordingTransformThrowSite.DecodableStartAacPacketHeader);
        }

        return body[1] switch
        {
            AacSequenceHeader => false,
            AacRaw => true,
            _ => throw InvalidFlv("The FLV AAC packet type is unsupported.", RecordingTransformThrowSite.DecodableStartAacPacketType),
        };
    }

    private static bool IsVideoMediaTag(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
        {
            throw InvalidFlv("The FLV video tag has no video header.", RecordingTransformThrowSite.DecodableStartVideoHeader);
        }

        if ((body[0] & 0x0F) != AvcCodecId)
        {
            return true;
        }

        if (body.Length < 5)
        {
            throw InvalidFlv("The FLV AVC tag is missing its packet header.", RecordingTransformThrowSite.DecodableStartAvcPacketHeader);
        }

        return body[1] switch
        {
            AvcSequenceHeader => false,
            AvcNalu => true,
            AvcEndOfSequence => false,
            _ => throw InvalidFlv("The FLV AVC packet type is unsupported.", RecordingTransformThrowSite.DecodableStartAvcPacketType),
        };
    }

    private static bool TryReadAvcConfiguration(
        ReadOnlySpan<byte> configuration,
        out int nalLengthSize)
    {
        nalLengthSize = 0;
        if (configuration.Length < 7 ||
            configuration.Length > MaximumAvcConfigurationBytes ||
            configuration[0] != 1 ||
            (configuration[4] & 0xFC) != 0xFC ||
            (configuration[5] & 0xE0) != 0xE0)
        {
            return false;
        }

        nalLengthSize = (configuration[4] & 0x03) + 1;
        if (nalLengthSize == 3)
        {
            return false;
        }

        var spsCount = configuration[5] & 0x1F;
        if (spsCount < 1)
        {
            return false;
        }

        var offset = 6;
        for (var index = 0; index < spsCount; index++)
        {
            if (!TryReadConfigurationNal(
                    configuration,
                    ref offset,
                    expectedNalType: 7))
            {
                return false;
            }
        }

        if (offset >= configuration.Length)
        {
            return false;
        }

        var ppsCount = configuration[offset++];
        if (ppsCount < 1)
        {
            return false;
        }

        for (var index = 0; index < ppsCount; index++)
        {
            if (!TryReadConfigurationNal(
                    configuration,
                    ref offset,
                    expectedNalType: 8))
            {
                return false;
            }
        }

        if (offset == configuration.Length)
        {
            return true;
        }

        if (!ProfileHasAvcConfigurationExtension(configuration[1]) ||
            configuration.Length - offset < 4 ||
            (configuration[offset] & 0xFC) != 0xFC ||
            (configuration[offset + 1] & 0xF8) != 0xF8 ||
            (configuration[offset + 2] & 0xF8) != 0xF8)
        {
            return false;
        }

        offset += 3;
        var spsExtensionCount = configuration[offset++];
        for (var index = 0; index < spsExtensionCount; index++)
        {
            if (!TryReadConfigurationNal(
                    configuration,
                    ref offset,
                    expectedNalType: 13))
            {
                return false;
            }
        }

        return offset == configuration.Length;
    }

    private static bool TryReadConfigurationNal(
        ReadOnlySpan<byte> configuration,
        ref int offset,
        int expectedNalType)
    {
        if (configuration.Length - offset < 2)
        {
            return false;
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(
            configuration.Slice(offset, 2));
        offset += 2;
        if (length < 1 || configuration.Length - offset < length)
        {
            return false;
        }

        var nalHeader = configuration[offset];
        var type = nalHeader & 0x1F;
        offset += length;
        return (nalHeader & 0x80) == 0 && type == expectedNalType;
    }

    private static bool ProfileHasAvcConfigurationExtension(byte profileIdc)
    {
        return profileIdc is 44 or 83 or 86 or 100 or 110 or 118 or 122 or
            128 or 134 or 135 or 138 or 139 or 144 or 244;
    }

    private static bool ContainsIdrNalUnit(
        ReadOnlySpan<byte> avccPayload,
        int nalLengthSize)
    {
        if (avccPayload.IsEmpty)
        {
            throw InvalidFlv("The FLV AVC NALU tag has no NAL units.", RecordingTransformThrowSite.DecodableStartEmptyNalPayload);
        }

        var offset = 0;
        var nalCount = 0;
        var hasIdr = false;
        while (offset < avccPayload.Length)
        {
            if (avccPayload.Length - offset < nalLengthSize)
            {
                throw InvalidFlv("The FLV AVC NAL length prefix is truncated.", RecordingTransformThrowSite.DecodableStartNalLengthPrefix);
            }

            uint nalLength = 0;
            for (var index = 0; index < nalLengthSize; index++)
            {
                nalLength = (nalLength << 8) | avccPayload[offset + index];
            }

            offset += nalLengthSize;
            if (nalLength == 0 ||
                nalLength > (uint)(avccPayload.Length - offset))
            {
                throw InvalidFlv(
                    $"The FLV AVC NAL length {nalLength} exceeds the {avccPayload.Length - offset} remaining tag bytes.", RecordingTransformThrowSite.DecodableStartNalLength);
            }

            var nalHeader = avccPayload[offset];
            var nalType = nalHeader & 0x1F;
            if ((nalHeader & 0x80) != 0 || nalType is 0 or > 23)
            {
                throw InvalidFlv("The FLV AVC NAL unit type is unsupported.", RecordingTransformThrowSite.DecodableStartNalType);
            }

            hasIdr |= nalType == 5;
            offset += checked((int)nalLength);
            nalCount++;
            if (nalCount > MaximumNalUnitsPerTag)
            {
                throw InvalidFlv("The FLV AVC NAL unit count exceeds supported bounds.", RecordingTransformThrowSite.DecodableStartNalCount);
            }
        }

        return hasIdr;
    }

    private static uint ReadUInt24(ReadOnlySpan<byte> value)
    {
        return ((uint)value[0] << 16) |
               ((uint)value[1] << 8) |
               value[2];
    }

    private static void CopyTo(
        byte[] destination,
        ref int offset,
        byte[] source)
    {
        source.CopyTo(destination, offset);
        offset += source.Length;
    }

    private static RecordingStreamTransformException InvalidFlv(string message, RecordingTransformThrowSite site)
    {
        return new RecordingStreamTransformException(message, site);
    }

    private enum GateState
    {
        Uninitialized,
        Probing,
        WaitingForIdr,
        StartupPrefixPending,
        NoDecodableVideoStart,
        PassThrough,
        Fallback,
    }

    private enum BootstrapTagKind
    {
        ScriptData,
        AvcSequenceHeader,
        AacSequenceHeader,
    }

    private sealed record BootstrapTag(
        BootstrapTagKind Kind,
        byte[] Bytes);
}
