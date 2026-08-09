# PCN-0486: Selected ISO BMFF Regular Movie Managed Decoder Preparation

## Scope reviewed

- exact binding from the selected ADR-0475 streams back to their regular-track
  compositions;
- owned consistent AVC decoder-configuration input and exact payload policy;
- ordinary AAC-LC decoder-config and format-intent parsing through the existing
  owned managed parameter, symbol, capability, and sample-entry consistency
  chain;
- aggregate AAC lease disposal and first-failure rejection without partial
  output.

## Verification

- focused managed tests cover selected AVC/AAC source retention, configuration
  ownership after caller-buffer mutation, exact AAC sample-rate/channel facts,
  managed lease disposal, video-only preparation without AAC declarations,
  invalid aggregate configuration, incomplete AAC descriptors, and incomplete
  symbol coverage;
- no runtime loading, native invocation, decoder open, packet submission,
  scheduling, output, or physical execution is required.

## Result

Accepted as the selected regular-movie managed decoder-preparation bridge
defined by ADR-0476.
