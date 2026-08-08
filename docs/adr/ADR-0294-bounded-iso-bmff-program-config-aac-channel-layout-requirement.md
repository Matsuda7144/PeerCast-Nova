# ADR-0294: Bounded ISO BMFF Program Config AAC Channel Layout Requirement

## Status

Accepted as a post-0.0.10e non-physical native-transfer prerequisite.

## Context

The existing AAC transfer path initializes a default FFmpeg channel layout from
channel count. A PCE preparation instead retains an explicit speaker mask, so
default initialization would discard reviewed layout intent.

## Decision

Project a source-bound PCE managed decoder preparation into an explicit channel-
layout transfer requirement. Require the preparation to remain available, its
decoder channel count to equal the reorder-plan count, and the mask population
count to equal that same count. Retain the exact preparation, 64-bit mask, count,
and the required FFmpeg initializer symbol `av_channel_layout_from_mask`.

The requirement is metadata only. It does not resolve or invoke the symbol.

## Consequences

Future native integration can distinguish explicit PCE layout initialization
from count-only default initialization without rediscovering the mask.

## Non-goals

Symbol coverage verification, ABI validation, function-pointer access, native
structure writes, decoder operations, rendering, and physical execution are not
included.
