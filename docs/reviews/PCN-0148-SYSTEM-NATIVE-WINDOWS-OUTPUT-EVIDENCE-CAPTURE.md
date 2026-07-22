# PCN-0148: System Native Windows Output Evidence Capture

## Milestone

0.0.10d Phase 2 counter plumbing

## Review result

`SystemCounterPathImplementedPhysicalRunPending`

## Implemented surface

The branch adds:

- `PlaybackWindowsOutputNativeEvidenceSnapshot`;
- `PlaybackWindowsOutputNativeEvidenceProcessor`;
- additive `PlaybackWindowsOutputBufferSnapshot.NativeEvidence`;
- `PlaybackControlledWindowsOutputCaptureContext`;
- `PlaybackControlledWindowsOutputCaptureBoundary`;
- focused counter and Phase 1 adapter tests;
- ADR-0140 and this review record.

The production Windows buffer boundary now wraps only the existing system native processor. Unsupported and ordinary injected processor routes remain unchanged and expose no physical-system evidence.

## Delegation assertion

Each video or audio call performs exactly one delegated processor call and returns its exact fixed result. Evidence recording occurs around that call and does not alter:

- scheduler reservation ownership;
- decoded frame ownership;
- D3D11／DXGI resource ownership;
- WASAPI resource ownership;
- audio-start state transition;
- resize or device-loss mapping;
- reset or teardown delegation.

No hidden retry, fallback, queue, second scheduler, device recreation, or background operation is introduced.

## Video evidence assertion

The native snapshot records:

- attempt count;
- successful present acknowledgement count;
- aggregate accepted bytes;
- native `WouldBlock` count;
- resize count;
- device-loss count;
- first acknowledged BGRA configuration and generation;
- configuration-conflict state.

Only `Processed` contributes present acknowledgement and bytes. `WouldBlock`, resize, device loss, format rejection, failure, or exception does not falsely acknowledge a presented frame.

## Audio evidence assertion

The native snapshot records:

- write-path attempt count;
- successful write acknowledgement count;
- aggregate accepted frames and bytes;
- native `WouldBlock` count;
- successful first-start acknowledgement count;
- device-loss count;
- first acknowledged PCM configuration and generation;
- configuration-conflict state.

A `WouldBlock` call with `startStream=true` does not consume first-start evidence. Only a successful delegated call acknowledges start.

## Provenance assertion

The default supported-Windows production constructor uses `SystemWindows` provenance.

Injected processors are not automatically upgraded to system provenance. The focused injected-wrapper test reaches the same capture adapter and is rejected by the Phase 1 gate as `SourceRejected`.

## Phase 1 connection assertion

The capture boundary maps the buffer snapshot and finite lifecycle counters into `PlaybackControlledWindowsOutputEvidence` and invokes the existing one-shot admission boundary.

It fails closed by emitting invalid configuration evidence when:

- any counter overflowed;
- the acknowledged native configuration changed;
- native generation differs from output generation;
- the buffer generation differs from output generation;
- native `WouldBlock` count differs from caller-visible retained-`WouldBlock` count.

The adapter cannot accept evidence independently from ADR-0139.

## Focused tests

`PlaybackWindowsOutputNativeEvidenceCaptureTests` covers:

1. video processed／would-block／resize／device-loss counters;
2. audio would-block followed by one start and later writes;
3. video and audio configuration-conflict latching;
4. successful system video capture through Phase 1;
5. successful system audio capture through Phase 1;
6. injected provenance rejection;
7. configuration and retained-would-block contradiction rejection;
8. buffer snapshot carriage and reset delegation.

## Scope guard

This phase does not:

- create an application window or message loop;
- implement an application dispatcher;
- activate a real D3D11 or WASAPI device in CI;
- load the external FFmpeg runtime;
- read or retain a controlled fixture;
- collect device, adapter, endpoint, HWND, pointer, handle, packet, or decoded-byte identifiers;
- claim visible video, audible audio, or complete physical validation;
- change production stop or owner-thread output teardown behavior.

## Remaining physical work

An explicit Windows 11 physical runner must still provide the application-owned real window and dispatcher, run independent finite video and audio validation using the exact approved runtime／fixture, capture the pre-cleanup buffer snapshot, prove production-first owner-thread cleanup, and submit both records through the Phase 1 gate.

Until accepted `SystemWindows` video and audio records exist, 0.0.10d remains in progress.
