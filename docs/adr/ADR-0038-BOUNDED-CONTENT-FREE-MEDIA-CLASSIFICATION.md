# ADR-0038: Bounded Content-Free Media Classification

- Status: Accepted
- Date: 2026-07-14

## Context

The 0.0.5a playback input owns bounded packet memory, but a decoder cannot be
selected safely from the PCP HTTP content type or channel metadata. Those
values describe transport or untrusted peer claims, not verified media bytes.

Retaining or printing an arbitrary media prefix would also expand the privacy
and diagnostic boundary. Classification therefore needs finite memory,
explicit discontinuity behavior, fixed results, and no returned signature
bytes.

## Decision

Add `PlaybackMediaClassifier` to `Nova.Playback`. It observes only payload
leases already owned by Playback and never moves media bytes through Core.

- transient prefix memory defaults to 64 KiB and has a hard 1 MiB maximum;
- the discontinuity limit defaults to four and has a hard maximum of 64;
- every source or latency discontinuity clears the retained prefix before the
  next candidate is examined;
- reaching a byte/discontinuity limit, completing input, recognizing a
  container, or disposing the classifier clears retained bytes;
- snapshots expose only fixed state/container/basis/limit enums and numeric
  counts;
- PCP response headers and metadata do not participate in classification.

The initial independently implemented recognizers are deliberately narrow:

- FLV file header, including version, reserved flag, and data-offset checks;
- Ogg `OggS` capture pattern with stream-structure version zero;
- three MPEG transport-stream sync bytes at 188-byte intervals;
- ASF Header Object GUID;
- EBML Header parsing with `webm` or `matroska` DocType.

MP3, AAC, MP4, codecs, and unknown EBML document types remain `Unknown` until
an independently justified bounded rule is implemented. No extension or
metadata guess is used as a substitute.

Add a finite `--playback-signature-probe` CLI path. Its acceptance gate
requires recognition, HTTP 200, no classification-time latency skip, no final
pending or outstanding leases, and completed shutdown. Packets intentionally
discarded only after a terminal classification are shutdown cleanup rather
than classification loss.

## Standards basis

- Adobe, *Video File Format Specification v10.1* (FLV)
- [RFC 3533: The Ogg Encapsulation Format Version 0](https://www.rfc-editor.org/rfc/rfc3533)
- [ITU-T H.222.0: Generic coding of moving pictures and associated audio information](https://www.itu.int/rec/T-REC-H.222.0)
- [Microsoft: ASF File Structure](https://learn.microsoft.com/en-us/windows/win32/medfound/asf-file-structure)
- [RFC 8794: Extensible Binary Meta Language](https://www.rfc-editor.org/rfc/rfc8794)
- [RFC 9559: Matroska Media Container Format Specification](https://www.rfc-editor.org/rfc/rfc9559)

These documents define public wire/file behavior. No existing PeerCast or
PeerCastStation implementation source was copied or used as a coding basis.

## Consequences

- Container selection has a bounded, reproducible evidence source independent
  of peer-provided labels.
- Media prefixes remain transient data-plane state and never become normal
  diagnostics.
- A discontinuity cannot accidentally concatenate unrelated byte ranges into
  one false signature.
- Recognition identifies a container only. It does not claim codec support,
  successful decoding, rendering, A/V synchronization, or playback.

