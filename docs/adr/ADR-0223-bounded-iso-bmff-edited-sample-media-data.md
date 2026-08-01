# ADR-0223: Bounded ISO BMFF Edited-sample Media-data Association

## Status

Accepted as a post-0.0.10e non-physical composition slice.

## Context

ADR-0222 binds complete edited appearances to the admitted sample's absolute
byte range. The existing top-level structural lookup can identify which `mdat`
payload contains that range without reading it.

## Decision

Add a pure managed lookup that:

- requires the typed edited-sample byte-range result;
- validates box-count configuration before input access;
- composes bounded top-level `mdat` containment;
- preserves all structural, file-bounds, and not-found outcomes; and
- returns admission, sample number, range, and containing box together.

## Consequences

Metadata callers can identify a complete edited sample's storage container
without losing the admission association or touching payload bytes.

## Non-goals

No payload slicing, packet ownership, codec configuration, decoder submission,
recording, or physical execution is included.
