# PCN-0040 Bounded Playback Input

## Purpose

`PcpPlaybackLatencyBuffer` is the first Milestone 0.0.5 data-plane component.
It adapts callback-scoped PCP packets to a single player reader without moving
payload bytes through Core or coupling Network to a decoder.

```text
PcpChannelSession
  -> awaited IPcpChannelPacketConsumer callback
     -> PcpPlaybackLatencyBuffer
        -> one owned pooled payload copy
        -> packet-count and byte-count bounds
        -> oldest queued packet eviction when required
        -> disposable PlaybackPacketLease
           -> future bounded format probe / decoder input
```

## Queue policy

Defaults are 64 queued packets and 8 MiB of queued payload. Configuration is
validated against absolute limits of 4,096 packets and 1 GiB.

If a new packet fits individually but the queue is full, the oldest queued
packets are disposed until both limits permit the new packet. The first packet
that remains after the gap is marked with `LatencySkip`. If nothing remains,
the marker is carried to the new packet.

If one packet exceeds the byte limit, it is counted and skipped without
renting payload memory. The next accepted packet carries the gap marker.

## Reader contract

Only one `ReadAsync` call may be active. It returns one
`PlaybackPacketLease`; the payload is valid only until that lease is disposed.
Disposal returns pooled memory and decrements outstanding ownership counters.
Access after disposal throws.

Canceling a read cancels only that observation. It does not stop the input
owner or discard the queue.

## Completion and stop

- `Complete()` transitions to draining and returns end-of-input after the
  queue is empty.
- `Stop()` transitions to stopped, discards every queued packet, and wakes a
  waiting reader.
- a lease already returned to the reader remains valid and visible in
  outstanding counters until disposal.

No background decoder task exists in this slice, so buffer shutdown has no
unbounded callback to await.

## Observability and privacy

`PlaybackInputStatusSnapshot` contains only fixed enums and numeric counters:
pending, outstanding, received, dequeued, skipped, and discontinuity-marked
packets/bytes. It contains no channel/session identifier, metadata, stream
position, packet type, payload, peer text, exception, or endpoint.

## Deferred work

- bounded container signature observation and format selection — implemented
  and verified in 0.0.5b;
- header/data reconstruction rules based on independent wire behavior;
- decoder lifecycle and discontinuity reset;
- audio clock, video scheduling, and A/V synchronization;
- wall-clock latency target and user controls;
- actual audio/video output and controlled playback verification.
