# ADR-0242: Bounded ISO BMFF AVC NAL-unit Classification

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0232 projects exact non-owning NAL-unit payload ranges but deliberately
does not interpret their one-byte AVC headers. Parameter-set composition needs
explicit VCL, IDR, and in-band parameter-set facts without copying payloads.

## Decision

Add a pure managed classifier that:

- consumes only an ADR-0232 framed sample;
- rejects empty payloads, a set forbidden-zero bit, and NAL type zero;
- projects `nal_ref_idc` and the five-bit NAL-unit type;
- identifies VCL, IDR, and parameter-set units; and
- retains each original non-owning NAL-unit object and aggregate presence facts.

## Consequences

Later AVC composition can make bounded decisions from typed NAL metadata while
preserving the exact owned sample payload and its offsets.

## Non-goals

No RBSP parsing, slice-header parsing, access-unit boundary inference, Annex-B
conversion, parameter-set injection, native call, demux event, or physical
execution is included.
