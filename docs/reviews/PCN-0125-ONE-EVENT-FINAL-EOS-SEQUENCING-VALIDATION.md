# PCN-0125: One-event Final EOS Sequencing Validation

## Scope

Validate the 0.0.9g finite parser facade selected after the controlled retained FLV fixture exposed the ordinary dual-EOS finalization gap.

## Validated implementation

`PlaybackOneEventFinalEosFlvParser`:

- owns one existing bounded FLV parser;
- delegates ordinary bootstrap and packet parsing without changing payload or timing behavior;
- tracks next per-stream sequence only from successfully constructed parser completions;
- latches final input when all bytes in the final operation are consumed;
- accepts the final signal on an empty operation or alongside the last complete packet;
- emits video then audio EOS as separate completions when both streams remain open;
- emits only the remaining EOS when video already ended explicitly;
- creates final completions through the exact parser-created source session;
- preserves source-session `ActiveCompletion` backpressure;
- uses one completion as the only pending owner and stores no copied EOS queue;
- faults closed on truncated final carry or later non-empty input;
- resets sequencing only on explicit discontinuity;
- returns completion-free terminal `EndOfStream` after both EOS completions are released.

## Focused tests

Six tests validate:

1. source-owned video EOS followed by source-owned audio EOS;
2. source-session rejection of another completion while each EOS completion is active;
3. explicit AVC EOS followed by only final audio EOS;
4. final signal attached to the last video packet with the next video sequence preserved;
5. truncated final carry and fault latching;
6. discontinuity generation reset and both final EOS completions accepted by the 0.0.9e state machine.

## Correction during validation

The first CircleCI build exposed one test-only constructor argument-name mismatch. The fake `PlaybackMetadataAwareNativePacketCompletion` call used `packetOwner`; the actual internal constructor parameter is `packet`. The test was corrected without changing runtime behavior.

## Ownership findings

- Ordinary packet completions remain owned by the lower-level parser and exact source session.
- Final EOS completions are created by that same source session and therefore participate in its sequence validation and active-completion gate.
- The facade does not create ownerless or detached EOS completions.
- The facade retains no EOS payload, event array, completion list, or second encoded queue.
- Completion disposal is the only signal that allows the next final EOS operation.
- The source and native decoder sessions remain non-owned by the packet-submission state machine.

## CI result

The corrected implementation head passed CircleCI restore, Release build, the full test suite, and format verification on routine Linux CI. The final documentation head must pass the same gate before merge.

## Exclusions

No PCP chunk composition, callback ownership, native FFmpeg execution, decoded-frame receive ownership, continuous drain, production backend activation, Windows device activation, runtime bundling／distribution, or audible／visible playback was added or claimed.

## Next review

0.0.9h should review the finite PCP chunk-to-FLV parser composition boundary, including callback-scoped memory, consumed-byte and suffix ownership, parser completion backpressure, final signaling, cancellation, discontinuity generation, and decoder-retained pause behavior.
