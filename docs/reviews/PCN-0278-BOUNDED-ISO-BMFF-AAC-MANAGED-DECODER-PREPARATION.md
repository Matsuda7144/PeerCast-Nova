# PCN-0278: Bounded ISO BMFF AAC Managed Decoder Preparation

## Scope

Review end-to-end managed AAC preparation through declared capability binding.

## Findings

- Each existing bounded stage executes at most once and retains exact source
  identity through the final owner.
- Successful preparation exposes exact intent, codec, rate, channels, extradata
  length, required symbols, and declaration provenance.
- Missing configuration, symbol rejection, and capability rejection produce no
  partial preparation.
- Failure and final disposal clear the padded managed parameter owner.
- No runtime, ABI, address, decoder context, native call, packet, or physical
  execution is involved.

## Decision

Accept bounded managed AAC decoder preparation. Verified runtime binding and all
native decoder execution remain incomplete.
