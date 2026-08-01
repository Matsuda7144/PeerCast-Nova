# ADR-0193: ISO BMFF Composition Sample-count Reconciliation

## Status

Accepted as a post-0.0.10e non-physical composition slice.

## Context

ADR-0192 reconciles decode timing with exactly one sample-size table. An
optional `ctts` table, when present, must describe the same number of samples.

## Decision

Extend the pure managed sample-count reconciler so that:

- absence of `ctts` remains valid;
- a present parsed version 0 or 1 `ctts` table must have the reconciled sample
  count;
- mismatch returns a distinct fail-closed result; and
- successful output records whether composition offsets were present.

The existing overload without `ctts` retains its behavior.

## Consequences

Callers can establish timing, size, and optional composition count agreement
without expanding timestamps or interpreting offset values.

## Non-goals

No timestamp expansion, offset arithmetic, dependency, sync-sample, chunk,
sample-description, or media-data reconciliation; lookup; decoder composition;
recording; or physical execution is included.
