# PCN-0135: Finite Generation-matched Scheduler Delivery and Output Lifecycle Boundary

## Milestone

0.0.9q — Finite Generation-matched Scheduler Delivery and Output Lifecycle Boundary.

## Review target

Validate the implementation selected by ADR-0127:

- one exact source-bound decoder generation owns scheduler ingress and finite delivery;
- scheduler reservation remains live until the final delivered／dropped／retained result;
- matching video／audio FFmpeg contexts are selected from the same native session;
- first-frame `DecoderReset` is acknowledged exactly once after successful reset admission;
- active delivery ends before scheduler and native decoder cleanup;
- no second queue, resident output loop, hidden retry, or duplicate native ownership is introduced.

## Changed runtime boundaries

- `src/Nova.Playback/PlaybackGenerationMatchedSchedulerDeliveryBoundary.cs`
- `src/Nova.Playback/BoundedPlaybackScheduler.cs`

## Focused tests

- `tests/Nova.Playback.Tests/PlaybackGenerationMatchedSchedulerDeliveryBoundaryTests.cs`

The focused suite verifies:

1. canonical video delivery consumes one reservation and increments delivery accounting;
2. late video explicitly drops and increments drop rather than delivery accounting;
3. `WouldBlock` retains the exact scheduler-owned frame for a later explicit delivery;
4. the first replacement-generation `DecoderReset` performs timing／output reset once and is delivered as ordinary discontinuity;
5. cancelled reset retains the same frame and reset marker until a later successful admission;
6. late audio retains ownership and reports audio recovery required;
7. device loss retains ownership, enters reset-required state, and permits delivery after explicit reset;
8. transfer during active delivery does not detach or dispose the later completion;
9. asynchronous stop cancels active output, waits for reservation release, and disposes scheduler frames before generation operations;
10. undefined output results fault the generation without silently consuming ownership.

## Compatibility review

The scheduler's existing `Complete(bool)` reservation API remains available. `true` maps to typed delivered completion and `false` maps to retained completion. Existing Windows output scheduler adapters and late-aware boundaries therefore keep their established reservation behavior.

Typed dropped completion is internal and used by the 0.0.9q generation boundary. It removes and disposes the reservation while incrementing only scheduler drop accounting.

The original `RunOneAsync` path is unchanged and remains available for diagnostic and historical finite callers. It is not selected as the production exit for the new generation.

## Ownership and failure checks

- ingress owns the scheduler and exact decode-boundary lifetime;
- decode boundary remains the sole native-session owner;
- reservation-held bytes remain accounted while retained;
- cancellation, `WouldBlock`, initialization／binding／resize, audio recovery, clock-not-started, and device loss do not consume the frame;
- delivered and explicit drop complete the reservation exactly once;
- invalid identity, context loss, conversion failure, output failure, and undefined enums fail closed;
- `StopAsync` cancels and waits for active delivery before ingress disposal;
- ingress disposal drains scheduler ownership before decode/native ownership.

## Routine validation

The initial runtime and focused-test branch head passed the routine CircleCI gate without diagnostic configuration changes:

- dependency restore;
- Release build with analyzers;
- full test suite;
- formatting verification.

The final documentation head must pass the same gate before Ready for Review and squash merge.

## Scope guard

This milestone does not add:

- production ABI-gate acquisition or runtime-locator composition;
- production decoder generation factory;
- backend policy activation;
- D3D11／WASAPI device activation or window lifecycle;
- continuous playback pump;
- automatic output retry;
- FFmpeg bundling or redistribution approval;
- controlled audible／visible playback.

## Result

The finite generation-matched scheduler delivery and output lifecycle boundary is implemented and validated. The next milestone is 0.0.9r Production Decoder Generation Factory and Activation Review.
