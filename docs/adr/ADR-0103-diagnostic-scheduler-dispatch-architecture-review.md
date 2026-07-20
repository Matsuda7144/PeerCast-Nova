# ADR-0103: Diagnostic Scheduler Dispatch Architecture Review

- Status: Accepted
- Milestone: 0.0.8s
- Production FFmpeg activation: Pending and unchanged

## Context

0.0.8r transfers at most one copied video frame and one copied audio frame into
the existing `BoundedPlaybackScheduler`. Accepted frames become scheduler-owned,
while the 0.0.8r completion retains the aggregate native decoder lease.

The repository already has three possible scheduler-to-output paths:

- `BoundedPlaybackScheduler.RunOneAsync`, which applies the older playback policy
  and awaited decoded-sink delivery path;
- `PlaybackWindowsOutputSchedulerAdapterBoundary`, which dispatches directly to
  the Windows output processor without late-aware timing;
- `PlaybackLateAwareTimedDispatchBoundary`, which preserves one scheduler
  reservation across bounded timing decisions, late disposition, and Windows
  output processing.

The next diagnostic connection must select one existing path without adding a
second queue, bypassing the established late policy, or activating an unapproved
backend or device path.

## Findings

### Dispatch seam

The next finite diagnostic transaction shall reuse
`PlaybackLateAwareTimedDispatchBoundary.DispatchOneAsync`.

It shall not call `BoundedPlaybackScheduler.RunOneAsync` for this connection,
because that would select the separate awaited-sink route rather than the
late-aware Windows output route under review. It shall not use
`PlaybackWindowsOutputSchedulerAdapterBoundary` as the final composition seam,
because that adapter does not apply the existing timing and late-disposition
boundaries.

No new decoded-frame queue, conversion buffer, scheduler implementation, or
output dispatcher is approved.

### Timing readiness

`PlaybackLateAwareTimedDispatchBoundary` requires each reserved item to have a
known, non-negative presentation timestamp. Existing decoder copy boundaries
allow unknown timestamps, and 0.0.8r intentionally does not invent or validate a
timing policy.

The next composition must therefore inspect each copied frame before scheduler
ingress:

- every `Copied` frame must have a known, non-negative presentation timestamp;
- `NeedMoreInput` remains valid and contributes no frame;
- unknown or negative timestamps fail closed before ownership is transferred;
- duration may remain unknown;
- no timestamp, duration, sequence, or discontinuity value is synthesized beyond
  the fixed 0.0.8r sequence and discontinuity contract.

Because 0.0.8r clears the detached source references, the safe composition seam
starts from the 0.0.8m managed-frame completion and invokes the 0.0.8r ingress
inside the same finite transaction. An arbitrary already-populated scheduler is
not a sufficient source of timing-readiness evidence.

### Scheduler ownership

The diagnostic transaction requires one fresh, caller-owned
`BoundedPlaybackScheduler` that is used exclusively for the transaction until
its completion is disposed.

Before ingress, the complete scheduler snapshot must be zero:
`QueueLength == 0`, `DropCount == 0`, `DeliveryCount == 0`, and
`OwnedByteCount == 0`. This is required because an emptied but previously used
scheduler can retain sequence history and reject the fixed 0.0.8r sequences.
External enqueue, reservation, dispatch, or disposal during the transaction is
outside the contract and must be treated as an invalid or faulted composition
when detected. The transaction does not create a second queue and does not claim
a general multi-producer scheduler protocol.

### Finite dispatch

The transaction attempts at most one late-aware dispatch for each frame accepted
by 0.0.8r, therefore at most two dispatch calls.

- Video remains the first attempted ingress and uses sequence `0`.
- Audio remains the second attempted ingress and uses sequence `1`.
- A consumed video result is `VideoProcessed` or `VideoDropConsumed`.
- A consumed audio result is `AudioProcessed`.
- Retained-late, wait-limit, would-block, clock-not-started, recovery-required,
  reset, device, binding, resize, initialization, invalid-environment,
  cancellation, disposal, and failure results stop the finite transaction.
- The transaction does not retry, start the continuous pump, or invoke audio
  recovery.

The existing bounded decision-count and wait-duration options remain the only
wait policy used by each dispatch call.

### Lease and cleanup

The aggregate native decoder lease remains owned until every accepted diagnostic
frame has been conclusively consumed or the scheduler and transaction completion
are explicitly disposed.

When all accepted frames are consumed and the scheduler has no remaining owned
frame, the transaction may release the lease and report full advancement. If a
result retains or leaves any accepted frame outstanding, the returned completion
must retain the lease and identify the outstanding media. Partial advancement is
not reported as full success.

Cancellation, invalid input, contradictory state, and unexpected failure must
not dispose a scheduler-owned frame through the old source completion. Cleanup
must remain idempotent and preserve scheduler ownership until scheduler disposal
or successful reservation consumption.

### Platform and activation

The public late-aware output path remains Windows 11 scoped. Cross-platform CI
shall validate the new orchestration through injected dispatch operations rather
than activating D3D11, WASAPI, FFmpeg, or a decoder factory.

The composition may surface `InitializationRequired`, `BindingRequired`,
`ResizeRequired`, `DeviceLost`, and `InvalidEnvironment`; it must not satisfy
those states by creating or activating resources.

## Decision

0.0.8s is a documentation and architecture-alignment milestone. It adds no
runtime wiring.

The next implementation milestone is 0.0.8t: add a finite diagnostic late-aware
scheduler dispatch transaction that owns the 0.0.8m preflight, delegates ingress
to the existing 0.0.8r transaction, performs at most two injected late-aware
dispatch calls, tracks consumed and outstanding media explicitly, and releases
the aggregate lease only after complete consumption or explicit cleanup.

Focused tests must cover unknown timestamps, zero/one/two-frame paths, ordering,
video drop, retained and recovery outcomes, partial advancement, cancellation,
fault cleanup, complete zero-snapshot scheduler preconditions, and
source-disposal safety.

## Non-goals

No demux, packet loop, flush, EOS, continuous decode, resident pump, automatic
audio recovery, complete A/V synchronization, timestamp synthesis, second
scheduler, production decoder ingress, backend activation, Windows device
activation, FFmpeg bundling, redistribution decision, or controlled audible or
visible playback claim is included.
