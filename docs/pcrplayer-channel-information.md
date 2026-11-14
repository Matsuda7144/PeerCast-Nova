# PCRPlayer channel information

The daily external-player path supplies PCRPlayer with the local playlist URL,
channel name, and validated HTTP(S) contact URL. These positional arguments are
documented in PCRPlayer's bundled Readme.txt. Arguments use ProcessStartInfo's
ArgumentList with shell execution disabled.

The playback host additionally supports these read-only loopback routes while
the channel's playback consumer exists:

- `/pls/<32-hex-channel-id>.pls`: a finite M3U pointing to the existing raw-FLV
  `/stream/<id>` route on the same dynamically allocated port.
- `/admin?cmd=viewxml`: UTF-8 PeerCast-compatible channel information under
  `peercast/channels_relayed/channel`. It includes channel ID/name, genre,
  description/comment, contact URL, media type, declared bitrate, local session
  uptime, track title/album/genre/artist/contact, and available connection counts.

The XML schema and playlist conventions were checked against PeerCastStation's
[AdminHost](https://github.com/kumaryu/peercaststation/blob/master/PeerCastStation/PeerCastStation.UI.HTTP/AdminHost.cs)
and [HTTPOutputStream](https://github.com/kumaryu/peercaststation/blob/master/PeerCastStation/PeerCastStation.HTTP/HTTPOutputStream.cs).
These are protocol references; their implementation code is not incorporated.

Each request reads immutable PCP tracker metadata from the same shared owner
that supplies this playback consumer. Missing fields fall back to the selected
catalog row; a successful catalog refresh can update that row during playback.
Peer totals count distinct unexpired HOST session identities and represent only
observed reports, not proof of a complete global audience. Catalog listener/relay
counts are a fallback for at most two minutes after observation. Unknown totals
are omitted. Local listeners are actual HTTP streaming children, local relays are
tracked downstream PCP peers, and neither counts metadata/playlist requests.
Bitrate is the declared channel bitrate, not measured delivery speed.

The host remains IPv4 loopback-only. The extra routes are opt-in through the
metadata provider, expose only currently active playback sources, and provide
no stop, bump, or other management commands. Peer IP addresses, session IDs,
credentials, and internal relay-tree objects are not exported. Existing HTTP
connection/header/time limits remain in effect. Streaming admission is checked
after routing, so a metadata poll can coexist with the default one video client
without increasing the video-client limit. XML escaping, scalar validation,
per-field lengths, eight-channel maximum, and a 512 KiB response bound constrain
output. Playback source removal and host shutdown remove this read path too.

Validation includes XML escaping/Unicode and size limits, peer deduplication and
expiry, safe launch arguments, catalog identity matching, and an HTTP integration
test that polls changing metadata during an occupied single stream slot, rejects
another stream and all management commands, then verifies source removal.

Windows acceptance: Nova-launched PCRPlayer displayed the channel name, FLV type,
description and local listener/relay counts, loaded the contact board and its
response count, and continued rendering video. A channel-info refresh displayed
the declared bitrate and description. No board post was submitted. Dynamic track
changes depend on the broadcaster supplying PCP metadata; that live scenario
was not independently exercised.
