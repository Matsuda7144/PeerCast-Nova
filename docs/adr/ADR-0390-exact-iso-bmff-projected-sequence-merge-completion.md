# ADR-0390: Exact ISO BMFF Projected-Sequence Merge Completion

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Apply the existing merge-completion factory to one exact exhausted ADR-0388 or
ADR-0389 projection. Retain the exact projection source and certify that final
cursors are exhausted and the cumulative step count equals the initial video
and audio remaining counts, without inventing completion for truncated state.

## Non-goals

Completed step reconstruction, event projection, acknowledgement, delivery,
scheduling, runtime access, native execution, rendering, or physical execution
are not included.
