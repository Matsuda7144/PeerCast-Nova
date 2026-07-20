# ADR-0104: Diagnostic Late-aware Scheduler Dispatch Transaction

- Status: Accepted
- Milestone: 0.0.8t
- Production FFmpeg activation: Pending and unchanged

## Context

0.0.8r transfers at most one copied video frame and one copied audio frame from
the 0.0.8m completion into the existing `BoundedPlaybackScheduler`. 0.0.8s
selected `PlaybackLateAwareTimedDispatchBoundary.DispatchOneAsync` as the finite
diagnostic scheduler-to-output seam and required timing preflight before the
0.0.8r ownership transfer.

The implementation must preserve the existing queue, timing, late-disposition,
and output boundaries. It must not start the continuous pump, invoke audio
recovery automatically, or activate FFmpeg or Windows output resources.

## Decision

Add `PlaybackDiagnosticLateAwareSchedulerDispatchTransaction` and its owned
completion.

### Preflight

Before calling 0.0.8r, the transaction requires:

- an undisposed `Advanced` 0.0.8m completion;
- a live aggregate native decoder lease;
- exact `Copied` or `NeedMoreInput` state/frame pairs;
- video and audio frames in their exact media slots;
- a known, non-negative presentation timestamp for every copied frame;
- no timestamp or duration synthesis;
- a live scheduler whose initial queue length, drop count, delivery count, and
  owned-byte count are all zero;
- valid bounded timed-dispatch options.

Invalid preflight disposes the source completion but does not dispose an
unrelated caller scheduler.

### Ingress

The transaction delegates ownership transfer to the existing
`PlaybackDiagnosticManagedFrameSchedulerIngressTransaction`.

- Video remains sequence `0`.
- Audio remains sequence `1`.
- Discontinuity remains `None`.
- `NeedMoreInput` contributes no frame.
- Partial ingress remains explicit.
- No atomic two-stream enqueue claim is added.

The accepted-media set is derived from the actual enqueue results and must match
the ingress completion count and the preflight source states. A faulted ingress
never advances into dispatch, even when video ownership was already accepted;
the returned faulted completion retains that scheduler ownership and native
lease for explicit cleanup.

### Finite dispatch

The transaction invokes one injected late-aware dispatch operation per accepted
frame, therefore no more than two calls.

Consumed outcomes are:

- `VideoProcessed`;
- `VideoDropConsumed`;
- `AudioProcessed`.

After every consumed outcome, scheduler queue length and owned-byte accounting
must match the outstanding-media set. A contradictory media result, undefined
enum, invalid item, invalid configuration, concurrent run, or failure is
`Faulted`.

Retained-late, wait-limit, empty, would-block, clock-not-started,
recovery-required, reset-required, initialization, binding, resize, device-lost,
invalid-environment, disposal, and other non-consuming outcomes stop the finite
transaction without retry. Cancellation remains explicit.

### Completion and cleanup

A full advancement releases the ingress-held native lease only after all
accepted scheduler frames have been consumed and scheduler owned-byte accounting
returns to zero.

A partial, stopped, cancelled, or faulted completion retains:

- the remaining scheduler ownership;
- the ingress completion;
- the aggregate native lease;
- accepted and consumed counts;
- outstanding video/audio flags;
- the last dispatch result.

Completion disposal is idempotent and disposes the scheduler before the ingress
completion, ensuring scheduler-owned managed frames are destroyed before the
native lease is released.

The scheduler is caller-created but must remain exclusively dedicated to the
transaction until the returned completion is disposed.

### Testability

The public constructor adapts the existing
`PlaybackLateAwareTimedDispatchBoundary`. Cross-platform tests inject internal
ingress and dispatch operations and consume actual scheduler reservations without
activating D3D11, WASAPI, FFmpeg, or decoder factories.

## Result model

The transaction reports:

- `Advanced` for complete accepted-frame consumption after full ingress;
- `NoFrames` for a valid two-stream `NeedMoreInput` completion;
- `InvalidCompletion`, `InvalidScheduler`, or `InvalidConfiguration` before
  ownership transfer;
- `IngressRejected` when no frame is accepted;
- `PartiallyAdvanced` when some progress occurred without full two-stream
  advancement;
- `DispatchStopped`, `Cancelled`, or `Faulted` with retained cleanup ownership.

Partial ingress followed by consumption of every accepted frame is still
`PartiallyAdvanced`, not full success. A faulted ingress is always `Faulted` and
is never downgraded to partial advancement.

## Non-goals

No demux, packet loop, flush, EOS, continuous decode, resident pump, automatic
audio recovery, duration cutoff, complete A/V synchronization, timestamp
synthesis, second scheduler, production decoder ingress, backend activation,
Windows device activation, FFmpeg bundling, redistribution decision, or
controlled audible or visible playback claim is included.

## Next

Review the completed finite diagnostic path through scheduler ingress and
late-aware dispatch before selecting any bounded demux or production-composition
milestone.
