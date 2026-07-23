# ADR-0149: Controlled Known-Audio Tone Probe

## Status

Accepted for implementation in milestone 0.0.10d Phase 5. Physical execution is
separately gated and is not part of this change.

## Context

The corrected controlled physical audio run produced valid deterministic
evidence and passed `AudioAccepted`, but the operator heard only a short
noise-like sound and recorded `Audio audible: No`. The existing composition
delivers one decoded fixture frame, averaging about 21.3 ms, so duration is a
plausible explanation. Float PCM conversion and WASAPI sample interpretation
remain an alternative boundary.

## Decision

Add an explicit diagnostic command,
`--controlled-windows-known-audio-tone-probe`, that generates a pure-managed
canonical 48 kHz stereo float tone and writes it once through the production
Windows audio buffer processor. The fixed payload is:

- 1,000 Hz, 80 ms, 3,840 sample frames;
- two identical interleaved channels;
- peak 0.20 with linear 5 ms leading and trailing fades;
- 30,720 bytes containing only finite values in `[-0.20, 0.20]`.

The probe reuses the application-owned visible runner, owner-thread dispatcher,
COM/D3D11/WASAPI activation, resource binding, stream initialization, native
evidence capture, production-first cleanup, and borrowed-owner checks. It
requires exactly one write acknowledgement and one Start acknowledgement.
WouldBlock and every other rejection are terminal; there is no retry, second
write, loop, fallback, recreation, or extra queue.

The path-free canonical record is accepted only when all fixed values, native
counters, cleanup, stopped runner, and borrowed-owner preservation agree.

## Diagnostic branch

- Recognizable beep: the canonical float PCM/WASAPI path can produce
  recognizable audio; investigate fixture one-frame duration or decoded
  content.
- Silence, noise, click, or uncertainty: investigate canonical float PCM sample
  interpretation and the WASAPI buffer contract.
- Deterministic rejection: stop at the first terminal boundary without an
  operator question.

## Non-goals

This decision does not establish final physical audio acceptance, replace
fixture audio, add production tone generation, start paired verification,
synthesize audio in normal playback, change normal production behavior or
buffer duration, or repair multi-frame delivery.
