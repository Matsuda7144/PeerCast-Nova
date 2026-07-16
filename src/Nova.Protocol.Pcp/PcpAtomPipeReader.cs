using System.IO.Pipelines;

namespace PeerCast.Nova.Protocol.Pcp;

/// <summary>
/// Drives a <see cref="PcpAtomDecoder"/> from a PipeReader.
///
/// When completeReader is true, this type completes the supplied PipeReader.
/// Each successful ReadAsync is paired with exactly one AdvanceTo call.
/// </summary>
public sealed class PcpAtomPipeReader
{
    private readonly PipeReader _reader;
    private readonly PcpAtomDecoder _decoder;
    private readonly bool _completeReader;
    private int _started;

    public PcpAtomPipeReader(
        PipeReader reader,
        PcpProtocolLimits? limits = null,
        bool completeReader = true)
    {
        ArgumentNullException.ThrowIfNull(reader);

        _reader = reader;
        _decoder = new PcpAtomDecoder(limits);
        _completeReader = completeReader;
    }

    public async Task ReadToEndAsync(
        IPcpAtomSink sink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException(
                "A PCP pipe reader instance can only be run once.");
        }

        Exception? failure = null;

        try
        {
            while (true)
            {
                var readResult = await _reader.ReadAsync(
                    cancellationToken).ConfigureAwait(false);

                var buffer = readResult.Buffer;
                var advanced = false;

                try
                {
                    if (readResult.IsCanceled)
                    {
                        _reader.AdvanceTo(buffer.Start, buffer.End);
                        advanced = true;
                        throw new OperationCanceledException(cancellationToken);
                    }

                    var decodeResult = _decoder.Decode(
                        buffer,
                        readResult.IsCompleted,
                        sink);

                    _reader.AdvanceTo(
                        decodeResult.Consumed,
                        decodeResult.Examined);

                    advanced = true;

                    if (decodeResult.Status == PcpDecodeStatus.ProtocolError)
                    {
                        throw new PcpProtocolException(
                            decodeResult.Error,
                            decodeResult.ErrorOffset);
                    }

                    if (decodeResult.Status == PcpDecodeStatus.Completed)
                    {
                        return;
                    }

                    if (readResult.IsCompleted)
                    {
                        throw new InvalidOperationException(
                            "The PCP decoder requested more data after the pipe completed.");
                    }
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
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            if (_completeReader)
            {
                try
                {
                    await _reader.CompleteAsync(failure).ConfigureAwait(false);
                }
                catch when (failure is not null)
                {
                    // Preserve the primary framing or cancellation exception.
                }
            }
        }
    }
}
