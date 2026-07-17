# ADR-0057 avcodec_open2 Boundary

## Status

Accepted for milestone 0.0.6h. Packet submission, frame reception, demux,
conversion, backend activation, redistribution, and license approval remain
pending.

## Decision

Nova opens only an already-owned decoder context whose fixed H.264 or AAC
parameters were successfully transferred through the 0.0.6g boundary. The
context lease records the parameter-transfer state and the opened state under
its existing synchronization lock.

The verified ABI/symbol gate now requires `avcodec_open2` from the same loaded
`avcodec-62.dll` lease. No DLL is rediscovered, re-hashed, re-opened, or loaded
through PATH or the current directory.

The context was allocated by `avcodec_alloc_context3` with the decoder returned
by `avcodec_find_decoder`. The open boundary therefore invokes
`avcodec_open2(context, NULL, NULL)`: the codec remains the one fixed during
allocation, and no option dictionary is introduced in this milestone.

`avcodec_open2` has one accepted success result: zero. Every nonzero result is a
fixed `NativeOpenRejected` failure. A rejected or exceptional native open may
have changed private context state, so Nova disposes the complete context lease
instead of permitting a retry. Missing symbols, unsupported platforms,
parameter-transfer absence, duplicate open, and disposed owners are rejected
before native mutation.

Successful open marks the context once and retains the context, gate, modules,
and verified runtime for later decoder I/O. A second open attempt is rejected
without another native call.

Public results contain only fixed enum categories. Native return values,
pointers, paths, hashes, codec bytes, option text, symbol addresses, and native
error strings are not diagnostics.

## Platform boundary

The production native call is available only on 64-bit Windows. Other platforms
return fixed `Unsupported` without touching the context. Managed boundary tests
run on both Windows and Ubuntu.

## Out of scope

- codec option dictionaries or free-form AVOptions;
- packet allocation or `avcodec_send_packet`;
- frame allocation or `avcodec_receive_frame`;
- flush, seek, discontinuity, or end-of-stream behavior;
- demux, audio resampling, video scaling, rendering, or audio devices;
- FFmpeg redistribution, backend activation, or license approval.
