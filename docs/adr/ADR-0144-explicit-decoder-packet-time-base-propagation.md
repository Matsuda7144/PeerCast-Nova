# ADR-0144: Explicit Decoder Packet Time-Base Propagation

## Status

Accepted for milestone 0.0.10d Phase 5 diagnostics remediation.

## Context

The controlled Windows physical video run completed fixture replay and scheduler admission but rejected the first reserved video frame before timing or native output. The decoded frame retained raw FFmpeg PTS and best-effort values of 621, while `AVFrame.time_base` was `0/1`. Managed PTS, best-effort timestamp, and duration consequently remained unknown.

The bounded FLV parser and metadata-aware packet boundary had already preserved PTS/DTS 621 and time base `1/1000`. The first loss of usable timing occurred because the decoder context did not receive its packet time base and the frame metadata boundary correctly treated the raw `0/1` value as unset.

Writing private `AVCodecContext` or `AVFrame` offsets would couple Nova to an unstable native layout. Synthesizing zero, sequence-derived, or wall-clock timestamps would hide the propagation defect.

## Decision

Add one explicit FFmpeg public-option boundary for bounded FLV decoder sessions:

- call required symbol `av_opt_set_q` on the owned `AVCodecContext`;
- use option name `pkt_timebase`, the exact parser-provided `PlaybackBoundedDemuxTimeBase`, and flags zero;
- configure only after codec parameters transfer and before `avcodec_open2`;
- configure video and audio independently and fail closed before the next step on any rejection;
- retain the configured value in the context lease only after native success;
- reject duplicate configuration, configuration before transfer, and configuration after open;
- clear retained state during context disposal.

The generic codec-open boundary does not require packet-time-base configuration. This requirement belongs only to the source-bound bounded FLV native decoder session.

For decoded metadata, select the effective time base as follows:

1. A raw frame numerator and denominator that are both positive remain authoritative.
2. A raw numerator of zero with a positive denominator is exactly unset; use the valid retained context packet time base when present.
3. An unset raw value with no retained configuration keeps the existing unknown-time behavior.
4. A negative raw numerator or nonpositive raw denominator is invalid metadata and never falls back.

The effective value converts PTS, best-effort timestamp, and duration. `AV_NOPTS_VALUE` remains unknown, zero duration remains unknown, negative duration remains invalid, and existing checked arithmetic and duration bounds remain unchanged. Raw native fields are never mutated.

## Consequences

The diagnosed frame `{ pts=621, best_effort=621, duration=0, time_base=0/1 }` from a context configured with `1/1000` produces known 621 ms PTS and best-effort timestamp with unknown duration. Managed frame copy and existing decoded-output/scheduler boundaries receive that metadata without changing generation, sequence, discontinuity, or ownership.

The ABI gate now requires exact symbol `av_opt_set_q` from the reviewed `avutil-60` role. No private offset write, timestamp synthesis, retry, sleep, polling, new queue, runtime fallback, fixture mutation, or physical-output claim is introduced.

This decision does not establish a successful physical video or audio run. Operator-visible video, operator-audible audio, record verification, and 0.0.10d completion remain pending.
