# PCN-0039 Playback Architecture Review

## Status

Accepted for Milestone 0.0.5a Playback Input Foundation.

Milestone 0.0.5 Playback remains in progress. This review accepts the bounded
player-input boundary; it does not claim format detection, decode, audio/video
output, or end-user playback.

## Dependency decision

```text
future host / composition root
        |
        +---- Nova.Network ---- Nova.Protocol.Pcp
        |          |
        |          +---- callback-scoped PcpChannelPacket
        |
        +---- Nova.Playback
                   |
                   +---- PcpPlaybackLatencyBuffer
                   +---- future format probe / decoder / renderer
```

The concrete dependency is `Nova.Playback -> Nova.Network`. Network remains
unaware of player and UI implementations. Core continues to receive only
immutable channel control status and never transports media payload.

## Ownership and backpressure

The upstream callback owns the source packet only until `ConsumeAsync`
returns. Playback therefore makes one bounded owned copy. The queue is limited
by packet count and payload bytes independently.

Unlike relay fan-out, a slow player is not disconnected on the first full
queue. Playback removes the oldest queued packets and keeps the newest data.
The first retained packet after the gap is marked `LatencySkip`. A PCP position
gap is separately mapped to `Source`; both fixed flags may be combined.

Only queued packets may be evicted. A payload already leased to the reader is
never mutated or reclaimed by the buffer.

## Shutdown decision

`Complete()` stops new ingress and lets the single reader drain pending
packets. `Stop()` stops new ingress, discards pending packets, and completes a
waiting read immediately. Both operations are synchronous and finite because
the buffer creates no decoder task.

Every returned packet is a disposable lease. Status exposes outstanding
packet and byte counts so a future owner can distinguish a completed queue
from a decoder that still holds memory.

## Format and rendering decision

No media format is inferred from PCP response headers. Metadata values remain
untrusted hints. The next slice must first add a bounded content signature
probe and independently document the controlled stream shape. Decoder and
renderer selection follows that evidence.

The initial backend must remain replaceable and UI-independent. Platform or
third-party decoder licensing must be reviewed before adding a dependency.

## Accepted 0.0.5a gate

- separate Playback project with no reverse Network dependency;
- packet- and byte-bounded latest-wins queue;
- one owned payload copy and disposable reader lease;
- explicit source and latency discontinuities;
- graceful drain, immediate stop, and outstanding-lease accounting;
- fixed-field status with no identifiers, metadata, positions, payload, peer
  values, exception text, or free-form message;
- focused deterministic tests for copy lifetime, both overflow limits,
  oversized input, discontinuities, cancellation, concurrency, completion,
  stop, and validation.
