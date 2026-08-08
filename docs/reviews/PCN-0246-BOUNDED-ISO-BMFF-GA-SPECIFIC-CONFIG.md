# PCN-0246: Bounded ISO BMFF GA Specific Config

## Scope

Review bit-precise GASpecificConfig projection from the owned
AudioSpecificConfig header boundary.

## Findings

- Unsupported object types and channel configuration zero remain explicit.
- Core-coder delay is read only when its dependency flag is present.
- Scalable layer, ER BSAC, and resilience fields are conditioned on their
  defining object types and extension flag.
- Truncation at conditional fields returns `NeedMoreData` without partial
  output.
- The result retains its source header and exact consumed bit length.
- No decoder, native operation, demux event, or physical execution occurs.

## Decision

Accept bounded GASpecificConfig parsing. Program Config Element, SBR/PS
extension, and channel-layout semantics remain incomplete.
