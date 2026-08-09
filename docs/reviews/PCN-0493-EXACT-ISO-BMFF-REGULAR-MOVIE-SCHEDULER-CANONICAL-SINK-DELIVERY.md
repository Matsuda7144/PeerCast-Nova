# PCN-0493: Exact ISO BMFF Regular Movie Scheduler Canonical Sink Delivery

## Scope reviewed

- one-owner composition of ADR-0482 scheduler ingress and one borrowed
  canonical decoded-frame sink;
- one active scheduler reservation with exact frame, sequence,
  discontinuity, selected role, and matching decoder-context selection;
- success-only reservation consumption and alternating post-delivery media
  preference;
- exact reservation retention on cancellation and fail-closed aggregate
  cleanup on context, conversion, sink, or integration rejection.

## Verification

- focused managed tests transfer one exact copied video frame through the real
  scheduler reservation and inject successful canonical delivery, verifying
  sequence, discontinuity, sink identity, consumption, and disposal;
- focused tests inject cancellation followed by success and prove the same
  frame and sequence are retained for retry;
- a blocking delivery fake proves concurrent delivery and ingress calls are
  rejected while the exact reservation remains active;
- focused tests inject sink failure and prove delivery operations, scheduler,
  decode progress, native-session fakes, and verified-gate fakes are closed;
- no runtime discovery, library loading, native invocation, output-device
  processing, or physical execution is used.

## Result

Accepted as the exact regular-movie scheduler canonical sink-delivery boundary
defined by ADR-0483. Clock admission, Windows output processing, application
integration, and all physical output remain unimplemented.
