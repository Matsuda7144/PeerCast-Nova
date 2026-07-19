# ADR-0084: Late-Aware Continuous Pump Boundary

## Status

Accepted for the 0.0.7h technical boundary only.

## Context

0.0.7g performs one bounded late-aware timed dispatch while preserving reservation ownership through wait, output dispatch, and late disposition. A finite owner is still required to repeat that one-item operation without hiding late outcomes or introducing unbounded polling.

## Decision

`PlaybackLateAwareContinuousPumpBoundary` repeatedly invokes the 0.0.7g operation under mandatory pump-count, monotonic-duration, consecutive-idle, and idle-delay limits.

- successful video/audio processing and consumed video drops count as progress;
- retained late items, wait-limit, empty, would-block, and clock-not-started count as bounded idle;
- audio recovery, reset, device loss, resize, initialization, and binding requirements stop the run and latch reset required;
- cancellation, stop, disposal, invalid results, and faults terminate finitely;
- reset stops an active run, delegates generation reset to 0.0.7g, and clears fixed counters only on success.

The boundary does not own the injected timed-dispatch boundary. It does not select keyframes, recover audio, duplicate frames, resample/stretch audio, correct drift, or activate native decoder/output resources.

## Consequences

0.0.7h establishes the first finite continuous path that preserves explicit late policy results. Audio recovery effects, precise deadline timers, complete A/V synchronization, and controlled audible/visible real-media playback remain pending.