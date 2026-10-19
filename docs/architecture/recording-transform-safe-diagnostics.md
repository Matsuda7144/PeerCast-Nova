# Safe recording transform diagnostics

This is optional evidence for a future explicitly authorized run. It does not
change recording, FLV admission, timestamps, exception propagation, terminal
History, retries or cleanup. No physical run is authorized by this document.

## Activation

Diagnostics are disabled by default. Before a controlled normal Nova.App GUI
startup, set the **process-scoped** environment variable
`NOVA_RECORDING_TRANSFORM_DIAGNOSTICS_DIRECTORY` to an explicitly chosen,
fully qualified **new Run-ID directory outside any Git repository**.
Pass it only to that process; do not persist it as a user/system setting.
Normal production recording composition reads this variable once during creation.
No GUI toggle, diagnostic runner, alternate recording pipeline or network call
is introduced. Unset/empty/invalid/unavailable output disables evidence only.

The directory must not already exist. Repository ancestors and reparse-point
ancestors are rejected. The file is created with CreateNew and never overwritten:
`recording-transform-diagnostics.jsonl`. Choose a new directory for each run.
Activation can leave an empty directory if file creation fails; it never deletes
a directory or changes a recording result.

## Interpretation

Schema version 1 contains only:

- EventKind: WriterAdmitted, TransformFailed or Terminal
- ActualSessionId: the existing sink-owned recording session GUID
- TransformPhase: Unknown, DecodableStart or TimestampNormalization
- ThrowSiteId: a stable named logical site (42 distinct IDs; Unknown = 0)
- SafeReason: a fixed input-rejection classification
- FaultStage: the existing RecordingFailureStage enum
- SafeExceptionType: Unknown or RecordingStreamTransformException
- TerminalKind: null before terminal, otherwise the existing lifecycle enum

A failure is annotated at the existing InvalidFlv helper call. The original
IOException-derived RecordingStreamTransformException and raw Message remain
unchanged internally. The sink's existing writer failure callback joins that
typed metadata to **its actual session**, without parsing a message or stack.
The transform never knows SessionId. Terminal is emitted after the existing
History publication; it does not create a second History fact.

Reason groups rejection conditions; Site distinguishes their source locations.
Neither proves whether a malformed upstream input or a code defect caused the
rejection. Unknown never falls back to raw exception text. Empty audio/video
headers use generic audio/video payload reasons, since the codec is not known.
ReadExactlyTruncated is one shared logical site per transform; it does not
disclose the dynamic description. Some existing guards are shadowed or bounded
out by preceding validation; they retain distinct IDs without changing reachability.
NoDecodableVideoStart on clean EOF/Stop is not a transform exception.

WriterAdmitted identifies a writer entering the existing sink write path; it
does not prove FLV bytes arrived, a file completed, or physical recording worked.
A Terminal event may contain Unknown transform metadata for a non-transform
outcome. Correlate it by ActualSessionId with History, never by channel name,
file name or a newly generated GUID. Existing History is not backfilled.

## Bounds, privacy and failure isolation

Callbacks perform no file I/O or await: TryEnter/TryWrite drops on contention or
overflow. Queue capacity is 128; retained session identities are capped at 256;
global admission is capped at 2048 records; deduplication permits at most three
records per session (one of each event kind). File size is capped at 1 MiB.
The single worker writes/flushes fixed-schema JSON lines. There are no retries.
Shutdown drains best effort with a two-second wait/cancellation bound. A stalled
OS operation may outlive that wait under worker ownership; it cannot hold recorder
shutdown. Rejected, dropped or lost evidence must be reported as unobserved.

No free text, raw Message, stack, inner exception, URI, tracker, payload, channel
title, output path, wall clock or environment values are part of the schema.
Undefined enum values and empty session GUIDs are rejected. Observer exceptions
are ignored; the original recording failure and cleanup remain authoritative.

## Verification scope

Managed synthetic inputs and temporary stores cover classification, session
correlation, privacy, bounds, optional output and behavior preservation.
They do not identify the historical failure's throw-site or prove a future
physical run. A new controlled physical recording requires separate explicit
authorization and can add a new Faulted History entry. Cause repair remains a
separate task after evidence.
