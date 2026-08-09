# ADR-0360: Bounded ISO BMFF Completed Merge-Event Traversal Dispatch Candidate Selection

## Status

Accepted as a post-0.0.10e non-physical managed metadata slice.

## Decision

Select eligible ADR-0359 entries in exact source order under a positive caller
candidate limit no greater than 131,072. Retain source index, eligibility, exact
event, and acknowledgement provenance; expose total eligible count and explicit
truncation without returning partial output on mismatch.

## Non-goals

Target identity, request projection, dispatch, delivery, runtime access, native
execution, rendering, or physical execution are not included.
