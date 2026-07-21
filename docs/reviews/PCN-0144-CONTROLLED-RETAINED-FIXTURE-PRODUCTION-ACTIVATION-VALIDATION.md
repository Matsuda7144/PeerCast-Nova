# PCN-0144: Controlled Retained-Fixture Production Activation Validation

## Milestone

0.0.9z

## Validation target

Validate the complete managed application path from controlled real FLV bytes through the bounded parser, production composition, exact source-bound generation, retained decode relief, scheduler ownership, generation-matched lifecycle output admission, caller-driven delivery, discontinuity replacement, and production-first cleanup.

## Controlled surface

The validation uses the real:

- `PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary`;
- `PlaybackProductionPcpFlvCompositionBoundary`;
- bounded FLV parser and parser-created source session;
- source-bound one-frame decode boundary;
- decoded-completion one-shot transfer contract;
- production packet sequencing and replacement lifecycle.

The harness injects only:

- physical Windows probe／activation／binding／stream／buffer behavior;
- actual ABI-gate and runtime loading;
- native FFmpeg frame receive;
- final scheduler delivery outcome.

No production runtime source is changed.

## Initial activation assertion

A controlled bootstrap packet proves:

1. lifecycle initialization completes;
2. production composition is created after owner-thread activation;
3. the real parser creates one exact source session and generation 0;
4. the production snapshot exposes 320×240 H.264 video and 48 kHz stereo AAC audio configuration;
5. lifecycle output configuration uses generation 0;
6. lifecycle state becomes `Ready`;
7. dispatcher access is asserted for activation, output configuration, and cleanup.

## Retained packet assertion

The controlled interleaved fixture contains one video packet followed by one audio packet. Native packet submission is injected as:

- video packet: accepted;
- audio packet: frame drain required;
- one audio frame receive: copied;
- retained audio packet retry: accepted.

The real production composition proves:

- two packet submissions;
- exactly one receive-backed decoded completion;
- exactly one scheduler transfer;
- exactly one retained packet retry;
- one retained managed frame;
- no hidden polling or second queue.

## Retained delivery assertion

The first caller-driven delivery returns `RetainedWouldBlock`. The retained frame count remains one. The second caller-driven delivery returns `AudioDelivered`, releases the retained frame, and increments the production delivered-frame count exactly once.

No lifecycle-owned retry occurs between the two calls.

## Discontinuity assertion

A controlled discontinuity bootstrap proves:

1. generation 0 is stopped completely;
2. parser source ownership is replaced;
3. generation 1／discontinuity 1 is created only after old stop completion;
4. lifecycle output configuration changes from initial generation 0 to replacement generation 1;
5. lifecycle returns to `Ready` with output-ready generation 1;
6. old and replacement generations never overlap.

## Cleanup assertion

Normal lifecycle stop proves:

- production stop is recorded before output disposal;
- the active controlled generation is disposed;
- retained frame ownership is released;
- the production creator is disposed;
- the application dispatcher remains live and unowned by playback.

## Focused tests

`PlaybackControlledRetainedFixtureProductionActivationValidationTests` covers:

1. controlled bootstrap, exact stream-derived output configuration, retained packet relief, scheduler transfer, retained delivery, and final committed audio delivery;
2. controlled discontinuity replacement, stop-old-before-create-new ordering, exact output-generation reconfiguration, production-first stop, and dispatcher preservation.

## Scope guard

This validation does not:

- load or distribute FFmpeg;
- inspect a host runtime directory;
- acquire real ABI gates or native contexts;
- activate physical COM, D3D11, DXGI, WASAPI, or event handles;
- create an application window or message loop;
- add a resident decode／delivery pump;
- add another encoded or decoded queue;
- hide retained packet or output retry;
- claim real-media native decode, audible output, or visible rendering.

## Routine validation

The implementation head must pass:

- `dotnet restore PeerCast-Nova.sln`;
- `dotnet build PeerCast-Nova.sln --configuration Release --no-restore`;
- `dotnet test PeerCast-Nova.sln --configuration Release --no-build --no-restore`;
- `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`.

## Next milestone

0.0.10a — Controlled Real External-Runtime Decode Activation Review.
