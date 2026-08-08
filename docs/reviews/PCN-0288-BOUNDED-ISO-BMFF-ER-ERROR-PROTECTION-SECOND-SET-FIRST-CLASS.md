# PCN-0288: Bounded ISO BMFF ER Error Protection Second-Set First Class

## Scope

Review first-class parsing from the exact second-set header end.

## Findings

- A missing second-set header and an explicitly empty second set are distinct.
- Parsing starts at the exact bit position retained by the second-set header.
- Conditional escape, concatenation, FEC, interleave, length, rate, and CRC
  fields reuse the bounded class syntax reader.
- The projected class retains the second-set header rather than exposing the
  internal first-set syntax anchor.
- Truncation produces no partial class.
- No remaining class, reordered-output vector, later set, decoder, native
  operation, or physical execution occurs.

## Decision

Accept bounded second-set first-class parsing. Complete second-set and general
later-set traversal remain incomplete.
