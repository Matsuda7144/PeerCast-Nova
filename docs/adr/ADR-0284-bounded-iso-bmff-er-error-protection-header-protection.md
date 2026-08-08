# ADR-0284: Bounded ISO BMFF ER Error Protection Header Protection

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Decision

After the exact ADR-0283 predefined-set end, parse the 1-bit
`header_protection` flag. When enabled, parse the following 5-bit `header_rate`
and 5-bit `header_crclen`; when disabled, consume neither conditional field.
Retain the predefined-set aggregate and exact end, and return no partial result
on truncation.

## Non-goals

`directMapping`, complete AudioSpecificConfig continuation, decoder or native
integration, and physical execution are not included.
