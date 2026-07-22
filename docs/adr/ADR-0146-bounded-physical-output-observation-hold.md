# ADR-0146: Bounded Physical Output Observation Hold

## Status

Accepted for milestone 0.0.10d Phase 5 operator observation remediation.

## Context

The controlled Windows physical run completes after exactly one delivered frame and immediately enters lifecycle cleanup. The application-owned runner window remains valid only until the outer composition stops the lifecycle and runner. Even when native present evidence is accepted, this interval can be too short for an operator to determine whether recognizable video was visibly rendered.

Deterministic native acknowledgement and operator-visible observation are intentionally separate evidence. Repeating delivery, extending scheduler waits, changing timestamps, or keeping a resident playback pump would alter production behavior and would not solve the missing human observation interval safely.

## Decision

Add one explicit bounded observation hold to the controlled physical-run composition:

- expose `ObservationHoldDuration` with a default of zero and a maximum of 30 seconds;
- parse optional command argument `--observation-hold-ms` in the inclusive range 0 through 30,000;
- enter `HoldingObservation` only after the requested video or audio item is delivered exactly once;
- keep the existing lifecycle, native output objects, borrowed application window, and message loop alive during the hold;
- perform no decode, scheduler delivery, retry, resubmission, output call, evidence mutation, or polling during the hold;
- honor cancellation and then execute the existing lifecycle-before-runner cleanup path;
- capture deterministic evidence after the hold and before lifecycle cleanup using the unchanged native counters;
- keep normal callers unchanged because the default duration is zero.

The dedicated controlled video helper requests a visible three-second hold. The duration is explicit operator-run configuration and is not written into the canonical physical record schema.

## Consequences

A successful one-frame video present remains visible for a finite operator observation interval before output teardown. The operator can report `Video visible: Yes` or `Video visible: No` without changing the deterministic admission contract.

The hold is outside scheduler timing and native delivery. It does not widen the 250 ms timing wait, synthesize timestamps, delay a frame before present, repeat output, add a queue, or create a resident task. Cancellation during the hold returns `Cancelled` and still stops lifecycle ownership before the application-owned runner.

This decision does not claim successful physical video evidence, recognizable visible video, audio output, paired-record verification, or completion of 0.0.10d. Those remain pending a post-merge Windows run.
