# ADR-0316: Caller-Explicit ISO BMFF Movie Codec Packet Event Queue Dispatch Eligibility

## Status

Accepted as a post-0.0.10e non-physical managed-demux policy slice.

## Context

ADR-0315 classifies deadlines but does not decide which classifications may
become dispatch candidates. Expired-event treatment is a caller policy and must
not be inferred.

## Decision

Require callers to choose `ReadyOnly` or `ReadyOrExpired`. Pending events are
always ineligible, ready events are always eligible, and expired events follow
the explicit policy. Retain each exact ADR-0315 observation and expose a
specific eligibility kind rather than only a Boolean.

Revalidate the existing 131,072-event bound, policy value, and positional
source identity. Fail closed for a missing snapshot, excessive count, undefined
policy, or foreign observation.

Eligibility is immutable metadata. It does not select a candidate, update a
cursor, invoke a callback, or dispatch anything.

## Consequences

Late-event eligibility is explicit and auditable while pending and ready
boundary behavior remains deterministic.

## Non-goals

Candidate selection, actual dispatch, cursor mutation, delivery outcome, clock
acquisition, repeated observation, scheduling, waiting, pacing, multi-sample
traversal, native packet submission, decoder execution, production MP4 demux
composition, rendering, and physical execution are not included.
