# ADR-0147: Single Physical Run Record Preflight

## Status

Accepted for milestone 0.0.10d Phase 5 run-result admission hardening.

## Context

The dedicated controlled video helper writes one repository-external canonical record and checks its fixed envelope and selected identity fields. The paired verification command later parses both records through the canonical codec and the independent run-set admission boundary.

A successful physical command exit must not be treated as sufficient permission to continue toward audio. Manual field checks can confirm the envelope but do not prove the complete record satisfies the same result, evidence, counter, cleanup, owner-preservation, runtime, and fixture contract used by paired admission.

The video record must therefore be admitted immediately after creation, before it can be used as the gate for any later audio run. This check must perform no physical output and must not expose repository-external paths.

## Decision

Add one explicit single-record preflight command:

- command argument: `--verify-controlled-windows-run-record`;
- required options: `--record`, `--expected-kind`, and `--fixture-sha256`;
- expected kind is independently `video` or `audio`;
- the record is read with the same bounded UTF-8 file reader used by paired verification;
- the canonical codec must parse the complete fixed schema;
- the parsed record kind must equal the explicitly expected kind;
- a fresh `PlaybackControlledWindowsIndependentRunSetBoundary` validates and consumes the record;
- video is accepted only as `VideoAccepted` and audio only as `AudioAccepted`;
- accepted preflight exits zero; malformed configuration or record exits two; semantic rejection exits one;
- the report contains no local path and explicitly states that no physical run was executed.

The dedicated controlled video PowerShell helper invokes this preflight only after the physical command exits successfully. The helper keeps the canonical record outside the repository, captures the preflight report only in a temporary repository-external file, deletes that temporary file, and rejects the run unless the report states `VideoAccepted`, expected kind `Video`, and `Record-Accepted: True`.

## Consequences

A zero exit from the video helper now means both:

1. the controlled physical command reported completion; and
2. the resulting external canonical record passed the exact admission contract that will later be used for the paired set.

The preflight starts no runner, opens no FFmpeg runtime or fixture, performs no decode or output, changes no record, and cannot turn a rejected physical result into an accepted one.

This decision does not claim that a Windows physical run occurred, that video was visible, that audio was audible, that an audio record exists, or that paired verification completed.
