# PCN-0256: Bounded ISO BMFF AVC Sample-only Annex-B Materialization

## Scope

Review owned Annex-B conversion of only the classified sample NAL sequence.

## Findings

- Invalid limits and missing classification fail before allocation.
- Every sample NAL uses an exact four-byte start code.
- Configuration parameter sets are never added by this boundary.
- Empty units and oversized projections fail without partial output.
- Output retains exact classification provenance and private ownership.
- No decoder, native operation, demux event, or physical execution occurs.

## Decision

Accept bounded sample-only Annex-B materialization. Automatic format and
injection decisions remain incomplete.
