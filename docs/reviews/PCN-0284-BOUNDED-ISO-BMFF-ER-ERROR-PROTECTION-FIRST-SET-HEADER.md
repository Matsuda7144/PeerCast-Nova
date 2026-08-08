# PCN-0284: Bounded ISO BMFF ER Error Protection First-Set Header

## Scope

Review first-set class-count parsing at the exact ADR-0273 prefix end.

## Findings

- Missing prefixes and zero-set configurations produce no set header.
- The exact prefix remains the source of the first-set declaration.
- Only the first 6-bit class count is consumed.
- Set index zero and the exact post-count position are retained.
- No class body or later set declaration is skipped or inferred.
- No decoder, native operation, or physical execution occurs.

## Decision

Accept bounded first-set header parsing. Per-class traversal and later set
continuation remain incomplete.
