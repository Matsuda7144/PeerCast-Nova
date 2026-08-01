# ADR-0173: Bounded ISO BMFF Box-header Parser

## Status

Accepted as a post-0.0.10e non-physical parser prerequisite.

## Context

MP4 and fragmented MP4 use the ISO Base Media File Format box structure. Before
container traversal or codec/sample metadata can be added, callers need an
exact structural boundary for the variable-length box header.

The header can contain a normal 32-bit size, a 64-bit extended size, a zero
size extending to the containing input, and a 16-byte user type for `uuid`
boxes. Parsing a header must not require or retain its payload.

## Decision

Add a pure managed, stateless ISO BMFF box-header parser that:

- reads at most 32 bytes;
- uses unsigned big-endian size and type fields;
- supports normal, extended-size, extends-to-end, and `uuid` headers;
- reports the exact header and payload sizes without buffering payload bytes;
- preserves the `uuid` user type as two fixed unsigned values;
- returns `NeedMoreData` for incomplete structural headers; and
- rejects declared sizes smaller than the applicable header.

A zero size is represented explicitly as extending to the containing input. The
parser does not invent a total size without a container boundary.

## Consequences

Callers can incrementally recognize a structurally complete ISO BMFF box header
without allocating based on an untrusted declared payload size.

This is only a header primitive. It does not establish a complete MP4 or
fragmented MP4 parser.

## Non-goals

No box traversal, nesting, payload buffering, `ftyp` policy, movie metadata,
sample tables, fragment runs, media-data association, codec configuration,
seeking, decoding, recording, or physical execution is included.
