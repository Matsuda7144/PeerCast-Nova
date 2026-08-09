# ADR-0373: Bounded ISO BMFF Track Admitted Sample Selection

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Select admitted ADR-0372 entries only, in exact source order, under one
positive caller maximum bounded by the sample-plan limit. Retain the exact
admission entries, total admitted count before truncation, caller maximum, and
explicit truncation state. Reject foreign entry ordering or inconsistent
disposition/admission state without a partial selection.

## Non-goals

Byte-range or `mdat` lookup, payload copy, sample-description selection,
AVC/AAC composition, packet construction, runtime access, native execution,
rendering, or physical execution are not included.
