# ADR-0102: Diagnostic Managed Frame Scheduler Ingress

- Status: Accepted
- Milestone: 0.0.8r
- Production activation: Unchanged

## Context

0.0.8m can retain one owned managed video frame and one owned managed audio
frame. 0.0.8q added aggregate scheduler byte budgeting. The next finite step is
an explicit ownership transfer into the existing scheduler.

## Decision

Add `PlaybackDiagnosticManagedFrameSchedulerIngressTransaction`.

- Accept only an undisposed, `Advanced` 0.0.8m completion.
- Detach frames and the aggregate native decoder lease exactly once.
- Attempt video before audio.
- Use sequence `0` for video and `1` for audio.
- Use discontinuity `None`.
- Treat `Enqueued` and `EnqueuedAfterDroppingOldest` as accepted ownership transfer.
- Do not claim atomic two-stream enqueue.
- If video is rejected, dispose the remaining audio frame and native lease.
- If audio is rejected after video acceptance, retain the native lease for the
  scheduler-owned video frame and report partial acceptance.
- If a stream returned `NeedMoreInput`, skip its enqueue without inventing a frame.

The source completion clears detached references so later disposal cannot destroy
scheduler-owned frames.

## CI policy

Routine GitHub Actions now run on Ubuntu only to conserve the private-repository
minute allowance. Windows validation is deferred to explicit milestone or
release checks when the allowance permits.

## Non-goals

No demux, packet loop, flush, EOS, resident decoder task, production ingress,
backend activation, Windows device activation, new queue, FFmpeg bundling, or
redistribution decision is included.
