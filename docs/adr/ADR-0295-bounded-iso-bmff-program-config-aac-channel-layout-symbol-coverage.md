# ADR-0295: Bounded ISO BMFF Program Config AAC Channel Layout Symbol Coverage

## Status

Accepted as a post-0.0.10e non-physical capability-declaration slice.

## Context

ADR-0294 identifies `av_channel_layout_from_mask` as the explicit PCE layout
initializer. Before runtime access, the caller's bounded declared-symbol set must
state that this separate capability exists.

## Decision

Verify the ADR-0294 requirement against a nonempty declared-symbol collection.
Reuse the existing maximum collection size, maximum symbol length, ASCII
alphanumeric-or-underscore syntax, and ordinal comparison. Require the exact
initializer symbol and retain the exact requirement plus verified symbol value.

Reject disposed preparation ownership before accepting coverage. Do not resolve
libraries, query addresses, or invoke functions.

## Consequences

The explicit PCE layout dependency now has a bounded declarative capability gate
separate from all runtime and ABI checks.

## Non-goals

Runtime discovery, library loading, symbol addresses, ABI validation, native
structure writes, decoder operations, rendering, and physical execution are not
included.
