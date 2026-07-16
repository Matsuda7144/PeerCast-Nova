# ADR-0026: One Owner for the TCP, HTTP, and PCP Channel Session

- Status: Accepted
- Decision: One `PcpChannelSession` owns each connection attempt from TCP
  connect through HTTP relay entry, HELO/OLEH/OK negotiation, post-handshake
  CHAN reading, Pipe completion, and connection disposal.
- Decision: The handshake and stream reader share the exact PipeReader. The
  handshake leaves bytes after OK unread for the stream reader.
- Decision: One stable local channel-session ID, Core state adapter, stream
  position, packet sequence, and packet consumer survive finite reconnects.
- Decision: Retry only connection, I/O, timeout, and unexpected-end failures,
  and only up to the configured attempt limit. RelayFull, HTTP/protocol
  rejection, malformed input, QUIT, and consumer failure are terminal.
- Decision: Bound connect, handshake, stream-idle, reconnect delay, Pipe read
  sizes, Atom content, packet content, and known text. No retry or queue is
  unbounded.
- Decision: Provide a nonblocking cooperative stop request for callbacks and an
  awaitable stop operation for owners. Cancellation closes the reader and TCP
  connection through the same ownership path.
- Decision: Expose only aggregate terminal counts and lifecycle outcomes from
  the session. Do not include identifiers, peer addresses, metadata, payload
  content, stream positions, or exception text.
- Reason: Splitting HTTP and PCP ownership can discard bytes already buffered
  after OK, leak Pipe completion responsibility, or race socket shutdown.
- Reason: Automatically retrying a failed data consumer can replay bytes and
  duplicate side effects. Protocol rejection is not a transport outage.
- Consequence: A reconnect relay request uses the last accepted Core position,
  while an explicit discontinuity does not silently move that position back.
- Consequence: Awaited single-consumer backpressure remains the ingress policy.
  The separate bounded relay fan-out was added in 0.0.4a under ADR-0028;
  player and recorder policies remain separate later work.
- Consequence: The outer `NovaApplication` module-stop deadline remains the
  final safety boundary if a consumer violates its cancellation contract.
