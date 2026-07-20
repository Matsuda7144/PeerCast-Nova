# PCN-0111: Diagnostic Scheduler Dispatch Architecture Review

## Scope

0.0.8s reviews the safe connection from the verified 0.0.8r scheduler ingress
to the existing timing, late-disposition, and Windows output boundaries.

This is a documentation and architecture-alignment milestone. It does not add
runtime wiring.

## Evidence reviewed

- `PlaybackDiagnosticManagedFrameCopyTransactionCompletion`
- `PlaybackDiagnosticManagedFrameSchedulerIngressTransaction`
- `BoundedPlaybackScheduler`
- `PlaybackWindowsOutputSchedulerAdapterBoundary`
- `PlaybackLateAwareTimedDispatchBoundary`
- `PlaybackLateAwareContinuousPumpBoundary`
- `PlaybackRecoveryAwarePumpOrchestrator`
- ADR-0100 through ADR-0102
- PCN-0108 through PCN-0110
- current README, roadmap, navigation, and changelog state

## Required conclusions

- The next diagnostic output seam reuses
  `PlaybackLateAwareTimedDispatchBoundary.DispatchOneAsync`.
- The older awaited-sink `RunOneAsync` route and the direct Windows scheduler
  adapter are not selected as the final late-aware composition seam.
- No second decoded-frame queue or scheduler implementation is introduced.
- Every copied frame must have a known, non-negative presentation timestamp
  before 0.0.8r transfers ownership.
- Unknown timestamps fail closed and are not synthesized; unknown duration
  remains valid.
- The safe transaction starts from the 0.0.8m completion and performs 0.0.8r
  ingress internally because detached source frames cannot be preflighted later.
- One fresh caller-owned scheduler is exclusively used for the transaction.
- Its initial `QueueLength`, `DropCount`, `DeliveryCount`, and `OwnedByteCount`
  must all be zero so prior sequence history cannot contradict the fixed 0.0.8r
  sequences.
- At most one dispatch is attempted per accepted frame, so the next transaction
  performs no more than two dispatch calls.
- Retained, wait, recovery, reset, device, environment, cancellation, disposal,
  and failure outcomes stop the finite transaction without an implicit retry.
- The continuous pump and recovery orchestrator remain outside the next
  transaction.
- The aggregate native lease is released only after every accepted diagnostic
  frame is consumed, or through explicit idempotent cleanup with scheduler
  ownership preserved.
- Partial advancement remains explicit and is not reported as full success.
- Windows output resources, FFmpeg, and decoder factories are not activated by
  the diagnostic composition.
- Production activation, bundling, redistribution, demux, continuous decode,
  and audible or visible playback approval remain unchanged.

## Next implementation

0.0.8t will add a finite diagnostic late-aware scheduler dispatch transaction
with focused tests for timing readiness, complete zero-snapshot scheduler
preconditions, zero/one/two-frame paths, result mapping, partial advancement,
lease lifetime, cancellation, failure, and cleanup.

## Documentation alignment

This milestone updates `CHANGELOG.md`, `NAVIGATION.md`, `ROADMAP.md`, and
`README.md` through 0.0.8s and identifies 0.0.8t as the next implementation
milestone.

## Automated validation

The final branch head must pass restore, Release build, the full test suite, and
format verification on the routine Ubuntu CI workflow before merge.

Status: Review accepted; final-head CI is required before merge.
