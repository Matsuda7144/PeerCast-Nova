# ADR-0196: ISO BMFF Partial-sync Sample Range Reconciliation

## Status

Accepted as a post-0.0.10e non-physical partial-sync slice.

## Context

ADR-0191 guarantees that parsed `stps` sample numbers are nonzero and strictly
increasing. The table still requires cross-table validation that every
one-based partial-sync sample number exists in the reconciled sample set.

## Decision

Extend the pure managed sample-count reconciler so that:

- absence of `stps` remains valid and distinct from a present empty table;
- the greatest sample number in a present nonempty parsed `stps` table must
  not exceed the reconciled sample count;
- an out-of-range number returns a distinct fail-closed result;
- successful output records whether an `stps` table was present; and
- existing overloads retain their behavior.

Strict ordering makes validating the greatest number sufficient to validate
the upper bound of every entry.

## Consequences

Callers can establish that every declared partial-sync sample refers to an
existing sample without assigning seek or keyframe meaning.

## Non-goals

No `stss` integration, seek policy, keyframe inference, packet flag
projection, sample lookup, decoder composition, recording, or physical
execution is included.
