# ADR-0192: ISO BMFF Sample-count and Size-table Reconciliation

## Status

Accepted as a post-0.0.10e non-physical composition slice.

## Context

The bounded `stts`, `stsz`, and `stz2` parsers project their own counts but do
not establish that decode timing and sample sizes describe the same samples.
`stsz` and `stz2` are alternative size-table forms and must not both be selected
for one reconciliation.

## Decision

Add a pure managed reconciler that:

- requires one parsed `stts` timing table;
- requires exactly one parsed `stsz` or `stz2` size table;
- requires timing and size sample counts to match exactly;
- accepts matching empty tables;
- reports whether the standard or compact size table was selected; and
- projects the reconciled unsigned 32-bit sample count and exact total sample
  bytes.

## Consequences

Callers can establish the first cross-table invariant and retain a single
unambiguous size-table selection without performing sample lookup.

## Non-goals

No `ctts`, `sdtp`, sync-sample, partial-sync, chunk, sample-description, or
media-data reconciliation; lookup; decoder composition; recording; or physical
execution is included.
