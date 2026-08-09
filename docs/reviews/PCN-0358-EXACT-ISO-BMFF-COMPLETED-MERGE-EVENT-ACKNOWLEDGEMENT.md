# PCN-0358: Exact ISO BMFF Completed Merge-Event Acknowledgement

## Decision

Accept one immutable reference-exact source/event/successor cursor triple only
when it advances exactly one event in the same ADR-0346 projection. This is
transition metadata, not evidence of consumption, delivery, native operation,
rendering, or physical execution.
