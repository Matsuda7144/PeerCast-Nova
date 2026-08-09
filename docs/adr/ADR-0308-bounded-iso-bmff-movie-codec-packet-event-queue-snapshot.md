# ADR-0308: Bounded ISO BMFF Movie Codec Packet Event Queue Snapshot

## Status

Accepted as a post-0.0.10e non-physical managed-demux ownership slice.

## Context

ADR-0307 exposes exact seconds metadata in deterministic cross-stream order. A
later demux loop needs a stable queue-shaped ownership boundary before any
consumer, clock, or scheduling policy is selected.

## Decision

Create a read-only snapshot containing at most the existing 131,072-event
bound. Assign each retained ADR-0307 time object a zero-based sequence index and
retain the exact source projection and packet event. Copy the queue-entry
references into an owned read-only collection.

Fail closed for a missing projection, an excessive event count, any difference
from the exact ADR-0306 order, or invalid exact seconds metadata. Revalidate the
nonzero `mvhd` timescale and exact denominator product at the ownership
boundary.

The snapshot intentionally exposes no enqueue, dequeue, cursor, clock,
deadline, or dispatch operation.

## Consequences

One current packet per selected track now has a bounded immutable event queue
snapshot with deterministic positions and complete source identity.

## Non-goals

Queue consumption, mutable queue state, clock-tick conversion, scheduling,
pacing, lateness policy, multi-sample traversal, native packet submission,
decoder execution, production MP4 demux composition, fragmented MP4,
streaming, rendering, and physical execution are not included.
