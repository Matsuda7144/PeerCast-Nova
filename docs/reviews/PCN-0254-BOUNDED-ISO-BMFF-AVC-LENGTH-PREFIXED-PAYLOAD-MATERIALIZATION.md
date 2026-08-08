# PCN-0254: Bounded ISO BMFF AVC Length-prefixed Payload Materialization

## Scope

Review owned length-prefixed byte materialization from the bounded parameter-set
prefix plan.

## Findings

- Invalid limits and missing plans fail before allocation.
- All configured prefix widths from one through four bytes are covered.
- Length representability and exact plan byte projection are revalidated.
- Configuration parameter sets remain ordered before original sample units.
- Output is privately owned and bounded by the existing 4 MiB packet limit.
- No decoder, native operation, demux event, or physical execution occurs.

## Decision

Accept bounded length-prefixed materialization. Automatic production injection
policy remains incomplete.
