# ADR-0441: Exact ISO BMFF Fragment Sample Merge Traversal Acknowledgement View Source Projection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Bind one exact traversal completion source to the acknowledgement view rebuilt
from that source. Require exact completion identity and acknowledgement count,
and propagate fail-closed completion or step-chain validation results without
accepting caller-supplied acknowledgement records.

## Non-goals

Delivery attempt or outcome, cursor mutation, clock acquisition or conversion,
scheduling, payload access, runtime lookup or invocation, native execution,
rendering, or physical execution are not included.
