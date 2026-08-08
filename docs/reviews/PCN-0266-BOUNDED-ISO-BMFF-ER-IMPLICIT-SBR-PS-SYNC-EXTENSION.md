# PCN-0266: Bounded ISO BMFF ER Implicit SBR/PS Sync Extension

## Scope

Review immediate implicit SBR/PS projection after supported ER `epConfig`.

## Findings

- ER parsing begins only at the exact post-config position.
- Existing marker and frequency validation is shared with the non-ER path.
- Marker absence is distinct from truncation after a recognized marker.
- Existing PS continuation retains the same exact source.
- No decoder, native operation, or physical execution occurs.

## Decision

Accept bounded ER implicit SBR/PS sync-extension parsing. Error Protection
Specific Config and decoder composition remain incomplete.
