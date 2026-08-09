# ADR-0314: Bounded ISO BMFF Movie Codec Packet Event Queue Deadline

## Status

Accepted as a post-0.0.10e non-physical managed-demux timing slice.

## Context

ADR-0313 places integral event intervals on a caller-owned timeline. A later
policy needs explicit ready and exclusive-expiration bounds without yet reading
a clock, waiting, or dispatching.

## Decision

Project every bounded ADR-0313 interval into immutable deadline metadata. Treat
the exact timeline start as the ready tick and the exact exclusive end as the
expiration tick. Retain the exact source time and preserve its duration.

Revalidate the existing 131,072-event bound, positional source identity, and
every nonempty deadline interval. Fail closed for a missing projection,
excessive count, foreign source event, or empty interval.

These values are managed timeline facts. No clock is acquired or observed, and
no waiting, selection, or dispatch occurs.

## Consequences

Each current queue event has an explicit bounded availability interval suitable
for later caller-driven observation and policy composition.

## Non-goals

Clock acquisition, observation, default timing policy, dispatch selection,
delivery outcome, scheduling, waiting, pacing, lateness policy, multi-sample
traversal, native packet submission, decoder execution, production MP4 demux
composition, fragmented MP4, streaming, rendering, and physical execution are
not included.
