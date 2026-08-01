# PCN-0241: Bounded ISO BMFF MPEG-4 Descriptor Header

## Scope

Review bounded parsing of the shared MPEG-4 descriptor tag/length prefix.

## Findings

- All one-to-four-byte terminated encodings are represented exactly.
- Common non-minimal four-byte encodings remain supported.
- Header truncation, payload truncation, unterminated length, trailing input,
  and payload limit have distinct outcomes.
- Limit validation occurs before input access.
- No descriptor body, decoder, or native operation is interpreted.

## Decision

Accept bounded MPEG-4 descriptor-header parsing. `esds` hierarchy semantics
remain incomplete.
