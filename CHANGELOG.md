# Changelog

## [Unreleased]

The detailed historical changelog through 0.0.8g remains preserved in Git
history at commit `f078dab5372a45a8c577c1c802a697ab72b45c02` and in the associated
ADR/PCN validation records. This active file keeps the current playback
integration sequence concise.

### 0.0.8t - Diagnostic Late-aware Scheduler Dispatch Transaction

- Added a finite diagnostic composition from the 0.0.8m managed-frame completion through the existing 0.0.8r scheduler ingress and late-aware timed dispatch seam.
- Required known, non-negative presentation timestamps and a fresh zero-snapshot scheduler before ownership transfer.
- Attempted at most one dispatch per accepted frame and at most two dispatch calls total.
- Treated video processing, consumed video drop, and audio processing as explicit media consumption.
- Preserved retained, recovery, reset, device, environment, cancellation, partial-ingress, and failure outcomes without implicit retry.
- Retained outstanding scheduler ownership and the native lease in an idempotent completion, disposing scheduler-owned frames before releasing the lease.
- Added focused cross-platform tests through injected dispatch operations without activating D3D11, WASAPI, FFmpeg, or decoder factories.
- Added ADR-0104 and PCN-0112. Demux, continuous decode, production activation, and controlled audible or visible playback remain outside scope.

### 0.0.8s - Diagnostic Scheduler Dispatch Architecture Review

- Selected `PlaybackLateAwareTimedDispatchBoundary.DispatchOneAsync` as the next finite scheduler-to-output composition seam.
- Rejected the older awaited-sink route, the direct untimed Windows adapter, a second queue, and automatic pump or recovery startup for this milestone.
- Required known, non-negative presentation timestamps before scheduler ingress without synthesizing timestamps or durations.
- Fixed the safe composition start at the 0.0.8m completion so timing readiness is checked before 0.0.8r detaches ownership.
- Required one fresh, exclusively used caller-owned scheduler and at most one dispatch attempt per accepted frame.
- Kept the aggregate native lease until every accepted frame is consumed or explicit idempotent cleanup disposes the remaining scheduler ownership.
- Preserved explicit partial, retained, recovery, reset, device, environment, cancellation, and failure outcomes.
- Added ADR-0103 and PCN-0111. Runtime wiring, activation, demux, continuous decode, and controlled audible or visible playback remain outside scope.

### 0.0.8r - Diagnostic Managed Frame Scheduler Ingress

- Added a finite diagnostic ownership transfer from the 0.0.8m managed-frame completion into the existing bounded scheduler.
- Attempted video before audio with fixed sequences 0 and 1 and discontinuity `None`.
- Skipped streams that returned `NeedMoreInput`.
- Cleared detached source references so later source disposal cannot destroy scheduler-owned frames.
- Preserved the aggregate native lease after partial acceptance when an accepted frame still requires it.
- Reported video rejection, audio rejection, partial acceptance, and faults without claiming atomic two-stream enqueue.
- Reduced routine GitHub Actions to Ubuntu only and cancelled superseded runs on the same PR.
- Added one-day build-log artifacts only on build failure.
- Added ADR-0102 and PCN-0110. Demux, continuous decode, production activation, and FFmpeg distribution remain outside scope.

### 0.0.8q - Bounded Scheduler Aggregate Byte Budget

- Added an aggregate owned-byte budget independent from frame-count capacity.
- Accounted queued, output-reserved, and `RunOneAsync` in-flight managed frames until scheduler ownership ends.
- Added checked plane-buffer aggregation, fixed single-frame oversize rejection, and `OwnedByteCount` snapshots.
- Applied `DropOldest`, `DropIncoming`, and `Reject` when either count or byte limits would be exceeded.
- Allowed `DropOldest` to remove multiple unreserved frames while protecting output reservations.
- Released byte accounting on delivery, policy disposal, cancellation, failure, reservation completion, eviction, and scheduler disposal.
- Kept the prior three-argument snapshot construction source compatible through an optional fourth value.
- Added ADR-0101 and PCN-0109. Decoder ingress, demux, duration cutoff, production activation, and new dependencies remain outside scope.

### 0.0.8p - Playback Cutover Architecture Review

- Reviewed the completed finite diagnostic path through awaited sink delivery before scheduler or production cutover.
- Fixed the future composition seam at the owned managed frame before conversion; converted callback payload is never queued.
- Required reuse of `BoundedPlaybackScheduler` and rejected a second decoded-frame queue.
- Identified aggregate scheduler byte budgeting as mandatory because frame-count capacity alone is not a sufficient memory bound.
- Deferred duration cutoff while unknown timestamps and durations remain valid at existing decode boundaries.
- Retained the current project split, `net10.0`, host-installed FFmpeg policy, and no-new-production-dependency position.
- Added third-party asset provenance and redistribution-permission requirements.
- Added ADR-0100 and PCN-0108 and synchronized project documentation.

### 0.0.8o - Diagnostic Awaited Sink Delivery Transaction

- Added one finite video-then-audio diagnostic transaction through the existing awaited decoded sink boundary.
- Delivered each copied frame only after validating its expected media kind.
- Used fixed sequences `0` and `1`, `None` discontinuity, and one caller-owned sink and cancellation token.
- Disposed copied frames after awaited delivery and retained only fixed states plus the aggregate native lease on success.
- Failed closed on cancellation, contradictory copy state, sink rejection, and unexpected failure.
- Added ADR-0099 and PCN-0107.

### 0.0.8n - Diagnostic Managed Format Conversion Transaction

- Connected one bounded H.264/AAC diagnostic transaction to the existing managed conversion boundary.
- Kept converted video/audio payload callback-scoped and retained summary metadata only.
- Disposed copied input frames before return and preserved the aggregate lease only on success.
- Added ADR-0098 and PCN-0106.

### 0.0.8m - Diagnostic Managed Frame Copy Transaction

- Added a finite H.264/AAC transaction that copies at most one owned managed frame per stream.
- Transferred managed frame and aggregate native lease ownership only on successful completion.
- Added reverse-order disposal and complete cleanup for contradictory, rejected, and faulted paths.
- Added ADR-0097 and PCN-0105.

### 0.0.8l - Diagnostic Frame Data Transaction

- Connected bounded packet submission to callback-scoped video plane and audio sample views.
- Retained only metadata, view count, and aggregate referenced-byte count.
- Prevented native pointers, callback views, and frame bytes from escaping their callback.
- Added ADR-0096 and PCN-0104.

### 0.0.8k - Diagnostic Frame Metadata Transaction

- Connected bounded packet submission to one callback-scoped metadata observation per stream.
- Retained validated managed metadata only and kept native AVFrame ownership inside the callback.
- Added ADR-0095 and PCN-0103.

### 0.0.8d-0.0.8j - Diagnostic Gate through Packet/Frame

- Added the diagnostic gate, bounded fixture, codec configuration, native parameter transfer, codec open, and one finite packet/frame attempt per stream.
- Kept demux, loops, flush, output, activation, and redistribution outside scope.

### 0.0.8a-0.0.8c - Recovery and Run Budget

- Added bounded audio recovery, recovery-aware pump orchestration, and deadline-aware finite run budgeting.
- Preserved explicit reset-required and fault outcomes without automatic production activation.

### 0.0.7a-0.0.7h - Timing and Late-aware Dispatch

- Added monotonic A/V timing decisions, bounded wait/redecision, late-item policy and reservation handling, and finite late-aware pumping.
- Kept resampling, drift correction, complete synchronization, and real-media claims pending.

### 0.0.6d-0.0.6z - External Decoder and Windows Output Boundaries

- Added verified runtime loading, native decoder ownership, packet/frame boundaries, managed copy/conversion, awaited delivery, bounded scheduling, Windows output lifecycle, and finite pumping.
- Kept production FFmpeg activation, bundling, redistribution, demux, and controlled audible/visible playback pending.

### Earlier history

Detailed 0.0.1 through 0.0.8g entries remain available in the repository history
at commit `f078dab5372a45a8c577c1c802a697ab72b45c02` and in milestone ADR/PCN files.
