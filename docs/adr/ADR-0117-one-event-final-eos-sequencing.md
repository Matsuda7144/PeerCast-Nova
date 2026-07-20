# ADR-0117: One-event Final EOS Sequencing

Status: Accepted

## Context

0.0.9f validated the real bounded FLV parser completion through the exact source-bound native session identity and the finite one-event packet-submission state machine. Packet order, timestamps, keyframe state, retained packet／flush backpressure, and explicitly separated per-stream EOS all composed correctly.

Ordinary final input remained uncomposable because `PlaybackBoundedFlvParser` created one source-owned completion containing both video and audio EOS events. The downstream state machine intentionally accepts exactly one event and rejected that completion before native work. The fix must preserve the same source session, completion backpressure, and per-stream sequence state without adding detached completions, an EOS copy queue, or a resident loop.

## Decision

Add `PlaybackOneEventFinalEosFlvParser` as the finite parser facade used by future composition.

The facade:

- owns and delegates ordinary parsing to one `PlaybackBoundedFlvParser`;
- observes only successfully constructed real parser completions to track the next video and audio sequence;
- preserves explicit per-stream EOS already emitted by the underlying parser;
- never asks the underlying parser to aggregate final EOS;
- accepts final input only after all caller bytes for that operation are consumed;
- rejects final input with an incomplete carry as truncated and faults closed;
- latches final state when the final signal accompanies the last complete packet or an empty operation;
- emits at most one source-owned EOS completion per public operation through the exact parser-created `PlaybackBoundedDemuxSession`;
- emits video EOS first when both streams remain open, then audio EOS after the first completion is released;
- emits only the remaining stream when the other stream already ended explicitly;
- uses `PacketsAvailable` for the first EOS and `EndOfStream` for the final EOS;
- keeps the active completion as the sole backpressure owner;
- rejects non-empty input after finalization begins;
- resets sequence, ended, and final state only with explicit discontinuity;
- returns terminal `EndOfStream` without another completion after both per-stream EOS completions have been released.

The facade stores only scalar sequence, ended, final, position, and generation state. It does not retain copied EOS events, a completion list, or another encoded queue.

## Consequences

The future PCP composition path must use `PlaybackOneEventFinalEosFlvParser` rather than call final input directly on the lower-level parser. Normal packet completions remain the original parser-created, source-owned completions. Final EOS completions are created by the same exact source session, so session validation, sequence commit, and active-completion backpressure remain authoritative.

0.0.9g closes the dual-EOS composition gap and allows both final flushes to enter the 0.0.9e state machine one event at a time. It does not add PCP chunk wiring, native FFmpeg activation, decoded-frame ownership, drain iteration, output delivery, backend activation, runtime distribution, or audible／visible playback.

The next milestone is 0.0.9h PCP Chunk-to-FLV Parser Composition Review. It must define callback ownership, consumed-byte and caller-suffix handling, final signaling, cancellation, discontinuity, and retained decoder backpressure before runtime composition is implemented.

## Validation

Six focused cross-platform tests cover source-owned video-then-audio final EOS, explicit video EOS, final signal attached to the last packet, truncated final carry, discontinuity reset, and both final flushes through the finite one-event state machine. The corrected implementation head passed CircleCI restore, Release build, the full test suite, and format verification.
