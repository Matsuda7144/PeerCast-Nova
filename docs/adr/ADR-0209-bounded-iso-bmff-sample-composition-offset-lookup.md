# ADR-0209: Bounded ISO BMFF Sample Composition-offset Lookup

## Status

Accepted as a post-0.0.10e non-physical timing-lookup slice.

## Context

ADR-0183 projects version-aware `ctts` runs, and ADR-0193 records whether the
optional table participated in sample-count reconciliation. A caller needs one
sample's offset without expanding the runs or guessing the absent-table rule.

## Decision

Add a pure managed lookup that:

- requires the reconciliation presence flag and actual optional `ctts` table
  to agree;
- requires a present table's total sample count to match reconciliation;
- rejects one-based sample numbers outside the reconciled range;
- traverses at most the parser-bounded run list;
- preserves version 0 unsigned and version 1 signed projected offsets; and
- projects zero with an explicit absence marker when `ctts` is not present.

## Consequences

Callers can obtain a version-correct composition offset for one sample while
distinguishing an implicit zero from an explicit table entry.

## Non-goals

No decode-time addition, presentation timestamp, timescale conversion, edit
list, packet projection, payload read, decoder composition, recording, or
physical execution is included.
