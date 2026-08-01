# ADR-0224: Bounded ISO BMFF Edited-sample Payload View

## Status

Accepted as a post-0.0.10e non-physical memory slice.

## Context

ADR-0223 identifies the exact file range and containing `mdat` for one admitted
sample. A non-owning payload view must verify that metadata against the current
file rather than trusting a location obtained from different bytes.

## Decision

Add a pure managed span view that:

- requires edited-sample media-data metadata;
- revalidates the sample against the current file length;
- reparses and matches the recorded top-level box location;
- revalidates `mdat` containment; and
- returns the exact sample slice without allocation.

Zero-size structural samples remain valid empty views. The returned span is
explicitly non-owning and reflects subsequent source-memory changes.

## Consequences

Callers can inspect an exact validated sample slice without confusing it with
owned packet memory.

## Non-goals

No ownership transfer, packet creation, codec parsing, decoder submission,
recording, or physical execution is included.
