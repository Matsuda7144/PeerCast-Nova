# ADR-0265: Bounded ISO BMFF AAC Transfer Symbol Coverage

## Status

Accepted as a post-0.0.10e non-physical decoder-composition prerequisite.

## Context

ADR-0263 records the symbol names required for AAC parameter transfer. Before
any runtime or ABI gate is touched, a caller-provided declaration can be checked
for complete name coverage using a finite managed-only boundary.

## Decision

Add a bounded coverage verifier that:

- requires one available ADR-0263 plan;
- accepts at most 256 declared names of at most 128 ASCII identifier characters;
- compares names with ordinal case-sensitive semantics;
- permits unrelated extra declarations but requires all five transfer symbols;
- retains the exact plan and exposes only the matched requirement names; and
- forwards synchronous and asynchronous disposal to the plan owner.

## Consequences

Missing or malformed declarations fail before any runtime interaction, while a
successful result remains tied to the exact managed AAC transfer plan.

## Non-goals

The declaration is not proof that a symbol exists. Runtime inventory, module
loading, ABI/version verification, symbol-address resolution, native parameter
transfer, decoder open, and physical execution are not included.
