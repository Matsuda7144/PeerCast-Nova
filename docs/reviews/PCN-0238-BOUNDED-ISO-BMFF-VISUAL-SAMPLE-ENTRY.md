# PCN-0238: Bounded ISO BMFF Visual Sample Entry

## Scope

Review bounded parsing of the common `avc1`／`avc3` VisualSampleEntry prefix and
child-box sequence.

## Findings

- Selection type, handler kind, current entry location, and fixed prefix are
  revalidated.
- Reserved fields and one-based data-reference invariants fail closed.
- Compressor name remains bounded raw bytes without assuming text encoding.
- Child locations are absolute within current `stsd` bytes and count bounded.
- No child payload, decoder, or native operation is interpreted.

## Decision

Accept bounded visual sample-entry parsing. AVC decoder-configuration parsing
remains incomplete.
