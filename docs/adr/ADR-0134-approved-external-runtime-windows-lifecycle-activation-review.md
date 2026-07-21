# ADR-0134: Approved External Runtime and Windows Lifecycle Activation Review

## Status

Accepted for milestone 0.0.9x.

## Context

0.0.9w approved one external LGPL FFmpeg runtime route and added an application-composition-root production entry. The production generation now owns verified runtime leases, ABI gates, native decoder contexts, decoded scheduler ownership, timing, and finite caller-driven delivery, while borrowing one `PlaybackWindowsOutputBufferProcessingBoundary`.

The Windows output stack already exists as separate technical boundaries:

1. 0.0.6t resource-free D3D11／WASAPI adapter probe;
2. 0.0.6u COM, D3D11 device/context, default WASAPI endpoint/client, and event-handle activation;
3. 0.0.6v renderer／audio configuration binding;
4. 0.0.6w swap-chain／render-target and WASAPI stream initialization;
5. 0.0.6x one-frame／one-buffer processing;
6. 0.0.6y one-item scheduler adapter.

The current production generation no longer uses the 0.0.6y scheduler adapter. `PlaybackGenerationMatchedSchedulerDeliveryBoundary` owns the bounded scheduler reservation and calls the 0.0.6x buffer processor directly. Reintroducing the older adapter would create a second scheduler-delivery owner.

The missing boundary is an application-owned lifecycle root that constructs and coordinates the existing Windows boundaries with the approved external runtime composition without duplicating native ownership, violating thread affinity, or adding a hidden playback loop.

## Findings

### The application owns the window, not the playback activation boundary

`PlaybackWindowsOutputActivationBoundary` owns MTA COM initialization, the D3D11 device and immediate context, the default WASAPI endpoint/client, and the WASAPI event handle. It does not create or own a Win32 window or message loop.

The application supplies one positive opaque window token during stream initialization. The future window and its message loop remain application-owned and must outlive the initialized video stream.

### Windows output has strict owner-thread affinity

Production activation records the creating thread. Every later activation borrow rejects a different thread as `InvalidEnvironment`. Binding, stream initialization, buffer processing, reset, and disposal all borrow activation-owned resources and therefore must execute on that same thread.

The production decode／delivery path is asynchronous and uses continuation-free awaits. Directly invoking the concrete buffer processor from an arbitrary asynchronous continuation does not prove that the native borrow occurs on the activation-owning thread.

Therefore an application-owned output dispatcher is required before production activation. The playback library must not silently create its own thread, queue, window loop, timer, or resident pump.

### Decoder and output generations are currently one delivery identity

Production delivery passes the active decoder generation into the Windows buffer records. Binding, stream initialization, and buffer processing require matching generations. For the first production activation, the application lifecycle must use the decoder generation as the output configuration generation.

A discontinuity replacement may create a new decoder generation while retaining the same activation-owned D3D11 and WASAPI resources. Before any item from the new generation is delivered, the application lifecycle must reset and reconfigure binding／stream state to the new decoder generation.

A separate independently advancing output epoch would require a new frame and scheduler identity contract and is outside this milestone.

### Device loss cannot safely be repaired by swapping the borrowed concrete output

A production generation retains the output port selected during construction. If the activation-owned D3D11 or WASAPI resources are invalidated, replacing the concrete output chain underneath the existing generation is not currently supported.

For the first application lifecycle boundary, device loss is therefore terminal for the complete lifecycle owner. It closes admission, stops the production composition, releases runtime／decoder／scheduler ownership, then tears down Windows output resources. Recovery is an explicit caller-created new lifecycle and source reconnection, not automatic revival or rollback.

## Decision

### Add one application-owned lifecycle root

0.0.9y will add one finite owner above the approved external runtime and existing Windows boundaries. It owns:

- the output adapter probe;
- the output activation boundary;
- the resource binding boundary;
- the stream initialization boundary;
- the buffer processing boundary;
- one dispatcher-backed output port;
- one `PlaybackProductionPcpFlvCompositionBoundary` created through `PlaybackFfmpegExternalRuntimeComposition`.

It borrows:

- the application-owned output dispatcher／owner thread;
- the application-owned window token and window lifetime;
- caller-owned configuration values.

It does not own or create the window, message loop, dispatcher thread, source transport, or FFmpeg binary.

### Require an explicit finite output dispatcher contract

The application supplies a dispatcher that can:

- report whether the caller currently has owner-thread access;
- invoke one bounded synchronous or asynchronous output operation on the owner thread;
- propagate cancellation and one fixed completion result;
- reject work after dispatcher shutdown.

The playback library adds no internal dispatcher thread and no unbounded command queue. Every native activation, binding, initialization, processing, reset, and teardown operation is routed through this dispatcher.

The existing concrete buffer processor remains usable for tests and direct owner-thread callers. Production application composition uses a dispatcher-backed output port. The generation delivery seam may be generalized internally from the concrete buffer class to the minimal process／reset interface needed for this port; no second delivery path or scheduler is added.

### Fixed initial construction sequence

The lifecycle root serializes construction through one operation gate:

1. validate manifest, backend policy, runtime-directory syntax, window token, canonical output formats, and bounded configuration without native access;
2. resource-free probe of Windows 11, D3D11, and WASAPI prerequisites;
3. on the application output dispatcher, activate MTA COM, D3D11, WASAPI, and the event handle;
4. construct binding, stream-initialization, and buffer-processing boundaries around the activation owner;
5. construct the approved external runtime production composition borrowing only the dispatcher-backed output port;
6. accept PCP／FLV input until an exact stream description and decoder generation exist;
7. derive bounded width, height, sample rate, and channel count from the production snapshot and combine them with application-selected BGRA32 and interleaved S16／F32 output formats;
8. on the dispatcher, bind video then audio and initialize video then audio using the exact decoder generation;
9. enter `Ready` only after both output streams match the active decoder generation.

No FFmpeg runtime path is searched or downloaded. Runtime file verification still occurs only when the production generation is created from an exact parser source description.

### Caller-driven operations only

The lifecycle root exposes finite wrappers for input, finalization, receive／drain, transfer, one-item delivery, resize, reset, and stop.

It does not add:

- a playback thread;
- a resident decode or delivery loop;
- a timer or wall-clock sleeper;
- a second scheduler queue;
- hidden retry;
- implicit output fallback.

`WouldBlock`, timing wait, initialization requirement, binding requirement, resize requirement, and retained scheduler ownership remain explicit caller-visible results.

### Initial and replacement output configuration

Initial stream configuration occurs after the parser-created source description becomes available and before the first delivery.

When the production snapshot reports a new decoder generation after discontinuity replacement, the lifecycle gate blocks delivery and performs this fixed dispatcher sequence:

1. reset audio buffer processing, then video buffer processing;
2. reset audio stream state, then video stream state;
3. reset the combined binding boundary;
4. bind video then audio with the new decoder generation;
5. initialize video then audio with the new decoder generation;
6. mark the new generation output-ready.

Activation-owned COM, D3D11 device/context, WASAPI endpoint/client, and event handle remain live. Decoder replacement does not recreate Windows devices.

The first new-generation scheduler item still carries `DecoderReset`; the existing generation delivery boundary resets timing and buffer state once before delivering it. Repeating a successful buffer reset to the same generation is allowed and does not duplicate native ownership.

### Resize

A retained `ResizeRequired` result does not consume the scheduler item. The application updates the window client dimensions and explicitly invokes video stream resize on the output dispatcher using the current decoder generation. Binding, audio stream state, FFmpeg runtime, and decoder ownership remain unchanged. The caller then retries one delivery.

### Device loss and unexpected output invalidation

`DeviceLost` maps to production `ResetRequired`, but the new application lifecycle must not attempt in-place activation replacement under an existing production generation.

On device loss:

1. latch terminal device-loss state and close new input／delivery admission;
2. cancel and await the active lifecycle operation;
3. stop the production composition and release retained scheduler items, decoded frames, native decoder contexts, ABI gates, and runtime leases;
4. on the output dispatcher, dispose buffer processing, stream initialization, binding, and activation in that order;
5. preserve the application-owned window and dispatcher;
6. require the caller to create a new lifecycle and reconnect the source.

There is no automatic device monitoring, recreation, fallback adapter, old-generation revival, or retained-frame migration.

### Normal stop and partial-construction cleanup

Normal stop uses the same ownership order without the device-loss result:

1. close admission and cancel the active lifecycle operation;
2. await and stop the production composition;
3. dispose the dispatcher-backed output port;
4. on the owner dispatcher, dispose buffer processing;
5. dispose stream initialization;
6. dispose resource binding;
7. dispose activation and uninitialize COM on its owning thread;
8. dispose the resource-free probe.

Partial construction unwinds only acquired resources in reverse order. Cancellation after a destructive step completes mandatory cleanup before returning; no rollback recreates a disposed resource.

### Snapshot and public surface

The lifecycle snapshot may copy:

- fixed lifecycle state and last result;
- decoder／discontinuity generation;
- output-ready generation;
- probe, activation, binding, stream, and buffer states;
- bounded stream dimensions and audio configuration;
- whether an operation or delivery is active.

It must not expose HWND values, COM pointers, D3D／DXGI interfaces, WASAPI interfaces, event handles, native runtime handles, raw HRESULTs, or dispatcher implementation objects.

## Rejected alternatives

- Calling the concrete buffer processor directly from arbitrary async continuations.
- Reusing `PlaybackWindowsOutputSchedulerAdapterBoundary` in parallel with the current generation-matched delivery owner.
- Creating a hidden playback-owned dispatcher thread or message pump.
- Creating or disposing the application window inside `Nova.Playback`.
- Initializing output before an exact parser stream description exists.
- Advancing an independent output generation without extending frame identity.
- Recreating D3D11／WASAPI resources in place after device loss while the old production generation remains alive.
- Automatically reconnecting the source or reviving the old decoder generation.
- Polling WASAPI capacity or retrying `WouldBlock` internally.
- Falling back to another adapter, runtime directory, manifest, or codec backend.

## Consequences

- The approved FFmpeg runtime can be connected to Windows output only through an explicit application-owned dispatcher and lifecycle owner.
- Existing Windows ownership boundaries remain singular and reusable.
- The current generation-matched scheduler delivery remains the only production scheduler-output authority.
- Initial and discontinuity output configuration use the exact decoder generation.
- Resize is explicit and non-destructive; device loss is terminal for the first lifecycle implementation.
- No Windows application window, message loop, continuous playback pump, real-device validation, or audible／visible playback is claimed by this review.

## Next

0.0.9y — Finite Approved External Runtime and Windows Lifecycle Activation Boundary.
