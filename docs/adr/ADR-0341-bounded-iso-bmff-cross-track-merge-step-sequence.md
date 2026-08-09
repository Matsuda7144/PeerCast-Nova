# ADR-0341: Bounded ISO BMFF Cross-Track Merge-Step Sequence

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0340 owns one exact caller-indexed cross-track step. A bounded caller-built
partial or complete merge needs immutable multi-step ownership with exact
cursor-state continuity.

## Decision

Own between one and 262,144 caller-provided ADR-0340 steps. Copy the input into
a read-only collection. Require merge indexes to equal their zero-based list
positions, require one exact equal-interval tie-break across the sequence, and
require every prior successor cursor set to be the exact next source cursor set.

Reproject each step from its exact advance and index to revalidate selected
source provenance. Fail closed for missing, empty, excessive, null, invalid,
non-contiguous, policy-mixed, or cursor-disconnected input without returning a
partial sequence.

## Consequences

A bounded caller-provided cross-track step chain has immutable index, policy,
initial-state, and final-state provenance without claiming exhaustive traversal.

## Non-goals

Automatic traversal, completeness or exhaustion proof, automatic merge-index
assignment, consumption or delivery, repeated appearance traversal, event
projection, scheduling, automatic packet construction, file or `mdat` reads,
native packet submission, decoder execution, rendering, or physical execution
are included.
