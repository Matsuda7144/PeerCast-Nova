# PCN-0272: Bounded ISO BMFF AAC Managed Codec Parameters

## Scope

Review managed codec-parameter creation and ownership from ADR-0261 input.

## Findings

- Creation retains exact ISO BMFF input provenance.
- The established factory supplies exact bytes plus 64 zero padding bytes.
- AAC sample rate and channel count remain unchanged.
- Disposal is idempotent and clears the owned lease storage.
- No native transfer, decoder open, or physical execution occurs.

## Decision

Accept bounded managed AAC codec parameters. Native transfer and decoder binding
remain incomplete.
