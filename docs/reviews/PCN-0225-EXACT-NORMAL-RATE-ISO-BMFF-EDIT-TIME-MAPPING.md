# PCN-0225: Exact Normal-rate ISO BMFF Edit-time Mapping

## Scope

Review exact mapping of one bounded movie-timeline point through a normal-rate
`elst` entry into media units.

## Findings

- Parsed movie and media timing inputs provide nonzero timescales.
- Empty edits and non-normal rates fail with explicit outcomes.
- `Int128` scaling covers the full bounded unsigned offset and 32-bit
  timescale domains.
- Non-integral media-unit results are rejected rather than rounded.
- Signed edit media origins are retained in the mapped result.
- No native or physical operation occurs.

## Decision

Accept exact normal-rate point mapping. Fractional mapping, interval overlap,
and sample-selection policy remain incomplete.
