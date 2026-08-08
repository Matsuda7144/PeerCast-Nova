# PCN-0251: Bounded ISO BMFF Implicit PS Sync Extension

## Scope

Review optional secondary PS sync-extension parsing after implicit SBR.

## Findings

- Missing primary result and primary SBR false are distinct.
- Short or mismatched secondary marker input reports optional absence.
- Exact marker `0x548` projects both false and true PS presence values.
- A marker without its presence bit returns `NeedMoreData` without output.
- Source and exact ending bit provenance are retained.
- No decoder, native operation, demux event, or physical execution occurs.

## Decision

Accept the bounded immediate implicit-PS boundary. Decoder and channel-layout
semantics remain incomplete.
