# ADR-0310: Exact ISO BMFF Movie Codec Packet Event Queue Acknowledgement

## Status

Accepted as a post-0.0.10e non-physical managed-demux metadata slice.

## Context

ADR-0309 advances an immutable cursor by one queue entry, but the three values
returned by that operation are not yet bound into one exact managed fact. Such
a binding must not claim that delivery, decode, or rendering occurred.

## Decision

Create an immutable acknowledgement only from the exact source cursor, exact
queue entry, and exact successor cursor of one valid advance. Retain all three
objects and expose the acknowledged sequence index.

Require the source and successor to retain the same queue snapshot, the entry
to be the exact object at the source cursor's next sequence index, and the
successor index to be exactly one greater. Fail closed for missing components,
a foreign snapshot or entry, an invalid source position, or a skipped cursor
transition.

The acknowledgement records only the consistency of managed cursor progress.
It does not record or authorize packet delivery or any external side effect.

## Consequences

One exact queue advance can now be retained as a source-bound immutable fact
without mutating the queue or either cursor.

## Non-goals

Delivery outcome, dispatch, clock-tick conversion, scheduling, pacing, lateness
policy, multi-sample traversal, native packet submission, decoder execution,
production MP4 demux composition, fragmented MP4, streaming, rendering, and
physical execution are not included.
