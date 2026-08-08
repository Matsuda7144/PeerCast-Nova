# ADR-0271: Bounded ISO BMFF AAC Sample-Entry Managed Decoder Preparation

## Status

Accepted as a post-0.0.10e non-physical decoder-composition prerequisite.

## Context

ADR-0270 makes sample-entry mismatch admission explicit, while ADR-0268 owns the
managed decoder-preparation artifacts. A caller needs one fail-closed boundary
that applies the sample-entry policy before creating those artifacts and retains
the exact sources behind both decisions.

## Decision

Add a pure managed composer that:

- requires an exact `mp4a` audio entry and AAC decoder-format intent;
- projects ADR-0269 consistency facts and applies the caller-selected ADR-0270
  policy before decoder preparation;
- creates the ADR-0268 managed preparation only after consistency admission;
- retains the same audio-entry, format-intent, consistency-decision, declared
  symbol, and declared-capability sources; and
- returns one disposable owner only when every stage succeeds.

Strict mismatches fail before any owner is returned. An admitted mismatch retains
the explicit override decision. Any partial decoder preparation is disposed
before failure is reported.

## Consequences

Managed AAC decoder preparation can now be made sample-entry-aware without
implicitly normalizing container declarations or losing source provenance.

## Non-goals

No profile policy is selected automatically. Runtime library loading, ABI or
symbol verification, native context allocation or mutation, decoder open,
speaker-layout binding, packet submission, and physical execution are not
included.
