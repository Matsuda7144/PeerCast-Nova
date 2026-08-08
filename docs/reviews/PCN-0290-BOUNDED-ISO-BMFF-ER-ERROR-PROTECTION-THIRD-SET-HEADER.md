# PCN-0290: Bounded ISO BMFF ER Error Protection Third-Set Header

## Scope

Review third-set class-count parsing from the exact second-set end.

## Findings

- Parsing is admitted only when the prefix declares at least three sets.
- The exact second set remains the source of the third-set declaration.
- Only the 6-bit class count is consumed; set index two and its end are retained.
- Truncation produces no partial header.
- No class body, later set, decoder, native operation, or physical execution
  occurs.

## Decision

Accept bounded third-set header parsing. Third-set bodies and general later-set
traversal remain incomplete.
