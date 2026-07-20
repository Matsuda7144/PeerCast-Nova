# PCN-0123: Finite One-event Packet Submission State Machine Validation

## Scope

Validate the 0.0.9e finite coordinator over one 0.0.9d metadata-aware packet or per-stream EOS completion.

## Verified behavior

- one `Begin` performs exactly one packet send or null flush;
- packet and flush `EAGAIN` retain the original source completion and parser backpressure;
- one `Advance` performs at most one frame receive;
- a retained packet or flush is retried at most once and only after explicit frame progress;
- repeated `EAGAIN` remains one active retained submission without another encoded copy;
- no-progress receive does not trigger a retry;
- per-stream sequence advances only after accepted packet or EOS input;
- non-increasing sequences fail before native work;
- accepted flush or observed decoder EOF blocks later input for that stream;
- video and audio sequence, flush, and EOF state remain independent;
- exact source reference and stream identity are required;
- concurrent input is rejected while the original completion remains retained;
- cancellation, native failure, and state-machine disposal release pending ownership;
- retained cleanup remains packet-first and parser-completion-second;
- source and native decoder sessions remain non-owned.

## Tests

Fifteen injected tests cover:

- immediate packet acceptance;
- packet retention and parser backpressure;
- receive progress followed by accepted retry;
- repeated retry backpressure followed by acceptance;
- no-progress receive without retry;
- immediate flush acceptance and post-flush rejection;
- retained flush retry and flush-state commit;
- decoder EOF for pending flush and packet;
- source mismatch;
- duplicate sequence rejection;
- concurrent input rejection;
- cancellation;
- native receive failure;
- idempotent disposal.

## Scope guard

No resident loop, second encoded queue, decoded-frame delivery, continuous drain, PCP composition, production backend activation, FFmpeg distribution, output device activation, or playback claim is added.

Status: Implementation head passed CircleCI restore, Release build, full tests, and format verification. Final documentation head must pass the same gate before merge.
