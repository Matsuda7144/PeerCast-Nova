# PCN-0110: Diagnostic Managed Frame Scheduler Ingress Validation

## Scope

0.0.8r transfers owned managed frames from the 0.0.8m completion into the
existing bounded scheduler.

## Acceptance criteria

- Source ownership detaches at most once.
- Video is attempted before audio.
- Video uses sequence 0 and audio uses sequence 1.
- Both use discontinuity `None`.
- `NeedMoreInput` streams are skipped.
- Accepted scheduler results transfer frame ownership.
- Video rejection disposes remaining unsubmitted ownership and the native lease.
- Audio rejection after video acceptance reports one accepted frame and retains
  the native lease.
- Faults after partial acceptance preserve the lease required by accepted work.
- Source disposal after transfer cannot dispose scheduler-owned frames.
- No atomic two-stream enqueue claim is made.

## CI policy

Routine GitHub Actions run restore, Release build, full tests, and format on
Ubuntu only. Windows CI is intentionally paused to conserve the free allowance.

The final branch head must pass restore, Release build, the full test suite, and
format verification on Ubuntu before merge.

Status: Verified.
