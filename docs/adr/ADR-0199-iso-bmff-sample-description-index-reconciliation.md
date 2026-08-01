# ADR-0199: ISO BMFF Sample-description Index Reconciliation

## Status

Accepted as a post-0.0.10e non-physical description-index slice.

## Context

The `stsc` parser guarantees nonzero `sample_description_index` values, while
the `stsd` parser provides the authoritative bounded entry count. Cross-table
validation is required before a run can refer to a sample description.

## Decision

Extend the pure managed chunk-table reconciler with explicit `stsd` overloads
that:

- require a parsed `stsd` table when those overloads are selected;
- require every one-based `sample_description_index` not to exceed the `stsd`
  entry count;
- return distinct missing-table and out-of-range failures;
- project the validated sample-description count;
- support combination with expected sample-count validation; and
- preserve existing overload behavior without claiming `stsd` validation.

## Consequences

Callers can establish that every chunk run refers to an existing structural
sample entry without reading or interpreting its payload.

## Non-goals

No sample-entry payload parsing, codec or decoder selection, data-reference
resolution, media-data bounds, lookup, recording, or physical execution is
included.
