# PCN-0016 PCP CHAN to Core Mapping

## Purpose

`PcpChannelStateAdapter` is the Network boundary between decoded PCP control
messages and protocol-independent Core status.

```text
incremental PCP decoder
  -> payload-free PcpChannelControlMessage
     -> PcpChannelStateAdapter
        -> immutable ChannelStatusSnapshot
        -> optional explicit PcpStreamDiscontinuity

pkt/data bytes -----------------------> future bounded Network data plane
```

The adapter never receives packet bytes from the control model and creates no
queue, socket, Pipe, cancellation source, or disposable resource.

## Mapping rules

1. A CHAN ID must equal the Core session channel ID before any mutation.
2. Protocol `info` fields map to channel metadata; `trck` fields map to the
   nested current-track snapshot.
3. A null protocol field means absent and preserves the current Core value.
4. An empty decoded string is an explicit value and is not replaced by the
   previous value.
5. A protocol bitrate outside Core's nonnegative `int` range is rejected before
   packet-position state changes.
6. Metadata and packet position from one CHAN message are published as one
   immutable Core revision.
7. Packet type and data length remain Network/Protocol control values; no
   semantic packet-type enum is inferred yet.

## Position rules

`PcpStreamPositionTracker` compares serialized wire positions modulo 2^32:

- zero delta: duplicate, no new Core revision unless metadata changed;
- delta from 1 through 2^31-1: forward, including numeric wrap;
- delta from 2^31 through 2^32-1: backward or ambiguous discontinuity.

A discontinuity leaves both the Core position and the last accepted wire
position unchanged. This permits a later valid forward value to recover from a
single stale or reordered observation.

## Ownership and concurrency

One live channel session must serialize calls through one adapter and keep that
adapter across reconnect attempts. `ChannelStatusTracker` remains internally
thread-safe, but the stateful position comparison intentionally has one owner.
The future session must surface discontinuities to each bounded media consumer
before deciding whether to flush, restart, or stop that consumer.

## Privacy and data-plane boundary

Metadata is user-generated and must not be logged by default. Mismatch and
range exceptions do not include channel identifiers or metadata values.
Snapshot publication carries no packet payload. This mapping completes the
control bridge but does not complete live reception, relay, playback, recording,
or backpressure behavior.
