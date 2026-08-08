# ADR-0262: Bounded ISO BMFF AAC Managed Codec Parameters

## Status

Accepted as a post-0.0.10e non-physical decoder-composition prerequisite.

## Context

ADR-0261 projects a validated input for the established codec-parameter factory.
The managed creation step should retain ISO BMFF provenance and explicit lease
ownership without implying native parameter transfer.

## Decision

Add a managed factory wrapper that:

- requires the exact ADR-0261 input resolution;
- invokes the existing bounded codec-parameter factory once;
- owns the resulting AAC lease and its exact 64-byte zero-padded extradata;
- retains the input resolution as provenance; and
- forwards synchronous and asynchronous disposal to the lease.

## Consequences

ISO BMFF AAC configuration reaches the existing managed padded ownership
boundary with a single explicit disposable owner.

## Non-goals

Native codec-parameter allocation or transfer, decoder-context mutation, decoder
open, external runtime loading, or physical execution are not included.
