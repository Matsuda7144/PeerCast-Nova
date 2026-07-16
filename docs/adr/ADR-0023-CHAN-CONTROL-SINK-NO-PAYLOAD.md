# ADR-0023: Decode CHAN Control Without Retaining Packet Payload

- Status: Accepted
- Decision: Interpret CHAN control fields from incremental `IPcpAtomSink`
  callbacks rather than an owned Atom tree in the future live stream path.
- Decision: Copy only bounded metadata and fixed-size control values. Record
  packet `data` length but never copy or expose its bytes through the control
  model.
- Decision: Ignore unknown fields for legacy compatibility, reject duplicate
  known fields, and require strict NUL-terminated UTF-8 for known text fields.
- Reason: Core snapshots need channel state, but media ownership and
  backpressure must remain in the Network data plane.
- Evidence: A content-free controlled observation against PeerCastStation
  6.0.0.0 showed CHAN `id`, `pkt`, `info`, and `trck` structures.
- Consequence: Network still needs a bounded live session that routes packet
  payload to explicit data-plane consumers; this ADR does not implement relay.
- Follow-up: 0.0.3d adds the separate bounded post-handshake packet boundary in
  ADR-0025 while keeping this control model payload-free.
