using System.Buffers.Binary;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Media;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Rtmp;

/// <summary>
/// Converts classic RTMP media bodies to FLV without touching encoded samples,
/// DTS or signed composition time. One awaited consumer owns each borrowed packet.
/// </summary>
internal sealed class ObsFlvPublishingSession(
    ChannelId channelId,
    ChannelSessionId sessionId,
    IPcpChannelPacketConsumer consumer,
    ObsRtmpIngestOptions options)
{
    private byte[]? _audio;
    private byte[]? _video;
    private byte[]? _metadata;
    private int _nalLengthBytes;
    private bool _headPublished;
    private bool _mediaStarted;
    private bool _videoEnded;

    public long PacketCount { get; private set; }

    public long ByteCount { get; private set; }

    public async ValueTask AcceptAsync(RtmpMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await AcceptCoreAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            byte? packetType = message.Type is 8 or 9 && message.Payload.Length >= 2 ? message.Payload[1] : null;
            var state = (_metadata is not null ? ObsFlvBootstrapState.Metadata : 0) |
                (_audio is not null ? ObsFlvBootstrapState.AudioConfiguration : 0) |
                (_video is not null ? ObsFlvBootstrapState.VideoConfiguration : 0) |
                (_headPublished ? ObsFlvBootstrapState.HeadPublished : 0) |
                (_mediaStarted ? ObsFlvBootstrapState.MediaStarted : 0) |
                (_videoEnded ? ObsFlvBootstrapState.VideoEnded : 0);
            ObsRtmpRejection.Enrich(exception, ObsRtmpRejection.ForMessage(message) with
            {
                PacketType = packetType,
                ConfigLength = packetType == 0 ? Math.Max(0, message.Payload.Length - (message.Type == 8 ? 2 : 5)) : null,
                BootstrapState = state,
            });
            throw;
        }
    }

    private async ValueTask AcceptCoreAsync(RtmpMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[]? tag = null;
        if (message.Type == 18)
        {
            var values = RtmpAmf0.Decode(message.Payload);
            var framed = values.Count > 0 && Equals(values[0], "@setDataFrame");
            var nameIndex = framed ? 1 : 0;
            if (values.Count <= nameIndex || values[0] is not string || values[nameIndex] is not string)
            {
                throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.Metadata,
                    ObsRtmpRejectionReason.MalformedMetadataEnvelope, "Invalid RTMP metadata envelope.",
                    new() { CommandOrEvent = values.Count > 0 && values[0] is string token ? ObsRtmpRejection.Token(token) : null });
            }

            if (values.Count != nameIndex + 2 ||
                !Equals(values[nameIndex], "onMetaData") ||
                values[nameIndex + 1] is not Dictionary<string, object?>)
            {
                // Decode the entire bounded payload before ignoring an unknown event or
                // unsupported body shape. Malformed/oversized AMF still fails closed.
                return;
            }

            // Strip only the RTMP wrapper string. Preserve the original metadata encoding.
            var payload = message.Payload.AsSpan(framed ? 16 : 0);
            tag = CreateTag(18, message.Timestamp, payload);
            if (!_headPublished)
            {
                Retain(ref _metadata, tag);
                tag = null;
            }
        }
        else if (message.Type == 8)
        {
            var bytes = message.Payload;
            if (bytes.Length < 3 || bytes[0] >> 4 != 10 || bytes[1] > 1)
            {
                throw ObsRtmpRejection.Create(bytes.Length >= 2 && bytes[1] == 0 ?
                    ObsRtmpRejectionStage.AacSequenceHeader : ObsRtmpRejectionStage.MediaPacket,
                    ObsRtmpRejectionReason.UnsupportedAudioPacket, "Only classic AAC audio is supported.");
            }

            if (bytes[1] == 0)
            {
                ValidateAac(bytes.AsSpan(2));
                RetainConfiguration(ref _audio, CreateTag(8, message.Timestamp, bytes));
            }
            else
            {
                if (_audio is null)
                {
                    throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.MediaPacket,
                        ObsRtmpRejectionReason.MissingAudioConfiguration, "AAC configuration is required before media.");
                }

                if (_mediaStarted)
                {
                    tag = CreateTag(8, message.Timestamp, bytes);
                }
            }
        }
        else
        {
            var bytes = message.Payload;
            if (_videoEnded || bytes.Length < 5 || (bytes[0] & 15) != 7 ||
                bytes[0] >> 4 is not (1 or 2) || bytes[1] > 2)
            {
                throw ObsRtmpRejection.Create(bytes.Length >= 2 && bytes[1] == 0 ?
                    ObsRtmpRejectionStage.AvcSequenceHeader : ObsRtmpRejectionStage.MediaPacket,
                    ObsRtmpRejectionReason.UnsupportedVideoPacket, "Only classic AVC video is supported.");
            }

            if (bytes[1] == 0)
            {
                if (bytes[0] >> 4 != 1 || bytes[2] != 0 || bytes[3] != 0 || bytes[4] != 0)
                {
                    throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AvcSequenceHeader,
                        ObsRtmpRejectionReason.MalformedAvcSequenceHeader, "Invalid AVC sequence header.");
                }

                _nalLengthBytes = ValidateAvc(bytes.AsSpan(5));
                RetainConfiguration(ref _video, CreateTag(9, message.Timestamp, bytes));
            }
            else
            {
                if (_video is null)
                {
                    throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.MediaPacket,
                        ObsRtmpRejectionReason.MissingVideoConfiguration, "AVC configuration is required before media.");
                }

                if (bytes[1] == 2)
                {
                    if (bytes.Length != 5 || bytes[0] >> 4 != 1 ||
                        bytes[2] != 0 || bytes[3] != 0 || bytes[4] != 0)
                    {
                        throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.MediaPacket,
                            ObsRtmpRejectionReason.MalformedAvcEndMarker, "Invalid AVC end marker.");
                    }

                    _videoEnded = true;

                    if (_mediaStarted)
                    {
                        tag = CreateTag(9, message.Timestamp, bytes);
                    }
                }
                else
                {
                    var idr = ValidateNalus(bytes.AsSpan(5));
                    if (_headPublished && (_mediaStarted || (bytes[0] >> 4 == 1 && idr)))
                    {
                        _mediaStarted = true;
                        tag = CreateTag(9, message.Timestamp, bytes);
                    }
                }
            }
        }

        if (!_headPublished && _audio is not null && _video is not null)
        {
            var head = new byte[13 + (_metadata?.Length ?? 0) + _audio.Length + _video.Length];
            byte[] header = [0x46, 0x4C, 0x56, 1, 5, 0, 0, 0, 9, 0, 0, 0, 0];
            header.CopyTo(head, 0);
            var offset = 13;
            // The existing managed FLV parser creates its session at the AAC
            // sequence header, after observing AVC. Canonicalize this order
            // regardless of the order OBS sent the configurations.
            foreach (var bootstrap in new[] { _metadata, _video, _audio })
            {
                if (bootstrap is not null)
                {
                    bootstrap.CopyTo(head, offset);
                    offset += bootstrap.Length;
                }
            }

            await PublishAsync(head, 0x64616568, cancellationToken).ConfigureAwait(false);
            _headPublished = true;
        }

        if (tag is not null)
        {
            await PublishAsync(tag, 0x61746164, cancellationToken).ConfigureAwait(false);
        }
    }

    private void RetainConfiguration(ref byte[]? retained, byte[] tag)
    {
        if (retained is not null)
        {
            if (!retained.AsSpan(11, retained.Length - 15).SequenceEqual(tag.AsSpan(11, tag.Length - 15)))
            {
                throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.FlvBootstrap,
                    ObsRtmpRejectionReason.CodecReplacement, "Midstream codec replacement is unsupported.");
            }

            return;
        }

        Retain(ref retained, tag);
    }

    private void Retain(ref byte[]? retained, byte[] tag)
    {
        var length = 13 + (_audio?.Length ?? 0) + (_video?.Length ?? 0) +
            (_metadata?.Length ?? 0) - (retained?.Length ?? 0) + tag.Length;
        if (length > options.MaximumBootstrapBytes)
        {
            throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.FlvBootstrap,
                ObsRtmpRejectionReason.BootstrapSizeLimit, "FLV bootstrap exceeds its budget.");
        }

        retained = tag;
    }

    private async ValueTask PublishAsync(byte[] bytes, uint type, CancellationToken cancellationToken)
    {
        if (ByteCount > uint.MaxValue - (long)bytes.Length)
        {
            throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.FlvBootstrap,
                ObsRtmpRejectionReason.PositionLimit, "The single-generation PCP position budget is exhausted.");
        }

        for (var offset = 0; offset < bytes.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(options.MaximumPacketBytes, bytes.Length - offset);
            var packet = new PcpChannelPacket(
                channelId, sessionId, PacketCount, type, (uint)ByteCount, ByteCount,
                bytes.AsMemory(offset, count), discontinuity: null);
            try
            {
                await consumer.ConsumeAsync(packet, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new ObsRtmpConsumerException(exception);
            }

            PacketCount++;
            ByteCount += count;
            offset += count;
        }
    }

    internal static byte[] CreateTag(byte type, uint timestamp, ReadOnlySpan<byte> payload)
    {
        var bytes = new byte[15 + payload.Length];
        bytes[0] = type;
        RtmpChunkReader.WriteUInt24(bytes.AsSpan(1), (uint)payload.Length);
        RtmpChunkReader.WriteUInt24(bytes.AsSpan(4), timestamp);
        bytes[7] = (byte)(timestamp >> 24);
        payload.CopyTo(bytes.AsSpan(11));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(11 + payload.Length), (uint)(11 + payload.Length));
        return bytes;
    }

    private static void ValidateAac(ReadOnlySpan<byte> bytes)
    {
        if (!AacLcAudioSpecificConfigParser.TryParse(bytes, out var sampleRate, out var channels, out var shortFrame) ||
            channels is not (1 or 2) || shortFrame ||
            sampleRate is not (96_000 or 88_200 or 64_000 or 48_000 or 44_100 or 32_000 or
                24_000 or 22_050 or 16_000 or 12_000 or 11_025 or 8_000 or 7_350))
        {
            throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AacSequenceHeader,
                ObsRtmpRejectionReason.UnsupportedAudioSpecificConfig, "Unsupported AAC-LC configuration.");
        }
    }

    private static int ValidateAvc(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 7 || bytes[0] != 1 ||
            (bytes[4] & 0xFC) != 0xFC || (bytes[5] & 0xE0) != 0xE0 ||
            (bytes[4] & 3) == 2)
        {
            throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AvcSequenceHeader,
                ObsRtmpRejectionReason.InvalidAvcConfiguration, "Invalid AVCDecoderConfigurationRecord.");
        }

        var offset = 6;
        ReadParameterSets(bytes, ref offset, bytes[5] & 31, 7, required: true);
        if (offset == bytes.Length)
        {
            throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AvcSequenceHeader,
                ObsRtmpRejectionReason.MissingAvcParameterSets, "AVC picture parameter sets are missing.");
        }

        var pictureCount = bytes[offset++];
        ReadParameterSets(bytes, ref offset, pictureCount, 8, required: true);
        if (offset < bytes.Length)
        {
            if (bytes[1] is not (100 or 110 or 122 or 144) || bytes.Length - offset < 4 ||
                (bytes[offset] & 0xFC) != 0xFC ||
                (bytes[offset + 1] & 0xF8) != 0xF8 ||
                (bytes[offset + 2] & 0xF8) != 0xF8)
            {
                throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AvcSequenceHeader,
                    ObsRtmpRejectionReason.UnsupportedAvcConfigurationExtension, "Unsupported AVC configuration extension.");
            }

            var count = bytes[offset + 3];
            offset += 4;
            ReadParameterSets(bytes, ref offset, count, 13, required: false);
        }

        if (offset != bytes.Length)
        {
            throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AvcSequenceHeader,
                ObsRtmpRejectionReason.TrailingAvcConfigurationBytes, "Trailing AVC configuration bytes.");
        }

        return (bytes[4] & 3) + 1;
    }

    private static void ReadParameterSets(
        ReadOnlySpan<byte> bytes, ref int offset, int count, int type, bool required)
    {
        if (required && count == 0)
        {
            throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AvcSequenceHeader,
                ObsRtmpRejectionReason.MissingAvcParameterSets, "AVC parameter sets are missing.");
        }

        for (var i = 0; i < count; i++)
        {
            if (bytes.Length - offset < 2)
            {
                throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AvcSequenceHeader,
                    ObsRtmpRejectionReason.TruncatedAvcParameterSet, "Truncated AVC parameter set length.");
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
            offset += 2;
            if (length == 0 || length > bytes.Length - offset ||
                (bytes[offset] & 0x80) != 0 || (bytes[offset] & 31) != type)
            {
                throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AvcSequenceHeader,
                    ObsRtmpRejectionReason.InvalidAvcParameterSet, "Invalid AVC parameter set.");
            }

            offset += length;
        }
    }

    private bool ValidateNalus(ReadOnlySpan<byte> bytes)
    {
        var idr = false;
        var count = 0;
        while (!bytes.IsEmpty)
        {
            if (++count > 4096 || bytes.Length <= _nalLengthBytes)
            {
                throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.MediaPacket,
                    ObsRtmpRejectionReason.InvalidNaluLength, "Invalid AVC NALU lengths.");
            }

            uint length = 0;
            for (var i = 0; i < _nalLengthBytes; i++)
            {
                length = (length << 8) | bytes[i];
            }

            bytes = bytes[_nalLengthBytes..];
            if (length == 0 || length > bytes.Length ||
                (bytes[0] & 0x80) != 0 || (bytes[0] & 31) is 0 or >= 24)
            {
                throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.MediaPacket,
                    ObsRtmpRejectionReason.InvalidNaluBody, "Invalid AVC NALU body.");
            }

            idr |= (bytes[0] & 31) == 5;
            bytes = bytes[(int)length..];
        }

        if (count == 0)
        {
            throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.MediaPacket,
                ObsRtmpRejectionReason.EmptyNaluMessage, "Empty AVC NALU message.");
        }

        return idr;
    }
}

internal sealed class ObsRtmpConsumerException(Exception innerException)
    : Exception("The PCP packet consumer rejected publication.", innerException);
