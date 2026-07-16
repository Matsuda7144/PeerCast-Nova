# PCN-0029 Fault-Resilient Relay Accounting

## Boundary

```text
PcpChannelSession
  -> one callback-scoped PcpChannelPacket
  -> coordinator-owned RelayIngressConsumer
       -> await PcpRelayFanout.ConsumeAsync
       -> increment accepted packet/byte totals
  -> bounded child queues
  -> downstream sessions
```

The ingress consumer does not copy or retain payload. It delegates the same
callback-scoped packet to the existing fan-out and records only two numeric
totals after successful acceptance. A fan-out exception does not increment
the aggregate.

## Snapshot semantics

`PcpRelayCoordinator.IngressSnapshot` is available during a run.
`PcpRelayCoordinatorResult.Ingress` captures the same aggregate at terminal
completion. Both mean:

- `AcceptedPacketCount`: complete packets accepted by the fan-out;
- `AcceptedPayloadByteCount`: their combined payload length.

These are not wire-byte totals and do not promise delivery to every child. A
coordinator may have no connected children, or a bounded child may later be
isolated as slow or failing. The downstream server's delivered/discarded
counters remain the authority for completed child outcomes.

When `PcpChannelSession` completes normally, its packet totals should equal
the ingress aggregate. When it faults, `PcpRelayCoordinatorResult.Upstream`
can be null while the ingress aggregate correctly remains nonzero.

## Privacy and compatibility

The aggregate has no identifier, endpoint, packet type, position, metadata,
payload, exception, or text. Its insertion does not alter HTTP, HELO/OLEH,
CHAN serialization, queue limits, reconnect behavior, or shutdown ordering.
It makes no inference about unobserved media header or bootstrap semantics.
