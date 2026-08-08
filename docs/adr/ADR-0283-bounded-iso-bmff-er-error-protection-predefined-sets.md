# ADR-0283: Bounded ISO BMFF ER Error Protection Predefined Sets

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Decision

Add one pure managed parser that traverses every set declared by the bounded
prefix, retains set and class indexes plus exact ends, validates each reordered
output as a full permutation, and enforces a configurable aggregate class limit
(default 4096, absolute maximum 16065). Missing, truncated, invalid-order, and
limit-exceeded inputs produce no partial aggregate.

## Consequences

Fourth and later predefined sets no longer require numbered parser types.

## Non-goals

Header protection, complete configuration continuation, decoder or native
integration, and physical execution are not included.
