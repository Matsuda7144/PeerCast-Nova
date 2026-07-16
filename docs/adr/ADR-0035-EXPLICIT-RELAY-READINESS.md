# ADR-0035 Explicit Relay Readiness

## Status

Accepted for Milestone 0.0.4h.

## Context

The 0.0.4g end-to-end tool waited for a loopback listener by inspecting the
Windows TCP table. The CLI also printed `Relay State: Ready` when the
coordinator task was merely incomplete. Listener binding currently happens
synchronously, but neither observation was an explicit coordinator contract.
This coupled safe orchestration to an operating-system command and left future
refactoring able to invalidate the task-state inference.

Readiness must mean that the coordinator-owned downstream listener has
successfully bound. Bind failure and stop-before-bind must complete the wait
without free-form error text or an indefinitely pending task. Canceling one
observer's wait must not stop the coordinator.

## Decision

`PcpRelayCoordinator` exposes `WaitForReadinessAsync` and the fixed
`PcpRelayCoordinatorReadinessOutcome` values `Ready`, `Stopped`, and `Failed`.
A run completes the readiness source as follows:

- `Ready` immediately after `PcpDownstreamRelayServer.RunAsync` binds its
  listener;
- `Failed` when listener startup throws or the run otherwise ends before a
  successful bind;
- `Stopped` when explicit stop, caller cancellation, or disposal wins before
  readiness.

The readiness source uses asynchronous continuations and completes only once.
Cancellation passed to `WaitForReadinessAsync` cancels that wait only; it does
not cancel or mutate coordinator ownership.

The relay CLI awaits this contract with an explicit non-canceling observer and
prints `Relay State: Ready` only for `Ready`. Its existing caller token still
owns coordinator cancellation and terminal result generation.

The one-command PowerShell gate starts the relay as its owned job, consumes
its output incrementally, and starts the downstream probe only after the exact
Ready line. It no longer inspects the system TCP table. A port already owned by
another process causes coordinator bind failure, no Ready line, and no
downstream connection attempt.

## Consequences

- Listener readiness is a Network contract rather than an inferred task or OS
  state.
- The end-to-end gate no longer requires `Get-NetTCPConnection`.
- Occupied-port failure is decided by the component that owns the bind and
  cannot attach the downstream probe to the unrelated listener.
- Start, session, relay-window, completion, and owned-job cleanup waits remain
  finite.
- No protocol, packet-type, bootstrap, payload, metadata, or Core boundary is
  changed.
- The controlled official-upstream real-media requirement was subsequently
  completed in PCN-0038, marking full 0.0.4 Relay Verified.
