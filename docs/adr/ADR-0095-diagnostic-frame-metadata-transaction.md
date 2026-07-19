# ADR-0095: Diagnostic Frame Metadata Transaction

## Status

Accepted for Milestone 0.0.8k.

## Context

0.0.8j reports one bounded frame-receive state per opened H.264/AAC context, but that state-only boundary frees its temporary `AVFrame` before returning. Metadata therefore cannot be extracted later from the 0.0.8j completion object.

## Decision

- Replace each state-only receive attempt in this milestone with the existing callback-scoped metadata boundary.
- Submit exactly one H.264 packet, receive/extract at most one video frame, then submit exactly one AAC packet and receive/extract at most one audio frame.
- Accept `Extracted` only when the callback produced managed metadata.
- Accept `NeedMoreInput` only when no metadata was produced.
- Reject every other send or metadata state and dispose the aggregate lease audio-first then video.
- Retain only bounded managed metadata after the callback returns.
- Preserve the aggregate lease on success so later diagnostic milestones may continue.

## Consequences

No native frame pointer, plane pointer, packet buffer, or native error value escapes the existing boundaries. Plane/sample access, managed payload copy, conversion, packet loops, flush, demux, output delivery, production activation, redistribution, and legal approval remain out of scope.
