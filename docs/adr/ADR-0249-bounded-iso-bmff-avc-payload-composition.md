# ADR-0249: Bounded ISO BMFF AVC Payload Composition

## Status

Accepted as a post-0.0.10e non-physical codec-composition slice.

## Context

ADR-0248 resolves explicit requirements, while ADR-0243 through ADR-0247 expose
the individual planning, materialization, and selection steps. Callers need one
bounded orchestration boundary that constructs only artifacts required by the
resolved format.

## Decision

Add a pure managed composer that:

- validates NAL-count and packet-byte limits before input access;
- requires one ADR-0242 classification and one ADR-0248 resolution;
- builds a parameter-set prefix plan only for formats that include configuration
  parameter sets;
- invokes only the length-prefixed or Annex-B materializer required by the
  resolved format;
- delegates final source-identity validation to ADR-0247;
- enforces the payload limit for the unchanged original form as well; and
- retains the resolution, selection, optional plan, and exact materialized
  artifact provenance.

## Consequences

All four explicitly requested AVC payload forms can be composed through one
bounded managed boundary without duplicate orchestration logic.

## Non-goals

No runtime requirement discovery, decoder-state policy, decoder submission,
native call, production demux event, or physical execution is included.
