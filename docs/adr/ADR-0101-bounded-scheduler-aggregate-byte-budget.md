# ADR-0101: Bounded Scheduler Aggregate Byte Budget

- Status: Accepted
- Milestone: 0.0.8q
- Production decoder ingress: Not connected

## Context

`BoundedPlaybackScheduler` previously limited only the number of queued managed
frames. A single managed frame may own a large bounded allocation, and
`RunOneAsync` removes an item from the queue before its awaited sink callback
finishes. Queue length alone therefore did not bound all memory still owned by
the scheduler.

## Decision

The scheduler has an independent configurable aggregate owned-byte budget.
The default is 64 MiB and the hard configuration limit is 1 GiB.

The byte count is the checked sum of every managed plane buffer length. Frames
with no positive-length planes fail closed as invalid. A single frame larger
than the configured budget is disposed and returned as `FrameTooLarge` before
full-policy processing.

The existing `DropOldest`, `DropIncoming`, and `Reject` policies apply whenever
either count capacity or byte capacity would be exceeded. `DropOldest` may
remove multiple unreserved frames. An output-reserved frame is never selected
for eviction; if reserved or in-flight ownership prevents the incoming frame
from fitting, the incoming frame is dropped.

Byte ownership includes:

- queued frames,
- output-reserved frames, and
- frames dequeued by `RunOneAsync` while policy waiting or awaited sink delivery
  is still active.

Accounting is released only when scheduler ownership ends through delivery,
policy disposal, cancellation, eviction, reservation completion, reset/dispose
cleanup, or integration failure.

`PlaybackSchedulerSnapshot` exposes `OwnedByteCount`. Its existing three-value
constructor remains source compatible through an optional fourth value.

## Compatibility and boundaries

This milestone changes no PCP, Channel, Relay, decoder ABI, output-device, or
production activation contract. It creates no second queue and connects no
diagnostic or production decoder to scheduler ingress.

No new production package, native dependency, media asset, or FFmpeg
redistribution decision is introduced.

## Consequences

Decoder-to-scheduler ownership transfer can now be reviewed against both item
and memory bounds. Duration-based buffering remains deferred because unknown
frame timestamps and durations are valid in existing contracts.

## Non-goals

No demux, packet loop, flush/EOS handling, decoder activation, frame ingress
transaction, duration cutoff, rendering change, audio recovery change, or
real-media playback claim is included.
