# ADR-0087: Deadline-aware Run Budget Policy

## Status

Accepted for milestone 0.0.8c.

## Context

The recovery-aware pump orchestrator can run only with fixed finite pump-count and duration limits. A future caller may also have a monotonic deadline, but passing the full requested pump limits can overrun the caller's remaining scheduling window or leave no safety margin for follow-up work.

This milestone must not add a resident scheduler, timer, delay, output dispatch, audio recovery effect, generation change, or FFmpeg backend activation. It must remain a pure managed policy over caller-supplied monotonic timestamps and existing pump options.

## Decision

Add `PlaybackDeadlineAwareRunBudgetPolicyBoundary` as a side-effect-free decision boundary.

The boundary:

- accepts non-negative monotonic `now` and `deadline` values;
- requires `deadline >= now`;
- validates the complete nested `PlaybackLateAwareContinuousPumpOptions` contract;
- subtracts a caller-supplied non-negative safety margin from the remaining deadline;
- limits run duration to the smaller of usable deadline time and the requested maximum run duration;
- limits pump count to the smaller of the requested maximum and the number of minimum-duration pump units fitting in the run budget;
- returns `DeadlineReached` when no complete minimum pump unit fits;
- preserves the requested idle, dispatch, and nested policy configuration without taking ownership;
- fails closed for invalid configuration and unexpected arithmetic failure.

The boundary does not read a clock itself. Timestamp acquisition and deadline selection remain caller-owned so tests and future orchestration remain deterministic.

## Consequences

A caller can derive one finite pump slice that cannot intentionally exceed its supplied deadline budget. The result is still only a budget: it does not guarantee actual execution time, preempt in-flight work, or implement precise deadline scheduling.

No code path activates FFmpeg, selects a backend, touches native resources, starts recovery, changes generations, or runs the pump.

## Deferred

- integration with `PlaybackRecoveryAwarePumpOrchestrator`;
- repeated deadline-aware orchestration;
- measured per-pump cost adaptation;
- preemption of in-flight dispatch;
- precise deadline scheduling and drift correction;
- real decoder, renderer, audio-device, or FFmpeg backend activation.
