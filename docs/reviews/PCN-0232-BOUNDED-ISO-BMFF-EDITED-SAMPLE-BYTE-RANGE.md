# PCN-0232: Bounded ISO BMFF Edited-sample Byte Range

## Scope

Review the typed association between edited admission and the reconciled sample
byte range.

## Findings

- The admitted sample number is carried by the input type.
- Callers cannot supply a second, conflicting sample number.
- Existing chunk, size, sample-range, and overflow checks remain authoritative.
- The already-resolved chunk and sample-description index are retained without
  a second lookup.
- Successful output preserves both admission appearances and absolute range.
- No file payload or native operation is accessed.

## Decision

Accept bounded edited-sample byte-range association. Top-level `mdat`
containment and production packet composition remain incomplete.
