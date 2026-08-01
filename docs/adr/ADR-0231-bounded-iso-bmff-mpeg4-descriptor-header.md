# ADR-0231: Bounded ISO BMFF MPEG-4 Descriptor Header

## Status

Accepted as a post-0.0.10e non-physical parser slice.

## Context

The `esds` payload uses MPEG-4 descriptor tags followed by a one-to-four-byte
7-bit continuation length. ES, decoder-config, decoder-specific, and SL-config
parsers must share one exact bounded interpretation.

## Decision

Add a pure managed descriptor-header parser that:

- parses one tag and one-to-four length bytes;
- accepts non-minimal encodings used by existing files;
- rejects unterminated four-byte continuation encodings;
- defaults to and cannot exceed the existing 1 MiB codec-extradata limit;
- verifies complete payload availability; and
- optionally requires exact input consumption or permits a container suffix.

## Consequences

Later `esds` hierarchy parsing can traverse descriptors without duplicating
variable-length arithmetic or weakening allocation bounds.

## Non-goals

No descriptor-body semantics, AAC configuration, demux event, decoder
submission, recording, or physical execution is included.
