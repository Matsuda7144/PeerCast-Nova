# PCN-0500: Shared Manual and Automatic Recording Runtime

## Scope reviewed

- explicit `Manual` and `AutomaticRule` session origins;
- a GUI-independent runtime API for manual start, stop, state, automatic rule
  evaluation, and shutdown;
- shared per-channel coordination for manual and automatic session ownership;
- cancellation of source startup when a channel stop arrives;
- composition of the existing PCP relay fan-out recording provider with the
  recording file sink;
- orderly shutdown of recording sessions before relay owners.

## Verification

- core tests cover no-active stop, duplicate and competing starts, source
  startup cancellation, source failure, terminal-session reconciliation,
  partial-file growth/finalization, and shutdown finalization;
- a headless PCP relay test writes packets through the existing fan-out reader,
  observes the partial file grow, stops recording, and verifies the finalized
  file bytes;
- all results are managed test evidence. No GUI integration or physical
  channel playback was performed; physical acceptance remains **NOT PROVEN**.

## Result

The #242 branch provides the manual/automatic recording boundary described by
ADR-0490. PR #230 remains a separate GUI integration lane. Production service
hosting, GUI wiring, and physical acceptance remain outside this review.
