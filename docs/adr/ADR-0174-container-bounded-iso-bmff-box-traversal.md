# ADR-0174: Container-bounded ISO BMFF Box Traversal

## Status

Accepted as a post-0.0.10e non-physical parser prerequisite.

## Context

ADR-0173 parses one ISO BMFF box header without reading its payload. The next
structural step is to advance across boxes inside a caller-provided, complete
container while preventing declared sizes from escaping that container.

The traversal boundary must resolve a zero-sized box relative to its containing
range, not an unbounded external stream.

## Decision

Add a stateless sequence reader that accepts a managed container span and an
offset and:

- returns end only when the offset equals the container length;
- rejects offsets outside the container;
- reuses the ADR-0173 header parser;
- resolves a zero-sized box to the remaining containing range;
- rejects incomplete structural headers and boxes extending beyond the
  container as truncated;
- rejects structurally undersized headers as invalid;
- exposes exact box, payload, and next offsets; and
- never reads, copies, allocates, or interprets payload bytes.

The passed span is treated as the complete containing range for this call.

## Consequences

Callers can walk a complete managed ISO BMFF container one box at a time while
every returned range remains inside the supplied memory.

This does not create a streaming parser or decide which box types are
containers.

## Non-goals

No incremental byte accumulation, recursive nesting policy, payload semantics,
movie or track metadata, sample tables, fragment runs, media-data association,
decoding, recording, or physical execution is included.
