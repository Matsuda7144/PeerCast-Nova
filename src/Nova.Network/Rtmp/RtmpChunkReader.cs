using System.Buffers.Binary;

namespace PeerCast.Nova.Network.Rtmp;

internal sealed record RtmpMessage(byte Type, uint StreamId, uint Timestamp, byte[] Payload)
{
    public int? ChunkStreamId { get; init; }
}

/// <summary>
/// Reassembles chunk formats 0..3 with bounded interleaving. Only incomplete
/// messages retain payload memory; completed bodies are transferred to the caller.
/// </summary>
internal sealed class RtmpChunkReader
{
    private readonly Stream _stream;
    private readonly ObsRtmpIngestOptions _options;
    private readonly Func<uint, CancellationToken, ValueTask> _acknowledge;
    private readonly Dictionary<int, ChunkState> _states = [];
    private readonly byte[] _scratch = new byte[11];
    private int _bufferedBytes;
    private ulong _receivedBytes = 3073;
    private ulong _acknowledgedBytes = 3073;
    private int _chunkBytes = 128;

    public RtmpChunkReader(
        Stream stream,
        ObsRtmpIngestOptions options,
        Func<uint, CancellationToken, ValueTask> acknowledge)
    {
        _stream = stream;
        _options = options;
        _acknowledge = acknowledge;
    }

    public uint AcknowledgementWindow { get; set; } = 2_500_000;

    public void SetChunkBytes(uint value)
    {
        if (value == 0 || value > _options.MaximumChunkBytes)
        {
            throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.RtmpControlMessage,
                ObsRtmpRejectionReason.UnsupportedChunkSize, "Unsupported RTMP chunk size.");
        }

        _chunkBytes = (int)value;
    }

    public void Abort(uint chunkStreamId)
    {
        if (chunkStreamId <= int.MaxValue &&
            _states.TryGetValue((int)chunkStreamId, out var state) &&
            state.Body is not null)
        {
            _bufferedBytes -= state.Length;
            state.Body = null;
            state.Offset = 0;
        }
    }

    public async ValueTask<RtmpMessage> ReadAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await ReadExactlyAsync(_scratch.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            var format = _scratch[0] >> 6;
            int id = _scratch[0] & 63;
            if (id == 0)
            {
                await ReadExactlyAsync(_scratch.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
                id = 64 + _scratch[0];
            }
            else if (id == 1)
            {
                await ReadExactlyAsync(_scratch.AsMemory(0, 2), cancellationToken).ConfigureAwait(false);
                id = 64 + _scratch[0] + (_scratch[1] << 8);
            }

            if (!_states.TryGetValue(id, out var state))
            {
                if (format != 0 || _states.Count == _options.MaximumChunkStreams)
                {
                    throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.RtmpChunkHeader,
                        format != 0 ? ObsRtmpRejectionReason.MissingChunkStreamContext : ObsRtmpRejectionReason.ChunkStreamLimit,
                        "Invalid RTMP chunk stream context.",
                        new() { ChunkStreamId = id, HeaderFormat = (byte)format, AssembledPayloadLength = 0 });
                }

                state = new ChunkState();
                _states.Add(id, state);
            }

            var continuing = state.Body is not null;
            if (continuing && format != 3)
            {
                throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.RtmpChunkAssembly,
                    ObsRtmpRejectionReason.MessageInterrupted, "RTMP message interrupted without abort.",
                    Context(id, format, state, state.Timestamp));
            }

            var headerLength = format switch { 0 => 11, 1 => 7, 2 => 3, _ => 0 };
            uint timeField = state.TimeField;
            if (headerLength != 0)
            {
                await ReadExactlyAsync(_scratch.AsMemory(0, headerLength), cancellationToken).ConfigureAwait(false);
                timeField = ReadUInt24(_scratch);
                state.Extended = timeField == 0xFFFFFF;
                if (format <= 1)
                {
                    state.Length = (int)ReadUInt24(_scratch.AsSpan(3));
                    state.Type = _scratch[6];
                }

                if (format == 0)
                {
                    state.StreamId = BinaryPrimitives.ReadUInt32LittleEndian(_scratch.AsSpan(7));
                }
            }

            if (state.Extended)
            {
                await ReadExactlyAsync(_scratch.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
                var extended = BinaryPrimitives.ReadUInt32BigEndian(_scratch);
                if (format == 3 && extended != state.TimeField)
                {
                    throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.RtmpChunkHeader,
                        ObsRtmpRejectionReason.ExtendedTimestampMismatch, "Inconsistent extended RTMP timestamp.",
                        Context(id, format, state, extended));
                }

                timeField = extended;
            }

            if (!continuing)
            {
                if (state.Length <= 0 || state.Length > _options.MaximumMessageBytes ||
                    state.Length > _options.MaximumBufferedBytes - _bufferedBytes)
                {
                    throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.RtmpChunkAssembly,
                        state.Length <= 0 ? ObsRtmpRejectionReason.InvalidMessageLength :
                        state.Length > _options.MaximumMessageBytes ? ObsRtmpRejectionReason.MessageSizeLimit :
                        ObsRtmpRejectionReason.BufferedBytesLimit,
                        "RTMP message exceeds its memory budget.",
                        Context(id, format, state, format == 0 ? timeField : unchecked(state.Timestamp + timeField)));
                }

                state.Timestamp = format == 0 ? timeField : unchecked(state.Timestamp + timeField);
                state.TimeField = timeField;
                state.Body = new byte[state.Length];
                state.Offset = 0;
                _bufferedBytes += state.Length;
            }

            var body = state.Body!;
            var length = Math.Min(_chunkBytes, state.Length - state.Offset);
            await ReadExactlyAsync(body.AsMemory(state.Offset, length), cancellationToken).ConfigureAwait(false);
            state.Offset += length;
            if (state.Offset == state.Length)
            {
                state.Body = null;
                _bufferedBytes -= state.Length;
                return new RtmpMessage(state.Type, state.StreamId, state.Timestamp, body) { ChunkStreamId = id };
            }
        }
    }

    private async ValueTask ReadExactlyAsync(Memory<byte> bytes, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.IoTimeout);
        try
        {
            while (!bytes.IsEmpty)
            {
                var count = await _stream.ReadAsync(bytes, deadline.Token).ConfigureAwait(false);
                if (count == 0)
                {
                    throw new EndOfStreamException();
                }

                bytes = bytes[count..];
                _receivedBytes += (uint)count;
                if (_receivedBytes - _acknowledgedBytes >= AcknowledgementWindow)
                {
                    await _acknowledge(unchecked((uint)_receivedBytes), cancellationToken).ConfigureAwait(false);
                    _acknowledgedBytes = _receivedBytes;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("RTMP input deadline expired.");
        }
    }

    internal static uint ReadUInt24(ReadOnlySpan<byte> bytes) =>
        (uint)((bytes[0] << 16) | (bytes[1] << 8) | bytes[2]);

    internal static void WriteUInt24(Span<byte> bytes, uint value)
    {
        bytes[0] = (byte)(value >> 16);
        bytes[1] = (byte)(value >> 8);
        bytes[2] = (byte)value;
    }

    private static ObsRtmpRejectionContext Context(int id, int format, ChunkState state, uint timestamp) => new()
    {
        MessageType = state.Type,
        ChunkStreamId = id,
        MessageStreamId = state.StreamId,
        Timestamp = timestamp,
        HeaderFormat = (byte)format,
        DeclaredPayloadLength = state.Length,
        AssembledPayloadLength = state.Body is null ? 0 : state.Offset,
    };

    private sealed class ChunkState
    {
        public int Length;
        public byte Type;
        public uint StreamId;
        public uint Timestamp;
        public uint TimeField;
        public bool Extended;
        public byte[]? Body;
        public int Offset;
    }
}
