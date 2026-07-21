# ADR-0135: Finite Approved External Runtime and Windows Lifecycle Activation Boundary

## Status

Accepted for milestone 0.0.9y.

## Context

ADR-0134 selected one application-owned finite lifecycle above the exact built-in approved or explicitly host-attested external FFmpeg production route and the existing Windows 11 D3D11／WASAPI output boundaries.

The existing production generation already owns verified runtime leases, ABI gates, H.264／AAC native contexts, decoded-frame scheduler ownership, timing, and one-item delivery. The existing Windows output boundaries separately own probe, activation, binding, stream, and buffer state. Production activation additionally records one owner thread, while decode and delivery continue asynchronously.

The missing implementation must join those owners without adding a second scheduler, hidden playback loop, internal dispatcher thread, window, message pump, runtime discovery, device fallback, or automatic recovery.

## Decision

### Add one finite lifecycle owner

Add `PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary` as the single application-composition owner for:

- one resource-free Windows output probe;
- one activation boundary owning COM, D3D11, WASAPI, and the event handle;
- one binding boundary;
- one stream-initialization boundary;
- one buffer-processing boundary;
- one dispatcher-backed production delivery path;
- one external-runtime `PlaybackProductionPcpFlvCompositionBoundary`;
- one serialized operation and stop domain.

The lifecycle borrows one application dispatcher and one positive opaque window token. It never owns or disposes either and never creates a window, dispatcher thread, message pump, queue, timer, source transport, or FFmpeg binary.

### Explicit dispatcher contract

Add `IPlaybackWindowsOutputDispatcher` with:

- `HasAccess` for owner-thread assertions;
- one finite generic `InvokeAsync` operation;
- fixed completed, cancelled, shutdown, and faulted invocation results;
- caller cancellation propagation.

The contract requires the application implementation to keep every activation borrow on the same owner thread. Nova does not supply a default dispatcher or create hidden thread infrastructure.

All production activation, binding, stream initialization, buffer processing, output reset, resize, and output teardown pass through this dispatcher.

### Policy and configuration preflight

Before probe, runtime-path observation, or native work, the lifecycle validates:

- one explicit fixed-drive Windows runtime directory;
- one positive window token;
- canonical BGRA32 video output;
- interleaved signed-16 or float-32 audio output;
- bounded audio-buffer duration;
- nonnegative initial PCP packet sequence;
- external-only backend policy;
- exact built-in approval or explicit host-attested policy admission.

A rejected manifest or backend policy returns a fixed result without probe, dispatcher, runtime, or native access.

### Fixed initialization sequence

One operation gate serializes initialization:

1. complete pure preflight;
2. run the resource-free Windows adapter probe;
3. dispatch activation on the application owner thread;
4. construct binding, stream, and buffer owners around the singular activation owner;
5. construct one manifest-specific external-runtime production composition;
6. accept input until the parser exposes an exact stream description and decoder generation;
7. derive bounded video and audio configuration from the production snapshot;
8. dispatch video binding, audio binding, video stream initialization, and audio stream initialization;
9. enter `Ready` only when binding, stream, and buffer snapshots all match the active decoder generation.

Delivery before exact output configuration returns `OutputConfigurationRequired` and performs no scheduler delivery.

### Dispatcher-backed production generation

Add an internal production-generation operations adapter that preserves the existing 0.0.9q generation-matched scheduler owner and replaces only the output invocation seam.

The adapter:

- retains the same source-bound decode, native session, scheduler, timing, reservation, and completion semantics;
- dispatches canonical video／audio buffer processing to the owner thread;
- dispatches generation reset for audio then video buffers;
- maps dispatcher shutdown and cancellation to existing fixed delivery results;
- adds no scheduler, queue, retry, or retained-frame copy.

The existing concrete output path remains unchanged for direct owner-thread callers and tests.

### Caller-driven finite operations

The lifecycle exposes finite wrappers for:

- PCP／FLV packet input;
- encoded-input finalization;
- one-frame ordinary receive;
- independent EOS drain;
- one-item scheduler delivery;
- explicit generation reset;
- explicit video resize;
- stop and disposal.

One nonblocking operation gate rejects concurrent calls as `AlreadyRunning`. There is no resident decode／delivery pump, capacity polling, timing sleeper, or automatic retry.

### Generation-matched output configuration

The decoder generation remains the output generation.

When the production snapshot reports a replacement generation, delivery remains blocked while the lifecycle dispatches this exact order:

1. reset audio buffer processing;
2. reset video buffer processing;
3. reset audio stream state;
4. reset video stream state;
5. reset combined binding;
6. bind video;
7. bind audio;
8. initialize video;
9. initialize audio;
10. mark the generation output-ready only after exact snapshot agreement.

Activation-owned COM, D3D11, WASAPI, and event resources remain live. Old and new decoder generations still follow the production composition's stop-old-before-create-new contract.

### Retained results and resize

`WouldBlock`, timing wait, initialization requirement, binding requirement, resize requirement, audio recovery, clock-not-started, and reset-required remain caller-visible. The existing scheduler reservation remains retained.

A retained resize result moves the lifecycle to `ResizeRequired`. The caller supplies new positive dimensions, the lifecycle dispatches same-generation video stream resize, and the caller later invokes one-item delivery again. The lifecycle does not retry or consume the retained item automatically.

### Terminal device loss

Any binding, stream, or buffer device-loss snapshot closes admission and latches `DeviceLost` for the complete lifecycle.

Cleanup order is fixed:

1. cancel and await the active serialized operation;
2. stop and dispose the production composition first, releasing scheduler items, decoded frames, native contexts, ABI gates, and runtime leases;
3. dispose the dispatcher-backed output path;
4. on the owner dispatcher, dispose buffer processing;
5. dispose stream initialization;
6. dispose binding;
7. dispose activation and owner-thread COM state;
8. dispose the resource-free probe;
9. preserve the borrowed application dispatcher and window token.

There is no in-place device recreation, fallback, source reconnect, frame migration, old-generation revival, or rollback. Recovery requires a caller-created new lifecycle.

### Stop, cancellation, and partial cleanup

`StopAsync` is idempotent. It closes admission, cancels the active operation, waits for the operation gate, and then uses the same production-first output cleanup order.

Partial initialization unwinds only acquired ownership. Activation failure never creates production ownership. Cleanup methods are idempotent and fixed-result; a later dispose does not repeat destructive release.

### Snapshot

The public snapshot copies only:

- lifecycle state and last result;
- decoder, discontinuity, and output-ready generations;
- bounded video and audio stream configuration;
- active-operation state;
- probe, activation, binding, stream, and buffer snapshots.

It exposes no HWND value, dispatcher implementation, COM pointer, D3D／DXGI／WASAPI interface, event handle, FFmpeg handle, raw HRESULT, runtime path, or mutable native owner.

## Analyzer decisions

Two source diagnostics and one focused-test diagnostic are intentionally file-scoped in `.editorconfig`:

- `CA1068`: the private serialization helper keeps cancellation adjacent to the delegated operation before the lifecycle admission flag;
- `CA1859`: partial-construction cleanup intentionally retains the minimal delivery-operations interface contract shared with the standard generation path;
- `CA1861`: short immutable order expectations remain adjacent to each lifecycle proof rather than becoming shared mutable test state.

No analyzer category is disabled globally.

## Verification

Cross-platform injected tests cover:

1. policy rejection before probe or dispatcher access;
2. probe → activation → production construction order;
3. delivery rejection before exact stream description;
4. initial bind／initialize before first delivery;
5. exact reverse reset and forward reconfiguration order for replacement generation;
6. retained resize without implicit retry;
7. terminal device loss and production-first teardown;
8. idempotent stop preserving dispatcher and window ownership;
9. stop cancellation of an active operation before cleanup;
10. activation-failure partial cleanup without production creation.

Routine Linux validation uses restore, Release build with analyzers, the full solution test suite, and format verification.

## Rejected alternatives

- Direct output calls from arbitrary asynchronous continuations.
- A second scheduler adapter or delivery owner.
- A playback-owned dispatcher thread, queue, message pump, or window.
- Output initialization before exact stream description.
- Independent output-generation advancement.
- Hidden resize, backpressure, timing, or runtime retry.
- In-place device recreation under an existing production generation.
- Automatic source reconnection or fallback runtime selection.
- Ownership of the application dispatcher or window token.

## Consequences

- The reviewed external-runtime route now has one finite application lifecycle entry to the existing Windows output boundaries.
- Native output access and teardown are explicitly owner-thread-dispatched.
- The 0.0.9q generation-matched scheduler remains the sole production scheduler-output authority.
- Initial and replacement output state use the exact decoder generation.
- Retained results remain caller-driven and non-destructive.
- Device loss is terminal and cleanup is production-first.
- No physical Windows device, application window, real-media decode／output, or audible／visible playback is claimed by Linux injected validation.

## Next

0.0.9z — Controlled Retained-Fixture Production Activation Validation.
