# ADR-0317: Bounded ISO BMFF Movie Codec Packet Event Queue Dispatch Candidate Selection

## Status

Accepted as a post-0.0.10e non-physical managed-demux selection slice.

## Context

ADR-0316 marks eligible events but does not create a bounded candidate set. A
later dispatcher needs an immutable source-ordered selection without being
invoked by this layer.

## Decision

Select eligible entries in exact source order up to a caller-explicit positive
maximum of 131,072. Retain each exact eligibility object, its source index, and
underlying queue entry. Expose the total eligible count and whether eligible
entries remain beyond the selected prefix. A valid selection may be empty.

Fail closed for a missing projection, excessive source count, an invalid
maximum, or foreign positional eligibility metadata.

The selection is an immutable candidate snapshot. It does not invoke a target,
advance a cursor, acknowledge delivery, or perform dispatch.

## Consequences

Callers can inspect a bounded prefix of exact dispatch candidates and detect
truncation without any side effect.

## Non-goals

Actual dispatch, target ownership, cursor mutation, delivery outcome, clock
acquisition, repeated observation, scheduling, waiting, pacing, multi-sample
traversal, native packet submission, decoder execution, production MP4 demux
composition, rendering, and physical execution are not included.
