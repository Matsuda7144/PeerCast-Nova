# ADR-0031 Owned Relay Coordinator and Finite Drain

## Status

Accepted for Milestone 0.0.4d.

## Context

Milestones 0.0.4a through 0.0.4c established the bounded fan-out, one
downstream child, and the owning downstream server. A complete one-channel
relay still needs one owner for the upstream `PcpChannelSession`, fan-out, and
downstream server. Without that owner, cancellation and termination order are
left to callers and the last packets accepted from upstream can be discarded
if downstream sessions are stopped before their bounded queues become idle.

The coordinator must not place media payloads, sockets, Pipes, or queues in
Core. It must also convert unexpected task failures to fixed results rather
than returning remote exception text.

## Decision

`PcpRelayCoordinator` owns exactly one upstream channel session, one
`PcpRelayFanout`, and one downstream relay server. The caller owns the
`ChannelStatusTracker` and upstream transport connector. The downstream server
starts before the upstream session so a child can connect without racing
listener creation.

Normal upstream completion or failure stops new ingress first, then asks the
fan-out to reach idle for a finite `DrainTimeout`. Pending counts include the
packet currently inside a child callback. A completed drain is followed by
downstream server stop and fan-out disposal. A drain timeout is observable and
shutdown continues; the relay never waits indefinitely.

Explicit stop and caller cancellation do not drain. They cancel upstream and
downstream promptly, await both owned boundaries, then dispose the fan-out.
This distinction prevents a user-requested shutdown from being delayed by a
slow peer while preserving already accepted packets after a natural upstream
end when possible.

The coordinator is single-use and has Created, Running, Stopping, and Stopped
states. Terminal reasons are fixed: Stopped, CallerCanceled, UpstreamEnded,
UpstreamFailed, DownstreamFailed, or InternalFailure. Drain outcome is fixed as
NotRequired, Completed, TimedOut, or Canceled. Nested upstream and downstream
results contain only existing fixed fields and numeric counts. No endpoint,
identifier, metadata, payload, exception, or peer-controlled text is added.

## Consequences

- One owner now defines start, cancellation, drain, and shutdown order for a
  complete one-channel relay path.
- Upstream reconnects reuse the same fan-out and downstream children.
- Natural upstream termination can preserve the last bounded queued packets.
- Explicit shutdown remains prompt and finite.
- Task failures become fixed coordinator results while Core retains its
  privacy-safe fault category and stage.
- Controlled upstream-wire to official-downstream interoperability is verified
  in 0.0.4d.
- A real media channel passing through an official upstream peer remains a
  separate verification gate before declaring all of 0.0.4 Relay complete.
