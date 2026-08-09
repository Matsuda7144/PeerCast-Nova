# PCN-0437: Bounded ISO BMFF Fragment Sample Presentation Order

## Decision

Accept exact same-fragment presentation/decode/index ordering with timescale and
source-index validation, without cross-stream merge, clock, scheduler, `mdat`,
payload, decoder, traversal, or physical execution.
