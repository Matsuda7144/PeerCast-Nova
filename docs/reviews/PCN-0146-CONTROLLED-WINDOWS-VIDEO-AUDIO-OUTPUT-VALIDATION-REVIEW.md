# PCN-0146: Controlled Windows Video／Audio Output Validation Review

## Milestone

0.0.10c

## Review result

`ApprovedForControlledPhysicalOutputValidation`

## Reviewed baseline

The review accepts the following existing boundaries as the only production path for the next validation:

- `PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary`;
- `IPlaybackWindowsOutputDispatcher`;
- `PlaybackWindowsOutputActivationBoundary`;
- `PlaybackWindowsOutputResourceBindingBoundary`;
- `PlaybackWindowsOutputStreamInitializationBoundary`;
- `PlaybackWindowsOutputBufferProcessingBoundary`;
- `PlaybackGenerationMatchedSchedulerDeliveryBoundary`;
- the exact approved external FFmpeg runtime and controlled FLV fixture used by 0.0.10b.

No second scheduler, output adapter, decoder owner, device-recovery path, or resident loop is approved.

## Ownership decision

The application owns:

- the real window and message loop;
- the owner-thread dispatcher;
- the lifetime outside the controlled validation call.

The playback lifecycle owns:

- probe;
- physical COM／D3D11／DXGI／WASAPI activation;
- binding, stream, and buffer boundaries;
- production composition and active decoder generation;
- finite delivery and cleanup operations.

The application window and dispatcher remain borrowed and must still be live after playback cleanup.

## Required controlled environment

The implementation validation must identify one Windows 11 physical runner and must use:

- one explicit fixed-drive runtime directory;
- `btbn.ffmpeg.20260721.win64.lgpl-shared` or the then-current exact built-in approved identity only after a separately recorded manifest refresh;
- one retained controlled fixture with fixed digest;
- one positive real window token;
- one dispatcher that proves owner-thread access for activation, configuration, delivery, resize／reset, and teardown.

The runtime and fixture remain repository-external. No external binary or media payload may enter Git, build output, publish output, CI artifact, installer, release, or cache publication.

## Video acceptance evidence

A video run is accepted only if it records:

1. successful physical output probe and owner-thread activation;
2. binding and initialization at the exact active decoder generation;
3. exact width, height, pixel format, and stride admission;
4. at least one caller-driven scheduler delivery reaching the real video buffer path;
5. a physical present acknowledgement from the existing output boundary;
6. no hidden retry when presentation reports backpressure;
7. explicit same-generation resize behavior, if resize is exercised;
8. production-first, owner-thread cleanup;
9. no device recreation, adapter fallback, or old-generation revival.

A screenshot may support the record but cannot replace deterministic delivery and present evidence.

## Audio acceptance evidence

An audio run is accepted only if it records:

1. successful physical endpoint activation on the owner thread;
2. exact format, sample rate, channel count, and buffer-duration admission;
3. at least one caller-driven scheduler delivery reaching the real audio buffer path;
4. deterministic accepted-byte or frame progress through WASAPI;
5. first-start state and later buffer progress;
6. caller-visible `WouldBlock` retention without an internal retry loop;
7. production-first, owner-thread cleanup;
8. no endpoint fallback, device recreation, or old-generation revival.

Operator-observed audible output is supporting evidence only.

## Sequencing assertion

The approved sequence is:

1. pure policy and configuration preflight;
2. probe;
3. owner-thread activation;
4. production composition creation;
5. parser-created exact stream description;
6. decoder generation creation;
7. video bind;
8. audio bind;
9. video initialize;
10. audio initialize;
11. finite caller-driven delivery;
12. explicit resize／reset only after caller-visible results;
13. production stop;
14. buffer → stream → binding → activation teardown on the owner thread;
15. preservation of the borrowed dispatcher and window.

Video and audio may be validated in separate runs. A combined run is deferred until both independent paths pass.

## Failure policy

The first identity, generation, ownership, format, physical activation, buffer-accounting, present, dispatcher, cancellation, or cleanup contradiction fails closed.

Failure must not trigger:

- runtime search, download, extraction, installation, fallback, or replacement;
- graphics-adapter or audio-endpoint fallback;
- source reconnect;
- device recreation;
- generation rollback or revival;
- retained-frame migration;
- hidden retry, polling, or a resident pump.

Cleanup still runs production-first and must remain idempotent.

## Evidence retention

The repository may retain only bounded and sanitized text evidence:

- milestone and result;
- approved runtime build ID;
- fixture digest;
- sanitized output configuration;
- operation, delivery, present, accepted-byte, and cleanup counters;
- fixed result enums and ordering logs;
- confirmation that no external binary or payload was published.

Do not retain absolute local paths, device identifiers, raw decoded bytes, packet bytes, native pointers, handles, or private runner details.

## Scope guard

This review does not implement or claim:

- physical D3D11 presentation;
- physical WASAPI audio;
- a Windows application window or dispatcher implementation in Nova;
- continuous or long-running playback;
- synchronization quality;
- device recovery;
- GUI, installer, recorder, or public release readiness;
- FFmpeg redistribution.

## Implementation authorization

A later 0.0.10d branch may add only the minimum controlled harness, sanitized evidence capture, and focused regression coverage necessary to execute this review. Production ownership and sequencing must remain unchanged unless a new ADR explicitly supersedes ADR-0138.

## Next milestone

0.0.10d — Controlled Physical Windows Video／Audio Output Validation.