# ADR-0268: Bounded ISO BMFF AAC Managed Decoder Preparation

## Status

Accepted as a post-0.0.10e non-physical decoder-composition prerequisite.

## Context

ADR-0259 through ADR-0266 define individually bounded AAC format intent,
extradata, codec-parameter ownership, transfer planning, symbol coverage, and
declared capability binding. Callers should not have to reproduce their
ownership transitions or partial-failure cleanup.

## Decision

Add a managed-only composer that performs each established stage once:

1. materialize owned AudioSpecificConfig extradata;
2. resolve the AAC FFmpeg codec-parameter input;
3. create the padded managed codec-parameter lease;
4. create the source-bound transfer plan;
5. verify bounded declared-symbol coverage; and
6. bind the plan to caller-declared decoder capabilities.

The composer returns one disposable preparation owner only after every stage
succeeds. On failure it disposes the outermost created owner and returns a
stage-specific rejection without a partial preparation.

## Consequences

The complete managed AAC decoder-preparation chain now has one entry point, one
success owner, exact source provenance, and deterministic failure cleanup.

## Non-goals

Verified runtime capability, runtime loading, ABI/version or address checks,
decoder discovery, context creation, native parameter transfer, decoder open,
packet submission, and physical execution are not included.
