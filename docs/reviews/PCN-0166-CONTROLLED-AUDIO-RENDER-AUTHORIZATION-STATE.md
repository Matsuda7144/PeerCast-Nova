# PCN-0166 — Controlled Audio Render Authorization State

Status: Current  
Milestone: 0.0.10d Phase 7  
Tracking issue: #129

## Purpose

Record the exact current boundary after the contradictory controlled physical audio runs, the five later audible but technically incomplete readiness-gated results, the consumed authorizations, and the completed prospective non-physical hardening.

This review supersedes older roadmap and navigation text that still presents 0.0.10b or the fixed-kind scheduler review as the active next gate. PCN-0167 contains the focused review of the latest managed command failure boundary.

## Established milestone state

### 0.0.10a

The exact BtbN 2026-07-21 win64 LGPL shared FFmpeg candidate is the active project-reviewed built-in manifest identity:

- build ID `btbn.ffmpeg.20260721.win64.lgpl-shared`;
- complete fixed archive, load-role, `bin`, expanded-inventory, build-configuration, license, dependency, and superseded-history evidence;
- no FFmpeg binary bundled or published by Nova.

### 0.0.10b

Controlled real external-runtime native decode validation completed with the approved runtime and retained FLV fixture:

- 10 H.264 packets and frames;
- 48 AAC packets and frames;
- 49,152 decoded audio samples;
- zero decode errors;
- complete native cleanup;
- padded planar and packed audio line sizes accepted while only logical sample bytes are copied.

This evidence does not establish physical D3D11 or WASAPI output.

### 0.0.10c

The controlled Windows video/audio output validation architecture review completed. It selected the application-owned window, message loop, and owner-thread dispatcher; playback-owned finite native lifecycle; independent video and audio records; deterministic evidence plus separate operator observations; no hidden retry, fallback, or device recreation.

### 0.0.10d video

Controlled Windows video output is established for the retained fixture:

- corrected D3D11 texture-copy and production upload probes completed with visible non-black patterns;
- the corrected physical fixture-video run produced `Completed` / `Accepted` evidence;
- independent preflight returned `VideoAccepted`;
- operator observation was `Video visible: Yes`.

## Retained physical audio evidence

The retained audio evidence remains contradictory. It establishes six recognizable ordinary fixture-audio observations, but it does not establish canonical completed audio evidence, stable delivery, physical completion, or formal audio acceptance:

1. an ordinary controlled run retained `3 / 2` delivery accounting and a noise-like transient;
2. a delivery-result diagnostic retained four caller-visible `AudioDelivered` results and no recognizable audio;
3. the known 80 ms managed tone and decoded 80 ms fixture slice were both recognizable, establishing canonical Float32 WASAPI output and decoded fixture sample interpretation;
4. the direct render-evidence helper executed once from approved main `4abadd280ceeae310b2528b6ea62f377211b1273`, returned `unexpected helper failure`, retained neither the requested canonical evidence nor the requested failure record, and had operator observation `Audio audible: No`;
5. the readiness-gated run from exact main `7fbeb8b0dabd9c6af5b2dc9d065d66fd05faf53d` returned child exit `1`, `DeliveryRejected`, terminal rejection `PreCleanupCheckpointRejected`, and final native stage `ActivationCleanupCompleted`; it produced no canonical completed or terminal record, retained a sanitized failure record with SHA-256 `C60F9E2901F710DABC80DD1C26D0E5ADCB7DCEF5C50A767F989DAF6798F604A8`, and had separate operator observation `Audio audible: Yes` for a recognizable beep;
6. the later readiness-gated run from exact main `77a947f98e7a9449523b49a39bc8b4e8d7ed430b` returned child exit `1`, `DeliveryRejected`, terminal rejection `PreCleanupTraceCardinalityRejected`, and final native stage `ActivationCleanupCompleted`; it produced no canonical completed or terminal record, retained a `798`-byte／`21`-line sanitized failure record with SHA-256 `E5AA6E01A55E662E18E9E70089CFDE46125153A57064BF38C2166AACB2DE2ECE`, and again had separate operator observation `Audio audible: Yes` for a recognizable beep;
7. the readiness-gated run from exact main `774342a03c33713b1b7f70e2cb664397c04f8d04` returned child exit signed／hex `1／00000001`, stdout／stderr bytes `0／3122`, final native stage `ActivationCleanupCompleted`, controlled run result `None`, and terminal rejection `None`; it produced no canonical completed or terminal record, retained a `755`-byte／`21`-line version 5 sanitized failure record with SHA-256 `F8B8A0553A6DDF0935BFD2C075FFE32E541659E3A13C1275130E655363B5FAF5`, and again had separate operator observation `Audio audible: Yes` for a recognizable beep;
8. the later readiness-gated run from exact main `847b0ea4cff8aca48c103e499c0bf2bca3fbc6b5` returned child exit `1`, `DeliveryRejected`, terminal rejection `PreCleanupTraceCardinalityRejected`, and final native stage `ActivationCleanupCompleted`; it produced no canonical completed or terminal record, retained an `826`-byte／`22`-line sanitized failure record with SHA-256 `BF1E4BD3304142CB53737F63C1D1849BF3B67546464C53188E874E21A01E50C1`, and again had separate operator observation `Audio audible: Yes`.
9. the next readiness-gated run from exact main `abb071f5fcb6e78f86ad5f185387adee060e29fb` returned child exit `1`, exact fallback `Result: Faulted`, managed failure phase `OutcomeWrite`, and final native stage `ActivationCleanupCompleted`; it produced no canonical completed or terminal record, retained a `795`-byte, `22`-line sanitized failure record with SHA-256 `2A2D40B9388D8F6E679D31FBBEBFA92448ED6F052C53E08A22441A7067AFA602`, and again had separate operator observation `Audio audible: Yes` for a recognizable beep.
10. the readiness-gated run from exact main `e0809e9b711d5bb5101130a26ca19944a94a0d31` returned child exit `1` and `DeliveryRejected / None` after `4 / 3 / 2` decode, outer-delivery, and delivered counts; it produced no completed record, retained canonical terminal record SHA-256 `859651D2DED3417F4F55BBAD1E210D1B48AC9E274681D669D6CF1C516E851C78`, and had separate operator observation `Audio audible: Yes` for a recognizable beep.

The exact failure trigger for the consumed `4abadd280...` invocation is unknowable. The confirmed defect was evidence loss. Native stderr and Windows PowerShell `ErrorActionPreference` interaction was plausible but not proven.

The `7fbeb8...`, `77a947f...`, and `774342a...` observations establish that recognizable ordinary fixture audio reached the operator in three separately authorized runs. None establishes physical completion or formal audio acceptance because none produced a canonical completed record or a canonical terminal record.

The `847b0ea...` observation establishes the same limited operator-visible result for a fourth separately authorized run. It likewise does not establish physical completion or formal audio acceptance.

The `abb071f...` observation establishes the same limited operator-visible result for a fifth separately authorized run. Its `Faulted / OutcomeWrite` evidence and missing canonical completed or terminal record likewise do not establish physical completion or formal audio acceptance.

The `e0809e9...` observation establishes the same limited operator-visible result for a sixth separately authorized run. Its canonical terminal record proves that the third audio reservation transitioned from a short retained wait to `AudioLate`; it does not establish physical completion or formal audio acceptance.

## Static root cause of `PreCleanupTraceCardinalityRejected`

The `77a947f...` rejection does not prove an extra native audio write. The scheduler trace was lost across two ownership-cleanup boundaries:

1. production `DeliveryRejected` entered `PlaybackProductionPcpFlvCompositionBoundary` terminal cleanup, which cleared and stopped the active decoder generation before returning to the lifecycle;
2. the lifecycle then mapped the result to `OutputRejected` and cleared the production owner before the later terminal snapshot.

The independently retained output-buffer/native audio trace remained available, so the terminal codec observed audio trace cardinality greater than delivery trace cardinality.

PR #201 merged as main `7e31627151f5a6dba7fb9e3f0b03cd08be3b14cf`. It retains the latest immutable generation trace before production cleanup, retains the resulting production trace before lifecycle cleanup, prefers live owners while available, and resets retained values when new owners are installed. Focused tests cover both cleanup boundaries. The correction does not change delivery, timing, endpoint selection, buffers, samples, native writes, or cleanup ordering.

The later `847b0ea...` rejection exposed a separate configuration-propagation defect. The controlled evidence composition selected both native audio-render tracing and scheduler delivery observations, but `PlaybackDispatcherBackedProductionGenerationFactoryOperations` omitted `CaptureDeliveryObservations` when it constructed the actual owner-thread generation delivery boundary. The native audio trace was therefore retained while the scheduler trace remained empty.

PR #213 merged as main `a6ec858162b1170cc35a5bbc94ae4068f4335090`. It forwards the existing capture option through the dispatcher-backed path and adds a focused contract test. It does not change delivery, timing, endpoint selection, buffers, samples, native writes, cleanup ordering, readiness, or authorization behavior.

The later `abb071f...` run exposed the next evidence-selection defect. The generation delivery trace has a fixed capacity of four observations and the controlled completed record requires four audio observations, but the trace writer recorded every reserved media kind. The mixed H.264/AAC fixture could therefore place a video delivery into the bounded trace before the four native audio writes, making the completed audio record invalid during outcome serialization.

PR #215 merged as main `94878943dfbd7df2c059bf5f347bcfbf0f24f272`. It limits the opt-in evidence trace to audio reservations and adds a mixed-media regression test proving that one preceding video delivery does not consume or overflow the four audio slots. It changes only observation selection; delivery, timing, endpoint selection, buffers, samples, native writes, cleanup ordering, readiness, and authorization behavior are unchanged.

The later `e0809e9...` terminal record exposed two related controlled-evidence boundaries. The third audio sequence requested an approximately `6 ms` wait, but the controlled wrapper used the fixed `21.3334 ms` cadence before resuming it; Windows scheduling overhead then made the reservation `AudioLate`. The fixed trace also appended the retained wait and its resumed result as separate observations even though completed evidence requires four unique delivered sequences.

ADR-0165 and PCN-0175 select the bounded non-physical correction. The opt-in controlled evidence path uses the latest exact positive retained-wait duration only when it does not exceed the fixed cadence, otherwise falls back to that cadence. Same-sequence trace updates replace the prior state and inconsistent identity fails closed as overflow. Production timing, late tolerance, decode, scheduling, native writes, buffers, and cleanup are unchanged.

## Static boundary of the `774342a...` run

A normal non-completed direct render-evidence command writes either one canonical terminal record or one exact `Result: <reviewed failure>` line. The controlled runner has a separate top-level catch that writes only a fixed `ManagedUnhandledException` marker and exits `1`. That path is consistent with exit `1` and zero stdout after native cleanup.

The retained version 5 child failure record does not store private stderr classification, so the historical marker cannot be proven and is not inferred. The exact historical managed exception source remains unestablished.

The helper console message `Sanitized helper failure record retained: No` reports only that no additional helper-level record was created because the existing child-capture failure record already occupied the exclusive failure path. It does not contradict the verified version 5 child failure record.

PR #203 merged as main `e265a2b47731214a2f26a0b4a4227721158490b2`. It prospectively catches unexpected direct managed-command failures, writes exact `Result: Faulted` when no canonical outcome has already been written, emits fixed private phase diagnostics, and emits the existing `ProjectionRejected` category for snapshot／terminal-projection failures. It publishes no raw exception or private process detail and does not change physical-output behavior.

PR #205 merged as main `46a9ff163e8b0fb4582175d4ff1d56cd753fdeb9`. It adds strict child failure record version 6 with exactly one allowlisted `Managed-Failure-Phase` field and a fixed 22-line canonical shape. Unknown, numeric, extended, or duplicate phase markers collapse to `None`. Versions 2 through 5 remain readable with effective managed phase `None`; version 5 remains the compatibility serializer constant and legacy preflight report shape is preserved.

No retained result establishes both canonical completed render evidence and recognizable audible fixture audio in the same run.

## Completed prospective non-physical boundary

The repository now contains the prospective evidence and authorization mechanics needed to review later decisions:

- non-perturbing fixed four-slot scheduler delivery trace;
- fixed four-slot WASAPI write and padding trace;
- hold-start, pre-cleanup, post-stop, and reviewed terminal checkpoints;
- sanitized native audio aggregate;
- strict bounded completed and terminal render-record codecs;
- exclusive completed, terminal, child-failure, or helper-failure retention;
- offline strict preflights for completed, terminal, child-failure, and helper-failure records;
- bounded retention of exact controlled-run failure result and terminal-record rejection;
- exact initialized-stream versus decoded-buffer format validation;
- bounded production and cleanup-stage diagnostics;
- fixed allowlisted readiness failure disclosure and direct readiness child-process capture;
- granular pre-cleanup terminal rejection categories for delivery trace, audio/native trace, trace cardinality, and trace result consistency;
- two-stage terminal scheduler-trace retention across generation and production owner cleanup;
- dispatcher-backed scheduler delivery-observation capture propagation;
- audio-only selection for the fixed controlled-audio delivery trace;
- bounded direct managed-command failure output and private phase diagnostics;
- strict managed failure phase retention in child failure record version 6 with version 2 through version 5 compatibility;
- a non-physical readiness preflight for exact clean `main`, reviewed inputs, fresh paths, runner build, and accepted video record;
- a strict canonical readiness-record codec and offline preflight;
- atomic UTF-8-no-BOM readiness-record capture;
- a readiness-required authorization entrypoint that validates the retained readiness record and invokes the existing helper at most once only after every gate succeeds.

The authorization entrypoint requires the exact accidental-invocation guard:

```text
I explicitly authorize exactly one controlled physical audio render run.
```

That literal string is not authorization by itself.

## Current gate

Issue #129 remains open.

All recorded readiness-capture and physical-run authorizations through the consumed `abb071f5fcb6e78f86ad5f185387adee060e29fb` run are permanently consumed. No current commit has a new readiness-capture or physical-run authorization. All associated readiness and outcome paths are consumed and must not be reused.

No additional concrete non-physical evidence gap is currently identified after PR #215. A later readiness-gated physical run is a separate reviewed authorization decision. Merge, documentation, CI success, existing readiness evidence, fresh-looking paths, and the literal invocation guard do not grant authorization.

A future controlled physical audio render run requires all of the following:

1. a separately merged exact `main` commit;
2. final-head and post-merge CircleCI success;
3. a newly captured and independently revalidated readiness record for that exact commit, fixture identity, operation bounds, and fresh paths;
4. new repository-external completed, terminal, and failure paths;
5. a new explicit one-run authorization;
6. execution through `tools/Run-ControlledWindowsPhysicalAudioRenderEvidenceWithReadiness.ps1` exactly once.

The underlying helper must not be invoked directly.

## Prohibited interpretation

The following are not established:

- canonical completed／accepted audio evidence for any run with recognizable fixture audio;
- physical completion or formal audio acceptance for any recognizable run;
- stable or deterministic ordinary controlled audio delivery;
- paired video/audio admission;
- completion of 0.0.10d;
- permission to rerun any historical helper or diagnostic;
- permission to reuse any consumed readiness or outcome path;
- permission to substitute another endpoint, fixture, buffer duration, timing policy, operation bound, probe, or output path.

## Project decision

0.0.10d is explicitly deferred without claiming canonical audio completion,
physical completion, formal acceptance, paired admission, or milestone
completion. Issue #129 remains open and all retained evidence keeps its existing
meaning.

A separately reviewed decision is still required before any later readiness capture or physical run. Additional wrappers, codecs, record variants, probes, or physical reruns should not be added unless a concrete new evidence gap is identified.

Project work continues in 0.0.10e through the shared non-physical Windows
application ownership boundary described by ADR-0160 and PCN-0170. This does not
reinterpret 0.0.10d as complete or grant physical authorization.

Paired verification remains blocked until one later canonical completed audio record passes its independent preflight and the separate operator observation for that same run confirms recognizable audio.
