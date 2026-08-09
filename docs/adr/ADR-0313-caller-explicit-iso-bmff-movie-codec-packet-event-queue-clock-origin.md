# ADR-0313: Caller-Explicit ISO BMFF Movie Codec Packet Event Queue Clock Origin

## Status

Accepted as a post-0.0.10e non-physical managed-demux timing slice.

## Context

ADR-0312 exposes caller-rounded integral clock intervals relative to zero. A
later composition may need those intervals expressed in a caller-owned timeline
without reading a clock or turning them into deadlines.

## Decision

Accept a caller-explicit signed `BigInteger` origin tick and add it exactly to
each integral start and exclusive end. Retain the exact ADR-0312 projection and
event-time objects. Preserve the duration unchanged and allow both positive and
negative origins without narrowing or overflow.

Revalidate the existing 131,072-event bound, exact positional source identity,
and every nonempty source interval. Fail closed for a missing projection,
excessive count, foreign source event, or invalid interval.

The origin is supplied metadata. No operating-system clock is read, and the
result is not a deadline or dispatch instruction.

## Consequences

Integral packet-event intervals can be placed exactly on an arbitrary
caller-owned signed timeline while preserving source identity and duration.

## Non-goals

Clock acquisition, default origin, deadline creation, delivery outcome,
dispatch, scheduling, waiting, pacing, lateness policy, multi-sample traversal,
native packet submission, decoder execution, production MP4 demux composition,
fragmented MP4, streaming, rendering, and physical execution are not included.
