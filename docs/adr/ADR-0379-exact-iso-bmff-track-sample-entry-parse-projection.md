# ADR-0379: Exact ISO BMFF Track Sample-Entry Parse Projection

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Revalidate every exact ADR-0378 selection against the caller-provided `stsd`
bytes in source order. Parse `mp4a` selections through the bounded audio
sample-entry parser and `avc1` / `avc3` selections through the bounded visual
sample-entry parser, retain each exact selection and parsed entry, and apply one
caller-bounded child-box limit to the complete projection.

## Non-goals

Codec-specific child selection, `avcC` / `esds` composition, compressed payload
conversion, codec packet construction, runtime access, native execution,
rendering, or physical execution are not included.
