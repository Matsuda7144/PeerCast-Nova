# PCN-0037 Legacy HTTP Response Text Boundary

## Directional policy

```text
downstream peer -> Nova request
  start line: strict ASCII
  field names: strict ASCII token
  field values: strict ASCII plus HTAB

official upstream -> Nova response
  status line: strict ASCII
  field names: strict ASCII token
  field values: visible ASCII / HTAB / opaque 0x80..0xff
```

Both directions retain the same finite header-byte limit, complete CRLF rules,
duplicate-field aggregation bound by that limit, and rejection of disallowed
control bytes. Response `obs-text` is permitted only after the first colon on
a non-start line.

## Data lifetime

Latin-1 is used as a lossless byte mapping, not as a claim about the peer's
human-language encoding. The relay handshake extracts only the existing
`Server` and `Content-Type` fields. Unknown channel information remains local
to the short-lived response model and is released after negotiation. It is
not copied into Core status, coordinator snapshots, acceptance output, or the
validation report.

## Real-media sequence

```text
PeerCastStation 6.0.0.0
  -> HTTP 200 with two obs-text field values
  -> Nova bounded response parser
  -> HELO / OLEH / OK
  -> 1,338 CHAN packets accepted by bounded fan-out
  -> one Nova downstream session receives four packets
  -> finite 60-second stop and complete cleanup
```

The packet and byte counts prove continuous bounded delivery through the
owned relay path. They do not expose or interpret media payload, packet-type
semantics, metadata values, stream position, or the controlled Channel ID.
