# PCN-0143: Finite Approved External Runtime and Windows Lifecycle Activation Boundary

## Milestone

0.0.9y

## Validation target

Validate the finite application lifecycle selected by ADR-0134 from exact external-runtime policy admission through Windows output probe, owner-thread activation, decoder-generation-matched output configuration, caller-driven delivery／resize／reset, terminal device loss, and production-first cleanup.

## Implemented surface

The milestone adds:

- `IPlaybackWindowsOutputDispatcher` and fixed invocation results;
- `PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary`;
- real lifecycle operations composing probe, activation, binding, stream, buffer, and production owners;
- an internal dispatcher-backed production generation adapter;
- focused normal, replacement, retained, cancellation, terminal, and partial-cleanup tests;
- ADR-0135 and this review.

## Ownership contract

`application → dispatcher／owner thread + window token`

`lifecycle → probe → activation → binding → stream → buffer`

`lifecycle → external runtime production composition → generation → delivery → scheduler ingress → scheduler frames`

`generation → source-bound decode → native H.264／AAC contexts → independent ABI gates／verified runtime leases`

The lifecycle borrows the dispatcher and window token. It owns no application window, dispatcher thread, message loop, source transport, or FFmpeg binary.

The existing generation-matched delivery remains the singular scheduler-output owner. The dispatcher-backed adapter changes only where output process and reset operations execute.

## Preflight assertion

Before probe, dispatcher invocation, runtime-path observation, or native access, the lifecycle validates:

- explicit fixed-drive runtime-directory syntax;
- positive opaque window token;
- canonical BGRA32 and interleaved S16／F32 output formats;
- bounded audio buffering;
- nonnegative packet sequence;
- external-only backend policy;
- exact built-in approval or explicit host-attested admission.

The policy-rejection test proves that neither probe nor dispatcher is touched.

## Initial lifecycle order

The validated order is:

1. pure preflight;
2. resource-free probe;
3. owner-dispatched activation;
4. binding／stream／buffer construction;
5. production composition construction;
6. caller-provided PCP／FLV input;
7. exact stream description and decoder generation;
8. dispatched video binding;
9. dispatched audio binding;
10. dispatched video initialization;
11. dispatched audio initialization;
12. `Ready` only after exact binding, stream, and buffer generation agreement.

Before stream description, one-item delivery returns `OutputConfigurationRequired` and does not enter scheduler delivery.

## Dispatcher assertion

The application dispatcher reports `HasAccess` only while an invocation is executing. Focused tests record that activation, initial／replacement configuration, resize, and teardown occur with access asserted.

The production generation operations preserve the standard source-bound decode, scheduler, timing, and reservation path. Video／audio processing and audio-then-video output reset are dispatched through the borrowed owner-thread contract.

No default dispatcher, background thread, queue, timer, message pump, or resident operation was added.

## Replacement generation order

When a new decoder generation appears, the lifecycle blocks delivery and verifies this order:

1. audio buffer reset;
2. video buffer reset;
3. audio stream reset;
4. video stream reset;
5. binding reset;
6. video bind;
7. audio bind;
8. video initialize;
9. audio initialize;
10. exact new generation marked output-ready.

The activation owner remains live. Production composition still enforces complete old-generation stop before parser reset and replacement generation creation.

## Retained scheduler ownership

The lifecycle maps production results without introducing automatic retry.

- `WouldBlock`, timing wait, initialization, binding, resize, audio recovery, clock-not-started, and reset-required remain caller-visible.
- A retained resize does not consume or retry the scheduler item.
- Explicit same-generation resize updates only video stream state.
- The caller performs the next one-item delivery attempt.

## Device-loss assertion

A device-loss state in binding, stream, or buffer snapshots is terminal.

The focused test proves:

1. delivery observes device loss;
2. lifecycle admission closes;
3. production stops first;
4. retained production/output-port ownership is released;
5. buffer is disposed;
6. stream is disposed;
7. binding is disposed;
8. activation is disposed;
9. later delivery returns the same terminal device-loss result;
10. the borrowed dispatcher and window remain outside cleanup ownership.

There is no in-place activation replacement, fallback adapter, runtime fallback, source reconnect, retained-frame migration, generation revival, or rollback.

## Cancellation and partial cleanup

The active-operation test starts one finite input operation, waits until subordinate work is active, invokes `StopAsync`, and proves:

- the linked stop token cancels the active operation;
- stop waits for operation serialization;
- production stop precedes output disposal;
- stop and disposal remain idempotent;
- the dispatcher remains live.

The activation-failure test proves:

- probe completes;
- activation rejection creates no production composition;
- only acquired output ownership is unwound;
- later disposal does not repeat output destruction.

## Focused tests

`PlaybackApprovedExternalRuntimeWindowsLifecycleBoundaryTests` covers:

1. policy rejection before probe and dispatch;
2. initial probe／activation／production order;
3. blocked delivery before stream description;
4. initial output configuration before delivery;
5. fixed replacement-generation reset／reconfiguration order;
6. retained resize and explicit retry;
7. terminal device loss and teardown order;
8. idempotent stop preserving borrowed ownership.

`PlaybackApprovedExternalRuntimeWindowsLifecycleFailureTests` covers:

9. cancellation of an active operation before production-first cleanup;
10. activation-failure partial cleanup without production creation.

The tests are cross-platform and inject dispatcher and lifecycle operations. They do not claim physical COM, D3D11, WASAPI, window, or real FFmpeg activation.

## Analyzer history

Initial Release analysis identified only:

- `CA1068` on the private lifecycle serialization helper;
- `CA1859` on the interface-typed partial-construction cleanup local;
- `CA1861` on short inline order expectations in focused tests.

Each diagnostic has a narrow file-scoped rationale in `.editorconfig`. No rule is disabled globally. Temporary diagnostic workflow, trigger, log, and CircleCI changes were removed; routine CircleCI configuration is restored byte-for-byte.

## Routine validation

The source and focused-test head passed:

- `dotnet restore PeerCast-Nova.sln`;
- `dotnet build PeerCast-Nova.sln --configuration Release --no-restore` with analyzers;
- `dotnet test PeerCast-Nova.sln --configuration Release --no-build --no-restore`;
- `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`.

The final documentation head must pass the same routine gate before Ready for Review and squash merge.

## Scope guard

This milestone does not:

- create the Windows application window or message loop;
- provide a playback-owned dispatcher implementation;
- search, download, extract, install, update, mirror, or bundle FFmpeg;
- convert host attestation into Nova approval;
- add another scheduler or encoded／decoded queue;
- add a resident decode or delivery loop;
- poll output capacity or retry retained results internally;
- recreate output devices after device loss;
- reconnect the source automatically;
- validate a retained real FFmpeg fixture through the new lifecycle;
- claim controlled real-media decode, physical output, or audible／visible playback.

## Next milestone

0.0.9z — Controlled Retained-Fixture Production Activation Validation.
