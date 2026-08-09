# PCN-0498: Coordinated ISO BMFF Regular Movie Timed Delivery Reset

## Scope reviewed

- exact delivery/sink/output ownership and initial-generation matching;
- one shared exclusive drive/reset gate and transfer exclusion;
- output-before-sink-before-timing commit order;
- previous-generation output and sink rollback after timing rejection;
- complete aggregate disposal after partial reset or rollback failure;
- retained scheduler reservation across reset.

## Verification

- an injected `GenerationMismatch` retains one video frame, coordinated reset
  changes generations from `9/11` to `13/17`, and the next timing decision and
  sole canonical delivery use generation `13` for that exact frame;
- an injected timing-reset rejection performs output/sink commits for `13/17`
  followed by exact rollback to `9/11`, while delivery remains retryable in
  `ResetRequired` and the frame stays queued;
- an injected output rollback fault closes delivery, scheduler, progress,
  frame, delivery operations, and reset operations;
- all tests use injected timing, canonical delivery, and reset operations.
  They do not activate output, load a native runtime, invoke native code, run a
  probe or diagnostic, capture readiness, or perform physical playback.

## Result

Accepted as the coordinated reset owner defined by ADR-0488. Decoder reset,
late-audio recovery, finite continuous pumping, application integration, and
physical output remain unimplemented.
