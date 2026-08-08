# ADR-0244: Bounded ISO BMFF AVC Length-prefixed Payload Materialization

## Status

Accepted as a post-0.0.10e non-physical codec-composition slice.

## Context

ADR-0243 proves an ordered parameter-set prefix plan and its exact encoded size.
A managed consumer that accepts ISO BMFF AVC length-prefixed NAL units needs an
owned payload using the selected one-to-four-byte prefix width.

## Decision

Add a pure managed materializer that:

- consumes only an ADR-0243 plan;
- revalidates prefix width, nonempty NAL payloads, representable lengths, and
  the plan's projected encoded byte count;
- writes every length in exact big-endian configured width;
- preserves configuration-then-sample NAL order;
- enforces the existing 4 MiB packet hard limit before allocation; and
- returns privately owned immutable-view payload bytes with source provenance.

## Consequences

The bounded plan can now become an exact ISO BMFF-style AVC payload without
depending on native code or modifying the original sample memory.

## Non-goals

No automatic injection policy, duplicate suppression, Annex-B conversion,
decoder submission, native call, demux event, or physical execution is included.
