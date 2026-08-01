# ADR-0222: Bounded ISO BMFF Edited-sample Byte Range

## Status

Accepted as a post-0.0.10e non-physical composition slice.

## Context

ADR-0221 identifies a numbered sample with one or more complete edited movie
appearances. Its storage lookup must use that same sample number rather than a
separately supplied value.

## Decision

Bind the sample number into the edited-admission result and add a pure managed
lookup that:

- requires an edited-sample admission;
- uses its bound sample number for the existing reconciled chunk/size lookup;
- retains the admission, exact absolute sample byte range, and resolved chunk
  location including sample-description index together; and
- preserves reconciliation, range, and offset-overflow failures.

## Consequences

Callers cannot accidentally associate an admission with a byte range requested
for a different sample number.

## Non-goals

No file traversal, payload read, packet creation, decoder submission,
recording, or physical execution is included.
