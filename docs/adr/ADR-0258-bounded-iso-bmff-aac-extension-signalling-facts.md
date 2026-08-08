# ADR-0258: Bounded ISO BMFF AAC Extension Signalling Facts

## Status

Accepted as a post-0.0.10e non-physical decoder-composition prerequisite.

## Context

ADR-0257 exposes core format facts while explicit and implicit SBR/PS parsers
retain separate provenance. Callers need one conflict-free projection of which
signalling form applies and the frequency declared by active SBR.

## Decision

Add a pure managed resolver that:

- accepts core facts plus optional source-bound implicit SBR and PS projections;
- uses the GA-owned explicit extension when present and rejects implicit inputs
  in that case;
- requires implicit SBR to reference the same GA config and implicit PS to
  reference that exact SBR projection;
- distinguishes none, explicit, and implicit signalling; and
- selects the extension sampling frequency only when SBR is present, otherwise
  retaining the declared core frequency.

## Consequences

SBR/PS presence and effective signalled sampling frequency are available without
mixing extension sources or silently changing channel topology.

## Non-goals

PS channel-count effects, speaker masks, resampling, extradata materialization,
decoder open, native calls, and physical execution are not included.
