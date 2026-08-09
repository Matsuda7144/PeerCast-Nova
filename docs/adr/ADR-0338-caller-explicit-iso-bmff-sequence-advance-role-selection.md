# ADR-0338: Caller-Explicit ISO BMFF Sequence Advance-Role Selection

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0337 compares selected video/audio packet-head movie intervals but does not
break equal-interval ties or choose which role may advance.

## Decision

Require the caller to provide either `PreferVideo` or `PreferAudio` for equal
movie intervals. Select the earlier role for unequal intervals, the caller's
role for equal intervals, the only present role for single-media state, or no
role for whole-set exhaustion.

Retain the exact ADR-0337 comparison, exact tie-break, and exact selected
cursor, head entry, and leading appearance. Revalidate that the selected source
is the exact current source retained by the comparison. Reject a missing
comparison, an unknown tie-break, or inconsistent comparison metadata without
returning a partial selection.

This selection is immutable metadata and does not advance the selected cursor.

## Consequences

One exact advance role can be chosen deterministically without an implicit
equal-time preference, cursor mutation, acknowledgement, or consumption claim.

## Non-goals

Cursor advance, successor cursor-set construction, acknowledgement, repeated
appearance traversal, cross-track merge, event projection, delivery, scheduling,
automatic packet construction, file or `mdat` reads, native packet submission,
decoder execution, rendering, or physical execution are included.
