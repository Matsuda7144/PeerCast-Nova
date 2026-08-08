# PCN-0243: Bounded ISO BMFF Elementary Stream Descriptor

## Scope

Review bounded `esds` FullBox and outer ES descriptor hierarchy parsing for the
selected `mp4a` entry.

## Findings

- Missing, duplicate, stale, non-audio, and malformed `esds` inputs fail closed.
- FullBox version and flags, outer descriptor tag, exact length, and owned byte
  limit are explicit.
- Optional dependency, URL, and OCR fields are bounds-checked before projection.
- Direct child traversal has configurable defaults and hard limits.
- Decoder-config and SL-config descriptors are unique and ordered; their body
  semantics are not inferred.
- Child payload views accept only locations owned by the parsed descriptor.
- No AAC decoding, demux event, native operation, or physical execution occurs.

## Decision

Accept bounded elementary-stream descriptor parsing. Decoder-config body,
decoder-specific AAC configuration, and SL-config semantics remain incomplete.
