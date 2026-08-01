# PCN-0227: Exact Normal-rate ISO BMFF Edit-interval Projection

## Scope

Review exact projection of one bounded normal-rate `elst` entry into half-open
movie and media intervals.

## Findings

- Entry selection is explicit and bounds checked.
- Movie bounds retain the checked edit-list duration domain.
- Empty edits never acquire synthetic media bounds.
- Only exact `(1, 0)` rate entries are accepted.
- `Int128` duration scaling rejects non-integral results rather than rounding.
- Signed media origins and zero-duration half-open intervals are preserved.
- No native or physical operation occurs.

## Decision

Accept exact normal-rate edit-interval projection. Sample overlap and admission
policy remain incomplete.
