# ADR-0378: Exact ISO BMFF Track Sample-Description Selection Projection

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Apply the existing edited-sample description selector to every exact ADR-0377
packet candidate in source order. Require the same parsed `stsd` box and handler
for the whole projection, retain every exact candidate and selection, and reject
source-chain, sample-description-index, codec, and handler mismatches without
constructing a partial projection.

## Non-goals

Sample-entry parsing, codec-specific child parsing, AVC/AAC payload composition,
codec packet construction, runtime access, native execution, rendering, or
physical execution are not included.
