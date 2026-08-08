# PCN-0255: Bounded ISO BMFF AVC Annex-B Payload Materialization

## Scope

Review deterministic owned Annex-B materialization from the bounded
parameter-set prefix plan.

## Findings

- Invalid limits and missing plans fail before allocation.
- Every NAL unit uses an exact four-byte start code.
- Empty units and oversized projected payloads fail without partial output.
- Configuration parameter sets remain ordered before original sample units.
- Output is privately owned and bounded by the existing 4 MiB packet limit.
- No decoder, native operation, demux event, or physical execution occurs.

## Decision

Accept bounded Annex-B materialization. Automatic production injection and
decoder-format selection remain incomplete.
