# ADR-0050 Installed FFmpeg Runtime Inventory

## Status

Accepted and verified in 0.0.6c.

## Decision

The pinned FFmpeg candidate is reduced to an exact runtime boundary of five
shared libraries: `avcodec`, `avformat`, `avutil`, `swresample`, and `swscale`.
Each file is pinned by portable file name, byte length, and SHA-256.

Verification accepts caller-owned streams, reports only fixed result categories,
and rejects missing, unexpected, size-mismatched, or digest-mismatched input
before any native library is loaded. Paths remain outside Playback contracts.

`ffmpeg.exe` and `ffprobe.exe` remain candidate-verification tools. `ffplay`,
`avfilter`, and `avdevice` are not part of the decoder runtime boundary.

License review remains pending and runtime verification does not authorize
activation or redistribution.
