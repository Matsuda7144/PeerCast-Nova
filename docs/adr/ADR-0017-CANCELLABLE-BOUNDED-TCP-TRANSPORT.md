# ADR-0017: Cancellable and Bounded TCP Transport

- Status: Accepted
- Decision: Every blocking transport operation accepts a cancellation token.
- Decision: Outbound connection attempts have a finite timeout.
- Decision: Listener backlog and accepted-connection concurrency are finite.
- Decision: Transport remains byte-oriented and knows nothing about PCP.
- Reason: A P2P node must remain stoppable and resource-bounded under failure or abuse.
