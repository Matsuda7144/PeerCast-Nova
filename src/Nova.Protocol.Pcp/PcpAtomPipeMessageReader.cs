using System.Buffers;
using System.IO.Pipelines;

namespace PeerCast.Nova.Protocol.Pcp;

/// <summary>
/// Reads one complete owned atom tree at a time from a shared PipeReader.
///
/// This reader does not complete the supplied PipeReader unless
/// the constructor option `completeReader` is true.
/// </summary>
public sealed class PcpAtomPipeMessageReader : IAsyncDisposable
{
    private readonly PipeReader _reader;
    private readonly PcpProtocolLimits _limits;
    private readonly int _maxBufferedBytes;
    private readonly bool _completeReader;
    private long _consumedBytes;
    private int _disposed;

    public PcpAtomPipeMessageReader(
        PipeReader reader,
        PcpProtocolLimits? limits = null,
        int maxBufferedBytes = 1024 * 1024,
        bool completeReader = false)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (maxBufferedBytes < PcpAtomCodec.HeaderSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBufferedBytes),
                maxBufferedBytes,
                "The buffer limit must fit at least one atom header.");
        }

        _reader = reader;
        _limits = limits is null ? new PcpProtocolLimits() : limits with { };
        _limits.Validate();
        _maxBufferedBytes = maxBufferedBytes;
        _completeReader = completeReader;
    }

    public async ValueTask<PcpAtom> ReadNextAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        while (true)
        {
            var result = await _reader.ReadAsync(
                cancellationToken).ConfigureAwait(false);

            var buffer = result.Buffer;
            var advanced = false;

            try
            {
                if (result.IsCanceled)
                {
                    _reader.AdvanceTo(buffer.Start, buffer.End);
                    advanced = true;
                    throw new OperationCanceledException(cancellationToken);
                }

                var candidate = buffer.Length > _maxBufferedBytes
                    ? buffer.Slice(0, _maxBufferedBytes)
                    : buffer;

                var sequenceReader = new SequenceReader<byte>(candidate);

                if (PcpAtomTreeCodec.TryRead(
                    ref sequenceReader,
                    _limits,
                    out var atom,
                    out var error))
                {
                    var consumed = sequenceReader.Consumed;

                    _reader.AdvanceTo(
                        sequenceReader.Position,
                        sequenceReader.Position);

                    advanced = true;
                    _consumedBytes += consumed;
                    return atom;
                }

                if (buffer.Length > _maxBufferedBytes)
                {
                    throw new PcpProtocolException(
                        PcpParseError.ContentLengthLimitExceeded,
                        _consumedBytes);
                }

                if (error != PcpParseError.NeedMoreData)
                {
                    throw new PcpProtocolException(
                        error,
                        _consumedBytes + sequenceReader.Consumed);
                }

                if (result.IsCompleted)
                {
                    throw new PcpProtocolException(
                        PcpParseError.UnexpectedEndOfStream,
                        _consumedBytes);
                }

                _reader.AdvanceTo(buffer.Start, buffer.End);
                advanced = true;
            }
            finally
            {
                if (!advanced)
                {
                    _reader.AdvanceTo(buffer.Start, buffer.End);
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_completeReader)
        {
            await _reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
    }
}
