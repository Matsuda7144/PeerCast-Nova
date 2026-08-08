# ADR-0232: Bounded ISO BMFF AVC Sample Framing

## Status

Accepted as a post-0.0.10e non-physical codec-packet slice.

## Context

ADR-0230 owns the selected visual entry's `avcC` record and exposes its one-to-
four-byte NAL-unit length width. ADR-0226 owns one edited sample payload and its
timing identity. AVC packet composition must reconcile those two boundaries and
must not trust length prefixes to drive unbounded allocation or out-of-range
slices.

## Decision

Add a pure managed framing boundary that:

- requires the candidate sample-description index to match the selected AVC
  configuration;
- accepts only video `avc1` or `avc3` configurations with a one-to-four-byte
  NAL-unit length field;
- defaults to 4,096 NAL units with a 65,536 hard limit;
- reads each length as an unsigned big-endian value and rejects zero-length,
  truncated, or trailing partial framing;
- requires at least one complete NAL unit and exact sample consumption; and
- projects non-owning ranges over the already owned sample payload without
  duplicating NAL-unit bytes.

## Consequences

Later demux-event composition can consume exact length-prefixed AVC access-unit
parts while retaining sample timing, edit appearances, and decoder
configuration identity.

## Non-goals

No Annex-B conversion, SPS/PPS injection, NAL semantic inspection, keyframe
inference, demux event, decoder submission, recording, native call, or physical
execution is included.
