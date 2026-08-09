# PCN-0489: Bounded ISO BMFF Regular Movie Native Decoder Session

## Scope reviewed

- exact ADR-0478 binding consumption and fixed video-then-audio transaction
  order;
- context creation, exact managed parameter transfer, `1 / mdhd.timescale`
  packet-time-base configuration, and decoder open;
- optional selected roles, distinct role gates, and exact selected-track source
  retention;
- first-failure cleanup of partial contexts, source preparations, and unused
  gates.

## Verification

- focused managed tests inject fake operations to verify exact role order,
  codec identity, track time bases, opened-session ownership, and partial-open
  cleanup;
- the production adapter only composes previously reviewed FFmpeg boundaries;
  it is not executed by these tests;
- no runtime discovery, path selection, library loading, symbol resolution,
  native invocation, packet submission, output, or physical execution is used.

## Result

Accepted as the bounded regular-movie native decoder-session boundary defined
by ADR-0479. Compressed-packet submission and all downstream decode/output work
remain unimplemented.
