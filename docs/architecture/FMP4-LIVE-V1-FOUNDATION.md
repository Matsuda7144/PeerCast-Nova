# PeerCast Nova fMP4 Live v1 Foundation

Status: Pure-managed foundation complete

PR #236 remains an independent Draft workstream. This document describes the completed production-independent fMP4 Live v1 foundation and the boundary that must remain intact until a separately reviewed production-integration phase begins.

## Purpose

Provide a deliberately narrow, reusable foundation for future PeerCast Nova fragmented MP4 live transport, relay, and playback without connecting that foundation to PCP production ingress, retention ownership, relay fan-out, decoder composition, Windows output, GUI, or MVP validation.

The foundation validates and projects bounded ISO BMFF/fMP4 structure into immutable managed metadata and sample-byte ranges. It does not own transport memory, live history, codec decoding, or output.

## Isolation

This branch is an independent workstream based on `main`.

It must remain separate from the active PR #229 playback, PR #230 GUI Next, PR #232 MVP Validation, and other independent workstreams. Do not merge, rebase, cherry-pick, transplant, copy implementation across workstreams, force-push, or rewrite history as part of this foundation.

PR #236 must remain OPEN, Draft, unmerged, and with auto-merge disabled while this workstream is active.

## Initial codec profile

The completed v1 foundation targets:

- fragmented ISO BMFF / fMP4;
- exactly one H.264/AVC video track in the initialization profile;
- exactly one AAC audio track in the initialization profile;
- one initialization segment followed by finite media fragments;
- media fragments that may contain video only, audio only, or both known tracks;
- no encryption;
- no adaptive bitrate switching;
- no mid-stream codec replacement or sample-description switching;
- no seeking requirement;
- no production playback connection in this foundation phase.

AV1, Opus, multiple renditions, browser delivery, adaptive bitrate, encryption, and mid-stream codec replacement remain later extensions.

## Strict live segment envelope

The v1 envelope is intentionally stricter than general ISO BMFF.

Initialization segment v1:

1. one finite `ftyp` box;
2. one finite `moov` box;
3. no additional top-level boxes.

Media segment v1:

1. one finite `moof` box;
2. one finite `mdat` box;
3. no additional top-level boxes.

A top-level size of zero (extends-to-end) is rejected even where ISO BMFF could otherwise permit it. The foundation requires explicit finite segment boundaries before a segment can be validated, retained, relayed, hashed, or considered for late join.

Future separately reviewed versions may admit forms such as `styp`, `sidx`, `emsg`, `prft`, multiple `mdat` boxes, or other valid ISO BMFF structures. Their exclusion from v1 does not imply that those structures are invalid ISO BMFF.

## Completed initialization projections

The pure-managed initialization path now provides:

- bounded track inventory from the validated `moov`;
- track handler projection;
- `tkhd` TrackId identity projection with duplicate and malformed identity rejection;
- bounded sample-description projection;
- AVC/AVC3 initialization configuration projection;
- AAC initialization configuration projection with descriptor and malformed-input bounds;
- an exact one-video/one-audio initialization profile binding;
- `mdhd` track timing projection for the bound video and audio tracks;
- `mvex`/`trex` fragment-default projection bound by initialization TrackId;
- v1 sample-description-index enforcement for fragment defaults.

Valid unrelated nested ISO BMFF siblings are tolerated where the v1 profile does not require exact nested cardinality. Required boxes and semantic identities still fail closed when missing, malformed, duplicated, or inconsistent.

## Completed media-fragment projections

The pure-managed media path now provides:

- media-fragment identity and `mfhd` sequence projection;
- `tfdt` decode-timeline projection;
- semantic binding of fragment TrackIds to the initialization video/audio profile;
- support for video-only, audio-only, and video+audio fragments using only known initialization TrackIds;
- resolved fragment runs using the existing ISO BMFF `trex` + `tfhd` + `trun` defaults/inheritance primitives;
- bounded media-segment payload projection that relates resolved sample byte ranges to the validated `mdat` payload;
- random-access sample classification;
- late-join eligibility projection using the identified video track rather than inferring codec configuration from arbitrary media bytes.

Unknown TrackIds, semantic projection mismatches, unsupported sample-description selection, malformed run/default structures, and invalid payload mapping fail closed.

## Live continuity and deterministic coverage

The foundation also includes:

- deterministic H.264/AAC fMP4 fixture coverage across the strict initialization/media envelopes;
- chunk-split regression coverage for bounded parsing behavior;
- a live epoch state primitive built on the existing fragment-sequence and track-decode continuity primitives;
- epoch retention for continuous media and epoch advancement for recognized discontinuity;
- fail-closed handling for invalid continuity state and epoch overflow.

Sequence continuity and decode timeline remain distinct concepts. A sequence transition alone is not used as a substitute for track decode-timeline validation.

## Late join contract

A future live transport may retain a validated current initialization segment and identify the next media fragment that the foundation classifies as an independently usable late-join point.

A late join must never infer decoder configuration from an arbitrary media fragment. Initialization TrackIds and codec configuration remain authoritative for binding later fragment TrackIds.

This foundation does not define PCP wire framing, retention ownership, retention lifetime, relay fan-out policy, decoder lifetime, or output scheduling.

## Production integration boundary

Pure-managed fMP4 Live v1 foundation work ends at validated structure, normalized metadata, continuity/eligibility decisions, and bounded sample-byte projection.

The next phase is not another parser-foundation slice. It is a separately reviewed production-integration phase that may compose these validated outputs with existing generic playback/decode primitives and, only under that phase's own safety constraints, connect them to transport and output ownership.

A future integration should preserve this ordering conceptually:

1. acquire a complete caller-owned bounded segment;
2. validate the strict v1 envelope;
3. project and bind initialization profile/defaults/timing or media identity/timeline/runs;
4. validate sample payload ranges and late-join/continuity semantics;
5. only then hand validated results to separately owned transport, retention, codec, or output composition.

The foundation must not grow implicit PCP framing, live-buffer ownership, decoder construction, relay fan-out, Windows output, GUI behavior, or MVP production validation merely to make that future integration convenient.

## Explicit non-goals of PR #236

PR #236 does not:

- define or consume PCP production wire framing;
- own live retention or relay fan-out;
- construct a production decoder pipeline;
- create Windows media/audio output;
- modify GUI behavior;
- connect to MVP production validation;
- claim end-to-end live playback completion;
- add encryption, ABR, multiple sample descriptions, or mid-stream codec replacement.

Those require independent design and validation after this foundation boundary.

## Completion criterion

The v1 pure-managed foundation is considered complete when the strict envelopes, initialization profile/default/timing projections, media identity/timeline/run/payload projections, random-access and late-join semantics, deterministic fixtures, and continuity/epoch primitives are all present with focused regression coverage and the authoritative CI for the exact branch HEAD is green.

After that point, further work in this PR should be limited to foundation defects, regression hardening, or documentation corrections. Production composition belongs to a separately authorized integration milestone.
