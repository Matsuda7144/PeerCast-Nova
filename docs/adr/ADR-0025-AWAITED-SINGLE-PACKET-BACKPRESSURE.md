# ADR-0025: Await One Owned Packet Before Decoding the Next Atom

- Status: Accepted
- Decision: Permit the incremental decoder to yield after one complete
  top-level Atom and report its consumed position as the examined position.
- Decision: Copy `pkt/data` from callback-scoped Pipe memory into at most one
  finite pooled buffer, then release it after the async packet consumer returns.
- Decision: Await the consumer before asking the PipeReader for more input or
  decoding a buffered following top-level Atom.
- Decision: Pass the owning session cancellation token to every consumer. A
  consumer must observe it and return promptly.
- Reason: Synchronous framing callbacks cannot await, and PipeReader memory
  cannot outlive `AdvanceTo`. One owned packet bridges that lifetime without an
  unbounded queue.
- Reason: Yielding with `Examined == Consumed` allows already-buffered following
  data to be returned by the next `ReadAsync`; marking the entire buffer examined
  could otherwise wait indefinitely for new network data.
- Consequence: Core state records a packet when framing accepts it, before the
  async consumer completes. Consumer failure terminates the owning session; it
  does not roll Core position backwards.
- Consequence: Payload memory is valid only during `ConsumeAsync`. A consumer
  that needs longer ownership must make its own bounded copy under its explicit
  relay, player, or recorder policy.
- Consequence: This boundary owns no socket and performs no handshake. The next
  session layer must hand it the same PipeReader after OK and configure finite
  StreamPipeReader behavior.
- Scope: This is ingress backpressure, not multi-consumer relay fan-out.
