# ADR-0157: Controlled Audio Fixed-Kind Scheduler Reservation

## Status

Accepted for implementation review.

## Context

The separately approved controlled-audio delivery diagnostic executed once from clean main `fe427fdc6094f55ea1a1f321217eff635febf898` and retained the exact outer lifecycle sequence:

1. `AudioDelivered`
2. `VideoDropped`

The run stopped with two outer delivery attempts, one delivered frame, zero retained WouldBlock, no accepted evidence, and operator observation `Audio audible: No`.

Controlled audio already selected `AudioOnly` for caller-driven decode and configured the scheduler's first successful output reservation as audio. That initial preference was insufficient. After the first successful audio delivery, the normal A/V generation contract changed its caller preference to video. A queued video frame was then reserved, judged late, consumed as `VideoDropped`, and surfaced to the audio harness as a terminal delivery rejection.

The scheduler's normal first-preference, caller-driven alternation, and opposite-kind fallback remain correct for production A/V playback. The defect is their reuse by a finite audio-only validation generation.

## Decision

Add `PlaybackSchedulerOptions.RestrictOutputToInitialKind`, defaulting to `false`.

When `false`, reservation behavior remains unchanged:

- the first successful reservation uses `InitialOutputKind`;
- later reservations use the caller's preferred kind;
- if the preferred kind is absent, the opposite kind is used as fallback.

When `true`:

- every reservation searches only `InitialOutputKind`;
- the caller's preferred kind does not override that selection;
- the opposite kind is never used as fallback;
- no matching item returns no reservation;
- unmatched queued items remain scheduler-owned until a matching reservation, bounded overflow policy, reset, or generation cleanup consumes them.

Enable the option only for controlled physical audio. Its existing settings remain:

- `MediaSelection = AudioOnly`;
- `InitialOutputKind = Audio`;
- bounded scheduler capacity and byte ownership;
- `DropOldest` controlled overflow policy;
- caller-visible retained and terminal results;
- bounded controlled-only `RetainedWait` resume.

Controlled video and normal production retain the default `false` value.

## Consequences

The four-frame audio harness can request four ordinary decode transfers and receive four ordinary audio scheduler reservations without an intervening video reservation. Queued video remains owned and is disposed during ordinary generation cleanup rather than being timed or presented by the audio validation.

No hidden delivery loop, retry, redelivery, repeat, coalescing integration, synthesis, timing rewrite, buffer change, output fallback, or device recreation is introduced.

The existing generation delivery `PreferVideoNext` state remains unchanged. In restricted mode it is intentionally ignored by the scheduler reservation selector; in normal mode it continues to drive A/V alternation.

## Validation

The implementation must prove:

- consecutive restricted reservations select audio while video is queued;
- restricted audio returns no reservation rather than falling back to video;
- unrestricted first preference, caller preference, and fallback remain unchanged;
- controlled audio enables the option;
- controlled video and production defaults leave it disabled;
- no physical output is executed during implementation review.
