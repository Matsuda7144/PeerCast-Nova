# Changelog

## [Unreleased]

### 0.0.6y Windows Output Scheduler Adapter Boundary

- Added explicit one-item pumping from the bounded scheduler to the Windows output buffer processor without a resident loop or timer.
- Added deterministic video/audio preference alternation and scheduler-owned reservation/acknowledgement so only successful dispatch consumes an item.
- Added fixed retry retention, generation reset, cancellation, disposal, validation, and fail-closed results without A/V synchronization or continuous playback.
- Made cross-media reset transactional with single-attempt audio rollback and an explicit partial-reset fault state when rollback cannot restore generation consistency.

### 0.0.6x Windows Output Buffer Processing Boundary

- Added finite one-frame BGRA32 upload/Present and one-buffer WASAPI write processing over synchronized activation-owned resource borrows.
- Added strict layout and generation validation, one-time audio Start, WouldBlock, resize, device-lost, and fail-closed fixed results.
- Added independent video/audio serialization, reverse audio-to-video cleanup, and idempotent reset/disposal without a scheduler, A/V synchronization, or a continuous playback loop.

### 0.0.6w Windows Output Stream Initialization Boundary

- Added finite video/audio initialization state above bound Windows output resources with independent generations.
- Added opaque window-token, bounded resize, shared/event-driven audio negotiation, event association, and buffer-duration contracts behind an injectable native boundary.
- Added reverse-order partial rollback, device-lost, reset, fail-closed, and idempotent disposal behavior without presenting frames or starting audio.

### 0.0.6v Windows Output Resource Binding Boundary

- Added finite video/audio binding state and generation transitions from activated Windows resources to the existing output configurations.
- Kept native resource ownership in the activation boundary while binding borrows resources only for synchronized callbacks.
- Added reverse-order partial rollback, reset, device-lost, fail-closed, and idempotent disposal behavior without rendering or audio streaming.

### 0.0.6u Windows Output Activation Boundary

- Added finite D3D11 device/context and WASAPI default-endpoint/client/event activation behind an injectable native boundary.
- Normalized platform, API, device, permission, environment, failure, and disposed outcomes to fixed public results.
- Made COM and native-resource ownership explicit with reverse-order partial-failure cleanup and idempotent disposal.

### 0.0.6t Windows Output Adapter Selection

- Selected Direct3D 11 for BGRA32 video and WASAPI shared/event-driven for interleaved S16/F32 audio.
- Added a deterministic non-activating Windows 11 probe for required System32 DLL exports with fixed results and HRESULT mapping.
- Fixed future threading, COM apartment, device generation, reset, and native-resource ownership boundaries.

### 0.0.6s Renderer / Audio Sink Boundary

- Added separate awaited contracts for canonical BGRA32 video and interleaved S16/F32 audio output.
- Added explicit configure, submit, flush, reset, device generation, and fixed failure results.
- Revalidated bounded payload layout and serialized each sink independently without OS APIs or queues.

### 0.0.6r Bounded Playback Scheduler

- Added an explicitly driven fixed-array scheduler with shared audio/video capacity.
- Added configurable DropOldest, DropIncoming, and Reject full-buffer policies.
- Reused the bounded playback policy and awaited sink delivery with deterministic ownership cleanup and snapshots.

### 0.0.6q Playback Policy / Clock Boundary

- Added a monotonic clock abstraction and a locked, bounded, pure managed timing policy.
- Added fixed deliver, finite-wait, drop, reset-clock, and reject decisions with configurable audio/video master selection.
- Fixed default and hard limits for wait, lateness, and lead without adding scheduling, queues, timers, or sink integration.

### 0.0.6p Awaited Decoded Sink Delivery

- Added serial awaited delivery from existing canonical frame boundaries to `IPlaybackDecodedFrameSink`.
- Preserved callback-scoped payload ownership, direct cancellation propagation, and sink backpressure without a decoder-owned queue.
- Added fixed cancellation/sink failure results and fail-closed handling for unexpected integration failures.
- Made disposal close admission without racing active or waiting sink deliveries.

### 0.0.6o Managed Format Conversion

- Added managed YUV420P/NV12 to BGRA32 conversion with fixed BT.601 limited-range coefficients.
- Added byte-preserving planar S16/F32 to interleaved PCM conversion.
- Covered odd video dimensions, bounded allocation, fixed failures, and callback payload cleanup.
- Added no native converter, SIMD/GPU optimization, delivery, queue, renderer, device, or demux.

### 0.0.6n Decoded Output Adapter

- Added a revalidating adapter from owned managed frames to existing decoded output models.
- Adapted padding-free BGRA32 and packed S16/F32 without format conversion.
- Returned fixed conversion-required results for valid YUV, NV12, and planar PCM layouts.
- Fixed Unknown time mapping and cleared temporary callback payloads on every path.

### 0.0.6m Managed Frame Copy

- Added owned managed copies of validated video planes and audio sample planes.
- Copied logical row/sample bytes only and omitted native video padding.
- Bounded total allocation to existing metadata limits and 256 MiB with fixed failure results.
- Added no conversion, decoded output delivery, demux, queue, renderer, or device.

### 0.0.6l Frame Plane / Sample Boundary

- Added callback-only, pointer-free views over validated FFmpeg video planes and audio samples.
- Checked formats, plane geometry, line sizes, pointers, overflow, and total referenced bytes before consumption.
- Preserved unknown timing values and context state on consumer rejection; native failures remain fail closed.
- Added no managed copy, format conversion, decoded output, demux, queue, renderer, or device.

### 0.0.6k Native Frame Metadata Extraction

- Added callback-scoped video/audio metadata extraction without retaining the native frame pointer.
- Pinned the isolated reader to the official FFmpeg 8.1 libavutil 60 Windows x64 public layout.
- Mapped fixed formats and valid-time-base timestamps into privacy-safe managed metadata.
- Represented unset time bases, FFmpeg timestamp sentinels, and zero duration as fixed managed unknown values.
- Reused decoded output bounds and rejected malformed, negative-duration, overflowing, and excessive values.
- Preserved opened context state on consumer rejection and discarded it after unexpected native failure.
- Added no plane/sample copy, conversion, decoded frame output, demux, binary, or dependency.

### 0.0.6j Callback-Scoped Native Frame Boundary

- Kept each received AVFrame inside one callback and freed it on every path.
- Preserved context state on consumer rejection and discarded it after unexpected native failure.
- Added no metadata extraction, payload copy, conversion, or output.

### 0.0.6i Packet / Frame Processing Boundary

- Added bounded encoded packet submission and decoded frame state receipt.
- Released temporary packet, padded buffer, and frame ownership on every path.
- Added no frame extraction, conversion, demux, or output.

### 0.0.6h avcodec_open2 Boundary

- Extended the verified FFmpeg gate with the fixed `avcodec_open2` symbol.
- Required a live owned context with completed parameter transfer before codec open.
- Added a Windows x64 call using the decoder fixed during context allocation and no option dictionary.
- Accepted only native result zero; every nonzero result or native exception discards the context fail closed.
- Rejected duplicate open and repeated parameter transfer before another native call.
- Added no packet/frame processing, demux, conversion, FFmpeg binary, or dependency.

### 0.0.6g Native Parameter Transfer Boundary

- Extended the verified FFmpeg gate with fixed native parameter allocation, layout, and transfer symbols.
- Added a Windows x64 boundary that transfers owned H.264 or AAC parameters into the existing decoder context.
- Allocated and freed temporary `AVCodecParameters` and zero-padded native extradata on every path.
- Cleared the managed parameter lease after success and discarded the context after rejected or exceptional transfer.
- Kept codec open, packet/frame processing, demux, conversion, FFmpeg binaries, and new dependencies out of scope.

### 0.0.6f Codec Parameter Ownership Boundary

- Added managed-only fixed H.264 and AAC codec-parameter inputs with fail-closed validation.
- Added named bounds for video dimensions, pixel count, audio sample rate, channel count, and extradata.
- Defensively copied required extradata and appended exactly 64 zero padding bytes.
- Added an idempotent ownership lease that clears the complete padded buffer and rejects later use.
- Kept validation behavior identical on Windows and non-Windows systems.
- Added no native parameter transfer, codec open, packet/frame processing, FFmpeg binary, or dependency.

### 0.0.6e Native Decoder Context Lease

- Added a live ABI/symbol gate lease that retains the already-loaded verified modules.
- Added fixed H.264 and AAC codec identifiers with fail-closed unknown-value rejection.
- Added decoder lookup and `AVCodecContext` allocation behind a testable native boundary.
- Transferred the live gate and verified runtime into an idempotent context owner on success.
- Freed the native context before reverse module unload and runtime-handle disposal.
- Added fixed unsupported and failure results without paths, hashes, native bytes, or error text.
- Added no codec open, parameter setup, packet/frame processing, demux, conversion, binary, or dependency.

### 0.0.6d FFmpeg ABI / Symbol Gate

- Consumed the live verified five-DLL lease without rediscovery, re-hashing, or managed re-open.
- Added bounded PE import inspection and fail-closed rejection of non-verified, non-system dependencies.
- Loaded only with `LoadLibraryExW` search scopes `DLL_LOAD_DIR` and `SYSTEM32`.
- Verified fixed FFmpeg ABI majors and required symbols through a testable native-loader boundary.
- Released every partially loaded module in reverse order on success or failure.
- Returned fixed privacy-safe results, including an unsupported result on non-Windows systems.
- Added no codec initialization, packet decode, frame output, FFmpeg binary, or external dependency.

### 0.0.6c2-fix1 runtime lease CI and artifact consistency

- Added explicit Windows-only test discovery with documented skip reasons.
- Added a non-Windows test proving the public locator fails closed as `PathRejected`.
- Kept managed backend policy, provenance, inventory, and artifact tests cross-platform.
- Added format verification to both Windows and Ubuntu GitHub Actions jobs.
- Regenerated the final SHA-256 list only after all other artifacts are frozen.

### 0.0.6c2 verified FFmpeg runtime lease — Verified

- Held all five verified DLL handles with write/delete sharing denied until disposal.
- Rejected UNC, non-fixed-drive, reparse-point, oversized-inventory, and undefined-enum inputs.
- Unified Windows DLL-name matching on `OrdinalIgnoreCase` and rejected case duplicates.
- Kept paths, hashes, manifest values, and DLL content outside normal diagnostics.
- Kept ABI checks, native loading, symbol lookup, decoding, and license approval pending.

### 0.0.6c1 explicit installed FFmpeg runtime locator — Verified

- Adopted user/host-installed FFmpeg with dynamic linking and no Nova bundling.
- Added one-directory explicit runtime location with fail-closed fixed results.
- Reused the 0.0.6c inventory verifier for size and SHA-256 checks.
- Added no PATH, registry, or default-location search and disclosed no input path.
- Kept ABI checks, native loading, decoding, and license approval pending.

### 0.0.6c installed FFmpeg runtime inventory — Verified

- Pinned five required shared libraries by name, size, and SHA-256.
- Added pre-load rejection for missing, unexpected, changed, or truncated runtime files.
- Rewrote `NAVIGATION.md` as a clean UTF-8 current-state index and updated the roadmap.
- Bundled no FFmpeg executable or DLL and retained pending activation review.

### 0.0.6b pinned FFmpeg backend candidate — Technically verified, activation pending

- Pinned one exact BtbN FFmpeg 8.1 LGPL shared Windows archive and digest.
- Added a repeatable version, configuration, demuxer, license, and SHA verifier.
- Kept license review pending and bundled no FFmpeg executable or DLL.

### 0.0.6a external backend artifact verification — Verified

- Added bounded, cancellable SHA-256 verification before external backend activation.
- Kept paths and free-form error text outside Playback contracts.
- Added no FFmpeg binary, decoder package, DLL, or executable.

### 0.0.5k decoder backend artifact manifest — Verified

- Required fixed upstream, version, license, and SHA-256 evidence for external artifacts.
- Rejected unsafe manifest tokens and normalized artifact digests.
- Added no decoder, codec package, or native binary.

### 0.0.5j decoder backend activation audit — Verified

- Added side-effect-free, deterministic backend policy audit entries.
- Added fixed multi-reason rejection flags without paths or backend exception text.
- Preserved fail-closed factory construction and added no external dependency.

### 0.0.5i decoder backend provenance policy — Verified

- Added mandatory fixed backend provenance declarations.
- Added deny-by-default policy for third-party, bundled, native, and unreviewed backends.
- Added no codec, native binary, or package dependency.

### 0.0.5h decoder backend conformance gate — Verified

- Added a repeatable fake-backend boundary gate.
- Added immutable-capability and sanitized-failure assertions.
- Added no codec, native library, or package dependency.

### 0.0.5g backend selection probe — Verified

- Routed the controlled lifecycle probe through the verified backend registry.
- Added fixed capability declaration without eager decoder creation.
- Kept the probe counter-only with no codec or native dependency.

### 0.0.5f decoder backend boundary — Verified

- Added lazy host-owned backend discovery and deterministic capability-based selection.
- Added immutable, bounded, privacy-safe backend descriptors and fixed failure mapping.
- Added seven fake-backend tests without introducing a codec or native dependency.

### 0.0.5e decoded output contract — Verified

- Added an awaited `IPlaybackDecodedFrameSink` data-plane boundary and passed it
  from `PlaybackDecoderRunner` through decoder creation context.
- Added canonical signed-16/float-32 interleaved PCM audio frames and packed
  BGRA32 video frames with strict finite layout validation.
- Kept decoded payload memory callback-scoped; a sink must copy before callback
  completion if it needs retention, while sink delay propagates explicit
  backpressure to the decoder runner.
- Added fixed sequence, timestamp, duration, and decoder-reset discontinuity
  metadata without strings, identifiers, endpoints, or backend-specific handles.
- Added a content-discarding null sink so lifecycle probes remain valid without
  claiming decode or playback output.
- Added seven deterministic tests for audio/video layout validation, default and
  injected sink selection, and awaited sink backpressure with encoded lease
  release after completion.
- Updated the diagnostic banner and PCP user agents to 0.0.5e.
- Accepted ADR-0041 and documented the architecture and completed validation in
  PCN-0048 and PCN-0049.
- Left real decoder integration, bounded decoded queues, rendering, devices,
  clocks, A/V synchronization, and audible/visible playback pending.

### 0.0.5d decoder lifecycle probe — Verified

- Added a finite loopback-only `--playback-decoder-lifecycle-probe` that runs
  controlled real-media ingress through Channel Session, Playback input, media
  classification, the decoder runner, and a cooperative CLI-internal counting
  decoder without introducing a real codec backend.
- Kept the diagnostic output to fixed enums, booleans, and counters. Channel ID,
  endpoint, metadata, payload, prefix bytes, positions, session identifiers,
  exception text, and decoded media are not emitted.
- Added independent duration, input-count, caller-cancellation, session-stop,
  runner-stop, and cleanup boundaries with a conjunctive `Lifecycle Gate`.
- Added 19 deterministic CLI tests for parsing bounds, privacy-safe summaries,
  gate conditions, connection failure, and decoder counter/disposal behavior.
- Passed the Windows Release build with zero source warnings and zero errors; all
  233 solution tests passed with zero failures or skips.
- Repeated all 13 decoder lifecycle tests 20 times (260 executions) without
  failure and completed `dotnet format PeerCast-Nova.sln --verify-no-changes`
  with no output.
- Verified controlled PeerCastStation input as `FlashVideo` / `FileHeader` with
  HTTP 200, five submitted inputs / 52,914 bytes, matching runner and decoder
  counters, one factory creation, one decoder disposal, zero skips, zero pending
  or outstanding leases, no deferred cleanup, completed shutdown, gate passed,
  and process exit 0.
- Updated the repetition and controlled-probe PowerShell wrappers to remove only
  nonexistent process-local `LIB` entries and restore the original environment
  afterward, matching `build.ps1` behavior.
- Accepted ADR-0040 and recorded the architecture and completed validation in
  PCN-0046 and PCN-0047.
- Left real decoder integration, decoded-frame ownership, rendering, clocks,
  A/V synchronization, and audible/visible playback pending.

### 0.0.5c replaceable decoder lifecycle — Verified

- Added synchronous backend-neutral decoder selection and a one-shot owned
  decoder runner in `Nova.Playback`; no real decoder or third-party codec
  library is included.
- Reconstructed a bounded bootstrap up to 64 KiB by default, replayed it once
  after recognition, and submitted any uncopied recognizing-packet remainder
  exactly once.
- Passed normal packets through their existing playback leases without an
  additional runner-owned payload copy; callback memory is valid only until
  the returned `ValueTask` completes.
- Serialized decoder callbacks and required reset before decoding every source
  or latency discontinuity packet.
- Added finite stop, complete, and dispose deadlines. A callback that ignores
  cancellation can retain at most its active packet lease until delayed
  cleanup observes completion.
- Kept terminal results to fixed classification/lifecycle enums, counters, and
  one deferred-cleanup flag.
- Added 13 deterministic lifecycle tests covering bootstrap, remainder, reset,
  unsupported and exception paths, caller/owner cancellation, finite complete,
  one-shot ownership, and result privacy.
- Passed the Windows Release build and all 214 solution tests, then passed all
  13 lifecycle tests for 20 repetitions (260 executions) and a clean format
  verification.
- Accepted ADR-0039 and documented the architecture, validation, and third-party
  decoder license boundary in PCN-0044 and PCN-0045.
- Left real decoder integration, decoded-frame ownership, rendering, clocks,
  A/V synchronization, and controlled audible/visible playback pending.

### 0.0.5b media signature classification

- Added a bounded transient media-prefix classifier in `Nova.Playback` with a
  64 KiB default, 1 MiB hard maximum, finite discontinuity limit, and no
  returned signature bytes.
- Added narrow independently implemented recognition for FLV, Ogg, MPEG-TS,
  ASF, WebM, and Matroska while leaving unsupported formats unknown.
- Cleared retained prefix bytes on discontinuity, recognition, limit, input
  completion, and disposal.
- Kept PCP response headers and metadata out of container evidence and kept
  all media bytes out of Core and fixed diagnostics.
- Added a finite privacy-safe Playback Signature Probe with one owned Network
  session, bounded player input, cooperative stop, and conjunctive cleanup
  gate.
- Separated classification-time skip accounting from intentional pending
  packet disposal after a terminal recognition.
- Verified controlled PeerCastStation 6.0.0.0 input as FlashVideo from a file
  header with HTTP 200, zero skips, no pending/outstanding leases, completed
  shutdown, gate passed, and exit 0.
- Added 16 classifier cases and 16 CLI cases; all 201 solution tests and both
  self-tests pass with zero build warnings or errors.
- Repeated all 29 Playback tests 20 times (580 executions) and all 31 CLI
  tests 10 times (310 executions) without failure.
- Updated the diagnostic banner and PCP user agents to 0.0.5b.
- Accepted ADR-0038 and documented the architecture and validation in
  PCN-0042 and PCN-0043.
- Left codec discovery, decoder lifecycle, rendering, clocks, A/V sync, and
  controlled audible/visible playback pending.

### 0.0.5a playback input foundation

- Added `Nova.Playback` with a one-way dependency on `Nova.Network`; Network,
  Core, Protocol, and UI boundaries remain decoder-independent.
- Added a packet- and byte-bounded PCP playback input with one owned pooled
  payload copy.
- Added a latest-wins policy that evicts the oldest queued packets and marks
  the first retained packet with an explicit latency discontinuity.
- Rejected individually oversized packets before payload allocation and carry
  their gap marker to the next accepted packet.
- Added separate fixed source/latency discontinuity flags, one-reader
  enforcement, disposable payload leases, and outstanding ownership counters.
- Added graceful queue drain and immediate finite owner stop without creating
  a decoder worker.
- Kept status free of identifiers, metadata, positions, payload, endpoints,
  peer text, exceptions, and free-form diagnostics.
- Added 13 focused Playback tests for ownership, limits, skipping,
  discontinuity, cancellation, concurrency, completion, and stop.
- Passed all 169 solution tests and both self-tests with zero build warnings
  or errors.
- Repeated all 13 Playback tests 20 consecutive runs (260 executions) with no
  failure.
- Updated the diagnostic banner and PCP user agents to 0.0.5a.
- Accepted ADR-0037 and documented the architecture and validation in
  PCN-0039 through PCN-0041.
- Left media-format probing, decoder selection, rendering, clocks, A/V sync,
  and real playback verification pending.

### 0.0.4 Relay verified

- Black-box observed an official PeerCastStation 6.0.0.0 HTTP 200 response
  whose status and field names were ASCII while two unknown field values used
  high octets.
- Allowed bounded legacy `obs-text` only in upstream response field values;
  downstream requests, response status lines, field names, CRLF, controls, and
  total header size remain strict.
- Used Latin-1 only as a lossless byte mapping and discarded unknown response
  values after handshake without moving them into Core or diagnostics.
- Removed peer-provided start/header-line text from parser error messages.
- Added six response/request direction, name, status, and control-byte tests.
- Repeated all 81 Network tests ten consecutive times (810 executions) with no
  failure.
- Re-ran the compatibility probe successfully with HTTP 200, `oleh`, `ok`,
  remote version 1218, and process exit 0.
- Passed the controlled 60-second official-upstream real-media gate with 1,338
  ingress packets / 17,381,199 bytes and four downstream packets / 51,633
  bytes.
- Recorded one accepted handshake, zero discard, zero incomplete cleanup,
  zero internal failure, completed shutdown, both acceptance gates passed, and
  process exit 0.
- Kept the Channel ID, metadata values, payload contents, positions, session
  identifiers, and command line out of the validation record.
- Updated the diagnostic banner and PCP user agents to final 0.0.4.
- Accepted ADR-0036 and documented the boundary and evidence in PCN-0037 and
  PCN-0038.
- Marked Milestone 0.0.4 Relay Verified; 0.0.5 Playback is next.

### 0.0.4h explicit relay readiness

- Added fixed `Ready`, `Stopped`, and `Failed` outcomes for the coordinator's
  downstream-listener readiness contract.
- Completed readiness only after successful bind, on startup failure, or when
  stop/cancellation/disposal wins before bind.
- Kept observer cancellation separate from coordinator cancellation and
  ownership.
- Made the CLI print its Ready marker only after the explicit Network outcome.
- Changed the one-command gate to consume the owned relay job's output stream
  and start the downstream probe only after the fixed Ready marker.
- Removed the gate's dependency on Windows TCP-table inspection.
- Verified that an occupied listener receives no downstream connection and
  that an absent upstream still produces a complete fixed failure result.
- Verified the isolated success path with 46 ingress packets / 184 bytes and 4
  delivered packets / 16 bytes, zero discard, cleanup, or internal failure.
- Added three readiness tests; all 150 tests and both self-tests pass.
- Repeated all 75 Network tests and all 15 CLI tests ten consecutive times
  (750 and 150 executions) with no failure.
- Updated the diagnostic banner and PCP user agents to 0.0.4h.
- Accepted ADR-0035 and documented the boundary and evidence in PCN-0035 and
  PCN-0036.
- Kept the official-upstream real-media run pending without channel
  enumeration or upstream process changes.

### 0.0.4g single-command controlled relay gate

- Added `Test-PeerCastStationRelayEndToEnd.ps1` to start the existing relay
  diagnostic and downstream Channel Session Probe from one command.
- Waited for the new IPv4-loopback listener before downstream connection and
  rejected an already occupied listen port before creating the owned job.
- Required both process exit codes, relay `Acceptance Gate: Passed`, and
  downstream `Stop Trigger: PacketLimit` for the final gate and exit code 0.
- Bounded listener start, relay duration, downstream session, post-relay job
  completion, and job cleanup without changing the upstream process.
- Verified synthetic success with 46 ingress packets / 184 bytes and 4
  delivered packets / 16 bytes, zero discard, cleanup, and internal failures.
- Verified upstream-absent failure and occupied-port preflight with exit code
  1, no Channel ID output, and no owned job or isolated listener left behind.
- Kept payload, metadata, position, endpoint, and packet/bootstrap semantics
  outside the orchestration boundary.
- Updated the diagnostic banner and PCP user agents to 0.0.4g.
- Accepted ADR-0034 and documented the boundary and evidence in PCN-0033 and
  PCN-0034.
- Kept the official-upstream real-media run pending; the script does not
  enumerate, stop, or reconfigure PeerCastStation.

### 0.0.4f bounded live relay diagnostics

- Added a finite CLI mode that runs the owned relay coordinator from an
  upstream peer to a loopback-only downstream listener.
- Bounded the diagnostic window to 1 second through 10 minutes, reconnects to
  0 through 10, downstream children to 4, and each child queue to 16 packets
  and 4 MiB.
- Added strict culture-independent parsing for upstream port, initial
  position, listen port, duration, minimum packet count, idle timeout, and
  reconnect count; unknown, missing, duplicate, and out-of-range options fail
  before opening a socket.
- Added a content-free acceptance gate requiring sufficient ingress and
  downstream delivery, positive payload bytes, one accepted handshake, zero
  discard/cleanup/internal failures, and completed shutdown.
- Added Ctrl+C cancellation and fixed stop-trigger/end/drain/counter output
  without channel ID, metadata, payload, position, exception, or peer text.
- Added `Test-PeerCastStationRelay.ps1` for the controlled two-terminal live
  relay procedure.
- Added 15 CLI tests for defaults, explicit values, invalid input, occupied
  listen-port failure, acceptance criteria, and result privacy.
- Repeated all 15 CLI tests ten consecutive times (150 executions) with no
  failure.
- Verified the executable three-stage path on isolated loopback ports:
  synthetic upstream -> CLI relay -> CLI downstream session. The final run
  accepted 46 packets / 184 bytes, delivered 4 packets / 16 bytes, recorded
  zero discard, incomplete cleanup, or internal failure, and exited 0 with
  `Acceptance Gate: Passed`.
- Updated the diagnostic banner and PCP user agents to 0.0.4f.
- Accepted ADR-0033 and documented the boundary and evidence in PCN-0031 and
  PCN-0032.
- Kept the real-media official-upstream run pending; the diagnostic command
  does not enumerate or alter the user's PeerCastStation channels.

### 0.0.4e fault-resilient relay accounting

- Added a coordinator-owned ingress aggregate that counts packets and payload
  bytes only after the bounded fan-out accepts them.
- Preserved ingress counts when an upstream task faults before
  `PcpChannelSessionResult` can be returned.
- Exposed the same fixed numeric aggregate as a live snapshot and in the
  terminal coordinator result without identifier, endpoint, metadata,
  payload, position, exception, or peer-controlled text.
- Extended the truncated-upstream integration case to deliver one complete
  packet before an incomplete atom, then verified matching ingress and
  downstream delivery counts despite a null upstream terminal result.
- Updated the isolated interoperability harness to use the coordinator-owned
  ingress aggregate for its acceptance gate.
- Updated the diagnostic banner and PCP user agents to 0.0.4e.
- Accepted ADR-0032 and documented the boundary and validation in PCN-0029
  and PCN-0030.
- Repeated all 72 Network tests ten consecutive times (720 executions) with no
  failure.
- Kept controlled real-media official-upstream verification pending before
  full 0.0.4 Relay.

### 0.0.4d owned relay coordinator

- Added one coordinator that owns the upstream channel session, bounded
  fan-out, and downstream relay server while keeping Core status and the
  upstream connector caller-owned.
- Started the downstream listener before upstream connection and preserved the
  same fan-out and downstream children across upstream reconnects.
- Added a finite fan-out idle wait whose pending counts include in-flight
  child writes.
- Drained accepted packets after natural upstream termination before stopping
  downstream sessions; explicit stop and caller cancellation remain prompt
  and skip drain.
- Added fixed coordinator lifecycle, end reasons, drain outcomes, nested
  privacy-safe summaries, and awaited-shutdown completion without external
  exception or peer text.
- Converted upstream and downstream task failures into fixed coordinator
  results while retaining Core's existing fixed fault category/stage/counts.
- Verified reconnect request positions 0 then 100 and continuous delivery of
  positions 100 and 200 through one unchanged downstream child.
- Verified synthetic upstream wire through Nova to official Stable 4.1.0 and
  development 7.1.1 downstream peers: 18/19 packets, 72/76 bytes, zero
  discard, complete cleanup, and zero internal failures.
- Recorded that an exploratory official-upstream chain did not re-relay the
  packet-only synthetic fixture; no unobserved packet type or bootstrap value
  was guessed or copied.
- Updated the diagnostic banner and PCP user agents to 0.0.4d.
- Accepted ADR-0031 and documented the boundary/evidence in PCN-0027 and
  PCN-0028.
- Added five Network tests; all 132 tests and both self-tests pass.
- Repeated all 72 Network tests ten consecutive times (720 executions) with no
  failure.
- Kept controlled real-media official-upstream verification pending before
  full 0.0.4 Relay.

### 0.0.4c bounded downstream relay server

- Added a single-use downstream relay server that owns one bounded TCP
  listener and every accepted PCP child session while leaving the fan-out and
  upstream channel session caller-owned.
- Kept TCP connection concurrency and fan-out child capacity as separate
  finite limits, including the existing 503/OLEH/QUIT path when no fan-out
  slot is available.
- Added multi-child lifecycle tracking and isolated protocol, timeout,
  disconnect, slow-consumer, and write failures to the affected child.
- Made explicit stop and caller cancellation terminate pending accepts,
  incomplete HTTP handshakes, and active children before awaited server
  completion.
- Added nonblocking `RequestStop`, awaited `StopAsync`, single-use lifecycle
  states, and fixed server end reasons.
- Added privacy-safe aggregate snapshots for connection, handshake, child end,
  cleanup, delivery, discard, and internal-failure counts without identifiers,
  endpoints, agents, metadata, payload, positions, exceptions, or peer text.
- Verified official Stable 4.1.0 and development 7.1.1 through the owning
  server; each completed one handshake, received four CHAN packets / 16 bytes
  with zero discard, and finished with zero cleanup or internal failures.
- Updated the diagnostic banner and PCP user agents to 0.0.4c.
- Accepted ADR-0030 and documented the boundary/evidence in PCN-0025 and
  PCN-0026.
- Added five Network tests; all 127 tests and both self-tests pass.
- Repeated all 67 Network tests ten consecutive times (670 executions) with no
  failure.
- Kept continuous real-channel upstream-to-Nova-to-downstream verification
  pending before full 0.0.4 Relay.

### 0.0.4b bounded downstream PCP child

- Black-box observed the downstream HTTP request and HELO shape from isolated
  official Stable 4.1.0 and development 7.1.1 binaries without using existing
  implementation source as a coding basis.
- Added strict-ASCII bounded HTTP request handling for the observed relay path,
  exact channel matching, PCP version 1, and finite numeric positions.
- Added HELO parsing and minimal OLEH creation with duplicate, field-kind,
  `port`/`ping`, GUID, string, and integer validation.
- Reserved a bounded fan-out slot before HTTP 200 and gated child writes until
  OLEH/OK completed, so negotiation cannot create an unbounded packet backlog.
- Added the compatible full path `HTTP 503 -> HELO -> OLEH -> QUIT` without
  allocating a child queue.
- Added reusable 72-byte CHAN prefix serialization and wrote the shared fan-out
  payload separately, avoiding a second payload-sized copy.
- Added finite packet/control writes, handshake timeout, remote QUIT/control
  monitoring, fan-out-completion wakeup, connection ownership, and two-stage
  finite cleanup.
- Added fixed-field downstream results with explicit cleanup completion and no
  identifier, endpoint, agent, metadata, payload, position, exception, or
  peer-controlled text.
- Kept cancellation-violating writers in their bounded fan-out capacity slot
  until real callback completion.
- Verified official Stable 4.1.0 and development 7.1.1 downstream handshakes;
  each received four CHAN packets / 16 bytes with zero discarded packets and
  completed cleanup.
- Updated the diagnostic banner and relay-entry user agent to 0.0.4b.
- Accepted ADR-0029 and documented the boundary/evidence in PCN-0023 and
  PCN-0024.
- Added five Protocol and twelve Network tests; all 122 tests and both
  self-tests pass.
- Repeated all 62 Network tests ten consecutive times (620 executions) with no
  failure.
- Kept a long-running accept-loop coordinator and continuous real-channel
  upstream-to-Nova-to-downstream verification pending before full 0.0.4 Relay.

### 0.0.4a bounded relay fan-out

- Added a relay fan-out consumer that copies each upstream payload once into
  finite pooled memory and shares it by reference across accepted children.
- Added independent packet-count and byte-count limits per child, including
  the packet currently being sent.
- Stopped only the slow child when its bound is reached and isolated child-sink
  exceptions from the upstream session and healthy children.
- Preserved packet sequence, numeric type, wire/stream position, payload, and
  explicit discontinuity through a callback-scoped relay packet without moving
  identifiers or payload into Core.
- Added fixed-field child state, termination reason, pending, delivered, and
  discarded counters without exception text, endpoint, identifier, metadata,
  or payload content.
- Added finite child-stop waiting. A cancellation-violating child retains its
  bounded capacity slot until actual cleanup, preventing unbounded broken-child
  accumulation.
- Enforced finite defaults and hard maxima for children, packets, bytes, and
  stop duration.
- Updated the diagnostic banner and relay-entry user agent to 0.0.4a.
- Accepted ADR-0028 and documented the boundary and validation in PCN-0021 and
  PCN-0022.
- Added ten Network tests; all 105 tests and both self-tests pass.
- Kept downstream HTTP/PCP negotiation, CHAN serialization, and live one-hop
  interoperability pending before Milestone 0.0.4 can be complete.

### 0.0.3f privacy-safe channel faults and two-build verification

- Added fixed Core fault category and stage enums with attempt and reconnect
  counts; no free-form string, exception, identifier, endpoint, metadata,
  payload, position, or protocol code crosses the observable Core boundary.
- Published terminal state and fault atomically, prohibited a direct transition
  to `Faulted`, retained the final fault through `Stopping` and `Stopped`, and
  cleared it only when an explicit reconnect begins.
- Classified bounded Network failures as connection, timeout, remote
  unavailable/rejected/ended, protocol, consumer, resource-limit, or internal
  failures without exposing exception text.
- Kept requested and cooperative stops fault-free.
- Added fixed-field Channel Session CLI fault output and updated the diagnostic
  relay-entry user agent to `PeerCastNova/0.0.3f`.
- Verified four live packets followed by finite cooperative stop against
  isolated official Stable 4.1.0 and development 7.1.1 peers.
- Accepted ADR-0027 and documented the model and sanitized evidence in
  PCN-0019 and PCN-0020.
- Added fault lifecycle, classification, retry-exhaustion, stop, and privacy
  coverage; all 95 tests and both self-tests pass.
- Marked Milestone 0.0.3 Channel Verified; 0.0.4 Relay is next.

### 0.0.3e end-to-end channel session

- Extracted one relay handshake path shared by the compatibility probe and the
  owning channel session.
- Handed the exact HTTP/handshake PipeReader to post-OK CHAN decoding so
  already-buffered channel bytes are retained.
- Added finite connect, handshake, stream-idle, reconnect, cancellation, Pipe,
  connection, and disposal ownership to `PcpChannelSession`.
- Preserved the stable local session ID, Core position adapter, next relay
  position, and packet sequence across reconnect attempts.
- Retried only finite transport, timeout, and truncated-input failures;
  consumer failures, malformed protocol, RelayFull, and explicit rejection are
  terminal.
- Added a callback-safe nonblocking stop request and an awaitable session stop.
- Kept terminal summaries free of channel IDs, peer/session IDs, addresses,
  metadata, payload contents, positions, and exception text.
- Added a finite live Channel Session CLI and PowerShell helper that report only
  aggregate counts and lifecycle outcomes.
- Made a probe deadline return exit code 2 after privacy-safe finite output so
  automated validation cannot mistake a timeout for packet success.
- Verified controlled PeerCastStation 6.0.0.0 reception through HTTP 200 and
  four live packets, followed by bounded cooperative shutdown.
- Updated the diagnostic relay-entry user agent to `PeerCastNova/0.0.3e`.
- Accepted ADR-0026 and documented the owning boundary in PCN-0018.
- Added session handoff, reconnect, rejection, consumer-failure, initial
  position, and callback-stop coverage; all 89 tests and both self-tests pass.

### 0.0.3d bounded packet boundary

- Added a decoder mode that yields after exactly one complete top-level Atom
  without examining buffered following Atoms.
- Added a post-handshake CHAN stream reader that maps control state and copies
  at most one bounded packet into owned pooled memory.
- Awaited the packet consumer before decoding the next top-level Atom, making
  upstream backpressure explicit without an unbounded queue.
- Kept packet payload callback-scoped and outside Core snapshots.
- Added monotonic per-session packet sequence, wire/absolute position, and
  explicit discontinuity delivery to the data-plane consumer.
- Propagated cancellation through the consumer and reported QUIT versus clean
  connection end as distinct finite results.
- Rejected packet, Atom-content, and metadata limit inconsistencies before the
  stream starts.
- Updated the diagnostic relay-entry user agent to `PeerCastNova/0.0.3d`.
- Accepted ADR-0025 and documented the boundary in PCN-0017.
- Added eight Protocol/Network tests; all 81 tests and both self-tests pass.

### 0.0.3c CHAN control mapping

- Added the Network adapter that maps payload-free CHAN control messages into
  immutable Core channel snapshots.
- Expanded Core metadata with stream, comment, extension, and current-track
  fields without introducing protocol or payload ownership.
- Added atomic metadata-and-position publication using one snapshot revision.
- Preserved absent partial-update fields while treating empty strings as
  explicit values.
- Expanded 32-bit PCP packet positions into monotonic 64-bit positions with
  modulo wrap handling.
- Reported backward or half-range-ambiguous positions as explicit
  discontinuities without moving Core state or committing the wire position.
- Rejected channel-ID mismatches and out-of-range bitrates before mutation.
- Updated the diagnostic relay-entry user agent to `PeerCastNova/0.0.3c`.
- Accepted ADR-0024 and documented the mapping boundary in PCN-0016.
- Added seven Core/Network tests; all 72 tests and both self-tests pass.

### 0.0.3b CHAN control decoder

- Added explicit, finite post-handshake Atom shape observation that retains no
  payload or decoded metadata values.
- Updated the diagnostic relay-entry user agent to `PeerCastNova/0.0.3b`.
- Redacted remote session IDs and addresses automatically during observation.
- Escaped non-printable or ambiguous Atom-name bytes before diagnostic output.
- Added an incremental CHAN control sink for `id`, `pkt`, `info`, and `trck`.
- Packet data is measured but never copied or exposed by the control model.
- Added strict UTF-8, trailing-NUL, embedded-NUL, duplicate-field, fixed-size,
  and metadata-length validation.
- Recorded the content-free PeerCastStation 6.0.0.0 observation in PCN-0014.
- Accepted ADR-0023 and documented the data-plane boundary in PCN-0015.
- Added synthetic tests for observation timeout/size limits and CHAN malformed,
  unknown, duplicate, and resource-limit behavior.

### 0.0.3a Channel control foundation

- Added distinct immutable channel and stable local-session identifiers.
- Added an explicit reconnect-aware channel session state machine.
- Added immutable metadata/status snapshots with monotonic revisions and stream
  positions.
- Added Core unit tests for valid, invalid, idempotent, reconnect, metadata,
  position, and terminal-state behavior.
- Added a configurable finite per-module shutdown deadline; cancellation and
  timeouts no longer prevent later modules from receiving a stop attempt.
- Corrected architecture documents that still described the removed
  `Nova.Common` project and an obsolete dependency direction.
- Accepted PCN-0013 and established the boundary later implemented in 0.0.3b.

### Build validation fixes (buildfix5)

- Made numeric Compatibility Probe output culture-invariant so the CLI builds
  cleanly with `CA1305` enabled.

### Build validation fixes (buildfix4)

- Added the missing PCP protocol namespace import to `PcpCompatibilityOptions`.
- Replaced ambiguous `ReadOnlySequence<byte>.ToArray()` resolution with an explicit bounded copy.
- Returned the concrete `Dictionary<string, string>` type from the private HTTP-header parser to satisfy CA1859 without weakening analysis.


### Added

- Optional, content-free ordered Atom-name tracing for live Compatibility Probe
  diagnostics
- Owned PCP control-atom tree model and codec
- Repeated PipeReader control-message reader
- PCP known control atom names
- Typed PCP wire-value codec
- HELO builder and OLEH parser
- Legacy HTTP relay request builder
- Bounded HTTP request/response header parser
- PCP compatibility probe
- 200/OK and 503/QUIT loopback integration tests
- CLI live compatibility command
- PowerShell compatibility helper
- Compatibility protocol and review documents
- ADR-0020 through ADR-0022

### Fixed

- Parse a complete leading handshake Atom before applying the buffer limit to
  following data received in the same Pipe read
- Classify HTTP 503 followed by QUIT before OLEH as `RelayFull`
- Added the missing PCP protocol namespace import to the compatibility probe result model
- Scoped CA1707 suppression to test projects so descriptive xUnit method names remain readable without weakening production analysis
- Made test byte-sequence expectations explicit `ReadOnlySpan<byte>` values, avoiding collection-expression inference as `int[]`
- Changed `PcpProtocolException` to derive from `Exception` because `InvalidDataException` is sealed, and added the standard exception constructors
- Added explicit xUnit global usings to every test project
- Made `NovaApplication` asynchronously disposable so its owned lifecycle gate is deterministically released
- Made `build.ps1` stop immediately after a failed native `dotnet` command
- Made `build.ps1` ignore only missing `LIB` directories for the current build process and restore the original environment afterwards
- Removed redundant direct `System.IO.Pipelines` package references for .NET 10,
  preventing NU1510 from being promoted to an error
- Made `build.ps1` independent of the caller's current directory and added
  transport/PCP framing self-tests
- PCP atom names now correctly support NUL-padded one-to-four-character names
  such as `sid`, `ver`, and `ok`

### Status

- 0.0.2c Verified
- official Stable 4.1.0 and official master development 7.1.1.0 both
  accepted the live probe with ordered top-level Atom trace `oleh`, `ok`
- 0.0.3 Channel is Verified, including official stable/development finite live
  packet sessions and privacy-safe observable fault status
- 0.0.4 Relay is Verified, including controlled real-media official-upstream
  integration through Nova to a downstream session
