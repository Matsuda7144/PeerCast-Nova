# PCN-0184: Container-bounded ISO BMFF Box Traversal

## Status

Implemented and verified as a non-physical parser prerequisite.

## Implemented boundary

- caller-supplied complete container and exact offset;
- sequential box, payload, and next offsets;
- extends-to-end resolution within the containing range;
- invalid-offset, invalid-header, truncation, end, and success categories;
- declared-size containment before a box is returned;
- no payload access or ownership.

## Verification

Managed tests cover two-box traversal, payload skipping, zero-size resolution,
`uuid` offsets, empty containers, both invalid offset directions, truncated
base and extended headers, undersized declarations, and ordinary or extended
sizes beyond the container.

No native runtime, output device, readiness capture, diagnostic, or physical
audio/video path was exercised.

## Remaining work

Incremental stream accumulation, nested-container policy, recognized payloads,
movie/sample metadata, fragmented runs, media data, and decoder composition
remain unimplemented.
