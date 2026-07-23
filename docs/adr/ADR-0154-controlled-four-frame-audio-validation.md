# ADR-0154: Controlled four-frame audio validation

- Status: Accepted
- Milestone: 0.0.10d Phase 5
- Date: 2026-07-23

## Context

The controlled physical audio run completed deterministic admission after one ordinary AAC decoded-frame delivery, but the approximately 21.3 ms output was perceived only as a noise-like transient. Two independent checks then succeeded:

- a managed 80 ms known tone was clearly audible;
- the retained fixture's chronological decoded first 80 ms was clearly recognizable.

The ordinary production design is intentionally one decoded audio frame per scheduler delivery. A real playback pump repeatedly drives that boundary. Coalescing every normal production delivery to 80 ms would add avoidable latency and would solve a finite validation-harness defect at the wrong layer.

## Decision

Keep ordinary production scheduler and output behavior unchanged.

For controlled physical audio validation only:

- select `AudioOnly` decode media while preserving the existing video run behavior;
- perform four ordinary decode transfers and four ordinary scheduler deliveries;
- require four ordinary WASAPI write attempts and acknowledgements;
- require exactly one WASAPI Start acknowledgement;
- retain the unchanged 100 ms audio buffer;
- retain the existing caller-visible terminal behavior for WouldBlock, cancellation, decode rejection, delivery rejection, device loss, and evidence rejection;
- perform no hidden retry, redelivery, fallback, device recreation, sample aggregation, resampling, amplification, repetition, silence insertion, or timestamp synthesis.

For the retained 48 kHz AAC fixture, four 1,024-sample frames represent 4,096 sample frames, approximately 85.3 ms. This remains below the configured 4,800-frame 100 ms buffer capacity.

The controlled option validation requires at least four decode operations and four delivery operations for audio. Video continues to require one.

## Evidence

The existing controlled Windows evidence gate already accepts multiple deliveries when:

- delivery attempts and delivered counts are positive and internally consistent;
- audio write acknowledgements equal delivered count;
- accepted frame and byte counts are exact;
- Start acknowledgement count is exactly one;
- WouldBlock, hidden retry, fallback, recreation, and device-loss evidence remain zero;
- lifecycle cleanup and borrowed-owner preservation remain accepted.

No record schema change is required. A successful controlled audio record is expected to report four delivery attempts and four delivered items.

## Consequences

The next physical audio run will exercise the ordinary production one-frame path four times rather than replacing that path with a batching policy. A recognizable operator observation can therefore complete independent audio acceptance without changing normal playback latency.

## Non-goals

- continuous resident playback pumping;
- normal production coalescing;
- scheduler or timing-policy changes;
- fixture or runtime changes;
- physical execution in this PR;
- paired video/audio verification.
