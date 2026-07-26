# PCN-0166 — Controlled Audio Render Authorization State

Status: Current  
Milestone: 0.0.10d Phase 7  
Tracking issue: #129

## Purpose

Record the exact current boundary after the contradictory controlled physical audio runs, the consumed one-run render-evidence attempt, and the completed prospective non-physical hardening.

This review supersedes older roadmap and navigation text that still presents 0.0.10b or the fixed-kind scheduler review as the active next gate.

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

The retained audio evidence remains contradictory and does not establish recognizable fixture audio:

1. an ordinary controlled run retained `3 / 2` delivery accounting and a noise-like transient;
2. a delivery-result diagnostic retained four caller-visible `AudioDelivered` results and no recognizable audio;
3. the known 80 ms managed tone and decoded 80 ms fixture slice were both recognizable, establishing canonical Float32 WASAPI output and decoded fixture sample interpretation;
4. the direct render-evidence helper executed once from approved main `4abadd280ceeae310b2528b6ea62f377211b1273`, returned `unexpected helper failure`, retained neither the requested canonical evidence nor the requested failure record, and had operator observation `Audio audible: No`.

The exact failure trigger for the consumed `4abadd280...` invocation is unknowable. The confirmed defect was evidence loss. Native stderr and Windows PowerShell `ErrorActionPreference` interaction was plausible but not proven.

No retained result establishes both canonical completed render evidence and recognizable audible fixture audio.

## Completed prospective non-physical boundary

The repository now contains all prospective evidence and authorization mechanics required before any later decision:

- non-perturbing fixed four-slot scheduler delivery trace;
- fixed four-slot WASAPI write and padding trace;
- hold-start, pre-cleanup, post-stop, and reviewed terminal checkpoints;
- sanitized native audio aggregate;
- strict bounded completed and terminal render-record codecs;
- exclusive completed, terminal, child-failure, or helper-failure retention;
- offline strict preflights for completed, terminal, child-failure, and helper-failure records;
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

The historical authorization at `4abadd280ceeae310b2528b6ea62f377211b1273` is permanently consumed. No current commit has a new physical-run authorization.

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

- completed recognizable physical fixture audio;
- stable or deterministic ordinary controlled audio delivery;
- paired video/audio admission;
- completion of 0.0.10d;
- permission to rerun any historical helper or diagnostic;
- permission to substitute another endpoint, fixture, buffer duration, timing policy, operation bound, probe, or output path.

## Next project decision

No additional physical-output-free evidence plumbing is required before the next authorization decision. Additional wrappers, codecs, or record variants should not be added without a concrete newly identified evidence gap.

Until a new explicit physical authorization is granted, project work should be limited to documentation reconciliation or a separately reviewed milestone that does not reinterpret 0.0.10d as complete.

Paired verification remains blocked until one later canonical audio record passes its independent preflight and the separate operator observation confirms recognizable audio.
