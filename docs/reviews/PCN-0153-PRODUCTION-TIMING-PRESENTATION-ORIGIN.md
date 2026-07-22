# PCN-0153: Production Timing Presentation Origin

## Milestone

0.0.10d Phase 5

## Review result

`ImplementedPhysicalVideoRevalidationPending`

## Diagnosed boundary

PR #99 restored the exact decoded video timestamp: raw PTS 621 with effective time base `1/1000` became known managed PTS 621 ms. The next controlled Windows video run reached the generation timing decision but returned `WaitLimitExceeded` because the production monotonic position source established its first playback position at 0 ms while the default maximum wait remained 250 ms.

The scheduler reservation was valid. Managed adaptation, conversion, Windows output processing, canonical native delivery, and D3D11 present were not reached. Native video attempt and acknowledgement counters therefore remained zero.

## Implemented boundary

- Production monotonic timing latches the first valid presentation timestamp together with the first started playback position.
- The first normal timing item has delta zero without changing its timestamp.
- Later target positions are calculated from the checked presentation-timestamp offset from the shared anchor.
- Video and audio use the same anchor and retain their source-relative timing.
- Existing early tolerance, late tolerance, maximum wait, late policy, reservation ownership, and generation checks are unchanged.
- Transactional timing reset clears the position history and presentation anchor only after all reset stages succeed.
- The injected absolute-position constructor retains the previous direct timestamp-minus-position contract.
- Invalid metadata, generation mismatch, discontinuity, negative or regressing position, overflow, cancellation, disposal, and reset failure remain fail closed.

No frame timestamp, packet timestamp, sequence, duration, discontinuity, or scheduler item is mutated. No zero metadata value, sleep, retry, polling, second queue, scheduler resubmission, tolerance widening, fixture mutation, or native-output fallback is introduced.

## Regression evidence

Focused cross-platform coverage verifies:

- the diagnosed first video PTS 621 ms is admitted immediately at production playback start;
- a later PTS 721 ms observed 100 ms later remains on time;
- a video PTS 621 ms and audio PTS 641 ms preserve a 20 ms cross-media offset through one shared anchor;
- successful timing reset clears the previous presentation origin and admits a new non-zero source origin;
- injected absolute-position mode still returns `WaitLimitExceeded` for 621 ms against explicit position zero.

Routine CI remains responsible for Release compilation with analyzers, the full solution test suite, and format verification.

## Deferred physical evidence

This implementation does not claim `Completed` video output, accepted `SystemWindows` evidence, a native present attempt or acknowledgement, recognizable operator-visible video, audio output, operator-audible audio, paired-record admission, or completion of 0.0.10d.

After merge, repeat only the controlled physical video run with the same approved runtime identity and exact retained fixture digest. Audio and combined record verification remain blocked until video completes, deterministic evidence is accepted, and the operator reports whether video was visibly rendered.
