# Borrowed publishing status for GUI composition

This independent extraction starts at main
`48b21aac93dc58d7e4e655118ea198096ff45613` after merged PR #313.
Source PR #270 is `0aa2bbec0da48a5a6787c86272cd3feac29e7192`, based on
old GUI foundation `95d107f8349d252a1417dd154e733a1cf23736af` and old
PR #259 `be9f7c4a8a0345d339b5beb92ea9a61d844e9e00`. It extracts
`INovaObsPublishingStatusSource`, `ObsPublishStatusSnapshot` and
`NovaObsPublishingProjection`; it introduces no publisher or lifecycle operation.

## Read-only application boundary

`INovaObsPublishingReadOnlyStatus.Read()` returns only channel/session identity,
availability, existing state presentation and existing typed terminal/rejection
classifications. The adapter borrows an already supplied status source. It has
no factory, socket, listener, timer, process, subscription, Start, Stop or Dispose.
Construction does not even read the source. It cannot keep an owner alive via an
observation task. Composition must stop using it before releasing the source.

| Observation | Availability |
| --- | --- |
| No supplied owner, failed read, invalid identity or different generation | Unavailable / 利用不可 |
| Created, no listener/TCP/HEAD or terminal result | NotStarted / 未起動（待受未開始） |
| Current nonterminal generation | Observed; reuse the existing factual phase label |
| Stopped or typed terminal result | Stopped; preserve errors/cancellation rather than report success |

Listener readiness is not OBS connection; TCP is not accepted FLV. Receiving
still requires the foundation's accepted HEAD. ChannelId/SessionId describe
owner identity even before startup, not a claim that a channel is broadcasting.
The existing snapshot has no channel display name, so no name is invented.

Existing generation/revision and terminal-dominance projection guards are
reused. The adapter also checks channel identity. A replacement owner requires
explicit new adapter composition; a delayed read cannot select itself. A stale
revision or post-terminal receiving observation retains the last accepted fact.
Source exceptions are converted to a fixed unavailable message, never exposed.

The network getter serializes reads and advances its observation Revision when
returned values change, including listener/TCP changes that do not change the
channel tracker. Repeated unchanged reads keep the same Revision. Projection
rejects equal or older revisions, so a delayed same-revision value cannot replace
accepted facts. These are observed-value revisions, not a complete event log.
Tracker, ingest and terminal-result reads are not an atomic cross-resource
snapshot; a concurrent transition may be reflected on the next read. Terminal
guards prohibit a later nonterminal projection after terminal acceptance.

RTMP endpoints, stream routes, HTTP URLs, free-form metadata, raw exceptions,
media and credentials are absent from the returned application record. No new
diagnostic collection, instrumentation, failure taxonomy or persistence is added.

`ObsPublishStatusSnapshot` and the legacy `NovaObsPublishingPanel` are low-level
DTOs containing URLs/routes. Neither is a safe disclosure or JSON boundary.
Only `NovaObsPublishingReadOnlySnapshot` may be passed to a future GUI or status
export. The existing Projection is reused internally; its connection fields are
not copied to the application-facing record. The Windows file adds value types
only, with no native rendering, window, resource handle or event wiring.

## GUI Next integration remains separate

The old Daily startup calls the factory, Start and a one-second periodic
Observation loop. None of that startup/polling/window wiring is extracted.
`NovaObsPublishingObservation` and its obsolete periodic-observation test are
intentionally excluded. All other source status/projection/runtime tests are
retained and adapted. The runtime wrapper owns one unstarted publish host;
construction and Snapshot do not Start it. Only an explicit runtime Start may
bind, while its application owner remains responsible for Dispose.

No normal startup composition invokes the runtime factory here. No GUI Next
navigation, Publishing row, timer, background observation, subscription or
Start/Stop button is added. Existing Daily, GUI Next, YP/Logs, connection/NAT,
recording/PCP admission and native lifetime code is preserved. Source #270/#271
and the old GUI branch are unchanged. A later independent #271 composition slice
must create at most one owner, pass only the sanitized status interface to GUI,
and dispose that owner after UI teardown. No graceful-stop claim is added;
cancellation/disposal remains distinct from proven normal EOS.

Managed adapter tests use an in-memory status source. No physical GUI, OBS,
publishing, WAN or NAT operation is executed. Existing foundation regressions
may exercise deterministic managed loopback fixtures.

Physical: **NOT_RUN**. Listener, TCP connection and accepted HEAD are separate
managed observations; none proves real video/audio, peer playback, normal OBS
stop, clean EOS or complete downstream drain. #313 Physical NOT_RUN, #311
Physical PARTIAL and #306 Physical NOT_ACCEPTED remain unchanged.
