# PCN-0127: Finite Awaited PCP-to-FLV Composition Validation

## Scope

Validate the 0.0.9i serialized awaited boundary selected by ADR-0118, from one callback-scoped PCP channel packet through the one-event FLV parser facade and finite source-bound packet-submission state machine.

## Validated implementation

`PlaybackPcpFlvCompositionBoundary`:

- implements `IPcpChannelPacketConsumer` in `Nova.Playback`;
- validates exact PCP channel ID, channel-session ID, and packet sequence;
- retains no PCP packet or payload memory after `ConsumeAsync` returns;
- advances one callback-local offset only by parser `ConsumedByteCount`;
- supports multiple complete FLV records in one callback and one incomplete record in bounded parser carry;
- processes each source-owned parser completion before resuming the same callback suffix;
- creates one exact source-bound decoder generation only after stream-description completion;
- owns the finite submission state machine and native session as one replaceable generation;
- retains the parser-created source session only as exact non-owning identity;
- bounds parser operations and decoder `Advance` transitions for every public operation;
- handles retained packet and retained flush `EAGAIN` only after explicit frame progress;
- fails closed when receive makes no progress, work limits are exceeded, parser accounting contradicts the callback, or identity／sequence／generation／submission validation fails;
- provides separate explicit `CompleteInputAsync` and never treats transport state or empty packet input as EOS;
- replaces the old generation before parser discontinuity reset;
- exposes fixed enum state and counters plus a fixed-message terminal exception;
- cleans the state machine before the native session, then parser, then generation factory;
- leaves production generation construction and activation outside the milestone.

## Focused tests

Thirteen cross-platform tests validate:

1. bootstrap, video, and audio records in one PCP callback with suffix preservation;
2. split FLV input using only bounded parser carry and no retained caller memory;
3. wrong channel identity and fixed terminal failure;
4. rejected non-monotonic PCP packet sequence;
5. exact generation creation before media submission and rejected generation creation;
6. packet `EAGAIN`, one frame receive, and successful retained retry;
7. decoder no-progress as terminal failure without callback success;
8. repeated retained work bounded by the configured decoder-advance limit;
9. explicit video-then-audio final EOS and idempotent finalization;
10. empty input and cancellation not being inferred as EOS;
11. discontinuity disposing the old generation and parser-created source before replacement;
12. idempotent generation／parser／factory cleanup ordering;
13. flush `EAGAIN`, explicit frame progress, retained flush retry, and completed finalization.

## Corrections during validation

The initial implementation head failed the routine CircleCI build because .NET 10 CA1513 required `ObjectDisposedException.ThrowIf` for the new disposed-state guards. Compiler and analyzer stages were temporarily separated to isolate the warning family. The manual guards were replaced with the standard helper, and the repository's original single routine CircleCI gate was restored byte-for-byte before continued validation.

Static review also corrected parser-accounting failure so it enters terminal cleanup, rejected completion-free `NeedMoreInput` during finalization, and made post-finalization packet input fail closed through the same ownership path.

A final requirement review added the dedicated retained flush-backpressure test after the first green implementation head. The resulting implementation and thirteen-test head passed the restored routine CircleCI restore, Release build, full test suite, and format verification.

## Ownership findings

- PCP payload memory remains borrowed from the awaited callback and is never stored in the composition object.
- Bounded parser carry is the only copied input retained across callbacks.
- A parser completion remains the sole pending encoded owner during packet or flush backpressure.
- The decoder generation owns the submission state machine and native session but not the parser-created source session.
- Discontinuity releases retained submission and native ownership before parser source replacement.
- Terminal failure and cancellation release all composition-owned resources and prevent later input from being accepted.

## Exclusions

No production ABI-gate acquisition, native runtime activation policy, decoded-frame receive iteration, decoded-frame ownership, scheduler ingress, Windows output wiring, backend-factory connection, FFmpeg bundling／distribution, or audible／visible playback is added or claimed.

## Next review

0.0.9j should review bounded decoded-frame iteration and independent per-stream EOS drain while preserving the exact source generation, finite callback work, no-second-encoded-queue rule, and explicit finalization contract completed in 0.0.9i.
