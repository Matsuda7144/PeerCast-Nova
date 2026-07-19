# Changelog

## [Unreleased]

The detailed historical changelog through 0.0.8g remains preserved in Git
history at commit `f078dab5372a45a8c577c1c802a697ab72b45c02` and in the associated
ADR/PCN validation records. This active file keeps the current playback
integration sequence concise.

### 0.0.8p - Playback Cutover Architecture Review

- Reviewed the completed finite diagnostic path through awaited sink delivery before scheduler or production cutover.
- Fixed the future composition seam at the owned managed frame before conversion; converted callback payload is never queued.
- Required reuse of `BoundedPlaybackScheduler` and rejected a second decoded-frame queue.
- Identified aggregate scheduler byte budgeting as mandatory because frame-count capacity alone is not a sufficient memory bound.
- Deferred duration cutoff while unknown timestamps and durations remain valid at existing decode boundaries.
- Retained the current project split, `net10.0`, host-installed FFmpeg policy, and no-new-production-dependency position.
- Added third-party asset provenance and redistribution-permission requirements.
- Added ADR-0100 and PCN-0108 and synchronized README, ROADMAP, NAVIGATION, and changelog documentation.

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

### 0.0.8j - Diagnostic Packet / Frame Transaction

- Sent one bounded H.264 packet and one bounded AAC packet to independently opened diagnostic contexts.
- Performed at most one frame-state receive per stream in fixed video-then-audio order.
- Added no extraction, loop, flush, demux, or output.
- Added ADR-0094 and PCN-0102.

### 0.0.8i - Diagnostic Codec Open Transaction

- Opened independently transferred H.264 and AAC diagnostic contexts with fixed ordering and reverse cleanup.
- Kept packet/frame processing and production activation outside the milestone.
- Added ADR-0093 and PCN-0101.

### 0.0.8h - Diagnostic Native Parameter Transaction

- Allocated independent H.264 and AAC native contexts and transferred bounded diagnostic codec parameters.
- Preserved deterministic partial-failure cleanup and kept codec open outside the milestone.
- Added ADR-0092 and PCN-0100.

### 0.0.8g - Diagnostic Decoder Configuration

- Added explicit bounded H.264 dimensions/codec data and AAC sample configuration/codec data.
- Reused managed codec-parameter validation, defensive copying, padding, and ownership.
- Added reverse cleanup when audio preparation fails after video preparation.
- Added ADR-0091 and PCN-0099.

### 0.0.8f - Diagnostic Fixture Adapter

- Adapted the bounded fixture to exactly one H.264 packet and one AAC packet.
- Preserved payload and timing without retry, seeking, packet loops, or native calls.
- Added ADR-0090 and PCN-0098.

### 0.0.8e - Bounded Diagnostic Playback Fixture

- Added a strict fixed envelope with one bounded H.264 payload and one bounded AAC payload.
- Added fixed validation for version, roles, timing, lengths, truncation, and trailing data.
- Added ADR-0089 and PCN-0097.

### 0.0.8d - Diagnostic Playback Pipeline Integration Probe Gate

- Added a diagnostic-only phase plan across the existing playback boundaries.
- Refused runtime and media path access while FFmpeg license review remains `Pending`.
- Kept production backend activation unchanged.
- Added ADR-0088 and PCN-0096.

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
