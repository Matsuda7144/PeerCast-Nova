# ADR-0246: Bounded ISO BMFF AVC Sample-only Annex-B Materialization

## Status

Accepted as a post-0.0.10e non-physical codec-composition slice.

## Context

ADR-0245 materializes an Annex-B payload that prefixes configured parameter
sets. A caller that has already configured its decoder separately also needs a
strict conversion of only the classified sample NAL units.

## Decision

Add a pure managed materializer that:

- consumes only an ADR-0242 classification;
- requires a nonempty sequence of nonempty sample NAL payloads;
- writes the exact four-byte start code `00 00 00 01` before every sample unit;
- never includes `avcC` parameter sets;
- enforces the existing 4 MiB packet hard limit before allocation; and
- returns privately owned immutable-view payload bytes with classification
  provenance.

## Consequences

Both parameter-set-prefixed and sample-only Annex-B forms are available without
conflating conversion with decoder initialization policy.

## Non-goals

No automatic format or injection decision, three-byte start-code optimization,
decoder submission, native call, demux event, or physical execution is included.
