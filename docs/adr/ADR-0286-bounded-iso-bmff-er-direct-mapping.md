# ADR-0286: Bounded ISO BMFF ER Direct Mapping

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Decision

For `epConfig` three only, parse the 1-bit `directMapping` value from the exact
ADR-0285 end and retain both true and false as signalling facts. Distinguish a
missing config, `epConfig` two, and truncation without inferring support for the
false value's unspecified continuation.

## Non-goals

Admission policy for false, decoder or native integration, and physical
execution are not included.
