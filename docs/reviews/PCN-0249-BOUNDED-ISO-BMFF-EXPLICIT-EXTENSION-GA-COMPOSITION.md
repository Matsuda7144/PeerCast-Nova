# PCN-0249: Bounded ISO BMFF Explicit-extension GA Composition

## Scope

Review composition of the owned explicit SBR/PS extension with bounded
GASpecificConfig parsing.

## Findings

- Direct non-extension parsing retains its prior source and bit boundary.
- Missing explicit-extension input is distinct from missing common header input.
- Effective core Audio Object Type and exact extension ending bit are used.
- Unsupported core types and Program Config Element requirements fail before
  General Audio bit consumption.
- Successful output preserves both common-header and explicit-extension
  provenance.
- No sync scan, decoder, native operation, demux event, or physical execution
  occurs.

## Decision

Accept bounded explicit-extension GASpecificConfig composition. Implicit sync
extension and Program Config Element semantics remain incomplete.
