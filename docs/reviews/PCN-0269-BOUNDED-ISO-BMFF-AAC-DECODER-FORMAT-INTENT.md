# PCN-0269: Bounded ISO BMFF AAC Decoder Format Intent

## Scope

Review managed AAC effective-rate and effective-channel-count intent.

## Findings

- Plain and SBR-only configurations retain their core channel count.
- Present PS converts only a mono core intent to stereo.
- Non-mono PS fails before a format intent is emitted.
- Provenance remains anchored to the resolved extension facts.
- No decoder, native operation, or physical execution occurs.

## Decision

Accept bounded AAC decoder format intent. Capability validation and decoder
composition remain incomplete.
