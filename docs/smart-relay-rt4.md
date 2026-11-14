# Smart Relay RT-4: local manual downstream disconnect

Modern's relay-tree context menu can request termination of one current owned
downstream. Classic and PCP wire formats are unchanged. This adds only an
in-process command contract between App and Network; no HTTP/RPC management
endpoint or process-wide disconnect registry is added.

## Identity and acceptance

- A selection carries ChannelId, OwnerSessionId, ConnectionId and a per-accepted
  child `Guid` stamp. The server's existing SessionEntry owns the stamp. HOST
  reports, upstream/input nodes and display strings never supply authority.
- After the confirmation dialog, the GUI resolves the original selection in a
  fresh snapshot. Cancellation, window closure, channel/Owner change, Recovery,
  closing state and a replacement child reject the selection without retargeting.
- Network enters the existing application Owner gates before its server gate.
  Current generation validation and exact SessionEntry lookup precede an atomic
  claim of the child's closing flag. Recovery closes this command boundary under
  the same server gate before publishing its state. No application callback is
  called with the server gate held; no await/cancellation/disposal occurs in the
  acceptance critical section. Child acceptance does not wait for its lifecycle
  lock, where existing disposal can execute cancellation callbacks.
- Repeated requests for a still-owned entry share its completion and cannot
  select another entry. A reconnect, including the same peer SID or a reused
  ConnectionId, requires a new stamp. Removed entries have no retained history.

## Termination and results

`Accepted` means the old child was marked, not that cleanup finished. After all
acceptance locks have been released, Network cancels only that child's existing
lifetime. Existing session/server owners perform fanout removal, pacing wait
cancellation, transport disposal, reservation release, traffic completion and
PeerDirectory lease cleanup. The GUI owns none of these resources.

The bounded completion observer waits for the exact entry task, transport cleanup
and admission/peer cleanup chain, and checks the saved child cleanup result.
Shared-listener cleanup still returns to the lending handler before joining the
borrowed transport. No new concurrent QUIT write is introduced: manual stop uses
the existing child cancellation/transport termination path so a partially written
media atom cannot be interleaved with QUIT or counted as delivered.

`Completed` is cleanup proof, not proof that discarded media reached the peer.
After five seconds, exceptions, uncertain cleanup or an uncooperative writer
produce `CleanupUnconfirmed`. Existing late cleanup retains its reservations;
the observer never disposes/releases again and never upgrades this result to a
retroactive success. Accepted operations are not canceled merely by closing the
GUI. Results are shown only for their original channel/Owner using the existing
one-second refresh. The confirmation displays a generated ID, even with IP
display enabled, and warns about indirect effects on viewers behind that peer.

## Scope and verification

The original per-channel server handles channel listeners, managed loopback and
its shared-listener borrowed children. Viewing and broadcast/OBS publishing
admission wrappers forward the same Owner-generation guard. A legacy admission
provider without this guard fails closed. Generic broadcast composition does not
currently create Phase W's shared viewing route; RT-4 does not add one.

Managed loopback tests cover A/B with A-only termination, HEAD/DATA continuation
for B, a new C using A's peer SID, stale stamps against C's current ConnectionId,
Owner replacement, Stop/Recovery rejection, finite pacing wait cancellation,
partial-atom accounting, held-writer cleanup and capacity retention, and actual
OBS-host admission without stopping the publisher. Dialog tests cover cancel,
closure, disconnected/replaced children and channel/Owner changes. These tests
are independent of PeerCastStation, external playback and WAN acceptance.
The OBS-host fixture has no RTMP producer: it verifies the real admission wrapper,
the surviving child's metadata/registration and the host remaining active. It
does not inject packets directly into publishing's fanout, which would bypass
PublishConsumer's mandatory HTTP-consumer contract. Broadcast HEAD/DATA
continuation is proved separately with the generic broadcast producer fixture.

Windows 11 build 26200 display check used the existing network-disabled Windows
Sandbox profile with read-only host mappings. Modern normal/minimum size, the
mouse/keyboard context menu of an unconnected Nova node (no disconnect item),
Classic's original channel details and normal application shutdown were checked.
No test process/Sandbox remained; the user confirmed no capture border/overlay
remained. The fixture has no actual child or HOST report, so the native eligible
menu, confirmation/cancel, result presentation and remote-peer menu remain
unverified on screen. Their managed identity/dialog tests do not replace that
display acceptance. No host preferences, YP publication, NAT or firewall settings
were changed.
