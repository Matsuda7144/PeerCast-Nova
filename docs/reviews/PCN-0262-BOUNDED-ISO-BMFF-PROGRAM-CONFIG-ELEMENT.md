# PCN-0262: Bounded ISO BMFF Program Config Element

## Scope

Review bounded traversal of the variable PCE body after ADR-0251.

## Findings

- Conditional mixdown fields are read only when their presence bit is set.
- All element arrays are bounded by fixed-width header counts.
- Truncation and nonzero alignment padding fail distinctly.
- The maximum 255-byte comment is copied into owned storage.
- No channel-layout inference, decoder, native operation, or physical execution occurs.

## Decision

Accept bounded syntactic PCE parsing. Channel-layout semantics and GA
continuation remain incomplete.
