# Changelog

## [Unreleased]

Detailed history through 0.0.8g remains preserved in Git history at commit
`f078dab5372a45a8c577c1c802a697ab72b45c02` and in milestone ADR/PCN records.

### 0.0.9j - Bounded Decode Iteration and Per-stream EOS Drain Review

- Reviewed decoded output after the 0.0.9i source-bound packet／flush composition.
- Confirmed that the current production `Advance` uses a state-only receive that frees a successful native frame before its data can be preserved.
- Selected one future source-bound receive authority that performs exactly one native receive and copies exactly one managed frame in the same callback-scoped operation.
- Required one active decoded completion across the generation with exact source, generation, stream, media, decoded sequence, and discontinuity identity.
- Required retained packet／flush retry only after a produced frame has been accepted; frame backpressure keeps both decoded and encoded ownership pending.
- Separated ordinary post-accept receive from the awaited PCP callback and limited each caller-driven operation to one frame.
- Distinguished parser EOS, accepted null-packet flush, flush-accepted drain, and decoder EOF.
- Required independent video／audio drain to EOF and rejected flush acceptance alone as full decoded completion.
- Defined mode-specific `NeedMoreInput`: normal after ordinary packet drain, no-progress failure during retained-send relief or accepted-flush drain.
- Defined frame-count／byte budgets, cancellation rules, discontinuity generation replacement, and decoded-frame-first cleanup.
- Selected 0.0.9k Source-bound One-frame Receive and EOS Drain Boundary.
- Added ADR-0120 and PCN-0128 without changing runtime source or tests.

### 0.0.9i - Finite Awaited PCP-to-FLV Composition Boundary

- Added a serialized awaited `IPcpChannelPacketConsumer` in `Nova.Playback`.
- Kept PCP packet payload memory callback-scoped and advanced a local suffix offset only by exact parser `ConsumedByteCount`.
- Supported multiple FLV records in one callback and one split record through the existing bounded parser carry without another encoded queue.
- Created one exact parser-source-bound decoder generation after `StreamDescriptionAvailable` through an injected factory.
- Passed every source-owned packet or per-stream EOS completion through the finite one-event submission state machine before resuming callback suffix input.
- Bounded parser operations and decoder advances independently and failed closed on no progress, accounting contradiction, identity／sequence／generation mismatch, rejected submission, undefined state, and work-limit exhaustion.
- Added separate explicit `CompleteInputAsync` for video-then-audio EOS and never inferred EOS from empty input, transport closure, reconnect, stop, timeout, cancellation, or failure.
- Replaced the submission state machine and native session before explicit parser discontinuity reset.
- Exposed fixed enum state, bounded counters, fixed-message terminal failure, and idempotent ordered cleanup.
- Added thirteen focused cross-platform tests plus ADR-0119 and PCN-0127.
- Kept production ABI-gate acquisition, backend activation, decoded-frame drain／ownership, scheduler ingress, output wiring, runtime distribution, and audible／visible playback unchanged.

### 0.0.9h - PCP Chunk-to-FLV Parser Composition Review

- Reviewed the awaited PCP packet callback through the 0.0.9g parser facade and 0.0.9e submission state machine.
- Selected callback-held, zero-suffix-copy composition: keep `ConsumeAsync` incomplete until all payload bytes are consumed or copied into bounded parser carry.
- Required exact `ConsumedByteCount` offset accounting and completion-before-suffix processing.
- Required active parser／decoder completion and decoder `EAGAIN` to pause new parser and PCP input.
- Required bounded state-machine `Advance` transitions; no receive progress and operation-budget exhaustion fail closed.
- Required exact PCP channel／session／packet-sequence identity and exact parser-created source generation.
- Separated owner-invoked finalization because the packet-consumer contract has no EOS callback; transport close, reconnect, stop, timeout, cancellation, and failure are not EOS.
- Defined discontinuity generation replacement and ordered state-machine, native-session, parser, and generation cleanup.
- Selected 0.0.9i Finite Awaited PCP-to-FLV Composition Boundary.
- Added ADR-0118 and PCN-0126 without changing runtime source or tests.

### 0.0.9g - One-event Final EOS Sequencing

- Added a finite FLV parser facade for future production composition.
- Delegated ordinary packet parsing to the existing bounded FLV parser without changing packet payload, timing, or keyframe behavior.
- Latched final input only after all caller bytes were consumed and rejected incomplete final carry as truncated.
- Emitted video then audio EOS as separate source-owned completions through the exact parser-created source session.
- Preserved explicit per-stream EOS and emitted only the still-open stream during finalization.
- Tracked next per-stream sequence only from successfully constructed real parser completions.
- Preserved one-active-completion backpressure without a detached completion, copied EOS list, or second encoded queue.
- Accepted final signaling on an empty operation or alongside the last complete packet.
- Reset final, ended, and sequence state on explicit discontinuity.
- Composed both final EOS flushes through the 0.0.9e state machine.
- Added six focused tests plus ADR-0117 and PCN-0125.
- Kept PCP composition, native FFmpeg activation, decoded-frame ownership, output, backend activation, and runtime distribution unchanged.

### 0.0.9f - Controlled Retained FLV Fixture Validation

- Added one controlled in-memory FLV fixture using the real bounded FLV parser and exact parser-created source session.
- Passed real source-owned completions through the source-bound native-session identity and 0.0.9e state machine with injected native results.
- Preserved video／audio interleave, independent per-stream sequence, AVCC／AAC payload, DTS, signed composition-time PTS, and keyframe state.
- Verified packet and flush `EAGAIN` retention without a second encoded copy or hidden retry loop.
- Verified retained parser backpressure returns `ActiveCompletion` with zero consumed bytes.
- Verified each retained transition performs one receive and one retry only.
- Verified explicit video EOS followed by final audio EOS reaches independent flush acceptance.
- Recorded that an unpartitioned final completion containing both EOS events is rejected fail closed before native submission.
- Selected 0.0.9g One-event Final EOS Sequencing before PCP composition.
- Added two focused tests plus ADR-0116 and PCN-0124 without changing parser runtime behavior or production activation.

### 0.0.9e - Finite One-event Packet Submission State Machine

- Added one finite state machine over exactly one 0.0.9d packet or per-stream EOS completion.
- Split work into one initial `Begin` send／flush and one bounded `Advance` receive-plus-optional-retry transition.
- Performed at most one frame receive and at most one retained packet／flush retry per advance.
- Retried only after explicit frame progress and surfaced no-progress without a hidden loop.
- Retained the original native packet／flush completion and parser backpressure across repeated `EAGAIN`.
- Committed per-stream sequence and flush state only after native acceptance.
- Tracked independent video／audio flush acceptance and decoder EOF and rejected later input for ended streams.
- Rejected source mismatch, non-increasing sequence, concurrent input, invalid stream identity, and post-flush packets before native work.
- Added cancellation, terminal-failure, and idempotent packet-first cleanup without owning source or decoder sessions.
- Added fifteen focused tests plus ADR-0115 and PCN-0123.
- Kept resident decode, decoded-frame delivery, second encoded queues, PCP composition, backend activation, FFmpeg distribution, and playback unchanged.

### 0.0.9d - Metadata-aware Native Packet and Flush Boundary

- Added one finite source-bound operation for exactly one bounded FLV packet or per-stream EOS completion.
- Required exact source identity, stream ID, media kind, codec role, keyframe role, and snapshotted time base before native work.
- Mapped integral PTS, DTS, duration, stream index, time base, and keyframe state without implicit rescaling.
- Mapped unknown PTS/DTS to `AV_NOPTS_VALUE` and unknown duration to `0`; rejected negative duration and non-integral conversion.
- Allocated FFmpeg-owned packet and padded payload storage and wrote only the reviewed FFmpeg 8 x64 packet metadata prefix.
- Issued at most one packet send or null flush per operation.
- Retained native packet plus active parser completion on packet `EAGAIN`, and retained per-stream EOS completion on flush `EAGAIN`.
- Preserved source finite-read backpressure and packet-before-parser idempotent cleanup.
- Accepted first-stream EOS from `PacketsAvailable` and final-stream EOS from `EndOfStream` completions.
- Added fourteen focused tests plus ADR-0114 and PCN-0122.
- Kept receive iteration, hidden retry, second encoded queue, PCP composition, backend activation, FFmpeg distribution, and output unchanged.

### 0.0.9c - AVPacket Metadata and Flush ABI Review

- Approved one future metadata-aware native packet／flush boundary only for the exact verified FFmpeg 8 runtime identity.
- Rejected direct field writes under a major-version-only or arbitrary-runtime assumption.
- Required `av_packet_alloc`／`av_packet_free` ownership and prohibited managed allocation based on `sizeof(AVPacket)`.
- Approved only the reviewed PTS, DTS, data, size, stream index, key flag, duration, and time-base prefix needed for decoding.
- Mapped unknown timestamps to `AV_NOPTS_VALUE`, unknown duration to `0`, and parser keyframe state to `AV_PKT_FLAG_KEY`.
- Required exact source-session stream and time-base matching without implicit rescaling.
- Selected `avcodec_send_packet(context, NULL)` as the only EOS flush representation and kept `avcodec_flush_buffers` outside EOS drain.
- Required one finite send attempt, explicit `EAGAIN` retention, no second encoded queue, and no hidden retry or drain loop.
- Added ADR-0113 and PCN-0121. Runtime source, native ABI implementation, packet submission, flush execution, drain, PCP composition, activation, distribution, and output remain unchanged.
- Audited ADR／PCN numbering and recorded ADR-0096 and PCN-0104 as permanent numbering-error gaps, following the ADR-0082 precedent without retroactive reuse.

### 0.0.9b - Source-bound Opened Native Decoder Session

- Added one finite factory from a live FLV parameter binding plus distinct video/audio ABI gate leases into one opened H.264/AAC decoder session.
- Snapshotted exact source-session identity, stream IDs, and time bases before native work.
- Completed video context creation, parameter transfer, and codec open before beginning audio.
- Consumed the binding and both gates on every result and returned no partial configured or opened success state.
- Failed closed on invalid configuration, source disposal, context rejection, parameter-transfer rejection, codec-open rejection, and faults.
- Added audio-before-video idempotent cleanup without owning the parser or source demux session.
- Added six focused injected-operation tests and fixed one CS9124 test-only compiler issue.
- Added ADR-0112 and PCN-0120.
- Kept AVPacket metadata writes, packet submission, flush, drain, PCP composition, backend activation, and output unchanged.

### 0.0.9a - Timestamp-aware Packet Submission Architecture Review

- Determined that source-bound opened native contexts must exist before packet submission.
- Rejected the old diagnostic native decoder lease for real FLV composition because it lacks source identity, stream IDs, or time bases.
- Selected 0.0.9b for source-bound native session ownership and 0.0.9c for exact AVPacket metadata and flush ABI review.
- Required active parser-completion retention under decoder backpressure, explicit null-packet EOS flush, bounded drain, no second encoded queue, and no hidden retry loop.
- Added ADR-0111 and PCN-0119.

### 0.0.8z - FLV Session Decoder-Parameter Binding

- Added a pure managed adapter from one exact live FLV session to independently owned padded H.264/AAC codec-parameter leases.
- Preserved source identity, stream IDs, and `1/1000` time bases without native calls or packet consumption.
- Added ADR-0110 and PCN-0118.

### 0.0.8x-0.0.8y - Bounded FLV Parser and Composition Review

- Added the pure managed bounded FLV v1 parser with owned packet/EOS completions, strict AVC/AAC bootstrap, timestamps, discontinuity, and one-active-completion backpressure.
- Reviewed the parser-to-decoder seam and rejected metadata-losing reuse of the paired diagnostic packet transaction.
- Added ADR-0108/0109 and PCN-0116/0117.

### Earlier active playback milestones

0.0.8v and earlier active integration entries remain available in repository history and their ADR/PCN records.