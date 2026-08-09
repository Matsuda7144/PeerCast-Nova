# ADR-0348: Exact ISO BMFF Completed Merge-Event Acknowledgement

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0347 advances one immutable event cursor, but the returned event and
successor are separate values. Later managed stages need an exact transition
record without treating cursor advance as evidence of delivery or consumption.

## Decision

Create an acknowledgement only from one exact ADR-0347 source cursor, the exact
event at its next index, and a successor cursor over the same ADR-0346 projection
at exactly the next index. Retain all three objects by reference and expose the
event index, global merge index, and appearance index.

Fail closed for missing values, projection mismatch, event mismatch, or a
skipped/repeated cursor transition. Return no partial acknowledgement.

## Consequences

One exact presentation-event cursor transition has immutable reference-exact
provenance suitable for later managed composition.

## Non-goals

Successful delivery, consumption, scheduling, clock acquisition, native packet
submission, decoder execution, rendering, or physical execution are not
claimed or included.
