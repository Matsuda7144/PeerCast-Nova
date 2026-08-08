# PCN-0285: Bounded ISO BMFF ER Error Protection First Class

## Scope

Review conditional first-class parsing after the exact ADR-0274 set header.

## Findings

- Missing and zero-class inputs produce no class projection.
- Escape and fixed-value branches consume their exact field widths.
- Concatenation, termination, and interleave fields are present only when their
  enclosing modes require them.
- Fixed rate width follows the projected FEC type.
- Truncation at any conditional field produces no partial class.
- No later class, reordering, decoder, native operation, or physical execution
  occurs.

## Decision

Accept bounded first-class parsing. Remaining class and set traversal, reordering,
header protection, and complete continuation remain incomplete.
