# PCN-0131: Finite Awaited Decoded Completion Composition Validation

## Milestone

0.0.9m — Finite Awaited Decoded Completion Composition Boundary

## Reviewed scope

This validation covers the new `PlaybackPcpFlvAwaitedDecodeCompositionBoundary`, its generation wrapper, injected awaited settlement seam, focused fake-native harness, and synchronized architecture records.

The milestone composes the real bounded FLV parser with the 0.0.9k source-bound one-frame decode boundary. It does not activate a production FFmpeg generation factory or transfer decoded ownership into the scheduler.

## Runtime boundary

The implementation verifies that:

- one `PlaybackSourceBoundOneFrameDecodeBoundary` is the generation's sole packet-submission and receive authority;
- PCP payload remains callback-scoped and parser suffix advances only by exact consumed bytes;
- exact PCP channel, session, and packet sequence are checked before parser work;
- retained packet／flush relief stays inside the open callback or finalization operation;
- one relief cycle contains one receive, one awaited settlement, completion release, and one retry-only transition;
- repeated `EAGAIN` requires another separately budgeted cycle;
- the retained send is not retried while settlement is incomplete;
- ordinary receive and accepted-flush drain remain explicit one-frame caller operations outside the PCP callback;
- encoded finalization, per-stream flush acceptance, per-stream decoder EOF, and full decoder drain remain distinct;
- video and audio drain independently;
- only `Consumed` is accepted from the 0.0.9m settlement seam;
- exact source／decoder-generation／discontinuity-generation／media／stream／sequence／byte ownership is revalidated before and after awaiting settlement;
- one shared frame-count and managed-byte budget spans every relief cycle in one public operation;
- discontinuity disposes settlement before the old decode and native generation;
- terminal failures preserve a fixed public exception message and release ownership fail closed.

## Focused tests

Eleven cross-platform tests cover:

1. bootstrap plus interleaved immediate packet acceptance through one decode generation;
2. callback blocking while retained packet relief settlement remains incomplete;
3. retry only after successful settlement and completion release;
4. repeated `EAGAIN` as two one-frame／one-retry cycles;
5. settlement rejection without retained retry;
6. cumulative frame-budget exhaustion across repeated relief;
7. one ordinary decoded frame outside the PCP callback;
8. ordinary `NeedMoreInput` as a normal finite result;
9. explicit finalization followed by independent video／audio drain to decoder EOF;
10. cancellation during awaited settlement without retained retry;
11. discontinuity replacement cleanup ordering and fixed invalid-identity failure.

The fixture uses the real FLV header, AVC／AAC sequence headers, real bounded parser, parser-created source session, 0.0.9k boundary, and injected packet-send／native-receive／settlement outcomes. No native FFmpeg runtime is required by these tests.

## Corrections during validation

The first source-only CircleCI runs failed before tests. Because direct private CircleCI logs were unavailable, temporary diagnostic workflows encoded the first compiler and analyzer identifiers into independent GitHub status contexts.

The compiler diagnostic identified CS1026 in the new FLV test fixture: one closing parenthesis was missing from `BinaryPrimitives.WriteUInt32BigEndian`. The fixture was corrected and the compiler-only build then passed for runtime source, support, and tests.

The analyzer diagnostic identified CA2250 in the runtime cancellation-translation path. The composition intentionally accepts cancellation from subordinate fixed enums and linked stop state in addition to caller-token cancellation, and must latch cleanup before outward translation. A narrow documented file-scoped CA2250 setting was therefore added.

All temporary compiler-bit, analyzer-bit, split-stage, and broad analyzer-suppression configurations were removed. The repository's routine single CircleCI merge gate was restored byte-for-byte.

## Validation gate

The final branch head must pass the restored routine CircleCI job:

- `dotnet restore PeerCast-Nova.sln`;
- Release build with analyzers;
- the full test suite;
- `dotnet format --verify-no-changes`.

Before final documentation synchronization, isolated diagnostics confirmed:

- compiler build: passed;
- full test suite: passed;
- format verification: passed;
- analyzer build after the targeted rationale: passed.

The final-head routine result is recorded in the pull request before merge.

## Ownership review

Successful awaited settlement leaves no active decoded completion or relief retry. Failure and cancellation release the active managed frame before retained submission and native session cleanup. Generation replacement disposes settlement first, followed by decode-boundary cleanup and native audio-before-video disposal.

The parser owns the source session. The generation keeps only an exact non-owning source identity. The factory is disposed after generation and parser cleanup during terminal composition disposal.

## Scope guard

This validation does not claim:

- transferable decoded completion ownership;
- scheduler enqueue or generation-wide scheduler sequence assignment;
- scheduler delivery or output-device lifecycle;
- production ABI-gate acquisition or generation factory;
- backend activation;
- Windows output activation;
- FFmpeg bundling or redistribution;
- audible or visible real-media playback.

## Next milestone

0.0.9n — Decoded Completion Transfer and Scheduler Ingress Review.

The next review must define one-shot managed-frame detachment, accepted／rejected scheduler transfer, generation-wide cross-media sequence commit, native-context lifetime while scheduler ownership remains, partial transfer cleanup, and discontinuity replacement of queued old-generation frames before implementation.
