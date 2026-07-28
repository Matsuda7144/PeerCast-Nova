# PCN-0167 — Managed Audio Render Failure Boundary

Status: Prospective non-physical hardening  
Milestone: 0.0.10d Phase 7  
Tracking issue: #129

## Purpose

Record the bounded failure gap exposed by the separately authorized exact-main
`774342a03c33713b1b7f70e2cb664397c04f8d04` run and define a prospective fix
without publishing or reconstructing private child output.

## Retained run facts

The consumed one-run evidence retained:

- child exit signed `1`, hexadecimal `00000001`;
- stdout byte count `0`;
- stderr byte count `3122`;
- final native audio stage `ActivationCleanupCompleted`;
- controlled run result `None`;
- terminal-record rejection `None`;
- no canonical completed or terminal record;
- version 5 sanitized child failure record SHA-256
  `F8B8A0553A6DDF0935BFD2C075FFE32E541659E3A13C1275130E655363B5FAF5`;
- separate operator observation of a recognizable beep and `Audio audible: Yes`.

These facts do not establish physical completion, completed evidence, formal audio
acceptance, stable delivery, paired admission, or completion of 0.0.10d.

## Static interpretation

A normal non-completed direct render-evidence command writes either:

1. one canonical terminal record; or
2. one exact `Result: <reviewed failure>` line.

The controlled runner has a separate top-level catch that writes only the fixed
`PCN-CONTROLLED-RUNNER-FAILURE: ManagedUnhandledException` marker and exits `1`.
That path is consistent with an exit `1` and zero stdout after native cleanup.
However, the retained version 5 child failure record does not store the private
stderr classification, so the historical marker cannot be proven and must not be
inferred.

The direct command previously allowed unexpected exceptions from operation
creation, composition execution, post-run snapshot capture, record projection,
outcome writing, or asynchronous disposal to escape to that runner-wide catch.

## Prospective correction

The direct managed command catches that bounded set of command-level failures and,
when no canonical outcome was already written:

- writes one exact `Result: Faulted` line;
- writes one exact allowlisted managed failure phase marker only to private stderr;
- writes the existing `ProjectionRejected` marker for snapshot or terminal-record
  projection failures;
- publishes no exception type, message, stack, path, endpoint, device identity,
  pointer, handle, HRESULT, packet, payload, or sample.

Child failure record version 6 prospectively retains the fixed managed phase as:

```text
Managed-Failure-Phase: <allowlisted phase or None>
```

The accepted phases are the exact direct-command lifecycle values:

- `OperationCreation`;
- `CompositionRun`;
- `SnapshotCapture`;
- `TerminalProjection`;
- `CompletedProjection`;
- `OutcomeWrite`;
- `CompositionDispose`.

The parser accepts exactly one canonical marker. Unknown, numeric, extended, or
duplicate markers collapse to `None`. Version 2 through version 5 records remain
strictly readable and have effective managed phase `None`. The existing version 5
schema remains the compatibility constant; the real child-capture failure path
selects version 6 when the phase boundary is available.

The offline failure-record preflight surfaces the managed phase only for a valid
version 6 record with a non-`None` phase. Legacy report shape is unchanged.

If a canonical terminal record was already written and only later disposal fails,
the existing exit-`1` terminal admission remains available. A completed record is
not promoted when later cleanup fails.

## Helper reporting terminology

The PowerShell message:

```text
Sanitized helper failure record retained: No
```

reports only whether the helper created an additional helper-level failure record.
It is expected to say `No` when a sanitized child-capture failure record already
occupies the exclusive failure path. It does not mean that no sanitized child
failure record exists.

## Safety boundary

This correction does not change delivery selection, timing, decode, scheduler
ownership, endpoint selection, audio-buffer duration, sample format, native write,
observation hold, cleanup ordering, readiness, authorization, or physical-output
behavior.

No readiness capture, standalone preflight, authorization entrypoint, physical
helper, ordinary audio command, probe, diagnostic, paired verification, or audio
device command was executed while producing this review.

## Authorization state

Every readiness-capture and physical-run authorization through the consumed
`774342a...` run remains permanently consumed. Existing readiness and outcome
paths are not reusable. A later run requires a new exact-main selection, new
repository-external paths, a newly authorized readiness capture, independent
readiness review, and a separate explicit one-run physical authorization.
