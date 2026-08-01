# ADR-0221: Bounded ISO BMFF Edited-sample Admission Lookup

## Status

Accepted as a post-0.0.10e non-physical composition slice.

## Context

ADR-0208 through ADR-0220 provide the individual metadata operations needed to
decide whether one numbered sample has complete appearances after edit-list
application. Callers should not need to reproduce their failure mapping.

## Decision

Add a pure managed lookup that composes:

1. exact numbered-sample presentation-time lookup;
2. bounded sample/edit media-overlap lookup;
3. exact rational movie-overlap projection; and
4. conservative complete-sample admission.

Configuration, reconciliation, sample range, missing timing, presentation
absence, unsupported rate, non-integral edit duration, overlap limit, invalid
projection, and partial-only rejection remain distinct outcomes.

## Consequences

One bounded API now connects reconciled sample timing to complete edited movie
appearances without reading payload bytes or selecting packets.

## Non-goals

No sample byte-range composition, packet selection, payload read, decoder
submission, recording, or physical execution is included.
