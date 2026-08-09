# ADR-0476: Selected ISO BMFF Regular Movie Managed Decoder Preparation

## Status

Accepted as the first selected-stream decoder-preparation ownership bridge
following ADR-0475.

## Decision

Require an exact ADR-0475 movie composition and preserve its sequence-set,
selection, cursor, merge-traversal, and selected-track source identities. For a
selected AVC track, require one consistent `avc1` or `avc3` configuration across
the bounded track window, retain its caller-explicit payload requirements, and
copy the exact `avcC` configuration record into an owned managed preparation
input.

For a selected AAC track, require one consistent `mp4a` elementary-stream
descriptor across the bounded window. Parse its decoder configuration,
AudioSpecificConfig, optional explicit SBR/PS prefix, and ordinary GA config;
resolve core and extension facts and a decoder format intent; then reuse the
existing sample-entry consistency, managed codec-parameter, transfer-plan,
declared-symbol, and declared-capability preparation chain. The caller must
provide the AAC symbol declaration, capability bounds, and consistency policy.
Reject the first failed stage without partial output, and dispose the owned AAC
parameter lease with the aggregate preparation.

## Consequences

A selected regular MP4 AVC/AAC movie now has one finite managed preparation
boundary. AVC configuration bytes survive caller-buffer mutation, while
ordinary AAC-LC reaches the existing owned managed parameter lease without
loading or invoking a runtime. Video-only selection does not require AAC
capability input.

## Non-goals

AVC managed codec-parameter allocation or transfer planning, program-config
AAC and implicit SBR search in this aggregate path, verified installed-runtime
capability binding, native parameter transfer, decoder open, packet submission,
scheduling, delivery, rendering, application integration, fragmented MP4, and
physical execution are not included.
