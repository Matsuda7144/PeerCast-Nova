# PCN-0156: Video-Gated Controlled Audio Run

## Milestone

0.0.10d Phase 5

## Review result

`VideoCompletedVisibleAudioPhysicalRunPending`

## Sequencing requirement

The independent run-set boundary requires one accepted video record and one accepted audio record. Native audio activation is permitted only after the retained video record passes the canonical verify-only single-record gate as `VideoAccepted`.

The one-shot audio run also enters cleanup immediately after one accepted delivery unless an explicit bounded observation hold is requested. A short hold allows the already accepted WASAPI buffer to reach the endpoint without changing the exact one-delivery deterministic contract.

## Completed video prerequisite

The corrected physical video helper was executed once from clean main `cd7442e18d18acfe2faf43470ef1c97f77ec513f`.

- runtime build: `btbn.ffmpeg.20260721.win64.lgpl-shared`;
- fixed fixture: `66,939` bytes with SHA-256 `32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15`;
- helper exit: `0`;
- result／evidence: `Completed／Accepted`;
- delivery attempts／delivered: `1／1`;
- retained WouldBlock: `0`;
- runner／lifecycle: `Stopped／Disposed`;
- borrowed window and dispatcher: preserved;
- canonical single-record preflight: `VideoAccepted`;
- operator observation: `Video visible: Yes`;
- retry, redelivery, additional Present, fallback, instrumentation, audio, paired verification, and extra probes: unexecuted.

This accepted external video record is now the exact prerequisite input to the audio helper.

## Implemented helper

- `tools/Run-ControlledWindowsPhysicalAudio.ps1` requires Windows, branch `main`, exact approved commit, and a clean working tree.
- It requires the approved external runtime, retained fixture, existing repository-external accepted video record, and a new repository-external audio record path.
- It rejects identical video and audio record paths and verifies the fixed fixture byte count and SHA-256.
- It restores the solution and builds the dedicated Release runner.
- It runs verify-only video single-record preflight before audio activation and requires `VideoAccepted`, expected kind `Video`, accepted record, and no physical execution by the gate command.
- It executes exactly one `audio` physical run only after the video gate passes.
- It requests a default one-second bounded observation hold after delivery.
- It retains the canonical audio record outside the repository and runs verify-only audio single-record preflight after physical success.
- It requires `AudioAccepted`, expected kind `Audio`, accepted record, and no physical execution by the preflight command.
- It uses collision-free external temporary files, removes them, and rechecks that Git remains clean.

## Preserved contracts

- physical video and audio records remain independent;
- the audio run accepts exactly one selected-media delivery;
- no hidden retry, fallback, resubmission, second queue, timestamp synthesis, audio synthesis, sample stretching, or fixture mutation is added;
- the observation hold issues no further decode or output operations;
- runtime paths, fixture paths, record paths, device and endpoint identities, handles, packets, decoded samples, and temporary reports are not published;
- paired-record verification is not started by the helper;
- audible observation remains separate supporting evidence.

## Remaining work

1. execute audio once through the gated helper using the accepted visible video record;
2. retain an `AudioAccepted` record and record `Audio audible: Yes／No` separately;
3. recreate the paired verification helper from current main and submit the two accepted external records;
4. review deterministic and operator evidence separately before declaring 0.0.10d complete.

## First audio attempt review

Clean main `36ff2d616965975f9d42879cfbf4e23ded99ace1` passed the retained video
record gate as `VideoAccepted`. The controlled audio command performed two decode
operations and transferred an audio frame. Its one delivery attempt returned
`DeliveryRejected`, with delivered count `0`, retained WouldBlock count `0`, and
no native audio evidence. The command did not run audio preflight or request an
operator audible observation. Paired verification was not started.

The first generation reservation retained the scheduler's video-first default
while fixture replay had queued both media kinds. It therefore selected video for
an audio-only run, whose outer composition rejected the non-`AudioDelivered`
result. WASAPI reachability remains unproven. Borrowed-owner `False` fields in the
early terminal record mean unassessed, not contradictory ownership.

The reviewed correction adds a bounded initial-output preference to the existing
scheduler. It applies only to the first successful reservation, is not consumed by
empty or cancelled attempts, and then yields to the existing caller preference and
alternation. Controlled video maps to video and controlled audio maps to audio.
Capacity `16`, maximum owned bytes `64 MiB`, and `DropOldest` remain unchanged;
normal scheduler defaults remain video and `DropIncoming`. A corrected physical
audio run is still required before audio preflight, operator observation, or
paired verification.

## Process-failure diagnostic review

The corrected-selection command was executed once from clean main
`e8b7fcac2f679eb5023ae8ab36571b86773d3950`. Its video gate returned
`VideoAccepted`, but the physical child emitted zero stdout bytes. The canonical
audio envelope was therefore absent. The helper exited `2` after creating a
zero-byte external audio-record file. Decode and delivery counters, runner and
lifecycle states, and borrowed-owner preservation remain unknown. Audio
preflight, audible observation, and paired verification were not run. There was
no retry, redelivery, fallback, or added instrumentation.

Because the former helper replaced the child result with a generic error and did
not preserve its stderr boundary, neither a specific WASAPI call nor a native
crash code is established. This result is not evidence against PR #112's
controlled initial-media selection correction.

The reviewed diagnostic boundary:

- requires unused, distinct, repository-external audio and failure record paths;
- captures one child exit code and concurrent stdout and stderr drains;
- never writes zero-byte or incomplete stdout to the audio record path;
- promotes only a complete canonical audio record;
- emits a fixed sanitized process-failure record only when canonical output is
  absent;
- records only an allowlisted stderr class and the final exact allowlisted native
  audio stage, never raw stderr;
- enables stage output only through the dedicated controlled runner's explicit
  diagnostic flag;
- preserves production output, retry, timing, queue, ownership, and exception
  normalization behavior.

The next run is diagnostic evidence gathering only. It is not final physical
audio acceptance, and paired verification remains blocked.

## Windows PowerShell 5.1 host-compatibility review

Clean main `4968e40f801f72bb06a71007a04a47911b6ac377` completed the Release
runner build but the diagnostic helper failed before physical-child start. No
audio or failure record was created. Physical output, audio preflight, audible
observation, and paired verification were not run. Consequently the attempt
provides no new WASAPI boundary, native exit code, or evidence against PR #112.

The supported helper host remains
`powershell.exe -NoProfile -ExecutionPolicy Bypass`. The execution-policy
rejection was before script load, and the process-scoped bypass made no
persistent policy change. The actual incompatibility was loading a `net10.0`
assembly and constructing the redirected child boundary inside Windows
PowerShell 5.1.

The reviewed correction moves exactly one physical-child launch into the
dedicated `net10.0` runner command
`--capture-controlled-windows-audio-child`. That command:

- receives the explicit runtime, fixture, digest, two record paths, bounds, and
  observation hold;
- starts the same runner assembly at most once with audio-only and native-stage
  diagnostics fixed on;
- uses managed `ProcessStartInfo.ArgumentList`;
- drains stdout and stderr concurrently as bytes;
- atomically promotes only a complete canonical audio record or one sanitized
  failure record;
- preserves the actual child exit in the failure record and never emits raw
  stderr.

The PowerShell helper continues to own all repository-external path checks and
the video preflight before invoking the capture command. It performs audio
preflight only for a canonical child success. PowerShell 7, retry, redelivery,
fallback, and paired verification are not introduced.
