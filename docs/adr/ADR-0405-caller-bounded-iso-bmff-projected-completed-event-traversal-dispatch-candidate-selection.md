# ADR-0405: Caller-Bounded ISO BMFF Projected Completed-Event Traversal Dispatch Candidate Selection

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Select eligible ADR-0404 entries in exact source order under one positive
caller maximum-candidate count. Retain the complete projected source, original
source indexes, total eligible count, and explicit truncation metadata.

## Non-goals

Target identity, binding, request projection, delivery, runtime access, native
execution, rendering, or physical execution are not included.
