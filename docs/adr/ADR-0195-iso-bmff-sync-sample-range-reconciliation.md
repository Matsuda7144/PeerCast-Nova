# ADR-0195: ISO BMFF Sync-sample Range Reconciliation

## Status

Accepted as a post-0.0.10e non-physical sync-sample slice.

## Context

ADR-0188 guarantees that parsed `stss` sample numbers are nonzero and strictly
increasing. The table still requires cross-table validation that every
one-based sample number exists in the reconciled sample set.

## Decision

Extend the pure managed sample-count reconciler so that:

- absence of `stss` remains valid and distinct from a present empty table;
- the greatest sample number in a present nonempty parsed `stss` table must
  not exceed the reconciled sample count;
- an out-of-range number returns a distinct fail-closed result;
- successful output records whether an `stss` table was present; and
- existing overloads retain their behavior.

Strict ordering makes validating the greatest number sufficient to validate
the upper bound of every entry.

## Consequences

Callers can establish that every declared sync sample refers to an existing
sample without expanding or interpreting keyframe behavior.

## Non-goals

No seek policy, keyframe inference, packet flag projection, partial-sync
integration, sample lookup, decoder composition, recording, or physical
execution is included.
