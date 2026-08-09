# PCN-0491: Bounded ISO BMFF Regular Movie Native Decode Progress

## Scope reviewed

- one-action packet, flush, or receive progress over the exact ADR-0480
  submission session;
- packet and flush backpressure relief through one managed-frame copy before
  exact retry;
- exclusive disposable frame completion, monotonic bounded frame sequence, and
  role/frame-kind agreement;
- exhausted-only video-then-audio flush, drain to per-role end of stream, and
  complete aggregate cleanup.

## Verification

- focused managed tests inject packet, flush, and receive results to verify
  packet backpressure, one copied frame, active-frame blocking, release, and
  same-packet retry;
- focused tests verify a complete four-packet merge followed by video flush
  backpressure, video drain, video EOS, audio drain, audio EOS, and completion;
- video-only and audio-only tests prove that only explicitly selected roles are
  flushed and drained;
- no runtime discovery, library loading, native packet/flush send, native frame
  receive, output, or physical execution is used.

## Result

Accepted as the bounded regular-movie native decode-progress boundary defined
by ADR-0481. Canonical decoded-output adaptation, scheduler ingress, and all
physical output remain unimplemented.
