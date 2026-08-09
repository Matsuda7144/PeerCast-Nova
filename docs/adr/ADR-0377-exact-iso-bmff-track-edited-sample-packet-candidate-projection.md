# ADR-0377: Exact ISO BMFF Track Edited-Sample Packet-Candidate Projection

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Apply the existing edited-sample packet-candidate factory to every exact
ADR-0376 owned payload in source order. Require the same media-timing object
used by the edit-admission projection and retain each exact owned payload,
presentation interval, complete edit appearances, media timescale, and
candidate without copying bytes again.

## Non-goals

Sample-description selection, AVC/AAC payload composition, codec packet or
track packet construction, runtime access, native execution, rendering, or
physical execution are not included.
