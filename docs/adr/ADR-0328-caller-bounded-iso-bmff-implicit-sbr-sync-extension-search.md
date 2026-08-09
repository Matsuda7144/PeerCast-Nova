# ADR-0328: Caller-Bounded ISO BMFF Implicit SBR Sync Extension Search

## Status

Accepted as a post-0.0.10e non-physical managed AAC configuration slice.

## Context

ADR-0240 and ADR-0256 parse an implicit `0x2B7` SBR sync extension only at the
exact immediate position after direct GA or admitted ER configuration. MPEG-4
AudioSpecificConfig consumers also search later residual bits one bit at a time,
but an unconstrained scan would violate the bounded parser contract.

## Decision

Search for the 11-bit `0x2B7` marker from the exact direct-GA or admitted-ER end
position, advancing one bit per candidate under a caller-explicit positive
maximum. Cap that maximum at the existing 1 MiB codec-extradata hard limit in
bits.

Distinguish source exhaustion from caller-limit exhaustion. On the first marker,
reuse the existing bounded SBR parser at its exact bit position and retain the
source GA configuration, optional exact ER admission, search start, marker
position, skipped-bit count, caller maximum, and parsed extension. Preserve the
existing malformed/truncated SBR outcomes. The existing immediate PS parser may
continue from the found SBR end position.

## Consequences

Direct GA and fully admitted ER configurations can discover a non-immediate SBR
marker with explicit work and provenance bounds while continuing to distinguish
absence, truncation, and malformed syntax.

## Non-goals

No search before the exact parsed configuration end, overlapping-marker
recovery after a malformed first marker, default scan limit, decoder
composition, native transfer, decoder execution, rendering, or physical
execution is included.
