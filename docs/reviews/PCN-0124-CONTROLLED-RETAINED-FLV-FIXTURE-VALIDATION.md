# PCN-0124: Controlled Retained FLV Fixture Validation

## Scope

Validate the real bounded FLV parser completion, exact parser-owned source session, source-bound native decoder session identity, and 0.0.9e finite one-event state machine without activating a native runtime.

## Verified behavior

- controlled FLV bootstrap creates the expected H.264/AAC source session;
- real parser completions preserve video, audio, video, audio interleave;
- video and audio sequence values advance independently;
- AVCC and AAC payloads reach the submission seam unchanged;
- DTS and signed composition-time PTS remain exact;
- keyframe and non-keyframe state remain exact;
- packet `EAGAIN` retains the original native packet owner and parser completion;
- flush `EAGAIN` retains the original per-stream EOS completion;
- parser calls return `ActiveCompletion` with zero consumed bytes while ownership is retained;
- each retained transition performs one receive and one retry only;
- explicit video EOS followed by final audio EOS reaches independent flush acceptance;
- cleanup remains idempotent and does not activate FFmpeg or production playback.

## Finding

A normal final parser operation with neither stream already ended returns one source-owned completion containing two EOS events. The one-event state machine rejects that completion before native submission and disposes it fail closed.

This is not treated as a successful production finalization path. The validation does not hide the mismatch by copying EOS events into detached completions or adding another queue.

## Tests

Two focused cross-platform tests cover the successful controlled retained path and the unpartitioned dual-EOS rejection. The implementation head passed CircleCI restore, Release build, all tests, and format verification.

## Next

0.0.9g must implement one-event final EOS sequencing in the parser so final progress emits at most one source-owned per-stream EOS completion at a time before PCP composition is attempted.

Status: Complete; final documentation head must pass CircleCI before merge.
