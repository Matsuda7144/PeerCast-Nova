# ADR-0227: ISO BMFF Edited-sample Description Selection

## Status

Accepted as a post-0.0.10e non-physical classification slice.

## Context

ADR-0222 now retains the one-based `stsc` sample-description index alongside
the admitted sample range. Codec policy must select exactly that `stsd` entry
and reconcile its type with the track handler before parsing entry payloads.

## Decision

Add a pure managed selector that:

- requires the edited range, parsed `stsd`, and parsed `hdlr`;
- uses only the bound one-based sample-description index;
- classifies `avc1`, `avc3`, and `mp4a` as codec candidates;
- requires AVC candidates on a video handler and `mp4a` on an audio handler;
  and
- distinguishes missing metadata, index range, unsupported entry, and handler
  mismatch failures.

## Consequences

Later entry-payload parsing is bound to the same sample/chunk lookup and cannot
silently choose a different `stsd` entry.

## Non-goals

No visual/audio sample-entry field parsing, codec configuration, extradata,
demux event, decoder submission, recording, or physical execution is included.
