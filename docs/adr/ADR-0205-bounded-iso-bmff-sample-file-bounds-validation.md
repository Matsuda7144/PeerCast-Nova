# ADR-0205: Bounded ISO BMFF Sample File-bounds Validation

## Status

Accepted as a post-0.0.10e non-physical range-validation slice.

## Context

ADR-0204 projects an overflow-checked absolute byte range for one sample, but
does not establish whether that range is contained by the source file.

## Decision

Add a pure managed validator that:

- accepts a projected sample range and an unsigned 64-bit file length;
- rejects a missing range, an overflowing offset-plus-size calculation, or an
  exclusive end that does not exactly equal that calculation;
- accepts an exclusive end at or before the file length; and
- permits a zero-size sample exactly at end of file while rejecting one after
  end of file.

## Consequences

Callers can fail closed before attempting to read a structurally invalid or
out-of-file sample range.

## Non-goals

No `mdat` association, box containment, file access, byte read, fragmented
run, codec selection, decoder composition, recording, or physical execution
is included.
