# ADR-0415: Caller-Bounded ISO BMFF Projected Completed-Event Traversal Dispatch Admitted-Invocation Selection

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Select admitted ADR-0414 entries in exact source order under one positive caller
maximum-selection count. Retain the complete projected source, total admitted
count, selected exact admission identities, and explicit truncation metadata.

## Non-goals

Target calls, request submission, delivery, invocation, native execution,
rendering, or physical execution are not included.
