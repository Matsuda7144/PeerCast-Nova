# PCN-0154: Bounded Physical Output Observation Hold

## Milestone

0.0.10d Phase 5

## Review result

`ImplementedPhysicalVideoRevalidationPending`

## Diagnosed boundary

The controlled physical composition stops the output lifecycle and application-owned runner immediately after one successful delivery and evidence snapshot preparation. The Windows window is shown correctly and the message loop remains active while the runner is running, but the one-frame fixture can complete too quickly for reliable operator-visible observation.

This is not a decoded timestamp, scheduler timing, native present, cleanup, or fixture defect. Deterministic present acknowledgement can succeed while the operator has no practical interval to inspect the window.

## Implemented boundary

- Added `ObservationHoldDuration` to the controlled physical-run options.
- Added explicit state `HoldingObservation` between successful delivery and evidence capture.
- Added command argument `--observation-hold-ms`, bounded from 0 through 30,000 milliseconds.
- Default composition behavior remains zero hold.
- The dedicated physical video helper requests a three-second hold.
- During the hold, no decode, receive, delivery, retry, scheduler operation, native output call, or evidence mutation occurs.
- The lifecycle, D3D11 output ownership, borrowed window, and runner message loop remain alive.
- Cancellation during the hold returns `Cancelled` and retains lifecycle-before-runner cleanup ordering.
- The canonical physical record schema is unchanged.

No timestamp, duration, sequence, generation, discontinuity, fixture, output format, scheduler capacity, wait tolerance, or native evidence counter is modified.

## Regression evidence

Focused cross-platform tests verify:

- cancellation can observe the explicit `HoldingObservation` state after exactly one delivered video frame;
- lifecycle and runner cleanup do not start before the hold is cancelled;
- cancellation then stops lifecycle ownership before the runner;
- delivery attempt and delivered counters remain exactly one;
- a hold above 30 seconds fails before runner start;
- command values below zero, above 30,000, or nonnumeric fail closed.

Routine CI remains responsible for Release compilation with analyzers, the full solution test suite, and format verification.

## Deferred physical evidence

This review does not claim `Completed` video output, accepted `SystemWindows` evidence, recognizable operator-visible video, audio output, operator-audible audio, paired-record admission, or completion of 0.0.10d.

After merge, repeat exactly one controlled physical video run from the new main commit with the same approved runtime and retained fixture. Record the canonical result and `Video visible: Yes／No`. Do not start audio or paired verification until both deterministic video evidence and the operator observation have been reviewed.
