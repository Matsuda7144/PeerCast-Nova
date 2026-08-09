# PCN-0495: Caller-Timed ISO BMFF Regular Movie Scheduler Delivery Admission

## Scope reviewed

- caller-supplied timing boundary and exact video/audio generation binding;
- reservation metadata projection before canonical conversion or sink access;
- exact retention for wait and late-audio recovery, plus accepted scheduler
  drop for late video;
- cancellation, reset-required admission, bounded counters, and fail-closed
  invalid timing results.

## Verification

- focused managed tests inject `Wait` followed by `ProcessNow` and prove the
  same frame, sequence, presentation timestamp, and generation are re-evaluated
  before one canonical delivery;
- focused tests inject `VideoLate` followed by `AudioLate`, verify only video
  is consumed as a scheduler drop, and prove audio remains queued with the
  exact audio generation and no sink callback;
- no delay, output device, system clock, runtime discovery, library loading,
  native invocation, D3D11, WASAPI, or physical execution is used.

## Result

Accepted as the caller-timed regular-movie scheduler delivery admission defined
by ADR-0485. Delay execution, reset/recovery orchestration, application pumping,
and all physical output remain unimplemented.
