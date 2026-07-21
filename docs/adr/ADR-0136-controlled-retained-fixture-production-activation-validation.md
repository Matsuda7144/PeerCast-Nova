# ADR-0136: Controlled Retained-Fixture Production Activation Validation

## Status

Accepted for milestone 0.0.9z.

## Context

0.0.9y added one finite application lifecycle above the exact approved or explicitly host-attested external-runtime production route and the existing Windows output ownership boundaries. Its injected tests proved policy-first construction, owner-thread dispatch, generation-matched output configuration, retained delivery, terminal device loss, cancellation, and production-first cleanup.

The remaining gap is a controlled integration proof using real FLV bytes and the real production composition. The lifecycle tests used a synthetic production snapshot, while the earlier 0.0.9f retained fixture stopped at the finite packet-submission state machine. The complete managed seam from real parser input through production generation creation, retained decode relief, scheduler transfer, lifecycle output admission, delivery retention, discontinuity replacement, and cleanup therefore still requires validation before real external-runtime activation.

This milestone must not require a physical Windows device, application window, real FFmpeg binary, native runtime lease, resident playback loop, second queue, hidden retry, or audible／visible playback claim.

## Decision

### Add one controlled production lifecycle fixture

Add a cross-platform test harness around the real `PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary` and real `PlaybackProductionPcpFlvCompositionBoundary`.

The harness uses:

- the real bounded FLV parser and parser-created source session;
- the real production composition and packet sequencing;
- the real source-bound one-frame decode boundary;
- the real decoded-completion transfer contract;
- one controlled generation root retaining exact managed-frame ownership;
- one application-style dispatcher with explicit owner access;
- copied binding, stream, and buffer snapshots for output admission.

Only physical Windows activation, concrete output processing, ABI-gate acquisition, runtime loading, and native FFmpeg receive are injected. The harness does not change production source behavior.

### Initial activation sequence

The controlled fixture must prove:

1. lifecycle policy and configuration preflight succeeds;
2. probe and owner-thread activation complete;
3. production composition is created;
4. a real FLV bootstrap packet creates one exact parser source session and decoder generation;
5. the lifecycle derives 320×240 BGRA32 video and 48 kHz stereo interleaved audio configuration from the real production snapshot;
6. output state is configured to decoder generation 0;
7. lifecycle state becomes `Ready` before delivery.

### Retained packet relief through scheduler ownership

The fixture then submits real interleaved video and audio FLV tags. The controlled native submission returns `EAGAIN` for the audio packet.

The real production path must perform exactly:

1. one retained packet admission;
2. one source-bound receive producing one managed audio frame;
3. one decoded-completion transfer into generation scheduler ownership;
4. one retained packet retry;
5. one final packet acceptance.

The test verifies one transfer, one retry, one retained frame, no second encoded or decoded queue, and no hidden loop.

### Retained delivery

The controlled generation returns `RetainedWouldBlock` for the first one-item delivery. Scheduler ownership remains unchanged. A later caller-driven delivery returns `AudioDelivered` and releases the retained frame.

The lifecycle must not retry delivery internally or consume the item on the retained result.

### Discontinuity replacement

A real discontinuity bootstrap packet must:

1. stop and dispose decoder generation 0;
2. reset the parser source;
3. create decoder／discontinuity generation 1;
4. cause lifecycle output reconfiguration from generation 0 to generation 1;
5. return to `Ready` only after exact generation agreement.

The controlled log must show old-generation stop completion before replacement generation creation.

### Cleanup

Normal stop remains production-first:

1. stop the real production composition and controlled generation owner;
2. dispose controlled output ownership;
3. preserve the borrowed application dispatcher and window token.

Cleanup remains idempotent and no physical Windows or FFmpeg owner is claimed.

## Verification

Two focused cross-platform tests cover:

1. real bootstrap activation, exact stream-derived configuration, retained packet relief, scheduler transfer, retained `WouldBlock`, and later committed audio delivery;
2. real discontinuity replacement, stop-old-before-create-new ordering, generation-matched output reconfiguration, production-first stop, and borrowed dispatcher preservation.

Routine validation uses restore, Release build with analyzers, the full solution test suite, and format verification.

## Rejected alternatives

- Repeating only synthetic production snapshots from 0.0.9y tests.
- Activating an unverified or downloaded FFmpeg binary in routine Linux CI.
- Claiming physical D3D11／WASAPI validation from copied output snapshots.
- Adding a second scheduler, encoded queue, decoded queue, or retained-frame copy.
- Hiding packet relief or delivery backpressure behind a loop or retry policy.
- Replacing the application dispatcher or window ownership contract.
- Treating controlled integration as audible or visible playback validation.

## Consequences

- The complete managed production seam from real FLV bytes through the finite application lifecycle is validated under controlled ownership.
- Exact stream-derived output configuration and discontinuity generation replacement are exercised through the real production composition.
- Retained packet and scheduler ownership remain explicit and caller-driven.
- Physical Windows output, real FFmpeg loading, and real-media native decode remain separate gates.

## Next

0.0.10a — Controlled Real External-Runtime Decode Activation Review.
