# ADR-0152: Controlled Decoded-Audio Slice Probe

## Status

Accepted for implementation in milestone 0.0.10d Phase 5. Physical execution
is separately gated and is not part of this change.

## Context

The controlled physical fixture-audio run reached accepted production/native
evidence but sounded noise-like and was not recognizable. The later fixed
known-tone probe on main `2ca34405b8bdd81611aa2414092c91a95a8d6d51`
completed with accepted evidence and operator observation
`Known audio tone audible: Yes`. The canonical 48 kHz stereo Float32,
production WASAPI, and endpoint path can therefore produce recognizable audio.
The remaining boundary is the fixture's decoded content, one-frame duration,
managed conversion, or assembly across frames.

## Decision

Add the explicit diagnostic command
`--controlled-windows-decoded-audio-slice-probe`. It uses only the approved
external runtime `btbn.ffmpeg.20260721.win64.lgpl-shared` and retained fixture
SHA-256
`32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15`.
The existing FLV parser, native AAC decoder, and managed conversion produce
canonical audio frames. Video events are not submitted to native decode.

The collector accepts only generation-matched, chronological, continuous
48,000 Hz stereo `PcmFloat32Interleaved` frames with exact buffer arithmetic.
It copies three complete 1,024-frame buffers and the first 768 sample frames of
the fourth, yielding exactly 3,840 sample frames / 80 ms / 30,720 bytes. The
fourth frame's remaining 256 samples are not copied. An incomplete aggregate is
never exposed.

The completed payload bypasses the scheduler and ordinary one-frame delivery
composition, without changing either. It is passed exactly once to the same
application-owned runner, dispatcher, production activation, binding,
100 ms stream initialization, output buffer processor, native evidence, and
cleanup boundaries used by the known-tone probe. WouldBlock, missing Start,
video Present, mismatch, cancellation, or cleanup contradiction is terminal.

## Constraints

There is no resampling, channel remix, volume change, normalization, fade,
synthesis, repetition, stretch, silence insertion, timestamp synthesis,
content-based selection, reordering, retry, second write, fallback, or device
recreation. Source frame ownership remains with the existing completion
boundary and is released exactly once after awaited settlement.

The runtime, fixture, canonical record, and decoded bytes remain outside Git,
build output, publish output, and CI artifacts.

## Non-goals

This does not change normal playback, implement general multi-frame scheduler
delivery, establish final physical audio acceptance, replace the fixture,
synthesize production audio, change timing policy, or start paired
verification.
