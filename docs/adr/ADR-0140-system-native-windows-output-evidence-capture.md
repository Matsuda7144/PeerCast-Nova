# ADR-0140: System Native Windows Output Evidence Capture

## Status

Accepted for milestone 0.0.10d Phase 2 counter plumbing.

## Context

ADR-0139 introduced a one-shot Phase 1 admission gate for sanitized physical Windows output evidence. The gate intentionally rejects injected provenance and requires exact runtime, fixture, generation, present／WASAPI acknowledgement, retry-policy, and cleanup evidence.

The production Windows output path already reaches `IDXGISwapChain.Present` and WASAPI `ReleaseBuffer／Start`, but its existing public snapshot exposes only output state, generation, and whether audio has started. A controlled physical runner therefore has no deterministic repository-owned counters that can be passed into ADR-0139 without reconstructing or manually asserting native progress.

The counter source must not change native ownership or create a second output route. It must preserve the existing finite caller-driven processing result and only observe the returned native result.

## Decision

Wrap the production `SystemPlaybackWindowsOutputNativeBufferProcessor` in one `PlaybackWindowsOutputNativeEvidenceProcessor` created by `PlaybackWindowsOutputBufferProcessingBoundary` on supported Windows systems.

The wrapper delegates exactly one existing video or audio operation and returns the exact delegated result. It adds no retry, fallback, queue, scheduler, device recreation, thread, timer, or resource ownership.

### Provenance

The production wrapper records `PlaybackControlledWindowsOutputEvidenceSource.SystemWindows`.

Injected processors are not automatically wrapped. Focused tests may explicitly construct an `Injected` wrapper, and the existing Phase 1 admission gate rejects that provenance.

### Video counters

For each delegated video operation, the wrapper records:

- processing／present-path attempt count;
- successful present acknowledgement count;
- aggregate accepted frame bytes;
- native `WouldBlock` count;
- resize-required count;
- device-loss count;
- the first successfully acknowledged pixel format, dimensions, stride, and generation.

A later successful acknowledgement with different video configuration latches a permanent configuration-conflict flag. The native result is not changed; the eventual evidence capture fails closed.

### Audio counters

For each delegated audio operation, the wrapper records:

- processing／WASAPI-write-path attempt count;
- successful write acknowledgement count;
- aggregate accepted frame and byte counts;
- native `WouldBlock` count;
- successful first-start acknowledgement count;
- device-loss count;
- the first successfully acknowledged sample format, sample rate, channel count, and generation.

A `WouldBlock` result does not consume a first-start acknowledgement. A later successful call with `startStream=true` records the first start. Configuration changes latch a permanent conflict.

### Overflow

All counters are bounded signed 64-bit values. Arithmetic overflow latches a permanent overflow flag without changing the delegated native result. Overflowed evidence cannot pass the capture adapter.

### Snapshot connection

`PlaybackWindowsOutputBufferSnapshot` gains an additive init-only `NativeEvidence` property. Existing positional construction remains source compatible. Unsupported and ordinary injected processors expose the default empty snapshot.

The existing approved-runtime lifecycle already carries the buffer snapshot, so no second lifecycle or output route is added.

### Phase 1 adapter

`PlaybackControlledWindowsOutputCaptureBoundary` converts one buffer snapshot plus finite lifecycle counters and cleanup evidence into the exact ADR-0139 evidence type, then calls `PlaybackControlledWindowsOutputEvidenceBoundary.ValidateAndConsume`.

The adapter rejects by construction when:

- native counters overflowed;
- video or audio configuration changed;
- captured native generation or buffer generation differs from output generation;
- native `WouldBlock` count differs from caller-visible retained-`WouldBlock` count.

It does not bypass or duplicate the Phase 1 admission rules.

## Verification

Focused cross-platform tests prove:

- video success, `WouldBlock`, resize, and device-loss accounting;
- audio `WouldBlock`, write acknowledgement, aggregate frames／bytes, and exactly one first-start acknowledgement;
- configuration-conflict latching without changing native results;
- successful video and audio conversion through the Phase 1 gate;
- injected provenance rejection;
- generation／configuration and retained-`WouldBlock` contradiction rejection;
- buffer-snapshot evidence carriage and unchanged reset delegation.

The tests inject native results and validate counter plumbing only. They do not execute COM, D3D11, DXGI, WASAPI, FFmpeg, a real window, or audible／visible playback.

## Consequences

The approved Windows output route now owns deterministic sanitized counter plumbing through the same buffer snapshot already carried by the finite lifecycle.

Physical video and audio runs still require an explicit Windows 11 runner, application-owned real window／message loop／dispatcher, exact approved runtime and fixture, and observed cleanup evidence. This ADR does not claim that those runs occurred.
