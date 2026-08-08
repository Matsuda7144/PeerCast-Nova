# ADR-0236: Bounded ISO BMFF GA Specific Config

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0235 projects the exact bit position after the leading MPEG-4
AudioSpecificConfig header. General Audio object types require a bounded decode
of the following GASpecificConfig fields before later AAC initialization can be
defined.

## Decision

Add a pure managed parser that:

- accepts only General Audio object types defined for GASpecificConfig;
- starts at the exact owned bit position projected by ADR-0235;
- projects frame-length, core-coder dependency, and optional core-coder delay;
- projects scalable layer, ER BSAC, and error-resilience extension fields only
  for the object types that define them;
- reports the exact consumed bit position; and
- rejects channel configuration zero until Program Config Element semantics are
  implemented.

## Consequences

Supported MPEG-4 General Audio configurations now have explicit bounded
configuration semantics without duplicating bit arithmetic or guessing absent
fields.

## Non-goals

No Program Config Element, implicit or sync-extension SBR/PS handling,
channel-layout resolution, decoder extradata composition, native call, demux
event, or physical execution is included.
