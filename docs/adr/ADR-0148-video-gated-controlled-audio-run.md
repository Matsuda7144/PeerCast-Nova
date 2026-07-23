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

## First controlled audio execution result

The first video-gated audio command ran from clean main
`36ff2d616965975f9d42879cfbf4e23ded99ace1`. The external video record gate
returned `VideoAccepted`. Fixture replay then completed two decode operations and
transferred an audio frame, but the single delivery attempt terminated as
`DeliveryRejected`: delivered count `0`, retained WouldBlock count `0`, and no
native audio evidence. Audio preflight and operator observation were not run.

The terminal record's borrowed-owner `False` values describe owners that were not
evaluated after the early terminal result; they are not evidence of an ownership
contradiction. The run did not prove that WASAPI was reached.

The diagnosed cause is a mismatch between the controlled audio-only request and
the generation scheduler's unconditional initial video preference. Because both
video and audio were queued, the first reservation could select video even after
the audio frame transfer. The outer audio-only composition correctly rejected the
inner `VideoDelivered` result. The bounded scheduler now accepts an explicit
initial output kind. Controlled video selects video and controlled audio selects
audio; normal production continues to default to video. This changes no retry,
queue, timing, output, or ownership contract. Audio execution and paired
verification remain pending until the corrected physical audio run is reviewed.

## Corrected-selection run process boundary

The first post-selection-correction audio attempt ran once from clean main
`e8b7fcac2f679eb5023ae8ab36571b86773d3950`. The retained video record gate
was accepted. The physical child produced zero stdout bytes, so no canonical
audio record was formed. The helper subsequently exited with code `2` and left
a zero-byte file at the requested external audio record path. Audio counters,
runner and lifecycle states, and borrowed-owner preservation are unknown. Audio
preflight, audible observation, and paired verification were not executed. No
retry, redelivery, fallback, or added instrumentation was used in that run.

The helper discarded the child exit code and stderr boundary when it replaced
the missing canonical stdout with its generic exit. Therefore this evidence does
not identify a WASAPI call or native termination code and does not refute the
initial-selection correction from PR #112.

The controlled audio helper now starts the physical child once through an
explicit redirected process boundary. It promotes stdout only after the complete
canonical audio envelope is accepted. Otherwise it creates no audio record and
retains a separate repository-external sanitized process-failure record containing
only byte counts, signed and hexadecimal child exit code, an allowlisted stderr
classification, and the last exact allowlisted native audio stage. Raw stderr is
never retained. The next physical execution is diagnostic only and cannot serve
as final physical audio acceptance.

## Windows PowerShell 5.1 diagnostic-host boundary

The first diagnostic helper invocation from clean main
`4968e40f801f72bb06a71007a04a47911b6ac377` restored and built the Release
runner successfully, then failed inside the helper before the physical child
started. Neither the audio record nor the process-failure record was created.
Physical audio, audio preflight, audible observation, and paired verification
were not executed.

The root boundary was a host mismatch: the supported runbook invokes
`powershell.exe`, but that helper attempted to load the `net10.0` CLI assembly
inside Windows PowerShell 5.1 and used APIs unavailable from that host. The
earlier execution-policy rejection occurred before script load; process-scoped
`-ExecutionPolicy Bypass` changed no persistent setting.

The helper now retains Windows PowerShell 5.1 as its supported host and performs
only validation, Release build, video preflight, and one invocation of a managed
capture command. The `net10.0` runner owns child `ArgumentList` construction,
concurrent byte capture, sanitized analysis, and atomic promotion. PowerShell 7
is not required. This correction supplies no new evidence of WASAPI reachability
and does not alter the PR #112 initial-media selection correction. The next run
remains diagnostic rather than final physical audio acceptance.

## Accepted deterministic audio with negative operator observation

The corrected diagnostic physical audio run executed once from clean main
`e7e82b01a1eb323954b8ef2375b025064a796a8d`. The helper exited `0`; the
retained video gate returned `VideoAccepted`; canonical audio preflight returned
`AudioAccepted`; and the physical record reported `Completed` / `Accepted`.
Fixture bytes and packets were `66,939` / `2`, decode operations were `2`,
delivery attempts and delivered items were `1` / `1`, and retained WouldBlock
was `0`. Runner and lifecycle ended `Stopped` / `Disposed`, both borrowed owners
were preserved, and no process-failure record was created.

The operator recorded `Audio audible: No`: a short noise-like sound was heard,
but it was not recognizable as the fixture audio. No retry, redelivery,
fallback, additional probe, or paired verification occurred. The deterministic
audio evidence remains valid, while recognizable physical audio acceptance is
incomplete and paired verification remains blocked.

The controlled composition delivers only the first selected audio frame and
then terminates. The retained fixture contains 48 AAC frames and 49,152 decoded
samples, averaging 1,024 samples per frame, or approximately 21.3 ms at 48 kHz.
The one-second observation hold emits no additional samples. That duration is a
plausible cause of the observation, but PCM conversion and sample
interpretation have not yet been separated from it.
