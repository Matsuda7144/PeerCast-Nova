# ADR-0448: Bounded ISO BMFF Fragment Sample Merge Traversal Deadline Projection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Project immutable decode and presentation deadline metadata from each
origin-shifted fragment traversal sample. Preserve exact signed ready ticks,
exclusive expiration ticks, and durations for both half-open intervals, retain
the exact clock-origin source, and validate the complete source-indexed interval
chain without observing a clock.

## Non-goals

Current-time observation, pending/ready/expired classification, scheduling,
delivery attempt or outcome, payload access, decoder submission, native
execution, rendering, or physical execution are not included.
