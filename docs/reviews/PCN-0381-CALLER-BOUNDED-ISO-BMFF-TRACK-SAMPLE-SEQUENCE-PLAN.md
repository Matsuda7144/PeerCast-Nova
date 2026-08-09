# PCN-0381: Caller-Bounded ISO BMFF Track Sample Sequence Plan

## Decision

Accept a bounded contiguous immutable sample-number plan from exact reconciled
sample-count metadata with caller start, caller maximum, remaining count, and
truncation state, without reading payload bytes, constructing packets, runtime
access, or physical execution.
