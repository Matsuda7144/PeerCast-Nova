# ADR-0166: Completed Render Evidence Paired Admission

## Status

Accepted

## Context

The 0.0.10d independent run-set command originally admitted one canonical
19-line physical video record and one canonical 19-line physical audio record.
Later controlled audio hardening replaced the audio acceptance candidate with a
strict 26-line completed render-evidence record containing the scheduler,
WASAPI, native aggregate, hold-start, pre-cleanup, and post-stop evidence needed
to establish technical completion.

The retained run from exact main
`6eb3558ec0efd434d9ee1e7221ba86d3c115684f` produced that strict completed
record. It passed its independent completed-record preflight, and the operator
separately reported both `Audio audible: Yes` and a recognizable retained-fixture
beep. The old paired command could not parse the newer record header, so a
non-physical format connection was still required.

Converting the new record into a legacy one-delivery record would discard its
four-delivery contract and would create evidence that was never retained.

## Decision

The independent run-set boundary accepts the strict completed audio
render-evidence record directly as an audio admission candidate.

- The existing legacy physical audio record remains readable.
- The accepted video input remains the canonical physical run record.
- The audio input is parsed first as the legacy record and then, with a
  disjoint header, as the strict completed render-evidence record.
- The render-evidence codec must validate its full checkpoint and native
  evidence contract before the run-set boundary can consume it.
- The run-set boundary independently checks current schema, milestone, audio
  kind, runtime identity, fixture identity, completed/accepted result, exact
  four-delivery counts, physical-run provenance, stopped/disposed cleanup,
  preserved borrowed owners, no audio-acceptance claim, and no publication.
- No synthetic legacy record is created.
- The report remains bounded and path-free and continues to state
  `Physical-Runs-Executed-By-This-Command: False`.

## Consequences

Paired admission can now use the retained accepted video record and the strict
completed audio render-evidence record without replaying either physical run.
Terminal and failure records remain rejected. The change starts no runner,
runtime, fixture replay, endpoint, decoder, scheduler, or audio/video device.

The retained records must be admitted again using a build from the final merged
main before 0.0.10d is marked complete.
