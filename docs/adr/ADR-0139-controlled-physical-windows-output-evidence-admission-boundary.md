# ADR-0139: Controlled Physical Windows Output Evidence Admission Boundary

## Status

Accepted for milestone 0.0.10d Phase 1.

## Context

ADR-0138 and PCN-0146 authorize a later finite physical Windows video／audio validation using the existing approved external-runtime lifecycle, application-owned window／dispatcher, D3D11／DXGI presentation path, and WASAPI render path.

The repository already contains native operations that reach `IDXGISwapChain.Present` and WASAPI buffer release／start. It does not yet have a single fail-closed contract that distinguishes physical-system evidence from injected evidence and verifies the exact runtime, fixture, generation, acknowledgement counters, retry policy, and cleanup order before a run can be recorded as accepted.

Physical execution is environment-dependent and cannot be inferred from Linux CI or injected tests. The first implementation step must therefore define a one-shot sanitized evidence gate without claiming that a physical run occurred.

## Decision

Add `PlaybackControlledWindowsOutputEvidenceBoundary` as a one-shot admission boundary for one independent video or audio validation record.

The boundary accepts only evidence whose source is explicitly `SystemWindows`. `Injected`, undefined, or absent sources are rejected and can never be interpreted as physical output proof.

The active runtime build ID must exactly equal `PlaybackFfmpegBackendCandidate.BuildId`. The observed fixture SHA-256 must match the expected fixed digest supplied when the boundary is created. Decoder and output generations must be nonnegative and equal.

### Video admission

A video record requires:

- BGRA32 with positive width and height;
- exact `width * 4` stride;
- positive present attempts and acknowledgements;
- one physical present acknowledgement for every committed video delivery;
- exact accepted-byte accounting for all acknowledged frames;
- no device-loss result in the accepted run.

A screenshot remains optional supporting evidence and is not part of this data contract.

### Audio admission

An audio record requires:

- interleaved signed-16 or float-32 PCM;
- positive sample rate and channel count;
- buffer duration within the existing Windows stream boundary limits;
- positive WASAPI write attempts and acknowledgements;
- exact accepted-frame and accepted-byte accounting;
- exactly one first-start acknowledgement;
- one write acknowledgement for every committed audio delivery;
- no device-loss result in the accepted run.

Operator-observed audible output remains supporting evidence only.

### Retry and ownership admission

The record must contain finite caller-driven delivery counts. Delivered and retained-`WouldBlock` counts must fit inside the declared attempt count.

Any hidden retry, device fallback, or device recreation count rejects the record. The boundary adds no loop, scheduler, queue, retry, fallback, device recovery, source reconnect, or runtime discovery.

### Cleanup admission

Cleanup evidence must prove this strict order:

1. production stop;
2. buffer disposal;
3. stream disposal;
4. binding disposal;
5. activation disposal.

Output teardown must occur on the owner thread. The borrowed application window and dispatcher must remain preserved.

### One-shot settlement

The first accepted or rejected record closes the boundary. A second record returns `AlreadyCompleted`; rejected evidence cannot be corrected in place. A new controlled run requires a new boundary.

The boundary retains only immutable sanitized metadata and counters. It owns no runtime directory, media payload, decoded bytes, device identifier, native pointer, handle, window, dispatcher, or OS resource.

## Verification

Focused cross-platform tests must prove:

- successful video admission;
- successful audio admission and exactly one first start;
- rejection of injected evidence;
- exact runtime, fixture, and generation matching;
- rejection of hidden retry, fallback, and recreation;
- deterministic present／buffer acknowledgement requirements;
- production-first owner-thread cleanup and borrowed-owner preservation;
- invalid expected digest, one-shot closure, and disposal behavior.

These tests validate the evidence contract only. They do not activate D3D11, DXGI, WASAPI, FFmpeg, a real application window, or physical playback.

## Consequences

0.0.10d now has a code-level fail-closed acceptance contract for sanitized physical-run evidence. A later Phase 2 must connect deterministic counters from the existing system native output path and execute independent video and audio runs on an explicit Windows 11 physical runner.

No physical output validation is claimed by this ADR or its Linux CI tests.
