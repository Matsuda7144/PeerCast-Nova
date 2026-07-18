# ADR-0077: Timed Scheduler Dispatch Boundary

## Status

Accepted for the 0.0.7b technical boundary only. FFmpeg backend activation,
bundling, and distribution remain unapproved, and license review remains
`Pending`. This Accepted status does not approve any of them.

## Context

0.0.7a decides one timing action but deliberately performs no timing effect.
Callers need a finite bridge from a scheduler-owned reservation through that
decision to Windows output without inventing item consumption or retry rules.

## Decision

`PlaybackTimedSchedulerDispatchBoundary.DispatchOneAsync` reserves at most one
item. It delegates each decision to the borrowed 0.0.7a boundary. Only
`ProcessNow` reaches the borrowed output dispatcher, and only a resulting
`VideoProcessed` or `AudioProcessed` acknowledges and consumes the reservation.
All Wait, late, clock, generation, discontinuity, recovery, cancellation,
invalid, and failure outcomes release the reservation without consumption.
Fairness preference advances only after successful processing.

The sole timing effect implemented here is a finite cancellable delay. A Wait
uses the exact decision duration and then redecides the same reservation. Each
call is bounded by `MaximumDecisionCount` from 1 through 16,
`MaximumTotalWait` greater than zero through two seconds, and
`MaximumSingleDelay`/`MinimumDelay` within a positive ordered range whose
maximum is one second. A Wait outside those limits returns `WaitLimitReached`
without delaying or consuming. No busy spin, resident task, unlimited retry,
wall clock, or `DateTime` use is introduced.

Concurrent dispatch is rejected. Reset closes dispatch admission, cancels a
pending delay, and waits for the in-flight operation. It then resets the output
processor in audio-generation then video-generation order before invoking the
0.0.7a transactional generation/timing-origin reset. Only success across all
three stages commits the new generations and `Ready`.

A video-output reset failure receives one audio-output rollback attempt. A
subsequent timing reset failure receives one timing rollback followed by
video-output then audio-output rollback. Successful rollback preserves the old
generation snapshot while returning the original fixed failure. Any rollback
failure exposes `PartialResetFault` and `Faulted`; partial lower state is never
hidden as a successful reset. There is no retry loop.

Dispose closes admission, cancels and waits, and is idempotent. Neither reset
nor dispose owns or disposes the scheduler, timing boundary, processor,
activation resources, frame, clock, or native handles.

## Consequences

Timing decision remains separate from timing effect. Late video/audio items are
retained and reported; this milestone does not drop, duplicate, resample,
stretch, reorder, or correct drift. Precise multimedia timers, complete A/V
synchronization, automatic seek, default-device monitoring, GUI pumping,
FFmpeg activation/demux/codec wiring, distribution/licensing approval, and
real-media playback remain unimplemented.
