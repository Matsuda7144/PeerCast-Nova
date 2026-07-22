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
