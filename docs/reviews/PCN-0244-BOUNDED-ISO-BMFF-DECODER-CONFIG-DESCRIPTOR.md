# PCN-0244: Bounded ISO BMFF Decoder Config Descriptor

## Scope

Review the selected `esds` decoder-config body and its direct nested descriptor
sequence.

## Findings

- The parser accepts only a decoder-config location owned by the ES descriptor.
- Fixed fields, reserved bit, audio stream type, buffer size, and bitrates are
  projected without native or decoder interaction.
- Nested descriptor count and payload bytes have explicit defaults and hard
  limits.
- Missing, duplicate, empty, truncated, and oversized decoder-specific info
  fail closed.
- Nested payload views accept only locations owned by the parsed decoder config.

## Decision

Accept bounded decoder-config parsing. AudioSpecificConfig and SL-config body
semantics remain incomplete.
