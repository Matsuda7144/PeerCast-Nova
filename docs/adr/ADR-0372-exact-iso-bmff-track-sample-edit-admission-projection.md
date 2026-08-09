# ADR-0372: Exact ISO BMFF Track Sample Edit-Admission Projection

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Evaluate every exact ADR-0371 plan entry, in source order, through the existing
sample presentation and conservative complete-edit admission chain. Retain the
exact plan entry and classify each valid lookup as `Admitted`, `NotPresented`,
or `PartialSampleRejected`; retain an exact admission only for admitted
entries. Invalid metadata or foreign plan ordering fails without a partial
projection.

## Non-goals

Byte-range or `mdat` lookup, payload copy, sample-description selection,
AVC/AAC composition, packet construction, runtime access, native execution,
rendering, or physical execution are not included.
