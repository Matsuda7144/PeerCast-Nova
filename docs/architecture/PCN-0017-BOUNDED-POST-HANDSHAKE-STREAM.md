# PCN-0017 Bounded Post-Handshake CHAN Stream

## Boundary

`PcpChannelStreamReader` consumes the shared PipeReader after relay-entry and
OK have completed. It joins framing, payload-free control mapping, and one
callback-scoped packet without giving payload ownership to Core.

```text
shared post-OK PipeReader
  -> decode exactly one top-level Atom
     -> CHAN control -> PcpChannelStateAdapter -> Core snapshot
     -> pkt/data -> one finite pooled owner
        -> await IPcpChannelPacketConsumer
           -> dispose owner
              -> decode the next Atom
```

`PcpChannelSession` now supplies this reader with the exact PipeReader used by
the HTTP and HELO/OLEH/OK handshake. PCN-0018 defines that owning session.

## Resource limits and ownership

- Atom content, packet data, and known text limits are positive, finite, and
  validated together before reading.
- Default Atom and packet content limits are 1 MiB; known text remains 4 KiB.
- Only one packet owner is created at a time.
- The packet view and its `ReadOnlyMemory<byte>` are valid only until the
  consumer's `ValueTask` completes.
- Metadata-only CHAN messages allocate no packet owner.
- Decoder or mapping failure disposes any pending owner before reader cleanup.
- No generic event bus or unbounded `Channel<T>` sits in the media path.

## Backpressure and cancellation

The reader advances past the completed Atom, then awaits the packet consumer.
It does not call `ReadAsync` or decode a following buffered top-level Atom until
that callback completes. On a NetworkStream-backed PipeReader this stops Nova's
upstream reads while the consumer is slow; OS socket buffering remains finite
and outside Core.

The session token is passed through the callback. Consumers must honor it
promptly. A consumer that ignores cancellation can still delay its owning
module; `NovaApplication`'s finite module-stop deadline remains the outer safety
boundary and does not make retained payload memory safe.

## Data unit

Each `PcpChannelPacket` exposes during the callback:

- channel and stable local-session identifiers;
- monotonic packet sequence;
- numeric packet type without unverified semantic labels;
- original 32-bit wire position;
- monotonic 64-bit position, or null for a discontinuity;
- explicit discontinuity details when present;
- callback-scoped payload memory.

Core position is updated when a complete CHAN is accepted, before consumer
completion. This is a received-wire position, not a per-consumer playback or
recording acknowledgement.

## Termination

The finite result distinguishes clean connection end from a top-level QUIT and
reports only counts and byte totals. It contains no metadata values, channel
identifier text, IP address, socket, Pipe, buffer owner, or exception.

## Completed by 0.0.3e

- the owning TCP/HTTP/PCP channel session;
- exact post-OK PipeReader handoff with coalesced-byte loopback evidence;
- finite connection, handshake, idle, reconnect, and shutdown behavior;
- packet reception and cancellation against controlled PeerCastStation 6.0.0.0.

## Completed by 0.0.3f

- finite packet-session validation against official Stable 4.1.0 and current
  development 7.1.1 peers;
- fixed privacy-safe fault category, stage, and attempt counts before UI
  exposure.

Per-consumer fan-out, bounded relay-child queues, and slow-child isolation were
added in 0.0.4a. They sit behind this one-consumer upstream backpressure
boundary and do not change its PipeReader ownership rules.
