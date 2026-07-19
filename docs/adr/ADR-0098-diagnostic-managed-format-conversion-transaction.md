# ADR-0098 Diagnostic Managed Format Conversion Transaction

## Status

Accepted for milestone 0.0.8n.

## Decision

- Submit exactly one bounded H.264 packet and one bounded AAC packet.
- Copy at most one managed frame per stream and immediately pass copied frames to the existing managed format conversion boundary.
- Use fixed diagnostic sequences: video 0 and audio 1, with no discontinuity.
- Accept a converted stream only when the conversion result is `Converted` and exactly one callback of the expected media kind was observed.
- Accept `NeedMoreInput` only when no managed frame, conversion state, or output summary exists for that stream.
- Retain only managed output summaries and the aggregate native decoder lease after success.
- Dispose copied input frames immediately after conversion callback completion.
- Dispose all copied frames and the complete aggregate lease on rejected, contradictory, or faulted paths.

## Consequences

Converted payload remains callback-scoped and is cleared by the existing conversion boundary. No converted bytes, native pointers, copied input frames, sink delivery, queueing, demux, packet loop, production activation, redistribution, or approval-state change is introduced.
