# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestones 0.0.10d／0.0.10e — Complete**

PeerCast Nova は、exact approved external FFmpeg runtime、controlled native decode、Windows video／audio output validation、shared Windows application ownership、legal／About projection、framework-dependent `win-x64` portable packaging boundaryまで実装・検証済みです。

### 0.0.10d — Controlled Physical Windows Video／Audio Output Validation

- active FFmpeg build ID: `btbn.ffmpeg.20260721.win64.lgpl-shared`
- controlled native decode: H.264 `10` packets／frames、AAC `48` packets／frames、audio samples `49,152`、decode errors `0`、complete cleanup
- controlled fixture video: `Completed／Accepted`、independent preflight `VideoAccepted`、operator observation `Video visible: Yes`
- final accepted audio run: exact main `6eb3558ec0efd434d9ee1e7221ba86d3c115684f`
- audio result: canonical `Completed／Accepted`、delivery `4／4`、strict 26-line record
- completed audio record SHA-256: `C6CD53C053F99FF43A1514E1162D3C90FE307C5FE32285F73013A612C2FBD100`
- independent completed-record preflight: `RenderEvidenceRecordValidated`
- same-run operator observation: `Audio audible: Yes`
- recognizable retained-fixture beep: `Yes`
- final independent paired video／audio admission: completed on merged main `4f38de92ce66d988479f5e22b263de9f84b32086`
- Issue #129: closed as `completed`

Historical contradictory and rejected runs remain preserved as historical evidence and are not reinterpreted as successful. Their authorizations and paths are consumed and must not be reused.

Current-state reviews:

- [PCN-0166 Controlled Audio Render Authorization State](docs/reviews/PCN-0166-CONTROLLED-AUDIO-RENDER-AUTHORIZATION-STATE.md)
- [PCN-0167 Managed Audio Render Failure Boundary](docs/reviews/PCN-0167-MANAGED-AUDIO-FAILURE-BOUNDARY.md)
- [Issue #129](https://github.com/Matsuda7144/PeerCast-Nova/issues/129)

### 0.0.10e — Shared Windows Application Ownership and Portable Packaging Boundary

- `Nova.Windows` is the sole owner of the Win32 window、HWND lifetime、message loop、owner thread、bounded dispatcher、shutdown、and native cleanup
- `Nova.App` provides finite startup／close／cancel shutdown above one shared host
- `Nova.App` projects the reviewed bounded FFmpeg legal／About notice and provenance material
- `Nova.Playback` borrows only the dispatcher contract and window token
- `Nova.Cli` continues to use the same shared host for explicitly selected controlled commands
- framework-dependent `win-x64` portable packaging is the only implemented application distribution boundary
- `Nova.App --help` and `Nova.App --about` expose bounded application and legal information without starting the Windows host
- FFmpeg remains external; no binary、archive、runtime pack、acquisition、installation、update、or invocation path is bundled
- installer、code signing、and updater mechanics remain separate future ADR decisions

## 実装済みの主な領域

### PCP互換・Channel受信

- PeerCastStation互換HTTP relay entryとPCP handshake
- CHAN control decodeとbounded payload受信
- backpressure、有限timeout、停止、再接続
- official Stable／developmentとの互換性検証

### Relay

- bounded fan-outとslow child isolation
- downstream relay session／server
- owned relay coordinatorと有限drain
- controlled real-media relay verification

### 再生基盤

- verified FFmpeg runtime locator／leaseとABI gate
- native decoder context、parameter transfer、codec open、packet／frame境界
- pure managed bounded FLV v1 parserとexact session decoder-parameter binding
- bounded payload-free ISO BMFF box-header and container traversal
- bounded ISO BMFF `ftyp` metadata projection
- bounded ISO BMFF FullBox version／flags prefix parser
- exact ISO BMFF `mvhd` version 0／1 timing projection
- exact ISO BMFF `mdhd` version 0／1 timing／language projection
- bounded ISO BMFF `hdlr` Video／Audio／Other classification
- exact ISO BMFF `tkhd` track identity／flags／raw dimensions
- bounded ISO BMFF `stsd` sample-entry list／range projection
- bounded ISO BMFF `stts` decode-time runs／checked aggregates
- bounded ISO BMFF `ctts` unsigned／signed composition-offset runs
- bounded ISO BMFF `stsz` uniform／individual sample-size projection
- bounded ISO BMFF `stsc` sample-to-chunk run projection
- bounded ISO BMFF `stco` 32-bit chunk-offset projection
- bounded ISO BMFF `co64` 64-bit chunk-offset projection
- bounded ISO BMFF `stss` ordered sync-sample projection
- bounded ISO BMFF `stz2` compact 4／8／16-bit sample-size projection
- bounded ISO BMFF `sdtp` typed sample-dependency projection
- bounded ISO BMFF `stps` ordered partial-sync sample projection
- optional ISO BMFF `ctts` composition-count reconciliation
- optional ISO BMFF `sdtp` dependency-count reconciliation
- optional ISO BMFF `stss` sample-number range reconciliation
- optional ISO BMFF `stps` partial-sync sample-number range reconciliation
- exact ISO BMFF `stsc`／`stco`／`co64` chunk-table reconciliation
- run-length ISO BMFF chunk sample-count projection and reconciliation
- ISO BMFF `stsc` sample-description index reconciliation
- bounded ISO BMFF chunk-to-`stsc` run lookup
- bounded ISO BMFF chunk-offset lookup across `stco`／`co64`
- bounded ISO BMFF sample-to-chunk location lookup
- bounded ISO BMFF per-sample size lookup across `stsz`／`stz2`
- bounded ISO BMFF absolute sample byte-range lookup
- bounded ISO BMFF sample file-bounds validation
- bounded ISO BMFF selected-`mdat` sample containment
- bounded ISO BMFF top-level sample-to-`mdat` lookup
- bounded ISO BMFF per-sample decode-time lookup
- bounded ISO BMFF per-sample composition-offset lookup
- exact ISO BMFF per-sample presentation-time lookup
- bounded ISO BMFF `Int128` media-time conversion
- bounded ISO BMFF per-sample presentation-interval conversion
- bounded ISO BMFF `elst` edit-list projection
- bounded ISO BMFF movie-time-to-edit-segment lookup
- exact normal-rate ISO BMFF movie-time-to-media-time edit mapping
- exact ISO BMFF edit-list／track-duration reconciliation
- exact normal-rate ISO BMFF edit movie／media interval projection
- bounded ISO BMFF sample／edit media-overlap lookup
- exact rational ISO BMFF sample／edit movie-overlap projection
- conservative complete-sample ISO BMFF edit admission
- bounded ISO BMFF sample-number-to-edited-admission composition
- bounded ISO BMFF edited-admission-to-sample-byte-range association
- bounded ISO BMFF edited-sample-to-`mdat` metadata association
- validated non-owning and bounded owned ISO BMFF edited-sample payloads
- codec-neutral ISO BMFF edited-sample packet candidate metadata
- bound ISO BMFF edited-sample `stsd` codec-candidate selection
- bounded ISO BMFF `avc1`／`avc3` VisualSampleEntry projection
- bounded ISO BMFF `mp4a` AudioSampleEntry projection
- bounded owned ISO BMFF `avcC` decoder-configuration projection
- bounded ISO BMFF MPEG-4 descriptor tag／variable-length header parser
- bounded ISO BMFF AVC length-prefixed sample framing
- bounded ISO BMFF `esds` outer elementary-stream descriptor projection
- bounded ISO BMFF audio decoder-config descriptor projection
- bounded ISO BMFF AudioSpecificConfig leading header projection
- bounded ISO BMFF GASpecificConfig projection for supported General Audio types
- bounded ISO BMFF predefined-MP4 SLConfigDescriptor projection
- bounded ISO BMFF explicit SBR/PS extension projection
- bounded explicit SBR/PS-to-GASpecificConfig composition
- bounded immediate implicit SBR sync-extension projection
- bounded optional implicit PS sync-extension projection
- caller-bounded non-immediate implicit SBR sync-extension search (ADR-0328)
- caller-explicit implicit SBR resolution policy (ADR-0329)
- bounded same-track codec packet sequence (ADR-0330)
- bounded immutable track codec packet sequence cursor (ADR-0331)
- exact track codec packet sequence acknowledgement (ADR-0332)
- bounded caller-ordered movie track sequence set (ADR-0333)
- explicit movie track codec packet sequence selection (ADR-0334)
- bounded selected track codec packet sequence cursor set (ADR-0335)
- exact selected sequence packet head snapshot (ADR-0336)
- exact selected sequence head movie-time comparison (ADR-0337)
- caller-explicit sequence advance-role selection (ADR-0338)
- exact selected sequence cursor-set advance (ADR-0339)
- exact cross-track sequence merge step (ADR-0340)
- bounded cross-track merge-step sequence (ADR-0341)
- caller-bounded cross-track merge traversal (ADR-0342)
- exact merge traversal continuation (ADR-0343)
- exact packet merge completion certificate (ADR-0344)
- exact completed merge-step global view (ADR-0345)
- bounded completed merge-event projection (ADR-0346)
- bounded completed merge-event cursor (ADR-0347)
- exact completed merge-event acknowledgement (ADR-0348)
- caller-bounded completed merge-event traversal (ADR-0349)
- exact completed merge-event traversal continuation (ADR-0350)
- exact completed merge-event traversal completion (ADR-0351)
- exact completed merge-event traversal acknowledgement view (ADR-0352)
- exact completed merge-event traversal seconds metadata (ADR-0353)
- caller-explicit completed merge-event traversal clock time (ADR-0354)
- caller-explicit completed merge-event traversal integral clock time (ADR-0355)
- caller-explicit completed merge-event traversal clock origin (ADR-0356)
- bounded completed merge-event traversal deadlines (ADR-0357)
- caller-explicit completed merge-event traversal deadline observation (ADR-0358)
- caller-explicit completed merge-event traversal dispatch eligibility (ADR-0359)
- bounded completed merge-event traversal dispatch candidates (ADR-0360)
- explicit completed merge-event traversal dispatch target set (ADR-0361)
- exact completed merge-event traversal dispatch target binding (ADR-0362)
- bounded completed merge-event traversal dispatch request (ADR-0363)
- explicit completed merge-event traversal dispatch target capabilities (ADR-0364)
- exact completed merge-event traversal dispatch target resolution (ADR-0365)
- explicit completed merge-event traversal runtime target registry (ADR-0366)
- exact completed merge-event traversal runtime target lookup (ADR-0367)
- caller-identified completed merge-event traversal invocation intent (ADR-0368)
- caller-explicit completed merge-event traversal invocation admission (ADR-0369)
- bounded completed merge-event traversal admitted invocation selection (ADR-0370)
- caller-bounded track sample sequence plan (ADR-0371)
- exact track sample edit-admission projection (ADR-0372)
- bounded track admitted sample selection (ADR-0373)
- exact track admitted sample byte-range projection (ADR-0374)
- exact track admitted sample `mdat` projection (ADR-0375)
- bounded track owned sample-payload projection (ADR-0376)
- exact track edited-sample packet-candidate projection (ADR-0377)
- exact track sample-description selection projection (ADR-0378)
- exact track sample-entry parse projection (ADR-0379)
- exact track codec-configuration projection (ADR-0380)
- bounded track codec-packet composition projection (ADR-0381)
- exact track codec-packet binding projection (ADR-0382)
- exact movie-track codec-packet binding projection (ADR-0383)
- exact movie-track codec-packet sequence projection (ADR-0384)
- exact movie-track codec-packet sequence-set projection (ADR-0385)
- caller-explicit movie-track sequence selection projection (ADR-0386)
- exact selected-sequence initial cursor-set projection (ADR-0387)
- caller-bounded projected-sequence merge traversal (ADR-0388)
- exact projected-sequence merge continuation (ADR-0389)
- exact projected-sequence merge completion (ADR-0390)
- exact projected completed merge-step view (ADR-0391)
- bounded projected completed merge events (ADR-0392)
- exact projected completed-event initial cursor (ADR-0393)
- caller-bounded projected completed-event traversal (ADR-0394)
- exact projected completed-event traversal continuation (ADR-0395)
- exact projected completed-event traversal completion (ADR-0396)
- exact projected completed-event acknowledgement view (ADR-0397)
- exact projected completed-event traversal time (ADR-0398)
- caller-explicit projected traversal clock time (ADR-0399)
- caller-explicit projected integral clock time (ADR-0400)
- caller-explicit projected traversal clock origin (ADR-0401)
- bounded projected traversal deadlines (ADR-0402)
- caller-explicit projected deadline observation (ADR-0403)
- caller-explicit projected dispatch eligibility (ADR-0404)
- caller-bounded projected dispatch candidate selection (ADR-0405)
- explicit projected dispatch target set (ADR-0406)
- exact projected dispatch target binding (ADR-0407)
- bounded projected dispatch requests (ADR-0408)
- explicit projected target capability set (ADR-0409)
- exact projected target capability resolution (ADR-0410)
- explicit projected opaque runtime-target registry (ADR-0411)
- exact projected opaque runtime-target lookup (ADR-0412)
- caller-identified projected invocation intent (ADR-0413)
- caller-explicit projected invocation admission (ADR-0414)
- caller-bounded projected admitted-invocation selection (ADR-0415)
- bounded fragmented-MP4 `trex` track defaults (ADR-0416)
- bounded fragmented-MP4 `mfhd` sequence header (ADR-0417)
- bounded fragmented-MP4 `tfhd` track-fragment header (ADR-0418)
- bounded fragmented-MP4 `tfdt` decode time (ADR-0419)
- bounded fragmented-MP4 `trun` sample run (ADR-0420)
- bounded fragmented-MP4 sample-default resolution (ADR-0421)
- bounded fragmented-MP4 sample-timing projection (ADR-0422)
- bounded fragmented-MP4 run data-origin resolution (ADR-0423)
- bounded fragmented-MP4 sample byte-range projection (ADR-0424)
- bounded fragmented-MP4 sample metadata projection (ADR-0425)
- bounded fragmented-MP4 rational-time projection (ADR-0426)
- bounded fragmented-MP4 presentation ordering (ADR-0427)
- bounded fragmented-MP4 sequence/decode continuity (ADR-0428, ADR-0429)
- bounded fragmented-MP4 continuity binding and exact chain (ADR-0430, ADR-0431)
- bounded fragmented-MP4 continuity summary (ADR-0432)
- bounded fragmented-MP4 sample batch binding (ADR-0433)
- bounded fragmented-MP4 sample batch chain (ADR-0434)
- bounded fragmented-MP4 presentation merge (ADR-0435)
- bounded fragmented-MP4 merge cursor, exact step, traversal, and completion (ADR-0436 through ADR-0439)
- bounded non-delivery fragmented-MP4 traversal acknowledgement view and source binding (ADR-0440 and ADR-0441)
- exact rational fragmented-MP4 acknowledgement timing and source binding (ADR-0442 and ADR-0443)
- caller-explicit integral fragmented-MP4 timing and source binding (ADR-0444 and ADR-0445)
- caller-explicit signed fragmented-MP4 clock origin and source binding (ADR-0446 and ADR-0447)
- bounded fragmented-MP4 decode/presentation deadline metadata and source binding (ADR-0448 and ADR-0449)
- caller-explicit fragmented-MP4 decode/presentation deadline observation and source binding (ADR-0450 and ADR-0451)
- caller-explicit fragmented-MP4 decode/presentation eligibility and source binding (ADR-0452 and ADR-0453)
- caller-bounded fragmented-MP4 eligible subset selection and source binding (ADR-0454 and ADR-0455)
- exact target-free fragmented-MP4 request metadata and source binding (ADR-0456 and ADR-0457)
- caller-explicit opaque fragmented-MP4 consumer identity and exact request binding (ADR-0458 and ADR-0459)
- caller-explicit opaque fragmented-MP4 batch identity and bounded consumer batch projection (ADR-0460 and ADR-0461)
- bounded fragmented-MP4 submission intents and caller-explicit descriptive classification (ADR-0462 and ADR-0463)
- caller-bounded fragmented-MP4 included-intent source-order selection (ADR-0464)
- exact fragmented-MP4 selected-intent manifest metadata (ADR-0465)
- exact fragmented-MP4 submission-manifest source binding (ADR-0466)
- declarative fragmented-MP4 manifest summary and bounded contiguous segments (ADR-0467 and ADR-0468)
- bounded fragmented-MP4 manifest-segment cursor and exact immutable step (ADR-0469 and ADR-0470)
- bounded fragmented-MP4 manifest-segment traversal and exact metadata completion (ADR-0471 and ADR-0472)
- exact non-acknowledging fragmented-MP4 completed-segment view (ADR-0473)
- bounded regular-track demux composition from exact parsed sources and file bytes to owned AVC／AAC movie-track packet sequences (ADR-0474)
- bounded same-movie regular-track composition, explicit AVC/AAC selection, and cross-track traversal (ADR-0475)
- selected regular-movie owned AVC configuration input and ordinary AAC managed decoder preparation (ADR-0476)
- bounded AVC managed codec parameters, transfer plan, symbol coverage, and declared capability binding (ADR-0477)
- exact selected AVC/AAC managed-preparation binding to caller-supplied verified ABI gate leases (ADR-0478)
- bounded selected regular-movie native decoder-session transaction with exact track time bases (ADR-0479)
- bounded exact-merge regular-movie metadata-aware native packet submission (ADR-0480)
- bounded regular-movie packet/backpressure/flush/drain progress with managed frame ownership (ADR-0481)
- exact regular-movie managed-frame transfer into the existing bounded scheduler (ADR-0482)
- exact one-reservation regular-movie canonical sink delivery (ADR-0483)
- exact canonical regular-movie Windows output buffer sink adaptation (ADR-0484)
- caller-timed regular-movie scheduler reservation admission (ADR-0485)
- bounded regular-movie timed-delivery wait driving (ADR-0486)
- bounded regular-movie Windows output reconfiguration (ADR-0487)
- coordinated regular-movie timed-delivery generation reset (ADR-0488)
- bounded regular-movie native decoder discontinuity reset (ADR-0489)
- bounded ISO BMFF AVC NAL-header classification
- bounded non-materialized AVC parameter-set prefix planning
- bounded owned AVC length-prefixed payload materialization
- bounded owned AVC Annex-B payload materialization
- bounded sample-only AVC Annex-B materialization
- explicit source-bound AVC payload-format selection
- explicit AVC decoder-payload requirements resolution
- bounded end-to-end managed AVC payload composition
- bounded ISO BMFF GA Specific Config prefix projection
- bounded ISO BMFF Program Config Element fixed-header projection
- bounded ISO BMFF Program Config Element body projection
- bounded ISO BMFF Program Config Element channel-topology projection
- bounded ISO BMFF Program Config Element ordered channel-slot mapping
- explicit ISO BMFF Program Config Element speaker-position assignment
- bounded ISO BMFF Program Config Element WaveFormat channel-layout projection
- bounded managed-only ISO BMFF Program Config Element channel-reorder planning
- bounded PCE-aware ISO BMFF AAC managed decoder preparation
- bounded managed ISO BMFF Program Config Element interleaved-PCM reordering
- bounded ISO BMFF PCE AAC explicit channel-layout transfer requirement
- bounded ISO BMFF PCE AAC channel-layout declared-symbol coverage
- bounded end-to-end ISO BMFF PCE AAC explicit-layout preparation
- bounded PCE-aware ISO BMFF GA Specific Config composition
- bounded ISO BMFF ER Audio `epConfig` projection
- bounded ISO BMFF ER Audio protection-requirement projection
- bounded ISO BMFF ER Error Protection Specific Config prefix projection
- bounded ISO BMFF ER Error Protection first-set header projection
- bounded ISO BMFF ER Error Protection first-class projection
- bounded ISO BMFF ER Error Protection first-set traversal
- bounded ISO BMFF ER Error Protection second-set header projection
- bounded ISO BMFF ER Error Protection second-set first-class projection
- bounded ISO BMFF ER Error Protection second-set traversal
- bounded ISO BMFF ER Error Protection third-set header projection
- bounded ISO BMFF ER Error Protection third-set first-class projection
- bounded ISO BMFF ER Error Protection third-set traversal
- bounded ISO BMFF ER Error Protection general predefined-set traversal
- bounded ISO BMFF ER Error Protection header-protection projection
- bounded complete ISO BMFF ER Error Protection Specific Config ownership
- bounded ISO BMFF ER direct-mapping projection
- explicit ISO BMFF ER direct-mapping admission
- bounded ISO BMFF ER configuration admission
- bounded admitted ISO BMFF ER implicit SBR／PS sync-extension projection
- bounded admitted ISO BMFF ER AAC decoder-format intent composition
- bounded admitted ISO BMFF ER AAC managed decoder preparation
- bounded ISO BMFF ER AAC managed decoder preparation
- bounded ISO BMFF ER implicit SBR／PS sync-extension projection
- bounded ISO BMFF AAC core format-facts resolution
- bounded ISO BMFF AAC extension-signalling facts resolution
- bounded ISO BMFF AAC decoder-format intent resolution
- bounded owned ISO BMFF AAC decoder extradata
- bounded ISO BMFF AAC FFmpeg codec-parameter input adaptation
- bounded ISO BMFF AAC managed codec-parameter ownership
- bounded managed-only ISO BMFF AAC parameter-transfer planning
- bounded managed-only ISO BMFF AAC transfer-symbol coverage
- bounded managed-only ISO BMFF AAC declared decoder-capability binding
- bounded end-to-end ISO BMFF AAC managed decoder preparation
- bounded ISO BMFF AAC `mp4a`／decoder-format consistency facts
- explicit ISO BMFF AAC sample-entry consistency admission policy
- bounded sample-entry-aware ISO BMFF AAC managed decoder preparation
- bounded codec-neutral ISO BMFF AVC／AAC packet projection
- bounded ISO BMFF track-bound codec packet metadata
- bounded ISO BMFF movie-timescale-bound track codec packet metadata
- bounded ISO BMFF movie codec packet stream indexing
- explicit ISO BMFF movie codec video/audio stream selection
- bounded ISO BMFF movie codec packet event projection
- exact ISO BMFF movie codec packet cross-stream event ordering
- exact ISO BMFF movie codec packet event-time seconds metadata
- bounded ISO BMFF movie codec packet event queue snapshot
- bounded ISO BMFF movie codec packet event queue cursor
- exact ISO BMFF movie codec packet event queue acknowledgement
- exact ISO BMFF movie codec packet event queue clock-time metadata
- caller-explicit ISO BMFF movie codec packet event queue integral clock time
- caller-explicit ISO BMFF movie codec packet event queue clock origin
- bounded ISO BMFF movie codec packet event queue deadline metadata
- caller-explicit ISO BMFF movie codec packet deadline observation
- caller-explicit ISO BMFF movie codec packet dispatch eligibility
- bounded ISO BMFF movie codec packet dispatch candidate selection
- explicit ISO BMFF movie codec packet dispatch target identity set
- exact ISO BMFF movie codec packet dispatch target binding
- bounded ISO BMFF movie codec packet dispatch request projection
- explicit ISO BMFF movie codec packet target capability set
- bounded ISO BMFF movie codec packet target resolution metadata
- explicit ISO BMFF movie codec packet runtime target registry
- bounded ISO BMFF movie codec packet runtime target lookup metadata
- bounded ISO BMFF movie codec packet dispatch invocation intent metadata
- caller-explicit ISO BMFF movie codec packet dispatch invocation admission
- bounded ISO BMFF movie codec packet admitted invocation selection
- exact ISO BMFF `stts`／`stsz`／`stz2` sample-count reconciliation
- managed frame copy、conversion、awaited delivery
- opened-decoder `avcodec_flush_buffers` ownership boundary
- count／byte bounded decoded-frame scheduler
- default production `250 ms` audio-only decoded-duration cutoff with sample-derived fallback
- finite late-aware scheduler dispatchとgeneration-matched output lifecycle
- Windows D3D11／WASAPI activation、binding、stream、buffer、device-loss境界
- controlled real external-runtime H.264／AAC decode validation
- accepted visible controlled fixture video
- completed／terminal／failure audio evidence、strict offline preflight、readiness-required one-run authorization boundary
- completed audio evidenceとindependent paired video／audio admission

### Current managed ISO BMFF packet/event boundary

ADR-0300 through ADR-0327 form a managed metadata chain from one exact
AVC-selected or AAC-original current sample through track/movie binding,
caller-ordered indexing, explicit video/audio selection, bounded event
projection, exact rational cross-stream ordering, and exact rational seconds
metadata, ending in a bounded immutable queue snapshot, cursor, and exact
one-step acknowledgement, with caller-explicit exact rational clock-time
metadata and caller-explicit integral-tick rounding.
The resulting integral intervals can be placed on a caller-explicit signed
timeline, projected as ready/exclusive-expiration deadlines, and classified at
one caller-explicit tick without reading a clock. Caller-explicit late-event
eligibility then produces a bounded source-ordered candidate snapshot.
Candidates bind by media kind to opaque caller-explicit video/audio target
identities and expose bounded request metadata without storing an invocable
target. Requests resolve against explicit media/payload capabilities without a
runtime object. Opaque runtime instance IDs and generations then bind by exact
target and media identity without exposing an instance reference. One
caller-explicit nonempty invocation ID and exact source-order indexes then form
an immutable invocation-intent batch without accessing or invoking a runtime
instance. Each exact intent can finally be classified by a caller-explicit
admitted or denied disposition without granting runtime access, then reduced
to a caller-bounded source-ordered admitted subset with explicit truncation.

This chain currently represents one current packet per selected track. It does
not implement multi-sample traversal, default rounding or origin policies,
clock acquisition, repeated observation, actual dispatch, delivery outcome,
runtime instance access or invocation, cursor mutation, scheduling, native packet submission, or
production MP4 playback composition.

ADR-0330 separately owns a bounded immutable same-track sequence of already
composed packets with strictly increasing sample numbers. Automatic sample-table
iteration and packet construction remain outside that slice. ADR-0331 adds pure
immutable one-entry traversal with explicit exhaustion and no delivery claim.
ADR-0332 retains one exact cursor/entry/successor triple as acknowledgement
metadata without claiming delivery or consumption.
ADR-0333 separately owns up to 256 exact same-movie track sequences with unique
track IDs and caller-ordered stream indexes, without selecting or merging heads.
ADR-0334 retains caller-explicit video and/or audio sequence selection from that
exact set without default inference, fallback, cursor creation, or merging.
ADR-0335 creates immutable initial cursors for exactly those selected roles,
without advancing a cursor, inspecting packet heads, or merging streams.
ADR-0336 projects their exact current entries without advancing or comparing
the cursors and retains explicit per-role and whole-set exhaustion.
ADR-0337 compares the leading video/audio movie intervals as exact rationals,
without applying a stream-index tie-break or selecting a cursor to advance.
ADR-0338 applies caller-explicit video/audio preference only to equal intervals
and retains the exact selected role and source without advancing its cursor.
ADR-0339 advances exactly that selected cursor by one immutable transition and
retains the non-selected cursor by reference in the successor cursor set.
ADR-0340 projects one caller-indexed cross-track step with exact stream, entry,
leading appearance, transition, and successor-state provenance.
ADR-0341 validates bounded caller step lists for contiguous indexes, one exact
tie policy, and reference-exact cursor-state chaining. ADR-0342 composes the
managed head/comparison/selection/advance chain into caller-bounded automatic
packet traversal with explicit exhaustion versus truncation.
ADR-0343 continues truncated traversal from the exact final cursor state without
re-emitting prior steps. ADR-0344 certifies packet-level completion only when
root remaining counts equal cumulative steps and all selected cursors exhaust.
ADR-0345 reconstructs one contiguous global step view across the exact initial
and continuation segments while retaining every original step by reference.
ADR-0346 expands every complete edit appearance from those completed packets
under a caller event bound and orders the immutable events by overflow-safe
exact rational movie time, global merge index, and appearance index.
ADR-0347 traverses that exact bounded event order with an immutable cursor and
explicit remaining and exhaustion state. ADR-0348 retains one exact
source/event/successor transition without claiming consumption or delivery.
ADR-0349 composes those transitions into a caller-bounded acknowledgement chain
with explicit exhaustion versus truncation. ADR-0350 continues from the exact
final cursor without re-emitting earlier transitions. ADR-0351 certifies whole
root-remaining traversal coverage by exact count and final exhaustion.
ADR-0352 reconstructs one contiguous global acknowledgement view across the
initial and continuation segments while retaining every original transition.
ADR-0353 adds exact rational seconds while retaining each acknowledgement.
ADR-0354 converts those values to overflow-safe exact rational clock ticks at a
nonzero caller-explicit rate without rounding or reading a clock.
ADR-0355 applies caller-explicit integral rounding, ADR-0356 adds a signed
caller origin, ADR-0357 retains exact ready/expiration deadlines, and ADR-0358
classifies them at one caller-supplied tick without reading a clock.
ADR-0359 applies explicit ready-only or ready-or-expired eligibility, and
ADR-0360 retains a caller-bounded source-ordered eligible subset without target
or runtime access.
ADR-0361 owns opaque caller media-target IDs, ADR-0362 binds exact candidates by
media kind, and ADR-0363 projects bounded packet/payload/deadline request
metadata without runtime object access or invocation. ADR-0364 owns bounded
caller-declared media and payload capabilities, and ADR-0365 resolves every
request exactly by target ID, media kind, and payload limit without runtime
lookup. ADR-0366 owns opaque runtime target registrations, and ADR-0367 retains
exact identity-only lookup results without accessing or invoking runtime
objects. ADR-0368 projects caller-identified source-ordered invocation intent
metadata without performing invocation. ADR-0369 classifies every exact intent
with a caller-explicit admitted or denied disposition, and ADR-0370 retains a
caller-bounded source-ordered admitted subset with explicit truncation without
granting runtime access.

ADR-0371 begins the production-composition foundation with a caller-bounded
contiguous sample-number plan over exact reconciled sample-count metadata. It
does not inspect file bytes or construct payloads or codec packets. ADR-0372
classifies every planned sample as admitted, not presented, or partial-sample
rejected through the existing edit chain, and ADR-0373 retains a caller-bounded
source-ordered admitted subset with explicit truncation. ADR-0374 resolves each
selected admission to its exact absolute byte range and sample-description
index through reconciled chunk and size metadata without reading payload bytes.
ADR-0375 locates each range inside a bounded `mdat` traversal, ADR-0376 copies
each payload into independently owned bytes under the existing hard limit, and
ADR-0377 binds the exact owned bytes to their original media timing,
presentation interval, and complete edit appearances.
ADR-0378 selects the exact `stsd` entry for every packet candidate while
retaining the complete source chain and shared handler provenance. ADR-0379
revalidates those selections against the original `stsd` bytes and parses each
`mp4a`, `avc1`, or `avc3` audio/visual sample entry under one bounded child-box
limit. Codec-specific child composition and packet construction remain
incomplete.
ADR-0380 then parses the unique `avcC` or `esds` configuration child for every
exact parsed entry under caller-bounded parameter-set, descriptor, and byte
limits. Connecting those configurations and owned sample bytes to the existing
managed AVC/AAC composers remains incomplete.
ADR-0381 applies those existing managed composition boundaries to every exact
track sample under caller-explicit AVC payload requirements, while AAC retains
its original owned bytes. ADR-0382 binds every packet to exact `tkhd`, `mdhd`,
and `hdlr` sources, and ADR-0383 validates and binds the exact `mvhd` timing and
complete edit appearances. Multi-track collection and selection remain
incomplete for this production-oriented chain.
ADR-0384 converts each exact movie-track binding into the existing bounded
same-track sequence. ADR-0385 collects caller-ordered unique-track sequences
under one exact movie timing, ADR-0386 applies explicit video/audio stream
indexes without inference, and ADR-0387 creates immutable index-zero cursors for
exactly those selections. Connecting the projected cursor set to the existing
bounded merge traversal remains incomplete.
ADR-0388 connects those exact initial cursors to the existing caller-policy,
caller-bounded cross-track merge traversal. ADR-0389 continues exact truncated
state without replay, and ADR-0390 certifies exhaustion and cumulative step
counts against the initial remaining packets. Completed step/event projection
from this production-oriented chain remains incomplete.
ADR-0391 reconstructs one exact global completed-step view across the projected
initial and continuation segments. ADR-0392 orders every complete edit
appearance under a caller event bound, and ADR-0393 creates an immutable
index-zero event cursor. ADR-0394 traverses its exact acknowledgement chain
under a caller event bound, ADR-0395 continues truncated state without replay,
and ADR-0396 certifies root-remaining count equality and final exhaustion.
ADR-0397 reconstructs one exact contiguous global acknowledgement view across
all projected traversal segments. ADR-0398 binds exact rational seconds to that
view, ADR-0399 applies a caller-explicit tick rate, and ADR-0400 applies
caller-explicit integral rounding policies. ADR-0401 applies one signed caller
origin without reading a clock, and ADR-0402 projects exact immutable deadline
intervals. ADR-0403 classifies them against one caller tick, ADR-0404 applies
caller-explicit late policy, and ADR-0405 selects eligible candidates under a
caller bound. ADR-0406 through ADR-0408 attach opaque media target identities,
bind exact candidates, and project immutable packet/deadline request metadata.
ADR-0409 and ADR-0410 attach caller-declared target capabilities and resolve
same-target, same-media payload limits. ADR-0411 and ADR-0412 attach opaque
runtime identity registrations and perform identity-only lookup. ADR-0413
projects caller-identified immutable intent metadata, ADR-0414 applies exact
caller admission dispositions, and ADR-0415 selects admitted entries under a
caller bound. Actual invocation remains intentionally incomplete.
ADR-0416 starts a separate fragmented-MP4 foundation with strict bounded `trex`
track-default parsing. Fragment traversal, run resolution, streaming, and
production fragmented demux remain incomplete.
ADR-0417 adds strict bounded `mfhd` parsing and retains its sequence number
without inferring cross-fragment continuity.
ADR-0418 through ADR-0420 add strict bounded `tfhd`, `tfdt`, and `trun` leaf-box
parsing with exact conditional layouts and bounded immutable run entries.
ADR-0421 resolves one parsed run's sample-description, duration, size, and flags
through explicit `trun`, `tfhd`, and `trex` precedence.
ADR-0422 projects exact decode and presentation time from `tfdt` and that
resolved run without narrowing cumulative values.
ADR-0423 through ADR-0425 resolve caller-explicit run data origins, project
arbitrary-precision sample byte ranges, and join them with exact timing under
track, description, count, index, and size checks.
ADR-0426 binds a nonzero media timescale to those samples as exact
arbitrary-precision rational timing without rounding.
ADR-0427 orders one fragment by exact presentation time, decode time, and source
index without mutating its source projection.
ADR-0428 through ADR-0432 classify exact sequence/decode continuity, bind both
dimensions, validate bounded adjacent transition chains, and project a
declarative discontinuity summary without selecting a reset action.
ADR-0433 through ADR-0473 bind exact fragment sample batches to continuity
transitions, produce one bounded cross-fragment presentation merge, and traverse
its remaining immutable cursor chain with exact source/index provenance and
completion proof, followed by a reconstructed non-delivery acknowledgement view
and exact arbitrary-precision rational, caller-policy integral, and
caller-origin timeline timing plus immutable and caller-observed
decode/presentation deadlines, independently caller-policy eligibility facts,
caller-bounded source-order selection, target-free request metadata, and exact
caller-explicit opaque consumer binding, bounded opaque batch metadata, and
non-invoking submission-intent classification and caller-bounded included-intent
selection, exact non-payload manifest metadata, and full-chain manifest source
binding, declarative aggregate facts, contiguous fragment manifest segments,
an immutable segment cursor-step boundary, bounded exhaustive segment traversal,
exact metadata-only completion certification, and non-acknowledging completed
segment reconstruction.
Delivery attempt/outcome, clock conversion, cross-track merge, `mdat` admission,
payload access, streaming, and production fragmented demux remain incomplete.

ADR-0474 begins the production-oriented regular MP4 composition phase. It
connects the existing sample plan through edit admission, byte-range and `mdat`
validation, owned payloads, sample-description and codec parsing, AVC／AAC
packet composition, and exact track／movie binding into one finite bounded
regular-track packet sequence. It retains caller-selected sample-window
truncation and copies compressed payloads before returning. Multi-track movie
composition is supplied by ADR-0475, which validates an exact same-movie track
set, applies caller-explicit video/audio selection, and produces a bounded
cross-track merge traversal while retaining owned packets and cursor state.
The selected managed decoder-preparation bridge is supplied by ADR-0476 below;
native submission, scheduling, and playback remain incomplete.

ADR-0476 binds those selected streams to the first managed decoder-preparation
ownership boundary. It owns the selected AVC configuration record and retains
the exact payload requirements; for ordinary AAC-LC it parses the selected
`mp4a` decoder configuration and reuses the existing managed parameter,
declared-symbol, declared-capability, and sample-entry consistency chain. AVC
parameter-transfer parity, verified runtime binding, native decoder open,
packet submission, scheduling, and playback remain incomplete.

ADR-0477 supplies that AVC parity. Selected AVC now reaches an owned padded
H.264 codec-parameter lease, exact transfer plan, required-symbol coverage, and
caller-declared dimension/pixel capability binding. Both selected AVC and
ordinary AAC managed preparations remain source-bound and are disposed by the
aggregate movie preparation. Installed-runtime verification, native transfer,
decoder open, submission, scheduling, and playback remain incomplete.

ADR-0478 binds the exact selected AVC/AAC parameter leases and transfer plans
to caller-supplied, already verified FFmpeg ABI gate leases. It checks role,
codec, source, availability, and nonzero required-symbol addresses without
runtime discovery, library loading, symbol lookup, or invocation. Native
parameter transfer and decoder context allocation/open are supplied by
ADR-0479 below.

ADR-0479 consumes that exact binding in one fail-closed video-then-audio
transaction. It creates each selected context, transfers the exact managed
parameters, configures `1 / mdhd.timescale`, opens the decoder, and retains sole
context/gate ownership with complete partial-failure cleanup. Managed tests use
only injected fake operations. Packet submission is supplied by ADR-0480
below.

ADR-0480 consumes that opened session and submits the precomputed merge steps
one packet at a time with exact owned payload, media-unit PTS/DTS/duration,
stream index, track time base, and IDR-derived key flag. Backpressure retains
the same packet and merge index; cursor state advances only after acceptance,
and any other rejection closes the complete session. Tests use injected fake
operations only. Flush and receive are supplied by ADR-0481 below.

ADR-0481 advances at most one packet, flush, or receive action per call. It
copies exactly one managed frame to relieve packet/flush backpressure, blocks
further progress while that completion is owned, flushes exhausted selected
roles in video-then-audio order, and drains each to EOS under a fixed frame
limit. Tests use injected operations only. ADR-0482 then consumes the exact
active completion and transfers its managed frame once into the existing
bounded scheduler with monotonic frame and scheduler sequences. Cancellation
before detach retains the frame for retry; enqueue rejection closes the
aggregate. Scheduler execution, canonical sink delivery, and playback remain
incomplete.

ADR-0483 consumes that scheduler-ingress owner and reserves at most one queued
frame per asynchronous call. It selects the exact video or audio decoder
context, reuses the existing awaited canonical sink adaptation/conversion
boundary, consumes only successful delivery, and retains the same reservation
on cancellation. Tests inject delivery results only. Clock admission, Windows
output processing, application integration, and physical output remain
incomplete. ADR-0484 supplies the next sink adapter: canonical BGRA32 and
interleaved PCM are projected without copying into the existing
generation-tagged Windows output buffer contract. The adapter borrows that
output lifetime and fails closed on every non-processed result. Timing
admission is supplied by ADR-0485: each exact reservation is classified before
conversion as process-now, retained wait, late-video drop, retained late-audio
recovery, clock-not-started, or reset-required using caller-explicit role
generations. Delay execution and output reset/recovery orchestration remain
incomplete. ADR-0486 supplies finite caller-side wait execution with bounded
decision, single-delay, and cumulative-wait limits. Cancellation during an
injected delay preserves the exact reservation for a later drive, while any
limit breach closes the aggregate. Timing/output reset coordination remains
incomplete. ADR-0487 adds the caller-quiesced output half: buffer, stream, and
binding layers are rebuilt with exact role generations, with complete rollback
to the prior configuration on rejection. Atomic delivery quiescence, timing
reset, and sink/delivery generation commit remain incomplete.
ADR-0488 supplies that atomic owner: one driver gate quiesces delivery while
output, sink, timing, and delivery generations commit in order. Timing
rejection restores the exact prior output/sink generations; partial rollback
closes the aggregate. Decoder reset, audio recovery, and continuous pumping
remain incomplete.
ADR-0489 adds the caller-quiesced decoder half: selected opened AAC and H.264
contexts are flushed audio-before-video through the verified existing
capability, retaining healthy ownership only after complete success. Atomic
integration under the delivery gate and late-audio recovery remain incomplete.

### Windows application boundary

- shared `Nova.Windows` application window／message-loop／bounded-dispatcher ownership assembly
- minimal `Nova.App` startup composition with finite close／cancel shutdown
- bounded legal／About projection
- framework-dependent `win-x64` portable packaging contract
- exact reviewed legal-file allowlist

## まだ完了していないもの

PeerCast Nova は現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- user-facing GUI and application features
- regular MP4 production composition and playback integration
- fragmented MP4 production playback, MP4 streaming, nested-box policy, and remaining payload semantics
- complete all-media decoded-buffer duration policy
- production discontinuity-time native reset integration
- complete A/V synchronization、resampling、and drift correction
- recorder
- installer
- code signing
- updater

FFmpeg binary bundling／redistributionは既定方針として禁止です。別runtime packを将来検討する場合は、新しいADRとexact corresponding source／build／notice compliance archiveが必要です。

## 次の開発方針

ADR-0473まででmanaged foundationは十分に厚くなりました。今後の主軸は、
metadata-only境界を機械的に増やすことではなく、既存のregular
non-fragmented MP4 chainをproduction vertical sliceへ接続することです。

0.1.0 Genesis candidateでは、既存PeerCast／PCP／FLV production playbackを
維持しつつ、selected H.264／AVCとAACによるregular MP4 production playback、
既存FFmpeg decoder infrastructure、scheduler、D3D11／WASAPI lifecycle、最低限の
`Nova.App` composition、portable distributionを主要目標とします。

優先順は、regular MP4 demux ownership／composition、selected AVC／AAC decoder
preparation、verified runtime capability binding、native parameter transferと
decoder open、compressed-packet submission、decoded outputの既存schedulerへの
transfer、既存Windows output lifecycle、finite stop／cancel／cleanup、`Nova.App`
integrationです。これは未実装部分を完了扱いするものではありません。

開発配分は厳密な規則ではなく、70%をMVP／productionへ直接近づく作業、20%を
そのために必要な基盤、10%をfragmented MP4などの将来研究へ向ける指針です。
ADR-0416〜0473のfragmented MP4 managed foundationは保持しますが、production
demux／playback、streaming、payload deliveryは0.1.0の必須条件ではありません。
recorder、installer、signing、updater、automatic device-loss recovery、完全な
A/V resampling／drift correction、advanced GUI、広範なcodec対応も後続工程です。

All readiness and physical-run authorizations used to establish 0.0.10d are consumed. Historical helpers／diagnostics and consumed paths must not be reused. Any future physical run remains a separate reviewed authorization decision.

## 日常視聴

Windows desktop からチャンネルを選び、Novaの `再生` を押すと、NovaはPCP/FLV入力を開始し、loopback限定のHTTP-FLV URLを用意してPCRPlayerを直接起動します。`停止` とウィンドウ終了ではPCRPlayer、HTTP listener、PCP sessionを順に解放します。Nova独自のmanaged decoder／scheduler／A/V outputはこの日常視聴経路に必要ありません。

PCRPlayerの解決順は `--pcrplayer-executable <path>`、`NOVA_PCRPLAYER_EXE`、PATH上の `PCRPlayer64.exe` です。明示設定が無効な場合は操作可能なエラーを表示し、別playerへ切り替えません。

## 必要環境

- .NET SDK 10
- Windows 11はWindows output境界の対象
- routine Linux CIはCircleCI

## ビルドとテスト

```powershell
dotnet restore PeerCast-Nova.sln
dotnet build PeerCast-Nova.sln -c Release --no-restore
dotnet test PeerCast-Nova.sln -c Release --no-build --no-restore
dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore
```

## 設計原則

- 1回の公開操作を有限化する
- resident loopや無限retryを暗黙に導入しない
- countだけでなくresource量もboundする
- ownership transferとcleanupを明示する
- 成功した処理だけをconsume／commitする
- undefined enumや不正値はfail closed
- reset途中の失敗を成功として隠さない
- native pointer、handle、HRESULTをpublic contractへ漏らさない
- 実装していない機能を完了扱いしない
- 第三者assetはprovenanceとredistribution permissionを記録する
- LGPL replacement freedomとruntime integrityを明示manifestで両立する
- exact built-in candidateだけをNova review approvalとし、host attestationをapprovalへ読み替えない
- Windows native resourceはowner threadで所有し、production decode pathはdispatcher-backed portだけを借用する
- output generationはactive decoder generationと一致させる
- device lossは旧generation revivalなしのterminal lifecycle resultとして扱う
- controlled fixture validationをphysical native activationの証拠へ読み替えない
- readiness evidenceをphysical authorizationへ読み替えない
- consumed one-run authorizationを再利用しない

## 詳細資料

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [Architecture Decision Records](docs/adr)
- [Implementation and validation reviews](docs/reviews)
- [FFmpeg external runtime compliance policy](docs/legal/FFMPEG-EXTERNAL-RUNTIME-COMPLIANCE.md)
- [FFmpeg distribution legal material](THIRD-PARTY-LICENSES/FFmpeg)

## ライセンス

PeerCast Novaは[Apache License 2.0](LICENSE)で公開されています。FFmpegはNovaのApache License 2.0には含まれず、外部runtimeとして別のlicense termsに従います。built-in candidateのNova reviewはexact identityだけに限定され、host-supplied runtimeはhost declaration／attestationでありNova approvalではありません。
