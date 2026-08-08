# ADR-0298: Bounded ISO BMFF Admitted ER AAC Decoder Format Intent

## Status

Accepted as a post-0.0.10e non-physical decoder-metadata slice.

## Context

ADR-0288 owns admitted ER configuration and ADR-0297 exposes immediate implicit
SBR/PS after its exact end. Existing AAC core, extension-signalling, and decoder-
format resolvers are individually source-bound but do not retain ER admission as
one composition result.

## Decision

Compose one admitted ER configuration, optional exact PCE topology, and optional
implicit SBR/PS into the existing AAC core facts, extension-signalling facts, and
decoder-format intent. Reject an SBR end that does not follow the admission,
source mismatch at any existing resolver stage, or invalid PS output projection.

Return one result retaining the exact admission and exact resolved intent. Do not
create decoder parameters or perform native operations.

## Consequences

ER configuration ownership and effective AAC sampling/channel intent now remain
connected through one managed source chain.

## Non-goals

Extradata materialization, decoder preparation, runtime loading, native calls,
rendering, and physical execution are not included.
