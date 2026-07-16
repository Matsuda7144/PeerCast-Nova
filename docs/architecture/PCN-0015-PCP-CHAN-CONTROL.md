# PCN-0015 PCP CHAN Control Decoder

## Purpose

`PcpChannelControlSink` converts incremental PCP Atom callbacks into bounded
control messages without taking ownership of media payload.

```text
PipeReader
   -> PcpAtomDecoder
      -> PcpChannelControlSink
         -> channel ID / metadata / packet header
         -> packet data length only

packet data bytes ---------------------------> future Network data plane
```

## Output model

`PcpChannelControlMessage` contains:

- 16-byte channel ID;
- optional packet type, 32-bit position, and payload length;
- optional channel info metadata;
- optional track metadata.

It contains no `byte[]`, `ReadOnlyMemory<byte>`, stream, Pipe, socket, or
disposable resource.

## Parsing rules

1. CHAN and its known sections must be parent Atoms.
2. Every completed CHAN requires one non-empty 16-byte `id`.
3. A present `pkt` requires `type`, `pos`, and `data`.
4. Known fixed-size fields require their exact observed length.
5. Known strings are copied only after the text limit check.
6. Text decoding is strict UTF-8, trailing-NUL required, embedded NUL rejected.
7. Unknown fields and sections are ignored for legacy compatibility.
8. Duplicate known sections or fields are rejected.
9. The parser uses names and nesting context, not child order.

## Backpressure boundary

The incremental decoder still waits until a complete leaf payload is present in
the current Pipe buffer before invoking a sink. A future live Channel session
must therefore configure a finite PCP content limit and route `pkt/data`
directly to an explicitly bounded relay/player/recorder path. The control sink
does not itself create a queue and does not make 0.0.4 Relay complete.
