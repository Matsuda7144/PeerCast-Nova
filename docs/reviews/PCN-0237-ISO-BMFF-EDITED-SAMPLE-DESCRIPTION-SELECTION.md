# PCN-0237: ISO BMFF Edited-sample Description Selection

## Scope

Review selection and handler reconciliation of the sample's bound `stsd` entry.

## Findings

- The one-based index comes from the same resolved `stsc` chunk location as the
  byte range.
- Supported candidate types are limited to `avc1`, `avc3`, and `mp4a`.
- Video/audio handler mismatches fail closed.
- Unsupported entry types remain distinct from malformed index metadata.
- No entry payload, codec configuration, decoder, or native operation is used.

## Decision

Accept edited-sample description selection. Codec-specific sample-entry parsing
remains incomplete.
