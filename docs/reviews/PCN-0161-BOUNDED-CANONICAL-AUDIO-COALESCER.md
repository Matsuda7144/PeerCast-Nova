# PCN-0161: Bounded canonical audio coalescer review

## Scope

Review the first reusable ownership primitive needed to close the recognizable controlled physical-audio gap after the accepted known-tone and decoded-fixture 80 ms observations.

## Accepted contract

`PlaybackBoundedAudioCoalescer` owns exactly one in-progress canonical batch plus at most one final-frame remainder.

Admission requires:

- exact non-negative generation;
- exact contiguous non-negative source sequence;
- `PcmFloat32Interleaved`;
- 48,000 Hz;
- two interleaved channels;
- positive source sample-frame count no larger than the configured target;
- exact payload byte accounting;
- configured owned-byte limit sufficient for the complete batch and retained remainder.

Publication requires:

- a complete target-sized batch;
- a non-negative output generation;
- no prior publication of the same batch.

After publication, retained remainder bytes become the chronological prefix of the next batch. No source sample is repeated or silently discarded.

## Failure behavior

- undefined or mismatched format and malformed payloads are rejected before sequence commit;
- generation or sequence mismatch does not mutate owned audio;
- cancellation is caller-visible and does not consume sequence;
- a ready unpublished batch rejects further input as `BatchPending`;
- disposal releases all owned bytes and permanently rejects input and publication;
- no retry, fallback, second queue, resampling, synthesis, or payload publication is hidden inside the boundary.

## Validation targets

Focused tests cover:

- `1,024 × 3 + 768 = 3,840` published sample frames;
- the remaining 256 frames from the fourth source frame retained for the next batch;
- byte-order preservation across all source-frame boundaries;
- exact-target publication without remainder;
- ready-batch backpressure;
- sequence, generation, format, rate, channels, payload length, and source-size rejection;
- cancellation without mutation;
- ownership-budget validation;
- disposal cleanup.

## Integration decision

This PR is primitive-only. The next PR may connect the coalescer to generation-matched scheduler delivery through an explicit production option that remains disabled by default. Controlled physical audio may enable the fixed 3,840-frame target only after that integration passes focused and full CI.

Physical decoded audio, known tone, video, or paired verification is not executed by this review.
