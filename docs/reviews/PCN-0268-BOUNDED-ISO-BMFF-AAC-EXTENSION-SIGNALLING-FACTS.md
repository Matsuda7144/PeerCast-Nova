# PCN-0268: Bounded ISO BMFF AAC Extension Signalling Facts

## Scope

Review source-bound resolution across absent, explicit, and implicit SBR/PS.

## Findings

- Explicit and implicit signalling cannot be mixed.
- Implicit SBR and PS require exact source identity.
- Effective sampling frequency changes only for present SBR.
- PS remains a presence fact and does not silently change channel count.
- No decoder, native operation, or physical execution occurs.

## Decision

Accept bounded AAC extension signalling facts. Decoder-ready format policy and
decoder composition remain incomplete.
