# ADR-0237: Bounded ISO BMFF SL Config Descriptor

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0233 requires one direct SLConfigDescriptor child but deliberately leaves
its payload uninterpreted. ISO BMFF uses the one-byte predefined value `2` for
the MP4 Systems configuration needed by the current bounded hierarchy.

## Decision

Add a pure managed parser that:

- reads only the SLConfigDescriptor location owned by ADR-0233;
- requires an exact one-byte payload;
- accepts only predefined configuration `2`; and
- distinguishes missing input, invalid hierarchy, malformed payload, and
  unsupported predefined configurations.

## Consequences

The required `esds` hierarchy can now prove the supported MP4 SL configuration
without implementing the large custom SLConfigDescriptor form.

## Non-goals

No predefined value `0` custom field parsing, other predefined configuration,
timestamp reconstruction, access-unit scheduling, native call, demux event, or
physical execution is included.
