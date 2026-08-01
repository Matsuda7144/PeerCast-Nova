# ADR-0207: Bounded ISO BMFF Sample Media-data Lookup

## Status

Accepted as a post-0.0.10e non-physical structural-lookup slice.

## Context

ADR-0206 validates containment within one caller-selected `mdat`. A complete
top-level box buffer can instead be traversed to locate the `mdat` payload that
contains a projected sample range.

## Decision

Add a pure managed lookup that:

- validates the sample range and whole-file containment before traversal;
- traverses only top-level boxes with the existing container-bounded reader;
- defaults to inspecting at most 4,096 boxes and rejects configurations above
  a 65,536-box hard limit;
- reuses selected-`mdat` containment for each structural box location;
- returns the first containing `mdat` location; and
- distinguishes invalid ranges, out-of-file ranges, invalid box sequences,
  exhausted box budgets, and absence of a containing `mdat`.

## Consequences

Callers can associate one sample range with its top-level media-data box using
bounded structural traversal without interpreting or copying payload bytes.

## Non-goals

No recursive traversal, payload read, packet projection, cross-sample
validation, fragmented run, codec selection, decoder composition, recording,
or physical execution is included.
