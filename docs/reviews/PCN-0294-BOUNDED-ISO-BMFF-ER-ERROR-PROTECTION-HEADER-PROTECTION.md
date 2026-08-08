# PCN-0294: Bounded ISO BMFF ER Error Protection Header Protection

## Findings

- Parsing begins at the exact predefined-set aggregate end.
- Disabled protection consumes only its 1-bit flag.
- Enabled protection retains both bounded 5-bit parameters.
- Truncation produces no partial result.
- No decoder, native operation, or physical execution occurs.

## Decision

Accept bounded header-protection parsing. `directMapping` and complete
AudioSpecificConfig continuation remain incomplete.
