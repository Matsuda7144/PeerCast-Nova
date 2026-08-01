# PCN-0239: Bounded ISO BMFF Audio Sample Entry

## Scope

Review bounded parsing of the common version-zero `mp4a` AudioSampleEntry prefix
and child-box sequence.

## Findings

- Selection type, audio handler, current entry location, and fixed prefix are
  revalidated.
- Reserved, data-reference, channel, sample-size, and sample-rate invariants
  fail closed.
- Fractional 16.16 sample-rate bits remain available without rounding.
- Child locations are absolute within current `stsd` bytes and count bounded.
- No child payload, decoder, or native operation is interpreted.

## Decision

Accept bounded audio sample-entry parsing. MPEG-4 elementary-stream descriptor
parsing remains incomplete.
