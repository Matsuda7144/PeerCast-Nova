# ADR-0344: Exact ISO BMFF Packet Merge Completion

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0343 can chain bounded merge segments to exhaustion. Completion needs an
exact proof that cumulative steps account for every packet remaining at the
root initial cursor state.

## Decision

Accept one exhausted ADR-0343 traversal source. Require its initial and final
cursor sets to retain the exact same selection and exact role sequences. Require
each final selected cursor to be exhausted. Compute the expected packet count as
the sum of initial selected-role remaining counts and require it to equal the
source cumulative step count.

Retain the exact source, initial/final cursor sets, per-role initial remaining
counts, and expected/actual total. Accept an already exhausted root as an exact
zero-step completion. Fail closed for missing, non-exhausted, policy-invalid,
source-mismatched, cursor-invalid, or count-mismatched input.

This certificate covers packet-level merge traversal only.

## Consequences

Callers can distinguish a completely accounted packet merge from truncation or
an inconsistent continuation chain without asserting consumption or delivery.

## Non-goals

Repeated edit-appearance traversal, presentation-event completeness,
consumption or delivery, scheduling, file or `mdat` reads, native packet
submission, decoder execution, rendering, or physical execution are included.
