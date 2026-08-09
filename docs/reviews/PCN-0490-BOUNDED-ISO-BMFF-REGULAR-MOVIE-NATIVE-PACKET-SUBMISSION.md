# PCN-0490: Bounded ISO BMFF Regular Movie Native Packet Submission

## Scope reviewed

- exact selected merge-step, stream, role, track, sample, and owned-payload
  retention;
- exact media-unit PTS, reconstructed DTS, duration, stream index, packet time
  base, and IDR-derived key-flag mapping;
- same-packet retention across send backpressure and cursor advance only after
  acceptance;
- exhausted/truncated completion and terminal cleanup after native rejection.

## Verification

- focused managed tests inject packet creation and send operations to verify
  the complete four-step video/audio merge, exact packet metadata, owned
  payload bytes, and final exhaustion;
- focused tests also verify one retained backpressure retry, single packet
  creation, stable merge index, acceptance advance, subsequent rejection, and
  complete decoder-session cleanup;
- no runtime discovery, library loading, native allocation, metadata write,
  packet send, decoder receive, output, or physical execution is used.

## Result

Accepted as the bounded regular-movie compressed-packet submission boundary
defined by ADR-0480. Flush, receive, decoded-frame transfer, and all downstream
output work remain unimplemented.
