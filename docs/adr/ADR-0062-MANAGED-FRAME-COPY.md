# ADR-0062 Managed Frame Copy

## Status

Accepted for milestone 0.0.6m.

## Decision

- Consume only the callback-scoped, validated 0.0.6l data views. Native frame
  and data pointers never leave that callback.
- Calculate every logical plane length and the total managed capacity with
  checked arithmetic before copying. Reuse decoded-frame metadata bounds and
  reject totals above 256 MiB.
- Copy only logical video row bytes, omitting native stride padding. Copy only
  logical audio sample bytes. Support YUV420P, NV12, BGRA32, packed S16/F32,
  and planar S16/F32.
- Transfer only an owned, disposable managed frame containing metadata, plane
  index, native stride, rows, logical row bytes, and copied bytes.
- Map insufficient input, overflow/excess, allocation failure, and consumer
  rejection to fixed results. Rejection disposes the copy but preserves the
  decoder context. Existing unexpected-native-failure handling remains
  fail-closed. Non-Windows execution remains unsupported before native access.

## Consequences

Disposal clears owned buffers and is idempotent. This milestone performs no
pixel/sample conversion, decoded queue or sink delivery, rendering, audio
device access, demux, A/V synchronization, or backend activation.
