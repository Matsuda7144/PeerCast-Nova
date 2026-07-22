# PCN-0151: Independent Physical Video／Audio Run Record Admission

## Milestone

0.0.10d Phase 5

## Review result

`RecordAdmissionImplementedPhysicalExecutionPending`

## Implemented surface

- canonical bounded schema-versioned text record for one physical video or audio run;
- command output migrated from informal lines to the canonical record;
- strict parser with fixed order, header, footer, enums, booleans, counters, and size limit;
- fail-closed two-record admission boundary;
- exact runtime build and controlled fixture identity matching;
- evidence and cleanup postcondition checks;
- duplicate-kind and terminal contradiction handling;
- bounded repository-external video／audio record verification command;
- dedicated runner dispatch for run mode or verification mode;
- focused codec, boundary, and command tests.

## Record review

The schema retains only sanitized deterministic values:

- `0.0.10d Phase 5` and schema version 1;
- `Video` or `Audio`;
- approved runtime build ID;
- normalized fixture SHA-256;
- fixed run and evidence result enums;
- finite fixture, decode, delivery, and `WouldBlock` counters;
- terminal runner and lifecycle states;
- borrowed-owner preservation booleans;
- external publication boolean.

It does not retain:

- runtime or fixture paths;
- device, adapter, endpoint, operator, machine, or account identities;
- HWND, thread ID, native pointer, handle, or COM identity;
- packet, decoded, screenshot, audio, or media bytes;
- timestamps that could identify a private runner session.

The parser rejects missing, extra, reordered, duplicated, malformed, oversized, or trailing fields. Numeric parsing is invariant and nonnegative. SHA-256 values are normalized to uppercase only after all 64 characters are confirmed hexadecimal.

## Pair admission review

One set accepts exactly one video record and one audio record. The set is incomplete after the first record and completes only after the other kind independently passes.

Each record must prove:

1. exact schema and milestone;
2. exact approved runtime build ID;
3. exact controlled fixture SHA-256;
4. `Completed` run result;
5. `Accepted` deterministic physical evidence;
6. positive bounded fixture, decode, and delivery progress;
7. exactly one accepted selected-media delivery;
8. zero retained `WouldBlock` in the completed record;
9. stopped application runner;
10. disposed playback lifecycle;
11. preserved borrowed window and dispatcher;
12. no external binary or payload publication.

Duplicate video or audio input rejects terminally. Runtime mismatch, fixture mismatch, failed evidence, incomplete cleanup, or malformed content also rejects terminally. A later record cannot repair, replace, roll back, or revive a rejected set.

## Verification command review

The explicit verification mode requires:

- one video record file;
- one different audio record file;
- the expected fixture SHA-256.

Each file is read within a fixed byte limit and decoded as strict UTF-8. The command validates that the labeled video file contains a video record and the labeled audio file contains an audio record before pair admission.

The command starts no physical runner, opens no FFmpeg runtime, opens no controlled fixture, and performs no video or audio output. Its sanitized report prints no supplied path and states `Physical-Runs-Executed-By-This-Command: False`.

## Focused tests

Coverage includes:

- canonical round trip;
- trailing and noncanonical content rejection;
- video-first and audio-first completion;
- duplicate-kind rejection;
- runtime and fixture mismatch;
- evidence rejection;
- cleanup and external-publication contradiction;
- missing verifier arguments;
- successful independent record file verification;
- swapped labeled kinds;
- runtime mismatch without path disclosure.

## Operator observation

Visible video and audible audio observation remain supporting notes outside the deterministic record. They cannot replace accepted native evidence and are not required for the Linux synthetic tests.

## Scope guard

This review does not claim:

- that a Windows 11 physical run occurred;
- visible D3D11 output;
- audible WASAPI output;
- accepted real video or audio records;
- combined A／V playback;
- synchronization quality;
- recovery, continuous playback, GUI, installer, or recorder readiness.

## Remaining work

Execute the dedicated physical-run command twice on an identified Windows 11 machine using the exact approved external runtime and retained controlled fixture:

1. one independent video run redirected to a repository-external record file;
2. one independent audio run redirected to a different repository-external record file;
3. one verification invocation over both files;
4. separate operator notes for visible video and audible audio.

Only two real accepted `SystemWindows` records may close 0.0.10d physical validation.
