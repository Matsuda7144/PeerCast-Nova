# PCN-0008 PCP Framing

## Status

Implemented for Milestone 0.0.2b.

## Purpose

TCP is a byte stream. A PCP header or payload may be split across any number of
reads, and one read may contain multiple atoms. The framing layer must therefore
be independent of TCP segmentation.

## Components

### PcpAtomSequenceCodec

Reads a fixed eight-byte atom header from a segmented `ReadOnlySequence<byte>`.
It advances only after a complete and valid header is available.

### PcpAtomDecoder

A stateful, single-consumer decoder that:

- accepts arbitrary sequence boundaries;
- preserves incomplete leaf headers and payloads;
- consumes complete parent headers immediately;
- tracks remaining child counts;
- emits parent-start, leaf, and parent-end events;
- enforces content, child-count, and nesting limits;
- reports absolute byte offsets for failures;
- detects truncated headers, leaf payloads, and parent trees at EOF.

### PcpAtomPipeReader

Owns the `PipeReader` read loop. Every successful `ReadAsync` is paired with one
`AdvanceTo`, and the reader is completed in `finally` when configured to own it.

### PcpAtomWriter

Writes leaf atoms and parent headers to any `IBufferWriter<byte>`. It exists for
tests and future handshake construction; it is not a full protocol message API.

## Payload lifetime

A `PcpLeafAtom.Payload` references memory owned by `PipeReader`.

The payload is valid only until the sink callback returns and the reader advances.
A sink that needs to retain data must call `CopyPayload` or copy into another
explicitly owned buffer.

The framing layer intentionally uses synchronous callbacks to make this lifetime
boundary visible. Asynchronous backpressure and media ownership belong to the
later data-plane design.

## PipeReader rules

The implementation follows these invariants:

1. Call `AdvanceTo` after every successful `ReadAsync`.
2. Never access a `ReadResult.Buffer` after `AdvanceTo`.
3. Preserve an incomplete atom by setting `consumed` before its header.
4. Set `examined` to the current buffer end when all available bytes were checked.
5. Process final buffered data before treating `IsCompleted` as EOF.
6. Complete the reader on success, cancellation, and protocol failure.

## Deliberate limits

The decoder does not yet:

- assign semantic meaning to atom names;
- implement the HTTP entry request;
- implement the PCP hello/response state machine;
- transfer payload ownership to relay/player/recorder;
- recover after a framing error.

A framing error faults the decoder permanently.
