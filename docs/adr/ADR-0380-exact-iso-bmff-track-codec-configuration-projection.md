# ADR-0380: Exact ISO BMFF Track Codec-Configuration Projection

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Apply the existing bounded codec-configuration parsers to every exact ADR-0379
sample entry in source order. Parse the unique `avcC` child for `avc1` / `avc3`
entries and the unique `esds` elementary-stream descriptor for `mp4a` entries,
retain each exact sample-entry source, and apply caller-bounded parameter-set,
descriptor, and configuration-byte limits without returning partial results.

## Non-goals

AVC sample framing, AAC decoder-config composition, compressed payload
conversion, codec packet construction, runtime access, native execution,
rendering, or physical execution are not included.
