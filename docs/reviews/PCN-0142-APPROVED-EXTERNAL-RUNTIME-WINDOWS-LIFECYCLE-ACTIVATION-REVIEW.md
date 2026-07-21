# PCN-0142: Approved External Runtime and Windows Lifecycle Activation Review

## Milestone

0.0.9x

## Validation target

Define the only acceptable application-level connection from the approved 0.0.9w external FFmpeg runtime route to the existing Windows 11 D3D11／WASAPI output boundaries, preserving owner-thread affinity, singular native ownership, generation identity, retained scheduler backpressure, and finite caller-driven work.

## Existing boundaries reviewed

| Milestone | Boundary | Existing responsibility |
| --- | --- | --- |
| 0.0.6t | `PlaybackWindowsOutputAdapterProbe` | Resource-free Windows 11／D3D11／WASAPI prerequisite probe |
| 0.0.6u | `PlaybackWindowsOutputActivationBoundary` | MTA COM, D3D11 device/context, default WASAPI endpoint/client, event handle |
| 0.0.6v | `PlaybackWindowsOutputResourceBindingBoundary` | Video／audio configuration binding and generation state |
| 0.0.6w | `PlaybackWindowsOutputStreamInitializationBoundary` | Window-token swap chain／render target and WASAPI stream initialization |
| 0.0.6x | `PlaybackWindowsOutputBufferProcessingBoundary` | One video frame or audio buffer processing and explicit reset |
| 0.0.6y | `PlaybackWindowsOutputSchedulerAdapterBoundary` | Earlier one-item scheduler reservation adapter |
| 0.0.9q | `PlaybackGenerationMatchedSchedulerDeliveryBoundary` | Current production scheduler reservation, timing, conversion, and output delivery |
| 0.0.9u | `PlaybackProductionPcpFlvCompositionBoundary` | Current production input／decode／transfer／delivery／replacement owner |
| 0.0.9w | `PlaybackFfmpegExternalRuntimeComposition` | Approved built-in／host-manifest production entry |

## Findings

### Native owner-thread requirement

`PlaybackWindowsOutputActivationBoundary` records the thread that activates COM, D3D11, and WASAPI. Its internal resource borrow returns `InvalidEnvironment` on another thread. Binding, stream initialization, buffer processing, and teardown all use that borrow.

Production decode and delivery use asynchronous operations with continuation-free awaits. A direct concrete output call from the resulting continuation cannot guarantee the activation-owning thread. An explicit application-owned dispatch seam is therefore required.

### Window ownership remains external

The activation boundary does not create a window. Stream initialization accepts an opaque positive window token and internally validates and uses it for DXGI swap-chain creation. The application remains responsible for window creation, destruction, client-size changes, and message dispatch.

### The old Windows scheduler adapter is superseded for production

0.0.6y solved one-item reservation before the generation-matched production owner existed. 0.0.9q now owns the production scheduler, reservation, timing, decoder context selection, conversion, retained output results, reset state, and stop ordering.

Placing 0.0.6y in the production route would create a second reservation and delivery authority. It remains a standalone technical boundary but is not composed by 0.0.9y.

### Generation coupling is real

The production delivery path writes the decoder generation into every Windows output buffer. Binding, stream initialization, and buffer processing reject mismatched generations. Initial and replacement output configuration must therefore use the exact active decoder generation.

### Device-loss replacement is not an in-place operation

The production generation borrows one output object for its lifetime. The activation chain cannot be swapped under that generation. Device loss must stop the whole lifecycle before activation resources are recreated.

## Decision assertions

1. One application lifecycle owner is the only production composition point.
2. The application supplies a dispatcher bound to the Windows output owner thread.
3. `Nova.Playback` creates no hidden dispatcher thread, message pump, timer, or resident loop.
4. The application owns the window and supplies an opaque positive window token.
5. The playback lifecycle owns probe, activation, binding, stream, buffer, dispatcher-backed output port, and production composition boundaries.
6. Production composition borrows the output port and never activates, binds, initializes, recreates, or disposes Windows devices.
7. All native output work and teardown are dispatched to the activation-owning thread.
8. The production generation-matched scheduler delivery remains the sole scheduler-output authority.
9. `PlaybackWindowsOutputSchedulerAdapterBoundary` is not inserted into the production path.
10. Initial Windows binding and initialization wait for an exact parser-created stream description.
11. Application-selected output formats remain BGRA32 video and interleaved S16 or F32 audio.
12. Width, height, sample rate, and channel count come from the production snapshot.
13. Binding, stream, buffer, and decoded delivery use the exact active decoder generation.
14. A discontinuity generation change blocks delivery until output state is reset and reconfigured to the new decoder generation.
15. Decoder discontinuity replacement reuses activation-owned D3D11 and WASAPI resources.
16. Resize is explicit, uses the current generation, retains the scheduler item, and retries only when the caller invokes delivery again.
17. `WouldBlock`, timing wait, binding requirement, initialization requirement, and resize requirement remain retained caller-visible outcomes.
18. Device loss is terminal for the first lifecycle owner.
19. Device loss does not automatically recreate resources, reconnect transport, revive a generation, migrate frames, or select a fallback.
20. Stop releases production runtime／decoder／scheduler ownership before Windows output ownership.
21. Windows teardown occurs buffer → stream → binding → activation on the owner dispatcher.
22. Activation disposal uninitializes COM on the activation-owning thread.
23. The application-owned window and dispatcher are never disposed by the playback lifecycle.
24. FFmpeg remains external, explicit-directory, checksum／ABI／symbol verified, and unbundled.
25. Snapshots expose only copied fixed state and configuration, never native resources.

## Required 0.0.9y implementation

### Application inputs

The finite owner must require:

- channel and session identity;
- approved built-in or host-supplied FFmpeg manifest;
- explicit runtime directory;
- explicit backend policy permitting third-party native code and rejecting bundling;
- application-owned output dispatcher;
- application-owned positive window token;
- canonical output format choices;
- bounded audio buffer duration;
- production parser／generation options.

### States

A fixed public state set should distinguish at least:

- `Created`;
- `Activated`;
- `WaitingForStreamDescription`;
- `OutputConfigurationRequired`;
- `Ready`;
- `ResizeRequired`;
- `DeviceLost`;
- `Faulted`;
- `Disposed`.

Undefined internal or subordinate states fail closed.

### Initialization

The owner must:

1. validate all pure inputs and backend policy before OS work;
2. run the resource-free adapter probe;
3. dispatch activation to the owner thread;
4. construct binding／stream／buffer boundaries;
5. create a dispatcher-backed process／reset output port;
6. create the approved external runtime production composition borrowing that port;
7. remain input-capable but non-deliverable until a stream description exists;
8. configure binding and streams on the dispatcher with the active decoder generation;
9. admit delivery only after output snapshots match that generation.

### Dispatcher-backed output port

The production delivery seam must depend on the minimal output process／reset contract rather than requiring arbitrary async continuations to call the concrete processor directly.

The dispatcher adapter must:

- dispatch exactly one process or reset call;
- preserve all fixed processor results;
- propagate cancellation without consuming retained scheduler ownership;
- reject work after lifecycle shutdown;
- expose no native pointer, handle, or dispatcher implementation;
- own neither the concrete processor nor the application dispatcher unless explicitly transferred by the lifecycle owner.

### Input and delivery serialization

One lifecycle operation gate must serialize:

- initialization;
- PCP input and explicit finalization;
- receive／drain／transfer;
- one-item delivery;
- output configuration;
- resize;
- discontinuity generation reconfiguration;
- terminal cleanup and stop.

No input callback performs resident output pumping. No output repair runs concurrently with delivery.

### Generation reconfiguration

When a completed production operation reveals a new active decoder generation, the owner must block delivery and dispatch:

1. audio buffer reset;
2. video buffer reset;
3. audio stream reset;
4. video stream reset;
5. binding reset;
6. binding configure with the new generation;
7. stream initialization with the new generation.

Only after matching snapshots are observed may delivery resume.

### Device loss

On a device-loss result, the owner must:

- close admission;
- cancel and await active work;
- stop and dispose the production composition;
- dispose the output port;
- dispatch buffer, stream, binding, and activation disposal;
- preserve window and dispatcher ownership;
- return a terminal device-loss result.

A new owner and source reconnection are required for recovery.

### Validation

Cross-platform tests must use injected dispatcher and native seams to prove:

- exact initialization order;
- policy rejection before probe／OS work;
- no delivery before stream configuration;
- all native work occurs under dispatcher access;
- one production scheduler owner only;
- initial generation binding;
- discontinuity output reconfiguration before delivery;
- retained `WouldBlock` and resize behavior;
- device loss stops production before Windows teardown;
- cancellation after destructive stop completes mandatory cleanup;
- partial construction unwinds in reverse order;
- window and dispatcher remain borrowed;
- stop is idempotent;
- no implicit runtime discovery, fallback, thread, queue, loop, or retry.

Controlled Windows validation remains a later explicit milestone after the cross-platform lifecycle implementation is green.

## Review-only scope

0.0.9x does not:

- add a lifecycle runtime class;
- change existing Windows output code;
- create a window or message loop;
- add a dispatcher implementation or worker thread;
- activate a physical Windows device;
- decode or present controlled real media;
- modify the approved manifest or FFmpeg license policy;
- bundle or download FFmpeg;
- claim audible or visible playback.

## Files added or updated

- `docs/adr/ADR-0134-approved-external-runtime-windows-lifecycle-activation-review.md`
- `docs/reviews/PCN-0142-APPROVED-EXTERNAL-RUNTIME-WINDOWS-LIFECYCLE-ACTIVATION-REVIEW.md`
- project milestone and navigation ledgers

No runtime source or tests are changed.

## Next milestone

0.0.9y — Finite Approved External Runtime and Windows Lifecycle Activation Boundary.
