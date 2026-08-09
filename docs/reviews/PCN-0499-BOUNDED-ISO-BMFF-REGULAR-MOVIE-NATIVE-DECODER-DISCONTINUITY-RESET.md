# PCN-0499: Bounded ISO BMFF Regular Movie Native Decoder Discontinuity Reset

## Scope reviewed

- exact selected-session borrowing and empty/disposed rejection;
- audio-before-video role ordering and one-role selection support;
- zero-wait active-reset exclusion and pre-operation cancellation;
- healthy ownership retention after complete reset;
- fail-closed session disposal after first-role rejection or partial reset;
- explicit caller-owned decode and delivery quiescence.

## Verification

- focused managed tests inject successful audio and video resets, prove exact
  role order, retain the session, and increment one reset count;
- an injected video rejection after accepted audio reset returns
  `PartialResetFault` and disposes both selected context owners;
- a pre-cancelled reset invokes no role operation and retains the session;
- the production adapter is not invoked. Tests do not load a runtime, call
  `avcodec_flush_buffers`, access output, run a probe or diagnostic, capture
  readiness, or perform physical playback.

## Result

Accepted as the borrowed decoder discontinuity-reset transaction in ADR-0489.
Integration under the ADR-0488 delivery gate and late-audio recovery remain
unimplemented.
