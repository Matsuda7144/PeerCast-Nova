# PCN-0283: Bounded ISO BMFF ER Error Protection Specific Config Prefix

## Scope

Review the fixed 16-bit Error Protection Specific Config prefix after `epConfig`
two or three.

## Findings

- Parsing begins at the exact ADR-0267 post-`epConfig` bit position.
- All four fixed-width fields retain exact bit order.
- The default set limit is 64 and cannot be configured above the 8-bit maximum.
- Missing, not-required, invalid-limit, over-limit, and truncated inputs produce
  no partial prefix.
- Set class counts, class bodies, and the overall configuration end are not
  skipped or inferred.
- No `directMapping`, decoder, native operation, or physical execution occurs.

## Decision

Accept bounded Error Protection Specific Config fixed-prefix parsing. Set and
per-class parsing, complete continuation, and decoder integration remain
incomplete.
