# ADR-0275: Bounded ISO BMFF ER Error Protection First Class

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0274 exposes the exact first-set class count and position. The first class
contains conditional control and fixed-value fields whose end can be determined
without crossing into another class.

## Decision

Add a pure managed parser that:

- requires the exact ADR-0274 first-set header and at least one declared class;
- projects length, rate, and CRC-length escape flags;
- conditionally projects concatenation, termination, and interleave values from
  the enclosing fixed-prefix modes;
- projects the FEC type and optional-class flag;
- reads either the 4-bit in-band length width or 16-bit fixed class length;
- reads a fixed class rate only when it is not escaped, using five bits for FEC
  type zero and seven bits otherwise;
- reads a 5-bit fixed CRC length only when it is not escaped; and
- retains the exact first-set header and consumed bit position.

## Consequences

The first class has a complete syntactic boundary suitable for a later bounded
set traversal without treating conditional fields as fixed width.

## Non-goals

FEC semantic validation, remaining classes, set reordering, later sets, header
protection, complete config traversal, `directMapping`, decoder configuration,
native calls, and physical execution are not included.
