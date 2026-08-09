# PCN-0487: Bounded ISO BMFF AVC Managed Decoder Preparation

## Scope reviewed

- exact selected AVC input resolution from visual dimensions and owned `avcC`;
- managed padded H.264 codec-parameter ownership and exact transfer planning;
- caller-declared required-symbol coverage and bounded H.264 dimension/pixel
  capability binding;
- aggregate video/audio lease disposal and partial-failure cleanup.

## Verification

- focused managed tests cover exact selected source retention, H.264 codec ID,
  dimensions, owned configuration bytes, aggregate asynchronous disposal,
  video-only preparation without AAC declarations, and incomplete AVC symbol
  rejection;
- no installed-runtime lookup, library loading, native invocation, decoder
  open, packet submission, scheduling, output, or physical execution is
  required.

## Result

Accepted as the bounded AVC managed decoder-preparation parity slice defined by
ADR-0477.
