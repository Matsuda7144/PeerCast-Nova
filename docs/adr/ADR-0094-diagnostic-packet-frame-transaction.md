# ADR-0094 Diagnostic Packet / Frame Transaction

## Status

Accepted for milestone 0.0.8j. Demux, packet loops, frame extraction, output delivery, production activation, redistribution, and license approval remain pending.

## Decision

Nova adds a diagnostic-only finite transaction for the opened H.264 and AAC contexts created by 0.0.8i.

The transaction validates H.264 video, AAC audio, and non-empty payloads, then performs exactly four ordered operations:

1. send the video packet;
2. attempt one video frame receive;
3. send the audio packet;
4. attempt one audio frame receive.

Packet submission must return `Sent`. A receive attempt may return `FrameAvailable` or `NeedMoreInput`; both represent valid bounded progress. Other results fail closed.

Any rejected operation or exception disposes the complete aggregate lease using the existing audio-then-video cleanup order. Success retains the lease and reports only fixed video and audio receive states.

## Out of scope

- repeated send or receive loops;
- flush, end-of-stream completion, seeking, or discontinuities;
- frame metadata or payload extraction;
- conversion, queueing, rendering, or audio output;
- general demux;
- backend activation or FFmpeg approval changes.
