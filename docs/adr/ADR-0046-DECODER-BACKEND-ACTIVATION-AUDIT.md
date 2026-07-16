# ADR-0046 Decoder Backend Activation Audit

## Status

Accepted and verified in 0.0.5j.

## Decision

The host can audit backend policy before constructing a decoder factory. Each
entry contains only the bounded backend ID and fixed rejection flags for license
review, third-party origin, bundled distribution, and native code. Audit order
is ordinal by ID and audit never initializes a backend.

Factory construction retains fail-closed enforcement. The audit is explanatory
evidence, not an authorization bypass.
