# ADR-0454: Caller-Bounded ISO BMFF Fragment Sample Merge Traversal Eligible Selection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Select a source-ordered prefix of fragment traversal eligibility entries under a
caller-explicit require-both or require-either decode/presentation combination
policy. Require a candidate limit from 1 through 65,536, retain source indexes
and exact eligibility/sample identity, count all eligible entries, and report
truncation without scheduling or dispatching a candidate.

## Non-goals

Default combination policy, priority reordering, scheduling, dispatch request
creation, delivery attempt or outcome, payload access, decoder submission,
native execution, rendering, or physical execution are not included.
