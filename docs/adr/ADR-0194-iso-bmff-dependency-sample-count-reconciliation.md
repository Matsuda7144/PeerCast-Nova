# ADR-0194: ISO BMFF Dependency Sample-count Reconciliation

## Status

Accepted as a post-0.0.10e non-physical dependency slice.

## Context

ADR-0193 reconciles decode timing, sample sizes, and optional composition
timing. An optional `sdtp` table stores exactly one dependency entry per sample
and therefore must describe the same sample count.

## Decision

Extend the pure managed sample-count reconciler so that:

- absence of `sdtp` remains valid;
- a present parsed `sdtp` table must have the reconciled sample count;
- mismatch returns a distinct fail-closed result;
- successful output records whether sample dependencies were present; and
- existing overloads retain their behavior.

## Consequences

Callers can establish optional dependency-table count agreement without
interpreting dependency values.

## Non-goals

No keyframe inference, packet flag projection, sync-sample reconciliation,
chunk mapping, timestamp expansion, decoder composition, recording, or
physical execution is included.
