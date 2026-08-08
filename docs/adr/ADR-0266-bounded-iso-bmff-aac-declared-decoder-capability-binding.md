# ADR-0266: Bounded ISO BMFF AAC Declared Decoder Capability Binding

## Status

Accepted as a post-0.0.10e non-physical decoder-composition prerequisite.

## Context

ADR-0265 establishes complete managed symbol-name coverage. A separate bounded
step can compare the AAC plan with caller-declared codec, sample-rate, and
channel capabilities without treating that declaration as verified runtime
evidence.

## Decision

Add a managed-only binder that:

- requires one available ADR-0265 coverage owner;
- accepts a finite declaration of codec identity, inclusive sample-rate range,
  and maximum channel count;
- confines declaration limits to the established AAC codec-parameter bounds;
- requires exact AAC codec identity and admits only plan facts within the
  declared sample-rate and channel limits;
- retains the exact declaration and source coverage; and
- forwards synchronous and asynchronous disposal through the ownership chain.

## Consequences

Invalid and unsupported declarations fail before any runtime interaction, and a
successful binding preserves all managed AAC provenance.

## Non-goals

Caller declaration is not verified decoder capability. Runtime inventory,
module loading, ABI/version verification, symbol-address resolution, decoder
discovery, native parameter transfer, decoder open, and physical execution are
not included.
