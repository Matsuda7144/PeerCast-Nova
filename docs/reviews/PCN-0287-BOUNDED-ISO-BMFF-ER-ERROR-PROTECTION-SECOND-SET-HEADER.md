# PCN-0287: Bounded ISO BMFF ER Error Protection Second-Set Header

## Scope

Review second-set class-count parsing from the exact first-set end.

## Findings

- Parsing is admitted only when the fixed prefix declares at least two sets.
- The exact first set remains the source of the second-set declaration.
- Only the second set's 6-bit class count is consumed.
- Set index one and the exact post-count position are retained.
- Truncation produces no partial header.
- No second-set class, later set, decoder, native operation, or physical execution
  occurs.

## Decision

Accept bounded second-set header parsing. Second-set bodies and general later-set
traversal remain incomplete.
