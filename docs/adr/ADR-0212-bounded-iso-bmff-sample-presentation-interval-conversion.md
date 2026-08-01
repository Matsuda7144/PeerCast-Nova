# ADR-0212: Bounded ISO BMFF Sample Presentation-interval Conversion

## Status

Accepted as a post-0.0.10e non-physical timing-composition slice.

## Context

ADR-0210 provides exact presentation interval units, and ADR-0211 converts one
media-time value with explicit rounding and magnitude policy. Converting both
interval edges together avoids independently rounding a duration that may no
longer equal the difference between managed endpoints.

## Decision

Add a pure managed composed lookup that:

- reuses exact presentation-unit lookup for one sample;
- converts start and exclusive end with the same parsed `mdhd` timing and
  conversion options;
- defines managed duration as converted end minus converted start;
- retains composition-offset value and explicit-presence projection; and
- distinguishes reconciliation, sample bounds, conversion configuration,
  missing timing, and magnitude failures.

## Consequences

Callers receive a bounded managed interval whose duration always equals the
difference between its rounded endpoints, including sub-tick cases.

## Non-goals

No edit list, wall-clock conversion, packet projection, payload read, decoder
composition, recording, or physical execution is included.
