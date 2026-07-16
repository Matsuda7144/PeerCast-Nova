# ADR-0019: Synchronous Sink at the Framing Boundary

- Status: Accepted
- Decision: Framing callbacks are synchronous.
- Reason: Leaf payloads reference PipeReader-owned memory that expires at AdvanceTo.
- Consequence: Consumers must copy payloads before returning when longer ownership is required.
- Consequence: Async backpressure will be implemented above framing with explicit owned buffers.
