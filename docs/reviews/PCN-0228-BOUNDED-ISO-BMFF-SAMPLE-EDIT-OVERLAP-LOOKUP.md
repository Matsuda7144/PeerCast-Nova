# PCN-0228: Bounded ISO BMFF Sample/edit Overlap Lookup

## Scope

Review bounded exact overlap lookup between one sample presentation interval
and all supported edit intervals.

## Findings

- Half-open comparisons exclude touching endpoints and zero-duration samples.
- Empty edits are skipped without synthetic media ranges.
- Repeated edits preserve multiple overlaps.
- Complete versus partial sample containment is explicit.
- Output allocation has configurable and hard limits.
- Edit traversal retains its running movie offset and is linear in entry count.
- Unsupported rates and non-integral edit durations fail closed globally.
- No native or physical operation occurs.

## Decision

Accept bounded sample/edit media overlap lookup. Movie-time projection and
partial-sample admission policy remain incomplete.
