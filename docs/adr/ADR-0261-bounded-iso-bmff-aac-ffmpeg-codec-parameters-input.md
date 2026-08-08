# ADR-0261: Bounded ISO BMFF AAC FFmpeg Codec Parameters Input

## Status

Accepted as a post-0.0.10e non-physical decoder-composition prerequisite.

## Context

ADR-0260 owns exact AAC extradata and ADR-0259 owns effective format intent.
The existing FFmpeg codec-parameter factory already defines audio numeric limits
and 64-byte padding ownership, so ISO BMFF code needs only a validated managed
input adapter rather than another allocator.

## Decision

Add a pure managed resolver that:

- requires ADR-0260 extradata and follows its retained format intent;
- validates effective sample rate and channel count against the existing FFmpeg
  codec-parameter factory limits;
- projects an AAC codec-parameter input with zero video dimensions and the exact
  owned extradata bytes; and
- retains the decoder-extradata source beside that input.

## Consequences

ISO BMFF AAC metadata can enter the established codec-parameter ownership
boundary without duplicating padding or numeric policy.

## Non-goals

Codec-parameter factory execution, native parameter transfer, decoder open,
codec capability discovery, or physical execution are not included.
