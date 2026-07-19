# ADR-0092: Diagnostic Native Parameter Transaction

## Status

Accepted for milestone 0.0.8h.

## Context

The diagnostic decoder configuration produces independent owned H.264 and AAC parameter leases. The existing native context lease owns its ABI gate and verified runtime parent, so two decoder contexts must not share one gate/runtime ownership chain.

## Decision

Add a finite diagnostic-only transaction that requires two distinct gate leases: one for H.264 and one for AAC. Create and configure video first, then audio. Transfer parameters through the existing native transfer boundary. On failure, release every acquired owner in audio-to-video order and consume the managed parameter owner.

The successful result owns two configured but unopened native decoder contexts. Codec open, packet submission, frame receive, production activation, and general demux remain outside this milestone.

## Consequences

- One context cannot invalidate the other by disposing a shared parent.
- Partial video success is unwound when audio creation or transfer fails.
- No new native symbol, parser, retry loop, background task, or production activation reference is introduced.
- A later milestone may open both configured contexts as a separate finite transaction.
