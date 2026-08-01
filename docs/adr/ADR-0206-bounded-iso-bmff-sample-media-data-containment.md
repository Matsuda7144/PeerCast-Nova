# ADR-0206: Bounded ISO BMFF Sample Media-data Containment

## Status

Accepted as a post-0.0.10e non-physical range-validation slice.

## Context

ADR-0205 establishes whole-file containment. A sample range must also reside
entirely in the payload of a selected media-data (`mdat`) box rather than its
header or another top-level box.

## Decision

Add a pure managed validator that:

- accepts one previously projected sample range, one box location selected by
  the caller, and the containing buffer's absolute offset;
- reuses exact sample-range consistency validation;
- requires the selected box type to be `mdat`;
- projects the absolute payload start and exclusive end with checked unsigned
  64-bit additions; and
- accepts only sample ranges fully contained by that payload, including a
  zero-size range exactly at its exclusive end.

## Consequences

Callers can reject a sample that points into a box header or outside the
selected media-data payload without reading any payload bytes.

## Non-goals

No automatic selection across multiple `mdat` boxes, whole-file traversal,
payload read, packet projection, fragmented run, codec selection, decoder
composition, recording, or physical execution is included.
