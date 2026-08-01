# ADR-0175: Bounded ISO BMFF File-type Box Parser

## Status

Accepted as a post-0.0.10e non-physical parser prerequisite.

## Context

ADR-0173 and ADR-0174 establish box-header parsing and complete-container
traversal without payload interpretation. The first recognized ISO BMFF payload
can be the file-type (`ftyp`) box because it has a small fixed prefix followed
by a simple repeated brand field.

Compatible-brand counts still come from untrusted input and require an explicit
projection bound.

## Decision

Add a pure managed parser for one complete `ftyp` box that:

- accepts ordinary, extended-size, and extends-to-end headers;
- requires the declared size to match the supplied complete box;
- requires the `ftyp` type;
- reads the major brand and minor version as unsigned big-endian values;
- validates the compatible-brand payload as exact 4-byte entries;
- limits compatible brands to a configurable maximum of 32 by default and 256
  as a hard limit;
- checks the limit before allocating the immutable projection; and
- returns fixed categories for configuration, completeness, length, type,
  payload, and limit failures.

This parser projects brand values but makes no acceptance or compatibility
decision from them.

## Consequences

Callers can recognize bounded file-type metadata without retaining the input
box or allocating according to an unlimited brand count.

Other ISO BMFF box payloads remain opaque.

## Non-goals

No supported-brand policy, MIME decision, nested-container policy, movie or
track metadata, sample tables, fragments, media-data association, decoder
composition, recording, or physical execution is included.
