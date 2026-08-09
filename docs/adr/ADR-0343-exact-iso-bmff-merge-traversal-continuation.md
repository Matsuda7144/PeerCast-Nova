# ADR-0343: Exact ISO BMFF Merge Traversal Continuation

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0342 distinguishes exhausted traversal from caller truncation. A truncated
caller needs to continue from the exact final cursor state without repeating or
copying previously emitted steps.

## Decision

Define one traversal-source contract exposing exact initial/final cursor sets,
the equal-interval tie policy, cumulative step count, and exhaustion. ADR-0342
traversals and continuations both implement this contract.

Continue only a non-exhausted source, from its exact final cursor set, with the
same tie policy and a positive additional limit that keeps the cumulative count
within 262,144. Retain the exact source and one new ADR-0342 segment. Expose the
source cumulative count as the segment's starting merge index, while the new
segment retains local zero-based step indexes and does not re-emit prior steps.

Fail closed for missing, exhausted, inconsistent, or count-invalid sources and
invalid limits without returning a partial continuation.

## Consequences

Finite caller-bounded traversal segments can be chained to exhaustion with exact
state and cumulative-count provenance and no duplicated output ownership.

## Non-goals

Flattening or copying prior segments, global reindexing of retained local steps,
repeated appearance traversal, consumption or delivery, event projection,
scheduling, file or `mdat` reads, native packet submission, decoder execution,
rendering, or physical execution are included.
