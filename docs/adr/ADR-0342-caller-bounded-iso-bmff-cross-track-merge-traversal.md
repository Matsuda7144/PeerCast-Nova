# ADR-0342: Caller-Bounded ISO BMFF Cross-Track Merge Traversal

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0341 validates a caller-built merge-step chain but does not compose the
existing head, comparison, role-selection, and cursor-advance boundaries into a
repeatable pure traversal.

## Decision

Require one exact ADR-0335 cursor set, one ADR-0338 equal-interval tie-break,
and a positive caller step limit no greater than 262,144. Repeatedly project the
ADR-0336 head snapshot, compare through ADR-0337, select through ADR-0338,
advance through ADR-0339, and project ADR-0340 steps with automatic contiguous
indexes. Validate the resulting nonempty chain through ADR-0341.

Stop on exact whole-set exhaustion or the caller limit. Retain the initial and
final cursor sets, exact policy, requested limit, immutable step sequence, and
explicit exhausted versus truncated state. Accept already exhausted input as a
zero-step exhausted traversal.

Fail closed for missing or invalid options, invalid cursor state, or any failed
intermediate transition without returning a partial traversal.

## Consequences

Selected video/audio packet sequences can be merged deterministically in pure
managed memory under an explicit finite caller bound and tie policy.

## Non-goals

Repeated appearance traversal, presentation-event expansion, consumption or
delivery, scheduling, automatic packet construction, file or `mdat` reads,
native packet submission, decoder execution, rendering, or physical execution
are included.
