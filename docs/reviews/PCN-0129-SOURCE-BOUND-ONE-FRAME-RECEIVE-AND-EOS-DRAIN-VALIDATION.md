# PCN-0129: Source-bound One-frame Receive and EOS Drain Validation

## Scope

Milestone 0.0.9k implements the finite source-bound receive and per-stream EOS-drain boundary selected by ADR-0120. It preserves exactly one successful native receive as one owned managed decoded completion and coordinates retained packet／flush relief without a state-only receive followed by a second receive.

This milestone does not wire the new generation boundary into the 0.0.9i PCP composition, convert or enqueue real-FLV decoded completions, acquire production ABI gates, activate a decoder backend, wire Windows output, distribute FFmpeg, or claim audible／visible playback.

## Implemented evidence

- one boundary owns the exact opened native session and one dedicated finite packet-submission state machine;
- construction validates exact FLV source binding and H.264／AAC context roles;
- each public receive operation performs at most one callback-scoped native receive／managed copy;
- a successful receive produces one source／generation／stream／media-bound managed completion without retaining a native pointer;
- video and audio use independent checked decoded sequences committed only after completion construction and work-budget acceptance;
- only one decoded completion may remain active across the generation;
- ordinary post-accept receive returns normal `NeedMoreInput` without faulting;
- pending packet and pending flush relief expose a copied frame before any encoded retry;
- releasing that completion enables a later operation to stage frame progress and perform at most one retry;
- repeated `EAGAIN` requires another one-frame operation and another one-retry operation;
- relief decoder EOF completes only the selected stream without retry;
- accepted video and audio flushes drain independently one frame per call until each decoder EOF;
- flush-drain and relief `NeedMoreInput` fail as explicit no progress;
- one-frame options and reusable caller-owned budgets bound managed bytes and cumulative frame count;
- cancellation before or after copy advances no decoded sequence and clears untransferred ownership;
- undefined, contradictory, metadata／data／allocation／native, frame-identity, no-progress, and budget failures terminate fail closed;
- cleanup clears the active managed frame, then retained submission ownership, then native audio and video contexts;
- the source session remains non-owned.

## Focused validation

Eighteen cross-platform focused tests cover:

1. exact source, generation, stream, media, and context-role validation before native work;
2. one callback-scoped native receive per public operation;
3. exact managed-frame ownership and source-bound completion identity;
4. one-active-completion backpressure;
5. independent video／audio decoded sequences;
6. ordinary `NeedMoreInput`;
7. multiple frames requiring multiple finite operations;
8. reusable frame-count and managed-byte budgets;
9. cancellation before and after copy;
10. contradictory frame identity and undefined native result cleanup;
11. disposed-source rejection before native work;
12. retained packet frame-before-retry ordering;
13. retained flush frame-before-retry ordering;
14. repeated packet backpressure with one frame and one retry per step;
15. relief no-progress and relief decoder EOF;
16. independent video／audio flush drain with frames before EOF;
17. flush-drain no-progress and copied-frame budget rejection;
18. idempotent decoded-frame-first, submission-second, native audio-before-video cleanup.

## Corrections during validation

The first routine build was split into compiler, analyzer, test, and format jobs. Production and test compilation succeeded, while the analyzer job isolated CA1068 on the public receive signature. The optional reusable cumulative budget intentionally remains a named trailing argument after the commonly used optional cancellation token, so a file-scoped documented analyzer setting was added rather than changing the ordinary call shape.

Focused test splitting then isolated one EOS-drain fixture error. The helper always requested `PacketsAvailable`, but `PlaybackBoundedDemuxSession` requires `EndOfStream` when the second per-stream EOS completes both streams. The helper now retries the exact event with the final source result only after the non-final form is rejected without state commit.

All diagnostic CircleCI configurations were removed after isolation and the routine single merge gate was restored byte-for-byte.

## Required final validation

The final documentation head must pass the routine CircleCI gate:

- restore;
- Release build with analyzers;
- full test suite;
- format verification.

The final diff must contain only the intended runtime source, focused tests, targeted documented analyzer configuration, ADR／PCN, and synchronized progress documents. No unresolved review thread may remain before merge.

## Deferred

- composition of this boundary into `PlaybackPcpFlvCompositionGeneration`;
- awaited decoded-completion consumption and real-FLV conversion／scheduler ingress;
- production generation factory and ABI-gate acquisition;
- backend and Windows output activation;
- discontinuity-time `avcodec_flush_buffers` policy;
- MP4／fragmented MP4 parsing;
- runtime bundling／distribution;
- controlled audible／visible playback.
