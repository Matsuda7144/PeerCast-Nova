# ADR-0148: Video-Gated Controlled Audio Run

## Status

Accepted for milestone 0.0.10d Phase 5 audio execution sequencing; the video gate is now satisfied.

## Context

Video and audio physical records are executed independently and paired only after both pass deterministic admission. The milestone requires the video result to be reviewed before audio begins. A manually supplied record path or a prior command exit is not sufficient proof that the video record remains canonical and accepted.

The corrected video run on clean main `cd7442e18d18acfe2faf43470ef1c97f77ec513f` used runtime build `btbn.ffmpeg.20260721.win64.lgpl-shared` and the fixed retained fixture. It completed with accepted evidence, exactly one delivered video item, stopped runner, disposed lifecycle, preserved borrowed owners, `VideoAccepted` single-record preflight, and operator observation `Video visible: Yes`.

The audio output lifecycle must not be torn down immediately after the first accepted WASAPI write. The deterministic contract remains exactly one selected-media delivery, but the existing bounded observation hold can keep the accepted audio buffer and endpoint lifecycle alive briefly without repeating or synthesizing audio.

## Decision

Use the dedicated Windows audio helper that:

- requires exact clean `main`, the approved runtime, the retained fixture, the newly accepted external video record, and a new external audio record path;
- keeps runtime, fixture, video record, audio record, and temporary reports outside the repository;
- verifies the exact fixture byte count and SHA-256;
- builds the dedicated Release runner;
- submits the video record to single-record preflight before native audio activation;
- continues only when the result is `VideoAccepted` for the fixed runtime and fixture;
- executes exactly one controlled physical run with `--output-kind audio`;
- requests the default one-second bounded observation hold after successful delivery;
- retains one canonical external audio record;
- submits a successful audio record to single-record preflight and requires `AudioAccepted`;
- uses GUID-named external temporary files and removes them;
- confirms the repository remains clean;
- does not perform paired-record verification.

The observation hold performs no extra decode, delivery, retry, resubmission, native output write, audio synthesis, sample stretching, or fixture mutation. It only delays cleanup while the already accepted WASAPI buffer remains owned by the existing controlled lifecycle.

## Consequences

The accepted visible video record opens the audio execution gate. A successful audio helper exit will establish an independently accepted audio record while keeping audible observation separate.

The selected decoded audio item may be brief. `Audio audible: Yes／No` is supporting evidence only and cannot make a rejected deterministic record acceptable or make an accepted record invalid by itself.

Paired verification remains blocked until the independent audio run is complete, its record passes `AudioAccepted`, and deterministic and audible observations have been reviewed separately.
