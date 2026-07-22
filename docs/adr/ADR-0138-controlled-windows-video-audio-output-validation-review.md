# ADR-0138: Controlled Windows Video／Audio Output Validation Review

## Status

Accepted for milestone 0.0.10c review.

## Context

0.0.10b proved finite H.264／AAC native decode with the exact approved external FFmpeg runtime and controlled FLV fixture. Existing Windows output boundaries already separate probe, activation, resource binding, stream initialization, buffer processing, generation-matched delivery, explicit resize, terminal device loss, and owner-thread teardown.

The next risk is physical output evidence. A successful native decode does not prove that decoded video reaches a real D3D11 presentation target or that decoded audio reaches a real WASAPI endpoint. Likewise, successful injected output tests do not prove real COM apartment ownership, device creation, endpoint negotiation, event-driven buffer behavior, resize, or physical cleanup.

The validation must remain controlled and finite. It must not introduce a resident playback loop, hidden retry, fallback device selection, runtime discovery, source reconnect, GUI ownership, or a claim of general playback readiness.

## Decision

### Milestone boundary

0.0.10c is a review-only milestone. It defines the evidence and ownership gate for a later controlled Windows output validation. Runtime source behavior is not changed by this review.

The later validation will use:

- Windows 11 on one explicitly identified physical runner;
- the exact approved external FFmpeg runtime already accepted by the built-in manifest;
- the controlled retained FLV fixture and the finite production lifecycle;
- one application-owned owner-thread dispatcher;
- one application-owned positive window token backed by a real window and message loop;
- the existing D3D11／DXGI and WASAPI boundaries without a second output path.

### Video evidence

The controlled video proof must record:

1. owner-thread COM and D3D11／DXGI activation;
2. binding to the exact application window;
3. stream initialization for the exact decoder generation and dimensions;
4. at least one decoded frame accepted through the generation-matched production delivery path;
5. one real present result or an equivalent direct physical presentation acknowledgement from the existing boundary;
6. same-generation resize handling without consuming a retained scheduler item;
7. terminal device-loss behavior without in-place recreation;
8. owner-thread teardown after production stop.

The evidence must distinguish “frame submitted to the physical presentation boundary” from a subjective visual-quality claim. Screenshots are optional supporting evidence, not the sole proof.

### Audio evidence

The controlled audio proof must record:

1. owner-thread COM and default render-endpoint activation;
2. exact accepted sample format, sample rate, channel count, and buffer duration;
3. at least one decoded audio frame accepted through the generation-matched production delivery path;
4. event／buffer progress showing that bytes were accepted by the physical WASAPI path;
5. explicit handling of `WouldBlock` without hidden retry;
6. exact first-start behavior and later buffer progress;
7. terminal device-loss behavior without endpoint fallback or recreation;
8. owner-thread teardown after production stop.

Audible output may be noted as operator-observed supporting evidence, but deterministic buffer and lifecycle evidence remains required.

### Combined sequencing

The validation uses the existing lifecycle sequence:

1. pure preflight and policy admission;
2. output probe;
3. owner-thread activation;
4. production composition creation;
5. exact stream-description and decoder-generation creation;
6. video binding, audio binding, video initialization, audio initialization;
7. finite caller-driven input, receive, transfer, and one-item delivery operations;
8. explicit resize or reset only when caller-visible results require them;
9. production stop;
10. buffer, stream, binding, and activation teardown on the owner thread;
11. preservation of the application window, message loop, and dispatcher.

Video and audio may be validated in separate focused runs if that produces clearer evidence. A combined run is not required until both independent paths pass.

### Failure and artifact rules

Any mismatch in runtime identity, stream configuration, decoder generation, owner-thread access, physical activation, buffer accounting, present result, or cleanup order fails the validation.

The controlled run must not:

- search `PATH` or the registry for FFmpeg;
- download, extract, install, mirror, cache-publish, or bundle FFmpeg;
- commit the media fixture, runtime binaries, decoded payloads, native pointers, absolute local paths, or device identifiers;
- automatically choose another graphics adapter or audio endpoint after failure;
- reconnect the source, revive an old generation, migrate retained frames, or rollback ownership;
- add a resident loop, second scheduler, encoded queue, decoded queue, or hidden retry policy.

Only bounded text evidence, fixed counters, sanitized configuration, result enums, ordering logs, and non-sensitive hashes may be retained.

## Validation requirements

Before implementation is accepted, focused tests must continue to prove the injected contracts for owner-thread access, generation matching, retained results, resize, terminal device loss, cancellation, partial cleanup, and idempotent stop.

The physical validation record must separately identify:

- Windows version and runner role;
- approved runtime build ID;
- fixture identity and digest;
- video and audio configuration;
- finite operation counts and accepted delivery counts;
- retained／backpressure results;
- resize／reset events, when exercised;
- production-first and owner-thread cleanup order;
- absence of bundled or published external binaries.

## Rejected alternatives

- Treating 0.0.10b native decode as physical output validation.
- Treating cross-platform injected output tests as real D3D11／WASAPI evidence.
- Building a GUI before the physical boundaries are validated.
- Adding a playback-owned output thread or resident delivery loop.
- Automatically recreating devices or selecting fallbacks after device loss.
- Recording only “video was visible” or “audio was audible” without deterministic lifecycle evidence.
- Combining video and audio into one mandatory run before either path can be diagnosed independently.

## Consequences

The next implementation milestone can validate real Windows output without widening ownership or retry behavior. Physical output evidence remains distinct from general application readiness, GUI work, packaging, and long-running playback.

## Next

0.0.10d — Controlled Physical Windows Video／Audio Output Validation.