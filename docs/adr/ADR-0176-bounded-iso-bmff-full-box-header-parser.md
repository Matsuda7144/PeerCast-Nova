# ADR-0176: Bounded ISO BMFF FullBox-header Parser

## Status

Accepted as a post-0.0.10e non-physical parser prerequisite.

## Context

Many ISO BMFF metadata boxes begin their payload with the common FullBox
prefix: an unsigned version byte followed by unsigned 24-bit flags. Individual
box parsers need one exact representation of that prefix before applying their
own supported-version and flag policies.

## Decision

Add a pure managed, stateless FullBox-header parser that:

- requires exactly four available prefix bytes;
- reads the combined field in big-endian order;
- separates the high byte as version and the low 24 bits as flags;
- returns `NeedMoreData` for every truncated prefix;
- does not validate box-specific version or flag values; and
- does not read, retain, or expose trailing payload bytes.

## Consequences

Later FullBox payload parsers can share a fixed, allocation-free structural
primitive while retaining responsibility for their own semantic policy.

This primitive alone does not recognize any box type.

## Non-goals

No box-length validation, version acceptance policy, flag acceptance policy,
movie or track metadata, sample tables, fragments, decoder composition,
recording, or physical execution is included.
