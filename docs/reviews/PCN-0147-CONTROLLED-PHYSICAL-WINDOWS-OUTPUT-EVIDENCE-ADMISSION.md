# PCN-0147: Controlled Physical Windows Output Evidence Admission

## Milestone

0.0.10d Phase 1

## Review result

`HarnessImplementedPhysicalRunPending`

## Implemented surface

The branch adds:

- `PlaybackControlledWindowsOutputEvidenceBoundary`;
- fixed validation kind, source, state, and result enums;
- sanitized video, audio, cleanup, aggregate-evidence, and snapshot records;
- one-shot accepted／rejected settlement;
- focused evidence-admission tests;
- ADR-0139 and this review record.

No existing production output ownership, scheduler, native call, lifecycle sequence, or cleanup implementation is replaced.

## Physical-source assertion

Only `PlaybackControlledWindowsOutputEvidenceSource.SystemWindows` may pass. `Injected`, `None`, and undefined values fail before output-specific acknowledgement checks.

This distinction prevents cross-platform fakes and injected native operations from being recorded as physical D3D11／WASAPI evidence.

## Identity assertion

Accepted evidence must contain:

- exact active built-in runtime build ID;
- exact expected controlled-fixture SHA-256;
- nonnegative decoder and output generations;
- exact decoder／output generation equality.

The evidence contract contains no runtime path, archive path, device ID, endpoint ID, adapter ID, HWND value, native pointer, handle, encoded bytes, decoded bytes, or media payload.

## Video assertion

Accepted video evidence proves:

- BGRA32;
- positive dimensions;
- exact four-byte stride;
- positive present attempt and acknowledgement counts;
- present acknowledgement count equal to committed delivery count;
- exact aggregate acknowledged frame bytes;
- zero device-loss events for the accepted run.

This is deterministic acknowledgement evidence. It is not a visual-quality or synchronization claim.

## Audio assertion

Accepted audio evidence proves:

- supported interleaved PCM format;
- positive sample rate and channel count;
- bounded stream buffer duration;
- positive WASAPI write attempt and acknowledgement counts;
- write acknowledgement count equal to committed delivery count;
- exact aggregate accepted frames and bytes;
- exactly one first-start acknowledgement;
- zero device-loss events for the accepted run.

This is deterministic buffer-progress evidence. It is not an audio-quality or synchronization claim.

## Retry assertion

Delivery attempts, committed deliveries, and retained `WouldBlock` counts are bounded and internally consistent.

The following must remain zero:

- hidden retry count;
- device fallback count;
- device recreation count.

The gate performs no retry and cannot revive or replace ownership after rejection.

## Cleanup assertion

Accepted cleanup evidence has strictly increasing operation order:

`production stop → buffer dispose → stream dispose → binding dispose → activation dispose`

It also confirms owner-thread teardown and preservation of the borrowed application window and dispatcher.

## Focused tests

`PlaybackControlledWindowsOutputEvidenceBoundaryTests` covers:

1. one-shot system-Windows video acceptance;
2. system-Windows audio acceptance with one first start and exact bytes;
3. injected-source rejection;
4. runtime, fixture, and generation mismatch rejection;
5. hidden retry, fallback, and recreation rejection;
6. deterministic video／audio acknowledgement rejection;
7. cleanup order, owner-thread, and borrowed-owner rejection;
8. invalid expected digest, disposal, and post-disposal rejection.

## Scope guard

This phase does not:

- create or own a real application window or message loop;
- provide an application dispatcher implementation;
- execute the existing physical D3D11／DXGI present call;
- execute the existing physical WASAPI write／start call;
- load or distribute FFmpeg;
- read a runtime directory or fixture;
- claim visible video, audible audio, physical playback, or complete 0.0.10d validation;
- add a resident loop, hidden retry, second scheduler, queue, fallback, or device recovery.

## Remaining physical work

Phase 2 must connect system-native deterministic counters to this gate without widening ownership, then execute independent finite video and audio runs on an explicit Windows 11 physical runner.

Until those runs produce accepted `SystemWindows` evidence, 0.0.10d remains in progress.
