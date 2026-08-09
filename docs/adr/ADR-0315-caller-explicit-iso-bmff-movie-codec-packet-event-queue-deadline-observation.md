# ADR-0315: Caller-Explicit ISO BMFF Movie Codec Packet Event Queue Deadline Observation

## Status

Accepted as a post-0.0.10e non-physical managed-demux timing slice.

## Context

ADR-0314 defines ready and exclusive-expiration bounds but deliberately does
not acquire or observe a clock. A pure classification can be made when a caller
supplies one exact signed timeline tick.

## Decision

Compare one caller-explicit signed `BigInteger` observation tick with every
bounded deadline. Classify ticks before ready as `Pending`, ticks from ready
through the tick before exclusive expiration as `Ready`, and ticks at or after
exclusive expiration as `Expired`. Retain the exact source deadline and expose
signed deltas until ready and expiration.

Revalidate the existing 131,072-event bound, positional source identity, and
every nonempty deadline. Fail closed for a missing projection, excessive count,
foreign deadline, or invalid interval.

The observation tick is supplied data. No clock is read, no wait occurs, and a
`Ready` classification does not authorize dispatch.

## Consequences

All current queue deadlines can be classified consistently at one exact
caller-owned timeline tick, including both interval boundaries.

## Non-goals

Clock acquisition, repeated observation, dispatch eligibility or selection,
delivery outcome, scheduling, waiting, pacing, lateness policy, multi-sample
traversal, native packet submission, decoder execution, production MP4 demux
composition, fragmented MP4, streaming, rendering, and physical execution are
not included.
