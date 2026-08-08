# ADR-0245: Bounded ISO BMFF AVC Annex-B Payload Materialization

## Status

Accepted as a post-0.0.10e non-physical codec-composition slice.

## Context

Some managed or native decoder boundaries consume Annex-B rather than ISO BMFF
length-prefixed AVC. ADR-0243 already supplies the exact ordered NAL sequence,
so format conversion can be isolated from decoder and policy decisions.

## Decision

Add a pure managed materializer that:

- consumes only an ADR-0243 plan;
- revalidates a nonempty sequence of nonempty NAL payloads;
- writes the unambiguous four-byte start code `00 00 00 01` before every unit;
- preserves configuration-then-sample NAL order;
- projects and enforces the existing 4 MiB packet hard limit before allocation;
  and
- returns privately owned immutable-view payload bytes with source provenance.

## Consequences

The same bounded plan can now produce deterministic Annex-B bytes without
altering the original ISO BMFF sample or configuration payloads.

## Non-goals

No three-byte start-code optimization, emulation-prevention rewriting,
automatic injection policy, decoder submission, native call, demux event, or
physical execution is included.
