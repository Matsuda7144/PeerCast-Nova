# ADR-0153: Bounded canonical audio coalescer

- Status: Accepted
- Milestone: 0.0.10d Phase 5
- Date: 2026-07-23

## Context

The independent controlled Windows checks established all of the following:

- one canonical 80 ms, 48 kHz, stereo Float32 tone produced a clearly recognizable beep;
- the retained fixture's chronological decoded first 80 ms also produced a clearly recognizable synthesized sound;
- the ordinary controlled physical-audio composition delivered only one AAC decoded frame, approximately 21.3 ms, and the operator perceived only a noise-like transient.

The decoder, managed conversion, Float32 interleaving, production Windows audio buffer, WASAPI activation, endpoint write, Start acknowledgement, and physical endpoint are therefore independently viable. The remaining acceptance gap is the duration of one ordinary production audio delivery.

## Decision

Add a generation-scoped `PlaybackBoundedAudioCoalescer` for canonical decoded audio.

The boundary:

- accepts only `PcmFloat32Interleaved`, 48,000 Hz, stereo frames;
- requires exact generation and contiguous source sequence;
- copies callback-scoped bytes immediately into bounded owned memory;
- publishes one caller-owned `PlaybackWindowsAudioSampleBuffer` only after the configured sample-frame target is complete;
- retains a final source-frame remainder for the next batch instead of dropping, repeating, stretching, or synthesizing samples;
- permits only one ready unpublished batch at a time;
- releases all owned bytes on disposal;
- defaults to 3,840 sample frames, matching the physically accepted 80 ms decoded-fixture slice.

This first change introduces and tests the ownership primitive without changing the current production delivery path. A subsequent change will connect it to generation-matched scheduler delivery under an explicit option, disabled by default.

## Consequences

The project now has one reusable ownership boundary capable of converting four 1,024-sample AAC decoded frames into a 3,840-sample output batch while preserving the fourth frame's remaining 256 samples for the next batch.

No runtime, fixture, decoder, scheduler, timing, Windows output, physical-record, or paired-verification behavior changes in this decision's implementation PR.

## Non-goals

- resampling or channel remixing;
- normalization, amplification, fading, silence insertion, repetition, or timestamp synthesis;
- automatic physical execution;
- final audio acceptance;
- paired video/audio verification;
- changing the default one-frame production delivery path in the primitive-only PR.
