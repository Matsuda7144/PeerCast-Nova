# ADR-0075: Finite Continuous Playback Pump Policy Boundary

## Status

Accepted for the 0.0.6z technical boundary only. FFmpeg backend activation,
bundling, and distribution remain unapproved, and license review remains
`Pending`. This Accepted status does not approve any of them.

## Context

0.0.6y provides explicit one-item pumping and retained backpressure, but callers
would otherwise need to invent continuous-loop bounds, idle waiting, stopping,
and reset behavior. An unbounded resident loop or busy spin is unacceptable.

## Decision

`FiniteContinuousPlaybackPumpPolicyBoundary.RunAsync` starts one explicit run.
Each iteration invokes the borrowed 0.0.6y adapter at most once. Every run must
provide all of these independently enforced bounds:

- pump count: 1 through 1,000,000;
- monotonic run duration: greater than zero through one hour;
- consecutive idle count: 1 through 100,000;
- idle delay: 1 millisecond through one second.

The production policy uses `Stopwatch` monotonic time, cancellable `Task.Delay`,
and a concrete adapter wrapper. The clock, delay/wake, adapter, and platform
check remain injectable for device-free tests. Backward, negative, overflowing,
or exceptional clock observations fail closed.

Video/audio success clears the consecutive-idle count. `Empty` and
`WouldBlock` increment it and perform exactly one bounded cancellable delay
unless the idle limit has been reached. No busy spin, hidden retry batch,
internal item drop, timer thread, or background resident task is introduced.

Device loss, resize, initialization/binding requirements, and adapter
`ResetRequired` stop the run with fixed recovery results. Invalid items,
unsupported format, undefined results, and exceptions fail closed. Caller
cancellation and explicit Stop both interrupt idle delay. Concurrent Run is
rejected, and Stop is idempotent.

Reset first stops an in-flight run, then delegates once to the 0.0.6y
transactional reset. Only reset success returns policy state to `Idle`.
`PartialResetFault` becomes `ResetRequired`; failure becomes `Faulted`.
Queued/reserved scheduler items remain under their existing owner. Dispose
closes admission and waits for the active run but does not dispose the
scheduler, adapter, processor, or activation resources.

Reset admission, run stopping, lower reset, and final state commit form one
exclusive operation represented by `Resetting` and an active-operation task.
While it is active, Run returns `ResetInProgress`, a second Reset returns
`AlreadyResetting`, and Stop returns `ResetInProgress`; none calls the lower
pump or changes queued ownership. Caller cancellation propagates to the lower
reset and commits `ResetRequired`, because partial lower changes cannot be
excluded. Dispose cancels and waits for the same reset operation, then fixes
`Disposed`; no reset or run continuation can overwrite that state.

## Consequences

0.0.6z implements finite continuous pump policy rather than unlimited
continuous playback. It does not implement timestamp A/V synchronization,
drift correction, video drop/duplication, audio resampling, precise deadline
scheduling, device monitoring, GUI/message pumping, FFmpeg activation or codec
wiring, distribution/licensing approval, or real-media playback.
