# ADR-0143: Independent Physical Video／Audio Run Record Admission

## Status

Accepted for milestone 0.0.10d Phase 5.

## Context

ADR-0142 added an isolated finite command that can execute one controlled Windows physical video or audio run and submit deterministic native evidence. The two runs must remain independent, but their sanitized outputs still need a common fixed contract before the milestone can distinguish:

- one successful video record;
- one successful audio record;
- two records produced by different approved runtime or fixture identities;
- duplicate, edited, incomplete, failed, or cleanup-inconsistent records.

The repository must not retain runtime paths, fixture paths, device or endpoint identities, HWND values, thread IDs, native handles, packet bytes, decoded bytes, screenshots, or external payloads. Operator observation is supporting evidence and must not replace the deterministic evidence gate.

## Decision

### Canonical run record

Each valid invocation of the dedicated controlled Windows physical-run command emits one bounded canonical text record with schema version 1.

The record contains only:

- milestone and schema version;
- video or audio kind;
- exact approved runtime build ID;
- normalized controlled fixture SHA-256;
- fixed run and evidence results;
- bounded fixture, decode, delivery, and caller-visible `WouldBlock` counters;
- terminal runner and lifecycle states;
- borrowed window and dispatcher preservation booleans;
- confirmation that no external binary or payload was published.

The codec uses a fixed field order, fixed header and footer, invariant numeric formatting, strict enums, and a 16 KiB character limit. Unknown, reordered, missing, duplicated, oversized, malformed, or trailing content is rejected.

### Independent set admission

`PlaybackControlledWindowsIndependentRunSetBoundary` consumes at most one video record and one audio record.

A record is accepted only when:

1. schema and milestone are exact;
2. kind is independently `Video` or `Audio`;
3. runtime build ID equals the expected approved build;
4. fixture SHA-256 equals the expected controlled fixture;
5. run result is `Completed`;
6. evidence result is `Accepted`;
7. fixture, decode, and delivery counters show finite positive progress;
8. exactly one selected-media delivery was accepted;
9. no caller-visible `WouldBlock` remains in a completed run;
10. runner is `Stopped` and lifecycle is `Disposed`;
11. borrowed window and dispatcher were preserved;
12. no external binary or payload was published.

The first accepted record leaves the boundary waiting for the other kind. The set becomes complete only after both independent records pass. Duplicate kind, identity mismatch, evidence contradiction, cleanup contradiction, or malformed input rejects the boundary terminally. No record replacement, rollback, merge, repair, or fallback is permitted.

### Verification command

The dedicated runner executable adds an explicit verification mode that reads one video record file and one audio record file within a fixed byte limit. It verifies the labeled kinds and passes both records through the same independent set boundary.

The verification command:

- starts no window or playback lifecycle;
- opens no FFmpeg runtime or controlled fixture;
- performs no physical output;
- prints no record file path;
- reports explicitly that it did not execute the physical runs.

### Operator observation

Visible video or audible audio observation remains a separately recorded supporting note. It is not encoded as deterministic evidence and cannot make a rejected record acceptable.

## Consequences

The repository can now receive and verify two repository-external sanitized physical-run records without importing private execution details or external media. Linux CI validates the codec, pair admission, and verification command using synthetic sanitized records only.

This decision does not establish that either physical run has occurred. 0.0.10d remains physically unvalidated until an identified Windows 11 operator executes video and audio independently and produces two accepted records from the exact approved runtime and fixture.
