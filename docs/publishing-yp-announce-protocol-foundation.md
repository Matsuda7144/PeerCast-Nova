# YP Announce Protocol Foundation

This slice implements one channel / one PCP IPv4 tracker, without enabling
announcement in the application. It neither proves YP interoperability nor
WAN reachability. PR #330 LAN physical acceptance remains deferred.

## Protocol reference

Behavior was independently implemented from the protocol used by
[PeerCastStation 390c4e847289c90a77dbe40cc04b7bc1cfbddf17](https://github.com/kumaryu/peercaststation/tree/390c4e847289c90a77dbe40cc04b7bc1cfbddf17):
`PeerCastStation.PCP/PCPYellowPageClient.cs` (handshake, CreateChannelBcst,
PostChannelInfo, PostHostInfo, 30-second update, ChannelRemoved),
`PeerCastStation.Core/Atom.cs`, `AtomCollectionExtensions.cs`,
`PeerCastStation.PCP/PCPVersion.cs`, and `PeerCastStation.Core/Channel.cs`.
No reference implementation code was copied.

* Direct TCP: existing 12-byte `pcp\n` preamble, protocol version 1; no HTTP GET.
* HELO: agent, SID, distinct BCID, servant version 1218, explicitly ready port.
* OLEH: mandatory nonempty SID; existing typed fields and `dis` are validated.
  Nonzero `dis`, QUIT, malformed data, EOF and deadlines fail the attempt.
* BCST: `ttl=1`, `hops=0`, `from=SID`, `vers=1218`, `cid=ChannelId`, `grp=1`
  (Root). Protocol version 1 and servant version 1218 are different fields.
* CHAN: `id`, `bcid`, existing bounded `info` and optional allowlisted `trck`.
* HOST: `id=SID`, `cid`, one explicit IPv4/port pair, listener/relay counts,
  uptime, version, tracker/receiving/available-relay/available-direct bits.
  IPv4 bytes use existing legacy reverse order; integers are little endian,
  GIDs use network order. No PeerCastStation-specific version identity is claimed.
* Stop: same BCST with Receiving cleared, then QUIT code 1000, then close.
  HEAD/DATA are never included. No registration/deletion acknowledgement exists
  in this implementation. Sending bytes is not evidence of listing or removal.

## Identity and lifetime

`PcpAnnounceOwner` is a single application-lifetime, in-memory broadcast scope;
production does not construct it yet. Its BCID uses the reference's leading-zero
wire-byte convention, is separate from SID, and remains unchanged across
Publishing restarts. No persistence or change to existing ChannelId allocation
is introduced. Reconnection is not implemented.

The worker captures local Generation, ChannelId, PublishingSessionId, sender SID
and BCID immutably. Existing `ObsPublishChannelHost` creates a new session ID per
generation and uses it as downstream OLEH SID. The owner explicitly requires the
supplied relay SID to match that PublishingSessionId for this host contract.
HELO SID, HOST ID and BCST From all use this captured sender SID. This is not a
claim that every future node/connection/generation must share one session ID.

Only a higher generation can replace a completed worker whose cleanup is proved.
Old update requests cannot target a new identity, nor can an old worker change
the current owner's slot. A deadline failure is sticky and blocks replacement,
even if the delayed operation subsequently returns. Do not replace the owner to
bypass that failure. Target YP BCID policies and server-side deletion races still
require validation before enabling real announcements.

## Worker and cleanup

Explicit Start is one-shot. Immutable public metadata is captured before connect;
updates are coalesced into one latest snapshot with a fixed advertised endpoint.
There is one serialized writer and one 30-second PeriodicTimer. A received control
stream is bounded to 32 atoms per second. Tracker IP/port observations never
change the advertised endpoint, and received atoms never trigger a new connection.

Connect/handshake/write defaults are 10 seconds each; shutdown deadline is also
10 seconds. These are Nova safety settings, not tracker protocol requirements.
The owner accepts Finish, ObsNormalEos, Dispose and cancellation as explicit stop
reasons. Stop rejects new updates, converges the writer, attempts Receiving=false
and QUIT once each, closes transport and joins tracked I/O/timer/read operations.
There is no retry loop. Abort closes the actual socket, not only its token.

Cancellation-resistant operations are retained/observed. Late connector results
are closed before they can write. A close failure, outstanding operation or
deadline expiration produces CleanupIncomplete, never cleanup success. A failed
notification remains unknown; successful writes do not prove YP removal.
Announce errors have no callback that stops OBS ingest, fanout or the Relay owner.

## Production isolation

All owner/worker/transport/activation types are internal to Network. No application
composition, GUI, saved schema, YP catalog, fallback tracker, Publishing Start or
Finish call site references them. Default activation is Disabled; NotReady does
no I/O. The only Ready factory in this slice permits an explicit numeric IPv4
loopback fixture endpoint, and the concrete connector independently rejects any
non-loopback address. It performs no DNS resolution. Real tracker communication
cannot be enabled by saving settings or starting Publishing.

The endpoint parser accepts an explicit `pcp://` tracker (default port 7144), but
rejects credentials, arbitrary paths (including normalized traversal), query,
fragment, IPv6, invalid/zero ports and controls. A parsed external endpoint still
cannot pass the foundation's loopback-only activation or connector.

Control messages are limited to 64 KiB, 128 children per atom and depth eight.
Existing finite atom readers/codecs are reused. Public metadata uses the existing
bounded channel encoder and explicitly selected track fields; credential URLs,
local-file URLs and control text are rejected. No generic settings bag, RTMP key
or filesystem path is accepted. Public text itself remains the caller's disclosure
responsibility.

Tests use the existing loopback TCP transport plus a small fake tracker, a manual
clock and cancellation-resistant in-memory I/O barriers. The application test
checks compiled type references for accidental production foundation call sites.

## Deferred

WAN listener/source admission, PCP ping ingress, external address discovery,
public capability/reachability proof, NAT/UPnP/port forwarding/firewall changes,
YP GUI/settings, authentication and YP-specific extensions, automatic reconnect,
real listing/removal confirmation and physical interoperability are not provided.
Current LAN subnet admission is unchanged. Future activation needs an explicit
public capability, relay SID/ping consistency and separately authorized acceptance.
