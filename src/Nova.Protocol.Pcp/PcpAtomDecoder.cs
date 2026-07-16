using System.Buffers;

namespace PeerCast.Nova.Protocol.Pcp;

/// <summary>
/// Incrementally decodes PCP atoms from arbitrary ReadOnlySequence boundaries.
///
/// Parent headers are consumed as soon as they are complete. Incomplete leaf
/// headers and payloads remain unconsumed so PipeReader can preserve them for
/// the next read.
///
/// This decoder is single-use and not thread-safe.
/// </summary>
public sealed class PcpAtomDecoder
{
    private readonly PcpProtocolLimits _limits;
    private readonly List<ParentFrame> _parents = [];

    private long _absoluteOffset;
    private bool _completed;
    private bool _faulted;

    public PcpAtomDecoder(PcpProtocolLimits? limits = null)
    {
        _limits = limits is null ? new PcpProtocolLimits() : limits with { };
        _limits.Validate();
    }

    public int OpenParentCount => _parents.Count;

    public long ConsumedByteCount => _absoluteOffset;

    public PcpDecodeResult Decode(
        ReadOnlySequence<byte> input,
        bool isFinalBlock,
        IPcpAtomSink sink)
    {
        return Decode(
            input,
            isFinalBlock,
            sink,
            stopAfterTopLevelAtom: false);
    }

    /// <summary>
    /// Decodes available input and can yield immediately after one complete
    /// top-level Atom. Yielding permits an async owner above framing to apply
    /// backpressure before any following top-level Atom is consumed.
    /// </summary>
    public PcpDecodeResult Decode(
        ReadOnlySequence<byte> input,
        bool isFinalBlock,
        IPcpAtomSink sink,
        bool stopAfterTopLevelAtom)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (_completed)
        {
            throw new InvalidOperationException("The PCP decoder has already completed.");
        }

        if (_faulted)
        {
            throw new InvalidOperationException("The PCP decoder is faulted and cannot be reused.");
        }

        try
        {
            return DecodeCore(
                input,
                isFinalBlock,
                sink,
                stopAfterTopLevelAtom);
        }
        catch
        {
            _faulted = true;
            throw;
        }
    }

    private PcpDecodeResult DecodeCore(
        ReadOnlySequence<byte> input,
        bool isFinalBlock,
        IPcpAtomSink sink,
        bool stopAfterTopLevelAtom)
    {
        var reader = new SequenceReader<byte>(input);

        while (true)
        {
            if (reader.End)
            {
                if (!isFinalBlock)
                {
                    return Commit(
                        input,
                        reader,
                        PcpDecodeStatus.NeedMoreData,
                        PcpParseError.NeedMoreData,
                        errorOffset: -1);
                }

                if (_parents.Count != 0)
                {
                    return Fail(
                        input,
                        reader,
                        PcpParseError.UnexpectedEndOfStream,
                        _absoluteOffset + reader.Consumed);
                }

                _completed = true;

                return Commit(
                    input,
                    reader,
                    PcpDecodeStatus.Completed,
                    PcpParseError.None,
                    errorOffset: -1);
            }

            var atomStartOffset = _absoluteOffset + reader.Consumed;
            var probe = reader;

            if (!PcpAtomSequenceCodec.TryReadHeader(
                ref probe,
                _limits,
                out var header,
                out var headerError))
            {
                if (headerError != PcpParseError.NeedMoreData)
                {
                    return Fail(
                        input,
                        reader,
                        headerError,
                        atomStartOffset);
                }

                if (isFinalBlock)
                {
                    return Fail(
                        input,
                        reader,
                        PcpParseError.UnexpectedEndOfStream,
                        atomStartOffset);
                }

                return Commit(
                    input,
                    reader,
                    PcpDecodeStatus.NeedMoreData,
                    PcpParseError.NeedMoreData,
                    errorOffset: -1);
            }

            if (header.IsParent)
            {
                if (_parents.Count >= _limits.MaxNestingDepth)
                {
                    return Fail(
                        input,
                        reader,
                        PcpParseError.NestingDepthLimitExceeded,
                        atomStartOffset);
                }

                if (!TryConsumeParentChild())
                {
                    return Fail(
                        input,
                        reader,
                        PcpParseError.InvalidParentState,
                        atomStartOffset);
                }

                reader = probe;

                var depth = _parents.Count;
                _parents.Add(
                    new ParentFrame(
                        header.Name,
                        header.Value,
                        depth,
                        atomStartOffset));

                sink.OnParentStarted(
                    new PcpParentStarted(
                        header,
                        depth,
                        atomStartOffset));

                var completedTopLevel = CloseCompletedParents(
                    sink,
                    _absoluteOffset + reader.Consumed);

                if (stopAfterTopLevelAtom && completedTopLevel)
                {
                    return CommitTopLevelAtom(reader);
                }

                continue;
            }

            if (probe.Remaining < header.Value)
            {
                if (isFinalBlock)
                {
                    return Fail(
                        input,
                        reader,
                        PcpParseError.UnexpectedEndOfStream,
                        atomStartOffset);
                }

                return Commit(
                    input,
                    reader,
                    PcpDecodeStatus.NeedMoreData,
                    PcpParseError.NeedMoreData,
                    errorOffset: -1);
            }

            var payload = input.Slice(probe.Position, header.Value);
            probe.Advance(header.Value);

            if (!TryConsumeParentChild())
            {
                return Fail(
                    input,
                    reader,
                    PcpParseError.InvalidParentState,
                    atomStartOffset);
            }

            reader = probe;

            var leafDepth = _parents.Count;

            sink.OnLeafAtom(
                new PcpLeafAtom(
                    header,
                    leafDepth,
                    atomStartOffset,
                    payload));

            var closedTopLevel = CloseCompletedParents(
                sink,
                _absoluteOffset + reader.Consumed);

            if (stopAfterTopLevelAtom &&
                (leafDepth == 0 || closedTopLevel))
            {
                return CommitTopLevelAtom(reader);
            }
        }
    }

    private bool TryConsumeParentChild()
    {
        if (_parents.Count == 0)
        {
            return true;
        }

        var index = _parents.Count - 1;
        var parent = _parents[index];

        if (parent.RemainingChildren <= 0)
        {
            return false;
        }

        _parents[index] = parent with
        {
            RemainingChildren = parent.RemainingChildren - 1,
        };

        return true;
    }

    private bool CloseCompletedParents(IPcpAtomSink sink, long endOffset)
    {
        var completedTopLevel = false;

        while (_parents.Count != 0)
        {
            var index = _parents.Count - 1;
            var parent = _parents[index];

            if (parent.RemainingChildren != 0)
            {
                return completedTopLevel;
            }

            _parents.RemoveAt(index);

            sink.OnParentEnded(
                new PcpParentEnded(
                    parent.Name,
                    parent.Depth,
                    parent.StartOffset,
                    endOffset));

            completedTopLevel |= parent.Depth == 0;
        }

        return completedTopLevel;
    }

    private PcpDecodeResult CommitTopLevelAtom(
        SequenceReader<byte> reader)
    {
        var consumed = reader.Position;
        var consumedCount = reader.Consumed;

        _absoluteOffset += consumedCount;

        return new PcpDecodeResult(
            PcpDecodeStatus.TopLevelAtomCompleted,
            consumed,
            consumed,
            PcpParseError.None,
            ErrorOffset: -1);
    }

    private PcpDecodeResult Commit(
        ReadOnlySequence<byte> input,
        SequenceReader<byte> reader,
        PcpDecodeStatus status,
        PcpParseError error,
        long errorOffset)
    {
        var consumed = reader.Position;
        var consumedCount = reader.Consumed;

        _absoluteOffset += consumedCount;

        return new PcpDecodeResult(
            status,
            consumed,
            input.End,
            error,
            errorOffset);
    }

    private PcpDecodeResult Fail(
        ReadOnlySequence<byte> input,
        SequenceReader<byte> reader,
        PcpParseError error,
        long errorOffset)
    {
        _faulted = true;

        return Commit(
            input,
            reader,
            PcpDecodeStatus.ProtocolError,
            error,
            errorOffset);
    }

    private readonly record struct ParentFrame(
        PcpAtomName Name,
        int RemainingChildren,
        int Depth,
        long StartOffset);
}
