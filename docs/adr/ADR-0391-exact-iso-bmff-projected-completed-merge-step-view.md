# ADR-0391: Exact ISO BMFF Projected Completed Merge-Step View

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Apply the existing completed merge-step view projector to one exact ADR-0390
completion projection. Retain the exact completion source and reconstruct every
initial and continuation segment as one reference-contiguous global step chain
with zero-based global merge indexes and exact final cursor continuity.

## Non-goals

Event projection, cursor traversal, acknowledgement, delivery, scheduling,
runtime access, native execution, rendering, or physical execution are not
included.
