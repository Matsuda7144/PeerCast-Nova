# ADR-0452: Caller-Explicit ISO BMFF Fragment Sample Merge Traversal Deadline Eligibility

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Apply independently caller-explicit ready-only or ready-or-expired policies to
the decode and presentation state of every fragment traversal deadline
observation. Retain exact observation identity, classify each dimension as
ineligible-pending, eligible-ready, ineligible-expired, or eligible-expired,
and expose the two eligibility facts plus their conjunction without selecting a
candidate.

## Non-goals

Default policy, cross-dimension selection policy, candidate selection,
scheduling, dispatch, delivery attempt or outcome, payload access, decoder
submission, native execution, rendering, or physical execution are not
included.
