# Phase 5F-17: relay admission and bandwidth policy foundation

Independent integration base: main 49dff172e0f65446f4eb08ded9a89d4347fb96bd.
Source: PR #291, bda79041984df58f6ed5980a886153df8bd2dc12.
Original stacked base: PR #290, 52080265e997570410c570d6a487e0b1130d1841;
its parent was PR #289, c5dd8006eaa94d1a53f59b80877471a377f299b2.

**RELAY ADMISSION POLICY FOUNDATION READY — RUNTIME ENFORCEMENT PENDING**

**BANDWIDTH POLICY FOUNDATION READY — BANDWIDTH ENFORCEMENT NOT AVAILABLE**

## Source investigation and integration boundary

| Existing boundary | Finding |
|---|---|
| `PcpDownstreamChildSession.NegotiateAndMonitorAsync` | Validates the downstream HTTP PCP request and channel, then reserves a fan-out child before sending HTTP 200 and accepting HELO. Exhaustion already returns HTTP 503 followed by PCP negotiation and RelayFull. This is a candidate future admission seam. |
| `PcpDownstreamChildSession.RunCoreAsync` | Cleanup stops the child, completes the owned reader, and disposes the owned transport. Routed sessions borrow transport/reader from the shared listener. Admission reservations must follow actual cleanup, not just cancellation. |
| `PcpRelayFanout.TryAddChild` | Atomically reserves a bounded worker; known workers retain their resource slots until their callbacks complete. Its default 32-child resource bound remains intact. |
| `PcpRelayFanoutRecordingStreamSourceLeaseFactory`, `PcpRelayCoordinator.AttachPlaybackConsumerAsync` | Recording and local media consumers also register fan-out children. ActiveChildCount is therefore not the number of downstream network relays. |
| `TcpTransportConnection` | Owns one transport, exposes a process-unique ConnectionId and actual remote endpoint, but no timed send-byte accounting. |
| `GenesisSharedPcpPlaybackRuntime.CreateOwner` | Owns one coordinator per channel/session, shared by playback and recording; its downstream listener uses loopback and an ephemeral port. Owner retirement checks object identity before removing a newer owner. |
| `NovaWindowsStartupComposition.RunGenesisAsync` | GUI Next creates the playback controller before loading GUI application state. It does not create the Daily public relay controller or a common broadcasting/viewing relay owner. |
| `NovaDailyPcpRelayController`, `NovaDailyPlaybackStartupComposition` | A separate Daily GUI command path creates a public IPv4 ephemeral listener and owns its NAT mapping runtime. It does not consume Connection.Startup. |
| `GenesisGuiNextApplicationState` | Loads connection preferences once into Startup. It now owns NetworkPolicies generated from this snapshot; Saved is excluded. No listener/controller is created by this addition. |

There is no production-wide downstream owner registry with broadcasting/viewing
classification reachable from this GUI Next startup boundary. Connecting the
policies would require widening the runtime composition and its ownership
contract. This phase intentionally stops at the allowed foundation boundary.
No production socket path calls the new reservation or bandwidth decision APIs.

## Relay counting and ownership contract

- One reservation represents one validated downstream PCP relay connection from
  this Nova node to a peer. A caller must provide a single stable connection ID.
- Upstream receiving connections, local HTTP/player outputs, playback consumers,
  recording consumers, OBS ingress, and TCP connections without an accepted PCP
  relay request are excluded.
- A broadcasting channel originates at this node. A viewing channel is received
  from another node, whether or not a local player is currently attached.
  Classification is supplied by the authoritative owner, never inferred from IP.
- MaxRelaysTotal is global across all channels and both address families.
  Channel limits count all outstanding generations of that ChannelId and use
  the current owner's broadcasting/viewing classification for new admission.
- Null disables the corresponding configured limit; zero refuses new matching
  admissions. This does not remove the existing independent fan-out safety bound.
- OpenChannel publishes one current owner per ChannelId. A new session retires
  the previous scope's admission rights; its live reservations remain counted.
  A duplicate current session is rejected without replacing its owner.
- Duplicate connection IDs return DuplicateRelay without a second lease. The
  original caller remains solely responsible for release.
- A short policy-instance lock combines checking both limits, adding the
  reservation, and incrementing counts. It performs no network I/O, waits,
  callbacks, enumeration of all connections, or background work.
- Dispose of the channel or policy prevents new admissions without clearing
  existing usage. The reservation is released only after real connection cleanup;
  failed establishment and cancellation use the same finally/disposal path.
- Cancellation before reservation changes no counts. Cancellation after
  reservation alone is not evidence that the transport ended.
- Lease release is idempotent and checked against the exact reservation object.
  Old owner cleanup and stale duplicate release cannot remove a new reservation.

## Bandwidth contract and available evidence

The child worker reports DeliveredByteCount after successful sink delivery.
These are cumulative payload counters shared with internal consumers, without
a timed, complete IPv4/IPv6 network-output aggregate. They exclude PCP framing,
control replies, and transport overhead. They cannot establish total send rate.

CHAN metadata reaches ChannelMetadataSnapshot.BitrateKbps through
PcpChannelStateAdapter.MergeMetadata. It is declared metadata, not a measured
send rate or a reservation for concurrent new connections. Missing/zero values
cannot justify a zero-cost admission.

- Decimal kbps means 1000 bits per second, or 125 bytes per second. Conversion
  widens to long before multiplication.
- Classify the actual downstream remote IP address; IPv4-mapped IPv6 counts
  against IPv4, ordinary IPv6 against IPv6. No listener/IPv6 creation changes.
- The numerical model is for downstream PCP relay output, including output from
  a broadcasting channel. Upstream reception, OBS ingress, local playback, and
  recording are outside this model. Loopback does not itself identify a local
  consumer: the validated connection role is authoritative.
- BandwidthAccountingSnapshot is a typed caller-supplied complete family
  aggregate. A family containing any unknown rate is null, not zero.
- Null budget allows without a bandwidth restriction; zero refuses a new relay.
  A positive budget requires complete accounting and positive candidate bitrate.
  Missing accounting or bitrate returns an explicit indeterminate decision.
- Known current plus candidate may equal the budget. Comparison avoids overflow.
- Evaluate does not reserve capacity. Independently evaluated snapshots cannot
  enforce concurrent bandwidth admission. No shaping, sampling, guessed bitrate,
  protocol rejection, or established-connection disconnection is implemented.

## PeerCastStation compatibility comparison

Primary sources inspected at commit 390c4e847289c90a77dbe40cc04b7bc1cfbddf17:

- [AccessController](https://github.com/kumaryu/peercaststation/blob/390c4e847289c90a77dbe40cc04b7bc1cfbddf17/PeerCastStation/PeerCastStation.Core/AccessController.cs)
- [Channel](https://github.com/kumaryu/peercaststation/blob/390c4e847289c90a77dbe40cc04b7bc1cfbddf17/PeerCastStation/PeerCastStation.Core/Channel.cs)

PeerCastStation separates direct downstream relays from direct viewers and
distinguishes broadcasting from relaying channels. Its relay count checks group
channels by network family; zero relay limits mean unlimited. Its upstream
accounting estimates bitrate times non-local relay/direct outputs.
Nova keeps the requested global total and PR #289 null/explicit-zero contract,
so these numeric semantics are deliberately not identical. No source was copied.
Nova's existing HTTP 503/PCP RelayFull wire behavior is unchanged; a future count
limit refusal must reuse it. No PCP packet, Channel ID, YP, or media semantics change.

## Startup and next minimal slice

GenesisGuiNextNetworkPolicies.Create reads only settings.Startup. Application
load creates these policies once; saving changes only next-start preferences.
Application shutdown retires the foundation. ListenPort and UseIpv6 remain
configuration data; listener binding and NAT-PMP/UPnP ownership are unchanged.

Next: define the GUI Next application-owned downstream relay registry and
channel-role boundary, then attach this shared count policy to the validated
PcpDownstreamChildSession admission seam and actual cleanup. Verify HTTP
503/RelayFull plus local playback/recording exclusion. Bandwidth first needs
complete family accounting and atomic capacity reservations; traffic shaping
is a separate task. No physical acceptance is claimed by managed tests.

## Smart Relay Phase D integration

The existing `RelayAdmissionPolicy` now evaluates the IPv4 bandwidth model under
the same gate as total and per-channel relay counts. Both checks commit one
reservation, so rejection cannot partially reserve either resource. The existing
Owner registry routes loopback, shared public and publishing children through
this policy; there is no second ledger. Reservations across retired generations
remain occupied until their original transport and child cleanup complete.

Positive CHAN bitrate is an admission estimate, not a measured send rate. Each
reservation retains its initial estimate; new admissions account for the greater
of that estimate and the Owner's current declaration. Unknown/zero declarations
or incomplete accounting refuse admission under a positive budget. A zero
budget refuses new children; a null budget preserves unrestricted bandwidth
admission. Count limits retain their separate null/zero/positive semantics.

Admission bitrate is cleared on each upstream attempt and on Smart Relay
recovery. Missing bitrate on a replacement upstream cannot inherit the previous
attempt's value. Existing user-visible metadata and media positions are preserved.
Classic/Modern's existing relay status reports total/channel reservations,
startup limits and estimated IPv4 bandwidth reservations, with measured traffic
explicitly Unknown. Saving still only changes next-start settings.

The existing numeric-IPv4-budget refusal of LAN/WAN publishing startup remains
unchanged. Publishing's loopback PCP endpoint explicitly joins the same Broadcast
Owner admission through the application factory. Internal HTTP and recording
consumers remain uncounted. Standalone hosts preserve the legacy default unless
their caller explicitly opts into loopback admission.

Current application-owned downstream routes are IPv4; this does not complete
IPv6 WAN accounting. Payload counters still do not establish a complete
measured network-output rate. Strict shaping, dynamic allocation, real
PeerCastStation compatibility, playback and WAN physical acceptance remain
unverified or deferred. Standard 503/HELO/OLEH/HOST(0..8)/QUIT 1003 is reused.
