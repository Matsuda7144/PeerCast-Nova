# ADR-0376: Bounded ISO BMFF Track Owned Sample-Payload Projection

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Copy every exact ADR-0375 sample payload, in source order, into independently
owned immutable bytes under the existing caller-configurable per-payload hard
limit. Retain exact media-data provenance, source file length, configured
maximum, and overflow-safe total copied byte count. Any invalid location,
empty payload, or limit failure rejects the whole projection.

## Non-goals

Sample-description parsing, AVC/AAC composition, codec packet construction,
runtime access, native execution, rendering, or physical execution are not
included.
