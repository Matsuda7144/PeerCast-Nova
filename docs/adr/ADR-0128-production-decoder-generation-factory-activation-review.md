# ADR-0128: Production Decoder Generation Factory and Activation Review

## Status

Accepted for milestone 0.0.9r architecture review.

## Context

0.0.9q completed one finite source-bound generation from decoded-completion transfer through scheduler reservation, exact H.264／AAC context selection, timing, managed adaptation／conversion, and result-aware Windows output. The remaining production gap is not another decode or output algorithm. It is the ownership-safe construction and replacement of that complete generation.

The repository already contains the required lower boundaries, but no production factory composes them:

- `PlaybackDecoderBackendInstalledRuntimeLocator` verifies one explicitly supplied fixed-drive directory against the exact pinned five-file inventory and returns a lease that keeps the files and directory chain guarded;
- `PlaybackDecoderFfmpegAbiSymbolGate` loads only the verified modules, validates dependencies, symbols, and major versions, and returns a module／symbol lease;
- `PlaybackBoundedFlvDecoderParameterBindingFactory` creates independently owned H.264 and AAC parameter leases from one exact live FLV source session;
- `PlaybackBoundedFlvNativeDecoderSessionFactory` consumes one binding plus distinct video and audio ABI gate owners and returns no partial opened success;
- `PlaybackSourceBoundOneFrameDecodeBoundary` owns the opened native session and finite packet／receive state;
- `PlaybackGenerationMatchedSchedulerDeliveryBoundary` owns decode-to-scheduler generation lifetime and borrows the Windows output processor.

Two existing ownership facts are decisive.

First, `PlaybackDecoderFfmpegContextLease.Dispose` disposes its context, its ABI gate lease, and its parent verified runtime lease. A video context and audio context must therefore not share one runtime lease. The existing source-bound session factory already requires distinct gate objects; production composition must extend that rule to two independent runtime→gate→context ownership chains.

Second, `PlaybackWindowsOutputActivationBoundary` is the sole owner of COM initialization, D3D11, WASAPI, and OS handles and enforces synchronized owning-thread borrows. Resource binding, stream initialization, and buffer processing are layered borrows over that application-owned activation. Decoder generation replacement must not destroy or recreate those device-wide resources.

Activation is also legally closed today. `PlaybackFfmpegBackendCandidate.Provenance.LicenseReview` is `Pending`, and `PlaybackDecoderBackendPolicy` rejects every provenance record whose license review is not `Approved`. No production path may observe a runtime directory, load a module, or create a native context before that policy gate succeeds.

## Decision

### Policy gate precedes all external observation

The future production factory will evaluate the pinned candidate provenance through an explicitly supplied `PlaybackDecoderBackendPolicy` before observing the runtime directory, creating parameter ownership, loading modules, or invoking native code.

A successful activation policy requires all of the following:

- candidate license review is `Approved`;
- third-party code is explicitly allowed;
- native code is explicitly allowed;
- the candidate distribution remains `OptionalExternal`, so bundled approval is neither implied nor used.

`Pending` maps to a fixed `LicenseReviewPending` result. A rejected or contradictory policy maps to a fixed policy-rejected result. The current pinned candidate therefore cannot produce a production generation. This review does not change its provenance, approve its license, register an FFmpeg backend in `PlaybackDecoderBackendFactory`, or add a normal playback activation reference.

The policy check occurs before path validation deliberately: a legally unavailable backend must not use a caller path as a side channel or perform unnecessary filesystem work.

### Explicit host-installed runtime only

After policy approval, the factory accepts one explicit runtime directory and the exact pinned inventory. It does not search `PATH`, the registry, conventional installation directories, adjacent application directories, or the network. It does not download, extract, update, bundle, or redistribute FFmpeg.

Every locator, gate, binding, and construction step is attempted at most once per factory operation. There is no retry, fallback runtime, alternate build, broad major-version acceptance, or best-effort activation.

### Two independent runtime and ABI ownership chains

The factory acquires resources in this order after pure managed input validation:

1. create the exact FLV decoder-parameter binding;
2. locate and verify a video runtime lease;
3. acquire the video ABI gate from that video runtime lease;
4. independently locate and verify an audio runtime lease from the same explicit directory and pinned inventory;
5. acquire the audio ABI gate from that audio runtime lease;
6. pass the binding and the two distinct gates to `PlaybackBoundedFlvNativeDecoderSessionFactory`.

The two locator results must contain different live lease instances, and the two ABI gate results must contain different live gate instances. Sharing one runtime lease between both gates is rejected even when both gates expose identical symbols. Each created context consumes one complete parent chain, and disposing either context cannot invalidate the other.

The source-bound native-session factory remains responsible for consuming the binding and both gate chains on every success or failure result. The production factory does not dispose those objects again after transfer.

### One complete generation product

Only after the opened H.264／AAC session succeeds will the factory construct:

1. one `PlaybackSourceBoundOneFrameDecodeBoundary` for the exact source, decoder generation, and discontinuity generation;
2. one fresh `BoundedPlaybackScheduler` with explicit count and owned-byte limits;
3. one generation-scoped `PlaybackAvTimingDecisionBoundary` using the same outer generation for video and audio;
4. one `PlaybackGenerationMatchedSchedulerDeliveryBoundary` using the exact decode boundary, native session, scheduler, timing boundary, and borrowed Windows output processor.

The returned production generation product owns the 0.0.9q boundary as its single cleanup root. Through that root it owns the scheduler, decode state, native session, two contexts, two ABI gates, two verified runtime leases, and copied codec parameters. It retains only non-owning identity references to the FLV source session and application output environment.

No partially constructed product is returned. Undefined subordinate results, source disposal during construction, generation contradiction, allocation failure, policy contradiction, locator rejection, gate rejection, native-session rejection, or constructor failure unwind every still-owned object in strict reverse order.

### Borrowed application output environment

The application owner creates and retains the Windows output chain:

- `PlaybackWindowsOutputActivationBoundary`;
- `PlaybackWindowsOutputResourceBindingBoundary`;
- `PlaybackWindowsOutputStreamInitializationBoundary`;
- `PlaybackWindowsOutputBufferProcessingBoundary`.

The production generation factory receives only a live buffer-processing boundary prepared by that owner. It never calls OS activation implicitly and never owns or disposes the supplied output boundary.

Generation reset may call the existing finite audio／video output-reset operations through 0.0.9q, but activation, window identity, device selection, resource binding, stream format negotiation, COM apartment lifetime, and device recreation remain application responsibilities. `InitializationRequired`, `BindingRequired`, `ResizeRequired`, and device loss remain explicit results to that outer owner rather than triggers for hidden factory work.

### Source ownership and generation identity

The FLV parser or higher composition remains the owner of the source demux session. The factory validates and borrows the exact live session; it does not dispose it. Parameter data is independently copied before native creation, while source identity, stream IDs, codecs, and time bases remain exact generation invariants.

Decoder generation and discontinuity generation are caller-supplied non-negative identities. A replacement generation starts scheduler sequence zero. The later composition owner must assign increasing decoder／discontinuity identities and must not reuse a stopped generation product.

### Finite creation, cancellation, and replacement

Factory creation is one finite serialized operation. Cancellation is checked before filesystem observation and before each irreversible ownership-transfer stage. Existing synchronous native boundaries remain finite single attempts; the factory does not create a background cancellation thread or abandon native ownership mid-call.

Discontinuity replacement uses stop-old-before-create-new ordering:

1. close upstream transfer and delivery admission for the old generation;
2. cancel and await its active delivery／reservation;
3. dispose scheduler-owned frames and active decoded ownership;
4. dispose decode／submission state and the native session;
5. dispose both context→gate→runtime chains through the native-session cleanup;
6. retain the application-owned Windows output environment;
7. create the replacement generation from the new exact source session and new identities.

The old generation is never revived if replacement creation fails, and a replacement context is never made available while an old reservation remains live. This favors deterministic ownership over gapless playback. Controlled composition and user-facing retry policy remain later work.

### Backend factory remains unchanged

`PlaybackDecoderBackendFactory` continues to perform generic descriptor provenance audit and deterministic backend selection. 0.0.9s will implement the reviewed production generation factory as an explicit boundary, but it will not register or expose a normal FFmpeg backend while the pinned license review is pending.

A later explicit approval decision must update provenance and separately review normal backend registration, distribution obligations, notices, source-offer requirements, and packaging before production activation becomes reachable.

## Rejected alternatives

- Sharing one verified runtime lease between video and audio contexts.
- Acquiring one ABI gate and passing it twice to the source-bound session factory.
- Checking the runtime path or loading modules before license／backend policy succeeds.
- Silently changing the pinned candidate from `Pending` to `Approved` as part of factory implementation.
- Searching `PATH`, the registry, neighboring application directories, or the network for a usable FFmpeg build.
- Downloading or extracting a runtime from the generation factory.
- Returning a partially opened video-only or audio-only generation.
- Letting a decoder generation own D3D11, WASAPI, COM apartment, window, or OS-handle lifetime.
- Recreating output devices automatically after `DeviceLost`, resize, initialization, or binding results.
- Creating a replacement before the old generation's active reservation and contexts have been released.
- Reviving an old generation after replacement construction fails.
- Adding a resident decode／delivery pump, hidden retry, or second queue to factory construction.
- Registering the candidate in the normal backend factory while license review remains pending.

## Consequences

- The future production factory has one unambiguous ownership graph from copied FLV parameters through two independent verified runtime chains to one finite decode／scheduler delivery generation.
- The current legal state remains fail-closed: production creation returns `LicenseReviewPending` before filesystem or native work.
- Video and audio context cleanup can no longer invalidate each other through an accidentally shared runtime lease.
- Application-wide Windows resources can survive decoder discontinuity and generation replacement.
- Replacement may create an intentional playback gap because old ownership is fully stopped before new native resources are acquired.
- Runtime bundling, redistribution, automatic acquisition, backend registration, device activation, continuous pumping, and audible／visible playback remain unimplemented.

## Next

0.0.9s — Policy-gated Production Decoder Generation Factory Boundary.
