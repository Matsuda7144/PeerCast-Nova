# PCN-0496: Bounded ISO BMFF Regular Movie Timed Delivery Wait Driver

## Scope reviewed

- one owned timing-enabled delivery and one active drive at a time;
- positive single-delay, cumulative-wait, and decision-count limits;
- exact retained-reservation re-evaluation after each admitted wait;
- cancellation-preserving retry and fail-closed wait-limit ownership;
- distinct terminal mapping for delivery, late/drop, recovery, clock, reset,
  cancellation, and failure results.

## Verification

- focused managed tests inject two finite waits followed by `ProcessNow` and
  prove the same frame, sequence, timestamp, and generation reach exactly one
  canonical delivery;
- a wait above the caller maximum invokes no delay or sink and closes the
  complete owned aggregate as `WaitLimitReached`;
- cancellation inside an inert blocking delay retains the queued reservation,
  after which the next drive delivers that exact frame once;
- tests use injected timing, delivery, and delay operations only. They do not
  read a system clock, activate output, load a runtime, invoke native code, or
  perform physical execution.

## Result

Accepted as the bounded wait driver defined by ADR-0486. Coordinated timing and
output reset, audio recovery, continuous application pumping, and physical
output remain unimplemented.
