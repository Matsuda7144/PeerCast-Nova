# ADR-0116: Controlled Retained FLV Fixture Validation

Status: Accepted

## Context

0.0.9e added a finite one-event state machine over the 0.0.9d metadata-aware packet and flush boundary. Its focused tests used injected demux completions, so the complete ownership seam from real bounded FLV bytes through the parser's source-owned completion still required validation before PCP composition or decoded-frame delivery.

The validation must not activate FFmpeg, create a resident decode loop, copy an encoded packet into another queue, or hide retry behavior.

## Decision

Add one controlled in-memory FLV fixture validation that uses the real `PlaybackBoundedFlvParser`, the exact parser-created `PlaybackBoundedDemuxSession`, a matching source-bound native decoder session with injected context owners, and `PlaybackFiniteOneEventPacketSubmissionStateMachine` with injected native results.

The successful fixture path must verify:

- deterministic video, audio, video, audio packet interleave;
- independent per-stream sequence values;
- AVCC and AAC payload preservation;
- FLV DTS and signed composition-time PTS mapping;
- video keyframe and non-keyframe mapping;
- packet `EAGAIN` retention of the original parser completion;
- flush `EAGAIN` retention of the original per-stream EOS completion;
- zero parser input consumption while either completion remains active;
- exactly one receive and one retry for each retained transition;
- explicit video EOS followed by final audio EOS and independent flush acceptance;
- cleanup without production runtime activation.

The fixture also records one unresolved composition boundary. When neither stream has ended, `PlaybackBoundedFlvParser` final input returns one completion containing both video and audio EOS events. The 0.0.9e state machine accepts exactly one event and therefore rejects this completion before native submission. This fail-closed result is correct for the current contract, but ordinary finalization cannot be composed until the parser emits final EOS one event at a time.

0.0.9f does not add an EOS adapter, detached completion, second queue, or parser behavior change merely to make the validation pass.

## Consequences

The real parser-to-state-machine packet path is validated for packet order, timing, keyframe state, encoded ownership, retained retry, backpressure, and separately emitted per-stream EOS.

The next milestone is 0.0.9g One-event Final EOS Sequencing. It must make final parser progress emit at most one source-owned EOS event per completion, preserve active-completion backpressure and per-stream sequence state, and avoid a copied or detached EOS queue before PCP composition proceeds.

Production backend activation, native FFmpeg execution, decoded-frame ownership, output delivery, runtime distribution, and audible or visible playback remain unchanged.

## Validation

Two cross-platform tests cover the successful retained controlled fixture and the dual-EOS finalization rejection. The implementation head passed CircleCI restore, Release build, the full test suite, and format verification. The final documentation head must pass the same routine Linux gate before merge.
