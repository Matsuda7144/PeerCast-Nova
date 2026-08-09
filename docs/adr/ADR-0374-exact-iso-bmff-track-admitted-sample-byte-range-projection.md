# ADR-0374: Exact ISO BMFF Track Admitted Sample Byte-Range Projection

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Resolve every exact ADR-0373 selected admission, in source order, through the
existing reconciled sample-to-chunk, chunk-offset, and sample-size lookup chain.
Retain each exact admission and its absolute offset, size, exclusive end, chunk
location, and sample-description index. Require strictly increasing source
plan indexes and fail without a partial projection on reconciliation, range, or
overflow errors.

## Non-goals

File-bound or `mdat` containment lookup, payload copy, sample-description
parsing, AVC/AAC composition, packet construction, runtime access, native
execution, rendering, or physical execution are not included.
