# PCN-0245: Bounded ISO BMFF Audio Specific Config Header

## Scope

Review bit-precise leading AudioSpecificConfig projection from the selected
decoder-specific-info descriptor.

## Findings

- Invalid limits and unsupported object-type indication fail before bit access.
- Normal and extended audio object types are distinguished exactly.
- Indexed, explicit, reserved, zero, and truncated sampling-frequency cases are
  covered independently.
- Channel configuration zero is preserved for later Program Config Element
  semantics rather than guessed.
- The output retains an owned bounded configuration view and exact header bit
  length.
- No decoder, native operation, demux event, or physical execution occurs.

## Decision

Accept bounded AudioSpecificConfig header parsing. AAC extension,
GASpecificConfig, and channel-layout semantics remain incomplete.
