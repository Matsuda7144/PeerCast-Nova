# ADR-0329: Caller-Explicit ISO BMFF Implicit SBR Sync Extension Resolution Policy

## Status

Accepted as a post-0.0.10e non-physical managed AAC configuration slice.

## Context

ADR-0240 and ADR-0256 provide exact-position implicit SBR parsing, while
ADR-0328 provides caller-bounded residual-bit search. Selecting one path by an
implicit default would hide both work and compatibility policy.

## Decision

Require callers to construct one explicit immutable policy through a validating
factory:

- `ImmediateOnly` requires no search-bit maximum; or
- `CallerBoundedSearch` requires a positive maximum within ADR-0328's existing
  codec-extradata hard bound.

Resolve either direct GA or exact admitted ER configuration through the selected
path. Retain the exact source, optional ER admission, policy, resolved SBR
extension, and ADR-0328 search provenance when search was selected. Preserve
distinct absence, search-limit, truncation, unsupported-type, and invalid-
frequency outcomes.

## Consequences

Callers can select compatibility search deliberately without creating a hidden
default or losing source provenance.

## Non-goals

No automatic policy selection, decoder capability inference, format-intent
mutation, native transfer, decoder execution, rendering, or physical execution
is included.
