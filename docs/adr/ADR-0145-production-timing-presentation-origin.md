# ADR-0145: Production Timing Presentation Origin

## Status

Accepted for milestone 0.0.10d Phase 5 physical-video diagnostics remediation.

## Context

After explicit decoder packet-time-base propagation, the controlled Windows video run produced a known presentation timestamp of 621 ms from the original raw FFmpeg value 621 and the configured `1/1000` time base. The generation scheduler admitted and reserved that frame, but the production timing boundary compared it against its first monotonic playback position of 0 ms.

The existing 250 ms maximum single wait therefore returned `WaitLimitExceeded` for a valid 621 ms source timestamp. The generation faulted before managed format adaptation, Windows output processing, or a native present attempt.

A container timeline is not required to begin at zero. Replacing 621 ms with zero, widening the wait limit, sleeping, or retrying would either destroy source timing meaning or hide the missing presentation-origin relationship.

## Decision

The production monotonic-clock construction of `PlaybackAvTimingDecisionBoundary` establishes one presentation origin from the first valid normal timing item:

- validate timestamp, generation, discontinuity, and the started non-negative monotonic position first;
- latch the exact first presentation timestamp and the current playback position without modifying either value;
- evaluate the first item at delta zero;
- evaluate later items against `anchor playback position + (current presentation timestamp - anchor presentation timestamp)` using checked arithmetic;
- use one shared anchor for video and audio so their original relative offsets remain intact;
- keep the existing early and late tolerances and the 250 ms maximum wait unchanged;
- keep the injected absolute-position constructor unchanged for focused policy and diagnostic callers;
- clear both monotonic-position history and the presentation anchor only after a successful transactional timing reset.

An invalid item, generation mismatch, discontinuity, clock failure, clock regression, arithmetic overflow, or partial reset remains fail closed under its existing result and ownership contract.

No raw or managed timestamp is rewritten. No zero timestamp is synthesized into frame metadata. No sleep, hidden retry, polling, second queue, scheduler resubmission, fixture mutation, or output fallback is added.

## Consequences

The diagnosed first video frame with presentation timestamp 621 ms and first playback position 0 ms is now `ProcessNow`. A later frame at 721 ms observed 100 ms later is also on time. A video frame at 621 ms followed by an audio frame at 641 ms observed 20 ms later preserves the original cross-media offset.

A successful generation reset permits a new source timeline, including one whose first timestamp is far from the previous anchor. Direct injected absolute-position tests still return `WaitLimitExceeded` when 621 ms is compared against an explicit absolute position of 0 ms.

This change removes the diagnosed timing-origin rejection only. It does not establish a native present acknowledgement, operator-visible video, audio output, accepted physical evidence, paired-record verification, or completion of 0.0.10d.
