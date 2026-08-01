# ADR-0216: ISO BMFF Edit and Track-duration Reconciliation

## Status

Accepted as a post-0.0.10e non-physical reconciliation slice.

## Context

The `elst` segment durations and `tkhd` duration are both expressed in the
movie timescale. A known track duration can therefore be checked without any
conversion or edit application.

## Decision

Add a pure managed reconciler that:

- requires a parsed edit list and track header;
- requires the track header duration to be known;
- requires exact equality with the edit list's checked duration aggregate; and
- projects the track ID, reconciled duration, and bounded edit count.

Unknown durations are not inferred from the edit list.

## Consequences

Later edit mapping can require an explicit structural duration invariant before
using a track's edit list.

## Non-goals

No movie-header duration comparison, timescale conversion, rate policy, sample
selection, payload read, decoder composition, recording, or physical execution
is included.
