# PCN-0488: Exact ISO BMFF Regular Movie Verified Runtime Capability Binding

## Scope reviewed

- exact selected AVC/AAC preparation-to-gate role and codec binding;
- exact managed parameter lease, declared symbol coverage, and gate symbol-map
  agreement;
- explicit missing/unexpected gate rejection and dynamic disposed-state
  availability;
- source-preparation ownership with caller-owned gate borrowing.

## Verification

- focused managed tests use inert in-memory symbol maps and an already
  constructed gate lease to cover exact video/audio binding, role and parameter
  identity, aggregate preparation disposal, caller gate retention, missing gate,
  incomplete symbols, and disposed gate rejection;
- no runtime locator, inventory verifier, dependency inspector, library loader,
  symbol lookup, version call, native invocation, decoder open, output, or
  physical execution is used.

## Result

Accepted as the exact regular-movie verified-runtime capability handoff defined
by ADR-0478.
