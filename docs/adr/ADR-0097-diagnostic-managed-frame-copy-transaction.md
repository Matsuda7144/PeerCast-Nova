# ADR-0097 Diagnostic Managed Frame Copy Transaction

## Status

Accepted for milestone 0.0.8m.

## Context

0.0.8l validates callback-scoped video plane and audio sample views while keeping native pointers inside the receive callback. The next diagnostic boundary must preserve frame content beyond the callback without retaining any native pointer or callback-only view.

The existing managed frame copy boundary already copies only validated logical row or sample bytes into owned managed buffers and returns an `IDisposable` managed frame.

## Decision

- Submit exactly one bounded H.264 packet, then attempt exactly one managed video frame copy.
- Submit exactly one bounded AAC packet, then attempt exactly one managed audio frame copy.
- Accept `Copied` only when exactly one managed frame was captured.
- Accept `NeedMoreInput` only when no managed frame was captured.
- On success, transfer ownership of any copied frames and the aggregate native decoder lease to one completion object.
- Dispose successful completion in reverse ownership order: audio frame, video frame, then aggregate native lease.
- On any rejected, contradictory, or faulted path, dispose any copied audio frame, copied video frame, and the complete aggregate lease before returning.
- Keep conversion, decoded output adaptation, sink delivery, packet loops, flush, demux, production activation, redistribution, and approval-state changes outside this milestone.

## Consequences

No native frame pointer, plane pointer, sample pointer, or callback-scoped view escapes its callback. Managed frame bytes may survive only through the explicit disposable completion ownership boundary. The transaction remains finite and diagnostic-only.
