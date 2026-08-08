# PCN-0273: Bounded ISO BMFF AAC Parameter Transfer Plan

## Scope

Review managed-only transfer planning from the ADR-0262 AAC parameter owner.

## Findings

- Missing or disposed managed parameters fail without producing a plan.
- AAC identity, sample rate, channel count, and extradata length remain tied to
  the retained ADR-0261 input.
- The plan retains the existing owner and does not duplicate extradata.
- The required symbol names exactly match the established FFmpeg parameter
  transfer boundary.
- Disposal is idempotent and clears the underlying managed parameter lease.
- No symbol address, native allocation, native call, decoder-context mutation,
  decoder open, external runtime, or physical execution is involved.

## Decision

Accept bounded managed AAC parameter-transfer planning. Capability binding and
all native transfer execution remain incomplete.
