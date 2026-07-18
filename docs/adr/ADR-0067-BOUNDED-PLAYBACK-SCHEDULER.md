# ADR-0067 Bounded Playback Scheduler

## Status

Accepted for 0.0.6r.

## Decision

The scheduler is explicitly driven through `RunOneAsync`; it starts no resident
loop or background task. Audio and video share one fixed-array ring buffer with
an explicit capacity. The default is 16 frames and the hard limit is 1,024.
No unbounded collection is used.

Full-buffer behavior is configured as `DropOldest`, `DropIncoming`, or `Reject`.
FIFO order is retained among accepted frames. Enqueue transfers frame ownership
to the scheduler. Every accepted, rejected, incoming-dropped, oldest-dropped,
policy-dropped, cancelled, delivered, or disposal-drained frame is disposed
exactly by the scheduler.

Each run evaluates the existing 0.0.6q policy. `DeliverNow` delivers through the
awaited 0.0.6p boundary; `Wait` awaits its finite bounded duration with linked
caller/disposal cancellation; `Drop`, `ResetClock`, and `Reject` return fixed
scheduler results. A single async gate permits one run and one sink callback at
a time. The gate and disposal token source are not disposed while operations may
still observe them; disposal closes admission, cancels active wait/delivery, and
drains queued ownership without an `ObjectDisposedException` race.

Cancellation, disposal, sink failure, delivery failure, and policy rejection do
not dispose the decoder context. Only unexpected integration failure is fail
closed. Snapshot queue length is bounded by capacity; drop and delivery counters
saturate at `long.MaxValue`.

## Non-goals

This milestone adds no resident loop, automatic background task, renderer,
audio device, frame duplication, resampling, advanced synchronization, demux,
backend activation, or FFmpeg distribution.
