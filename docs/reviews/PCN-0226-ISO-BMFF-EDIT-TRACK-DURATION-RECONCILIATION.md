# PCN-0226: ISO BMFF Edit and Track-duration Reconciliation

## Scope

Review exact reconciliation of a bounded `elst` duration aggregate with the
known `tkhd` duration.

## Findings

- Both values share the movie timescale, so no rounding is involved.
- Missing inputs, unknown duration, and mismatch are distinct outcomes.
- Unknown track duration is never inferred.
- Successful output binds the duration and edit count to the parsed track ID.
- No native or physical operation occurs.

## Decision

Accept exact edit/track duration reconciliation. Movie-header comparison and
sample-selection policy remain incomplete.
