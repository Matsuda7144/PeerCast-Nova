# PCN-0152: Decoded Timestamp Propagation

## Milestone

0.0.10d Phase 5

## Review result

`ImplementedPhysicalVideoRevalidationPending`

## Diagnosed boundary

The controlled physical video run reserved video sequence 44 but returned `InvalidItem` from generation-matched scheduler reservation validation. Raw FFmpeg frame evidence was PTS 621, best-effort timestamp 621, duration unknown/zero, and time base `0/1`. The parser, metadata-aware packet, and managed native session retained the exact source time base `1/1000`; `AVCodecContext.pkt_timebase` had not been configured.

The first conversion to unknown occurred in `PlaybackDecoderFfmpegFrameMetadataBoundary.TryConvertTime`. Timing, adapter, conversion, Windows output, and native present were not reached. This was neither a fixture replay defect nor the previously corrected controlled scheduler overflow policy.

## Implemented boundary

- Requires exact reviewed `avutil-60` export `av_opt_set_q`.
- Calls `av_opt_set_q(context, "pkt_timebase", exactTimeBase, 0)` through a sequential two-`int` rational.
- Accepts only positive numerator and denominator within existing bounded-demux component limits.
- Preserves the exact source rational without reduction or rescaling.
- Enforces create, parameter transfer, packet-time-base configuration, then codec open for video; repeats the same order for audio.
- Retains configured state only after a nonnegative native result and clears it on disposal.
- Makes native rejection and native exception fail closed without exposing pointers, paths, or native error text.
- Leaves the generic codec-open contract unchanged for callers outside the bounded FLV session.

## Metadata review

- A positive raw `AVFrame.time_base` remains authoritative even when it differs from the configured context value.
- Exactly unset raw `0/positive` uses the retained configured value.
- Exactly unset raw with no configured value preserves unknown timestamps.
- Negative numerator or nonpositive denominator remains `InvalidMetadata`; configuration is not a repair path.
- PTS, best-effort timestamp, and duration use the same effective value.
- `AV_NOPTS_VALUE`, zero duration, checked conversion, maximum duration, and negative-duration rejection retain their existing contracts.
- No raw field is overwritten and no zero, sequence, packet count, or wall-clock timestamp is synthesized.

## Regression evidence

Cross-platform injected coverage verifies:

- exact symbol, option name, rational, flags, and native call order;
- unsupported, invalid, unavailable, pre-transfer, duplicate, post-open, missing-symbol, native-rejection, and native-exception results;
- privacy-safe results and complete context cleanup;
- exact video/audio binding time bases and fail-closed per-stream session cleanup;
- diagnosed 621 ms video and audio fallback, raw-authoritative behavior, old unknown behavior, malformed raw rejection, `AV_NOPTS_VALUE`, and zero duration;
- propagation through callback-scoped frame data into the managed copy boundary;
- preservation of existing decoded-output timestamp, sequence, discontinuity, scheduler, and ownership contracts.

## Deferred physical evidence

This review does not claim `Completed` video output, accepted physical evidence, a native present acknowledgement, recognizable visible video, audio output, audible audio, record verification, or completion of 0.0.10d. After merge, only the video run may be repeated with the same approved runtime, controlled fixture, and exact digest. Audio and combined verification remain blocked until video is completed, accepted, and visibly confirmed by the operator.
