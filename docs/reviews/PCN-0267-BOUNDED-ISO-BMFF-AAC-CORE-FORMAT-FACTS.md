# PCN-0267: Bounded ISO BMFF AAC Core Format Facts

## Scope

Review source-bound AAC declared-rate and core-channel-count resolution.

## Findings

- Indexed configurations one through seven map exactly to MPEG-4 channel counts.
- Configuration zero requires the exact parsed PCE topology.
- Reserved indices and zero-channel topology fail closed.
- Declared sampling frequency is not presented as SBR-adjusted output rate.
- No decoder, native operation, or physical execution occurs.

## Decision

Accept bounded AAC core format facts. Effective SBR/PS output format and decoder
composition remain incomplete.
