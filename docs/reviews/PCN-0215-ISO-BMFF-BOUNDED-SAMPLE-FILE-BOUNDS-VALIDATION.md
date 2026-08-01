# PCN-0215: Bounded ISO BMFF Sample File-bounds Validation

## Status

Implemented and verified as a non-physical range-validation slice.

## Implemented boundary

- exact offset, size, and exclusive-end consistency validation;
- explicit overflow rejection;
- unsigned 64-bit file-length containment; and
- precise zero-size end-of-file behavior.

## Verification

Managed tests cover ranges before and exactly at file end, ranges beyond file
end, zero-size ranges at and after file end, inconsistent exclusive ends,
overflow, and missing input.

No native runtime, file read, output device, readiness capture, diagnostic,
probe, or physical audio/video path was exercised.

## Remaining work

Media-data bounds and association, byte reads, fragmented runs, codec
selection, and decoder composition remain unimplemented.
