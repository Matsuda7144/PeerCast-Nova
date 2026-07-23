# PCN-0165: Controlled Audio Fixed-Kind Scheduler Reservation Review

## Scope

Review the minimal correction for the controlled four-frame audio run after the retained delivery diagnostic proved the outer sequence `AudioDelivered` followed by `VideoDropped`.

## Evidence retained before implementation

The approved diagnostic ran exactly once on main `fe427fdc6094f55ea1a1f321217eff635febf898`.

- run result: `DeliveryRejected`
- delivery result 1: `AudioDelivered`
- delivery result 2: `VideoDropped`
- decode operations: `3`
- delivery attempts: `2`
- delivered: `1`
- retained WouldBlock: `0`
- evidence: `None`
- operator observation: `Audio audible: No`

No repeat, alternate endpoint, changed timing, changed buffer, probe, or paired verification followed.

## Root-cause review

The controlled audio lifecycle already requested audio-only decode and configured audio as the scheduler's initial output kind. The scheduler applied that initial kind only to the first successful reservation. The ordinary generation delivery then committed `PreferVideoNext = true` after `AudioDelivered`.

On the second outer delivery, the scheduler followed the normal caller preference and reserved a queued video frame. The timing boundary correctly classified it as late, completed it as dropped, and returned `VideoDropped`. The audio harness correctly rejected a non-audio result.

Therefore:

- WASAPI did not reject the second outer delivery;
- `RetainedWait` was not the second outer result;
- the audio frame itself was not shown to be malformed;
- the failure was media-kind selection at the scheduler reservation boundary.

## Reviewed correction

`PlaybackSchedulerOptions.RestrictOutputToInitialKind` is opt-in and defaults to `false`.

Restricted mode:

- always searches the configured initial output kind;
- never falls back to the opposite kind;
- leaves unmatched items queued and owned;
- returns no reservation when the requested fixed kind is absent.

Controlled audio sets:

- initial kind `Audio`;
- fixed-kind restriction `true`;
- decode media selection `AudioOnly`.

Controlled video and ordinary production leave the restriction disabled.

## Ownership and boundedness

The correction does not add a queue or transfer ownership. Existing scheduler ownership remains authoritative.

- matching reservations use the existing one-shot reservation object;
- retained completion keeps the same item owned;
- delivered or dropped completion consumes exactly that reservation;
- unmatched video remains bounded by the same count, byte, overflow, reset, and cleanup rules;
- generation cleanup disposes remaining owned frames;
- no retry or hidden inner delivery is introduced.

## Compatibility review

Normal production behavior remains:

- initial video preference;
- caller-driven A/V alternation after the first successful reservation;
- opposite-kind fallback when the preferred queue is empty;
- `DropIncoming` default overflow policy.

The controlled video lifecycle remains unrestricted and video-first.

## Required automated evidence

- two consecutive restricted reservations choose audio while video is queued;
- restricted audio does not fall back to queued video;
- unrestricted mode retains initial preference, caller preference, and fallback;
- controlled audio enables the restriction;
- controlled video and production defaults leave it disabled;
- restore, Release build with analyzers, full tests, and format verification pass on the final head.

## Physical gate

This review authorizes no physical output. A later run requires a merged correction commit, successful main CI, new repository-external record paths, and explicit one-run approval.
