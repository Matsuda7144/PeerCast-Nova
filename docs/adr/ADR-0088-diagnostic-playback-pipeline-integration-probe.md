# ADR-0088: Diagnostic Playback Pipeline Integration Probe

## Status

Accepted for a diagnostic-only, activation-neutral staged harness. Real-media execution remains blocked while the pinned FFmpeg candidate license review is `Pending` and while no public bounded demux contract exists.

## Context

The playback work from 0.0.6d through 0.0.8c introduced individually verified boundaries for native decode ownership, managed conversion and delivery, scheduler and timing policy, Windows output resources, late-item handling, audio recovery, recovery-aware pumping, and deadline budgeting.

The boundaries have not yet been exercised as one diagnostic lifecycle. The goal is to detect contradictions in generation propagation, reservation completion, reset ordering, fail-closed mapping, cancellation, and disposal. Audible or visible output is not a success criterion.

A single undifferentiated harness would risk becoming a second production pipeline and would make failure localization difficult. The CLI remains one command, but the internal plan is split into five finite, non-interleaved phases.

## Decision

Add a CLI-only diagnostic probe named `--playback-pipeline-integration-probe`.

The probe:

- is not referenced by `PlaybackDecoderBackendFactory`, `IPlaybackDecoderBackend`, or the normal playback activation path;
- checks `PlaybackFfmpegBackendCandidate.Provenance.LicenseReview` before accessing runtime or media paths;
- refuses `Pending`, rejected, and undefined review values with fixed enum results;
- uses only `PlaybackDecoderBackendInstalledRuntimeLocator` with `PlaybackFfmpegBackendRuntimeInventory.PinnedCandidate` after approval;
- retains the verified runtime lease while acquiring the existing ABI/symbol gate lease;
- emits no runtime directory or media path;
- creates no retry loop, resident task, activation bypass, or ownership transfer.

The fixed phase plan is:

1. `RuntimeDecode`: runtime ABI, decoder context, codec parameters, packet/frame, metadata, plane/sample, and managed copy.
2. `ManagedDelivery`: decoded output adapter, managed conversion, awaited delivery, policy clock, and bounded scheduler.
3. `SchedulingTiming`: A/V timing, timed dispatch, and reservation-preserving late-aware decisions.
4. `OutputLifecycle`: renderer/audio sink and Windows adapter, activation, binding, initialization, buffer, and scheduler-adapter lifecycle.
5. `RecoveryOrchestration`: finite pump, late-aware continuous pump, audio recovery, controlled restart, and deadline budgeting.

Every boundary belongs to exactly one phase. Phase groups are contiguous and cannot interleave. A later phase is admitted only after the previous phase reports a fixed successful terminal result. Snapshots expose current phase, last completed phase, last stage, generation values, reservation count, and recovery count without free-text diagnostics.

The intended minimal approved flow is one video packet, one audio packet, and one start-recover-stop transaction. That flow cannot be executed from a general MP4/FLV input until a separate bounded demux or diagnostic fixture contract is approved. The probe therefore fails closed with a fixed diagnostic-media result rather than inventing a demux or activation path.

## Ownership and reset

Existing owners remain unchanged. Runtime, ABI gate, decoder context, frames, scheduler items, Windows resources, and sinks retain their existing disposal contracts.

The integration order fixes these invariants:

- video and audio generations remain explicit and are not selected by the probe;
- one reservation is completed exactly once by the existing late-aware contracts;
- audio recovery precedes pump reset;
- output cleanup remains audio-before-video where the existing Windows boundaries require it;
- fault, reset-required, cancellation, and disposal are represented by fixed results.

## Consequences

The current pinned candidate returns `LicenseReviewPending` before path access. This is the expected validation result, not a successful media run.

The staged structure can be merged and reviewed independently from the later media fixture. It does not claim that phase transitions or start-recover-stop have executed yet.

A future license approval does not automatically activate production playback. A separate bounded diagnostic media input/demux decision is still required before the full packet path can execute.