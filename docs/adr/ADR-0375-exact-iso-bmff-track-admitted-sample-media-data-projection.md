# ADR-0375: Exact ISO BMFF Track Admitted Sample Media-Data Projection

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Locate every exact ADR-0374 byte range, in source order, inside one bounded
top-level `mdat` traversal. Retain the exact range source and containing box
location, plus file length and caller box limit, without retaining the caller's
file buffer. Fail without a partial projection for invalid box sequences,
limits, bounds, or missing containment.

## Non-goals

Payload copy, sample-description parsing, AVC/AAC composition, packet
construction, runtime access, native execution, rendering, or physical
execution are not included.
