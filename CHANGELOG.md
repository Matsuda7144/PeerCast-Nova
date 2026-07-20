# Changelog

## [Unreleased]

The detailed historical changelog through 0.0.8g remains preserved in Git
history at commit `f078dab5372a45a8c577c1c802a697ab72b45c02` and in the associated
ADR/PCN validation records. This active file keeps the current playback
integration sequence concise.

### 0.0.9a - Timestamp-aware Packet Submission Architecture Review

- Reviewed the complete path from one active FLV demux completion through source-bound native contexts, packet metadata, decoder backpressure, and EOS drain.
- Confirmed that 0.0.8z has exact source identity and codec parameters but no opened native context pair.
- Rejected direct reuse of the diagnostic native decoder lease because it does not retain source-session identity, stream IDs, or time bases.
- Selected 0.0.9b to create one source-bound opened native decoder session by consuming the 0.0.8z binding and two distinct gate/runtime ownership chains.
- Required video context creation, parameter transfer, and codec open before the corresponding audio sequence, with no partial success state.
- Confirmed that the existing packet boundary accepts payload bytes only and cannot preserve PTS, DTS, duration, time base, keyframe state, sequence, or discontinuity generation.
- Required a separate 0.0.9c AVPacket metadata and flush ABI review because the current major-version symbol gate does not prove an exact packet structure layout.
- Fixed the active parser completion as the only pending encoded-packet owner when send requires frame drain; no second queue or hidden retry loop is allowed.
- Required EOS to use explicit null-packet flush and bounded per-context drain rather than an empty encoded packet.
- Added ADR-0111 and PCN-0119. Runtime source, tests, native ABI, packet submission, and activation remain unchanged.

### 0.0.8z - FLV Session Decoder-Parameter Binding

- Added a pure managed adapter from one live bounded FLV demux session to the existing managed FFmpeg codec-parameter ownership boundary.
- Required exact FLV container, video stream 0 H.264, audio stream 1 AAC, distinct identifiers, and `1/1000` time bases.
- Passed exact parser-owned video dimensions, audio configuration, AVCDecoderConfigurationRecord, and AudioSpecificConfig through the existing validating factory.
- Created video parameters before audio parameters and disposed partial video ownership when audio creation failed.
- Added a binding that owns only independently copied padded video/audio parameter leases while retaining a non-owning exact source-session reference.
- Snapshotted stream identifiers and time bases for later identity-aware packet composition.
- Kept parameter ownership valid after later source-session disposal and kept binding disposal independent from parser/session lifetime.
- Added fixed invalid-session, source-disposed, video-rejected, audio-rejected, and fault results with fail-closed undefined-result cleanup.
- Added audio-before-video idempotent cleanup and focused cross-platform tests.
- Added ADR-0110 and PCN-0118 without packet copy, native context creation, codec open, PCP composition, backend activation, or Windows output changes.

### 0.0.8y - Parser-to-Decoder Composition Architecture Review

- Reviewed the 0.0.8x bounded FLV parser against the existing managed codec-parameter, native parameter-transfer, codec-open, and packet/frame boundaries.
- Confirmed that the immutable H.264／AAC session description maps without information loss to `PlaybackDecoderFfmpegCodecParametersFactory`.
- Selected a pure managed FLV session decoder-parameter binding as 0.0.8z, retaining exact source-session identity while owning only independently copied padded parameter leases.
- Required exact FLV container, stream-role, codec, stream-ID, `1/1000` time-base, codec-data, video-dimension, and audio-configuration validation.
- Rejected direct reuse of the paired diagnostic packet transaction because real FLV emits one interleaved event at a time rather than a fixed video-then-audio pair.
- Confirmed that the current packet boundary accepts payload bytes only and cannot preserve PTS, DTS, duration, time base, keyframe state, discontinuity, or sequence.
- Required later decoder-backpressure handling to retain the active parser completion when packet send needs frame drain rather than disposing the native lease or encoded payload.
- Required EOS to use explicit flush and bounded drain instead of an empty encoded packet.
- Deferred timestamp-aware AVPacket construction, native ABI additions, packet submission, decode iteration, PCP composition, production activation, and controlled playback.
- Added ADR-0109 and PCN-0117. Runtime source and tests remain unchanged in this architecture-review milestone.

### 0.0.8x - Pure Managed Bounded FLV v1 Demux Parser

- Added caller-pushed finite FLV v1 parsing with consumed-byte progress and at most one active 0.0.8v completion per operation.
- Added one bounded carry buffer for an incomplete FLV header, tag header, body, or previous-tag-size field without retaining caller suffixes or complete unreported packets.
- Added strict FLV signature, version, flags, data-offset, tag type, data-size, stream-ID, timestamp, and previous-tag-size validation.
- Added ordered AVC then AAC sequence-header bootstrap and immutable H.264／AAC 0.0.8v session creation before media.
- Added bounded AVCDecoderConfigurationRecord, SPS dimension and cropping parsing, parameter-set validation, and AVCC NAL length walking.
- Preserved FLV DTS, signed composition-offset PTS, keyframe state, unknown duration, and length-prefixed AVCC packet payload.
- Added bounded AAC-LC AudioSpecificConfig parsing for indexed／explicit sample rate and channel configuration while ignoring legacy FLV audio-rate fields.
- Added bounded script-tag skipping, exact duplicate configuration skipping, and fail-closed changed configuration handling.
- Added explicit AVC EOS, final missing-stream EOS, final truncation detection, and discontinuity generation reset.
- Added terminal fault latching, one-active-completion backpressure, idempotent carry／configuration cleanup, and parser-owned session disposal.
- Added focused pure managed tests and ADR-0108／PCN-0116 without PCP composition, decoder submission, production activation, or Windows device activation.

### 0.0.8w - First Bounded Container Parser Selection Review

- Compared FLV v1 with MP4／fragmented MP4 against the 0.0.8v parser-neutral contract and the existing callback-scoped sequential PCP input boundary.
- Selected a pure managed bounded FLV v1 demux parser as 0.0.8x because H.264＋AAC FLV maps directly to the current stream roles, PTS／DTS model, keyframe state, and non-seekable PeerCast path.
- Required caller-pushed finite chunks, a consumed-byte count, and at most one active 0.0.8v completion per operation.
- Allowed one bounded incomplete-record carry buffer while rejecting a parser-owned producer queue, complete unreported packet retention, and caller-suffix copying.
- Required FLV header and previous-tag-size validation, bounded script-tag skipping, AVC／AAC sequence headers before media, and fail-closed handling of malformed or changing configuration.
- Required bounded AVC configuration and SPS parsing for dimensions, AVCC NAL length validation, FLV DTS plus signed composition offset for PTS, and keyframe mapping from frame type.
- Required bounded AAC AudioSpecificConfig parsing for AAC-LC sample rate and channels while leaving duration unknown.
- Required explicit discontinuity reset, final-input truncation detection, per-stream EOS, and state commit only after successful 0.0.8v completion construction.
- Deferred MP4／fragmented MP4, avformat, PCP composition, decoder submission, production activation, and controlled real-media playback.
- Added ADR-0107 and PCN-0115. Runtime source and tests remain unchanged in this architecture-review milestone.

### 0.0.8v - Pure Managed Bounded Demux Output Contract

- Added one container-neutral pure managed demux session with exactly one owned H.264 video description and one owned AAC audio description.
- Added stable stream identifiers, fixed container and codec enums, bounded codec data, media configuration, and positive rational time bases.
- Added finite packet and EOS events with monotonic per-stream sequences, owned payloads, separate known／unknown PTS and DTS, optional duration, keyframe state, and discontinuity.
- Added checked rational timestamp conversion with negative timestamp preservation and explicit invalid-base, invalid-limit, and magnitude failures.
- Added caller-selected limits beneath hard project limits for input bytes, packet payload, codec data, packet count, aggregate owned bytes, skipped records, timestamp magnitude, and time-base components.
- Added one-active-completion backpressure: the next finite read is rejected until payload ownership is disposed.
- Added explicit independent stream EOS and final EOS only after both streams end; packets after EOS and contradictory terminal states fail closed.
- Added idempotent scheduler-independent cleanup that clears active packet data before audio and video codec data.
- Added focused pure managed tests without FLV／MP4 parsing, native calls, decoder submission, production activation, or Windows device activation.
- Added ADR-0106 and PCN-0114. Concrete parser selection and decoder composition remain later reviewed milestones.

### 0.0.8u - Bounded Demux Output Contract Review

- Reviewed the finite diagnostic path through scheduler ingress and late-aware dispatch before production composition.
- Confirmed that the repository has no general FLV／MP4 demux contract and that the fixed diagnostic fixture cannot represent real container packet ordering, DTS, keyframes, stream changes, or EOS.
- Selected a container-neutral pure managed bounded demux output contract as the next milestone before any concrete parser or activation wiring.
- Required immutable H.264／AAC stream descriptions, distinct stream identifiers, bounded codec data, rational time bases, and independently known or unknown PTS and DTS.
- Required bounded packet count, payload bytes, aggregate owned bytes, skipped records, input observation, and timestamp conversion with checked allocation.
- Fixed caller-driven finite reads as the backpressure boundary and rejected a parser-owned producer queue or resident reader.
- Deferred FLV／MP4 parsing, avformat integration, production decoder ingress, continuous decode, flush, EOS drain, activation, and controlled real-media playback.
- Added ADR-0105 and PCN-0113. Production FFmpeg and Windows output activation remain unchanged and unapproved.

### 0.0.8t - Diagnostic Late-aware Scheduler Dispatch Transaction

- Added a finite diagnostic composition from the 0.0.8m managed-frame completion through the existing 0.0.8r scheduler ingress and late-aware timed dispatch seam.
- Required known, non-negative presentation timestamps and a fresh zero-snapshot scheduler before ownership transfer.
- Attempted at most one dispatch per accepted frame and at most two dispatch calls total.
- Treated video processing, consumed video drop, and audio processing as explicit consumption.
- Preserved retained, recovery, reset, device, environment, cancellation, partial-ingress, and failure outcomes without implicit retry.
- Retained outstanding scheduler ownership and the native lease in an idempotent completion, disposing scheduler-owned frames before releasing the lease.
- Added focused cross-platform tests through injected dispatch operations without activating D3D11, WASAPI, FFmpeg, or decoder factories.
- Added ADR-0104 and PCN-0112. Demux, continuous decode, production activation, and controlled audible or visible playback remain outside scope.

### 0.0.8s - Diagnostic Scheduler Dispatch Architecture Review

- Selected `PlaybackLateAwareTimedDispatchBoundary.DispatchOneAsync` as the next scheduler-to-output composition seam.
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
- Accounted queued, reserved, and in-flight frames until ownership ends.
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
