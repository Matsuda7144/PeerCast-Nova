# ADR-0248: Explicit ISO BMFF AVC Decoder-payload Requirements

## Status

Accepted as a post-0.0.10e non-physical codec-composition slice.

## Context

ADR-0247 exposes four source-bound payload forms but deliberately does not infer
which one a decoder wants. A caller must express the two independent facts:
Annex-B versus length-prefixed framing, and whether configuration parameter sets
must be included in band.

## Decision

Add a pure managed requirements resolver that:

- requires a caller-owned requirements object;
- accepts only two explicit booleans: Annex-B required and configuration
  parameter sets included;
- maps the four combinations exactly to the four ADR-0247 formats; and
- retains the exact requirements object as resolution provenance.

## Consequences

Payload format choice can be deterministic without embedding runtime detection,
sample-entry guesses, or decoder state into the managed composition primitives.

## Non-goals

No runtime capability discovery, decoder-state inference, automatic parameter-
set policy, decoder submission, native call, demux event, or physical execution
is included.
