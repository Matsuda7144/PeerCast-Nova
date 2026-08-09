# ADR-0423: Bounded ISO BMFF Fragment Run Data Origin Resolution

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Resolve one parsed run's data origin from caller-declared nonnegative positions.
Prefer an explicit `tfhd` base-data-offset, otherwise use the caller's `moof`
offset when `default-base-is-moof` is set, otherwise require a caller-provided
implicit base. Apply a present signed `trun` data offset exactly; when absent,
require a caller-provided implicit run start. Retain both origin classifications
and arbitrary-precision resolved positions, and reject negative caller or
resolved positions.

## Non-goals

Implicit fragment traversal state, prior-run discovery, file access, `mdat`
lookup, byte-range containment, streaming, decoder submission, native execution,
rendering, or physical execution are not included.
