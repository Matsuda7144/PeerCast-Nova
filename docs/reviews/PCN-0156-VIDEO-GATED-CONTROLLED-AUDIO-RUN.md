# PCN-0156: Video-Gated Controlled Audio Run

## Milestone

0.0.10d Phase 5

## Review result

`ImplementedVideoAndAudioPhysicalRunsPending`

## Sequencing problem

The independent run-set boundary requires one accepted video record and one accepted audio record, but an audio execution helper did not yet enforce that the retained video record was canonical and independently accepted before native audio activation.

The one-shot audio run also enters cleanup immediately after one accepted delivery unless an explicit bounded observation hold is requested. A short hold is useful for allowing the already accepted WASAPI buffer to reach the endpoint, but it must not change the exact one-delivery deterministic contract.

## Implemented helper

- Added `tools/Run-ControlledWindowsPhysicalAudio.ps1`.
- Required Windows, branch `main`, exact approved commit, and a clean working tree.
- Required the approved external runtime and retained fixture.
- Required one existing repository-external video record and one new repository-external audio record path.
- Rejected identical video and audio record paths.
- Verified fixed fixture byte count and SHA-256.
- Restored the solution and built the dedicated Release runner.
- Ran verify-only video single-record preflight before audio activation.
- Required `VideoAccepted`, expected kind `Video`, and no physical execution by the gate command.
- Executed exactly one `audio` physical run only after the video gate passed.
- Requested a default one-second bounded observation hold after delivery.
- Retained the canonical audio record outside the repository.
- Ran verify-only audio single-record preflight after a successful physical exit.
- Required `AudioAccepted`, expected kind `Audio`, and no physical execution by the preflight command.
- Used collision-free GUID external temporary files and removed them.
- Rechecked that Git remained clean.

## Preserved contracts

- one physical video record and one physical audio record remain independent;
- the audio run accepts exactly one selected-media delivery;
- no hidden retry, fallback, resubmission, second queue, timestamp synthesis, audio synthesis, sample stretching, or fixture mutation is added;
- the observation hold issues no further decode or output operations;
- runtime paths, fixture paths, record paths, device and endpoint identities, handles, packets, decoded samples, and temporary reports are not published;
- paired-record verification is not started by the helper;
- audible observation remains separate supporting evidence.

## Validation

The helper relies on the already tested physical command, single-record preflight, canonical codec, independent run-set boundary, and bounded observation-hold contracts. Routine CircleCI remains responsible for Release compilation with analyzers, full solution tests, and format verification. The PowerShell helper requires the post-merge Windows operator run for physical validation.

## Remaining work

1. execute video once from the new main commit and retain a `VideoAccepted` record plus `Video visible: Yes／No`;
2. execute audio once through the gated helper and retain an `AudioAccepted` record plus `Audio audible: Yes／No`;
3. submit the two external records to paired verification;
4. review deterministic and operator evidence separately before declaring 0.0.10d complete.
