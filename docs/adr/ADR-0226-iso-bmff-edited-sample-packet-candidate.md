# ADR-0226: ISO BMFF Edited-sample Packet Candidate

## Status

Accepted as a post-0.0.10e non-physical metadata slice.

## Context

ADR-0225 owns the admitted sample bytes but intentionally does not invent a
codec, stream ID, or rounded timestamp. The final codec-neutral boundary should
retain the exact timing domain needed by later sample-description policy.

## Decision

Add a pure managed packet-candidate factory that:

- requires the owned edited-sample payload and parsed media timing;
- revalidates the exact presentation interval identity;
- requires at least one complete edited movie appearance; and
- exposes owned bytes, sample identity, exact presentation units, appearances,
  and the unsigned media timescale together.

The candidate is not a bounded-demux event and carries no inferred codec or
stream identity.

## Consequences

Later sample-description composition has one codec-neutral input without losing
exact timing or edit admission provenance.

## Non-goals

No codec classification, extradata, stream ID, keyframe policy, timestamp
rounding, demux event, decoder submission, recording, or physical execution is
included.
