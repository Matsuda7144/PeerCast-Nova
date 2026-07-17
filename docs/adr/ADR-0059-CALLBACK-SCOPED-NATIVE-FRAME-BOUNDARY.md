# ADR-0059 Callback-Scoped Native Frame Boundary

## Status

Accepted for milestone 0.0.6j.

## Context

Milestone 0.0.6i can submit encoded packets and observe whether a decoded frame is available, but it releases the temporary `AVFrame` before any later extraction stage can inspect it. Returning a raw frame pointer or a long-lived child lease would couple frame lifetime to the decoder context, verified module lease, and native free function.

## Decision

A received `AVFrame` remains owned by the native boundary. The boundary allocates one frame, calls `avcodec_receive_frame`, invokes one internal consumer while the frame is valid, and frees the frame before returning on every path.

The frame pointer is never exposed through a public result or retained after the callback. Only an opened, live decoder context with the fixed frame symbols may enter the boundary. `EAGAIN`, EOF, allocation failure, native rejection, consumer rejection, and unexpected native failure map to fixed privacy-safe result categories.

Consumer rejection does not invalidate the decoder context. Unexpected native failures dispose the context fail closed.

## Consequences

- Native frame ownership is finite and locally auditable.
- Parent context and loaded modules remain valid for the complete callback.
- A consumer must copy or transform any required data before returning.
- Frame metadata extraction, plane validation, pixel/audio conversion, demux, queues, rendering, devices, and activation remain separate milestones.
