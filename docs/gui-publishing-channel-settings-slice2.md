# Publishing Settings GUI v1 — Slice 2

The Publishing tab's **配信設定…** opens one modeless, main-owned Win32 window.
It edits channel name, genre, description, broadcaster comment, contact URL and
optional declared kbps. IPv4 loopback, OBS RTMP and FLV stay fixed. The window uses
the existing dark-blue palette/font lifetime, keyboard Tab/Escape navigation,
DPI-scaled controls and vertical scrolling. Save never starts a listener.

## Settings and persistence contract

- Draft belongs to the open editor. Cancel/X discards it. Invalid input or failed
  save retains the draft and identifies the failed field.
- Saved/NextStart belongs to the application root's
  `PublishingSettingsController`. `SettingsRevision` is separate from the
  runtime status `SourceRevision`. Dirty drafts and pending saves disable Start;
  Finish/Interrupt retain their existing admission rules.
  Start compares the active draft with the latest Saved snapshot, including a
  save completing after its original editor closed and another editor reopened.
- Applied is the immutable saved snapshot captured at explicit Start admission.
  Saving while a generation runs does not change its options or metadata.
- Schema 1 is stored at
  `%LOCALAPPDATA%\PeerCast Nova\publishing-preferences.json`, limited to 64 KiB.
  A missing file uses Local publish / empty optional fields / unset bitrate.
  Supported unknown JSON properties are retained. Invalid, oversized or future
  schema files are preserved and warn; Start stays blocked until explicit Save.
- A save serializes/flushes a same-directory temporary file and atomically moves
  it over the destination. Only successful replacement commits Saved/revision.
  I/O runs off the UI thread and outside publishing owner admission locks.
  This does not promise directory-level durability across abrupt power loss.
- Name is trimmed, 1–256 UTF-16 code units. Genre is at most 128 Unicode scalars;
  description/comment at most 1024 each. URL is empty or absolute HTTP/HTTPS,
  at most 2048 UTF-16 code units, without userinfo or surrounding whitespace.
  Bitrate is blank or an integer 1–2147483647. It is a declaration, not a measured rate.
  Malformed Unicode, NUL, control/newline characters and PCP text exceeding
  4096 UTF-8 bytes including terminal NUL are rejected. URLs are never fetched.

## Start and lifecycle

The root loads settings before constructing the initial owner. Start captures
Saved/revision once. An unchanged, idle owner can start directly. A stale idle
owner must still have no admitted commands and no Start/stop/lifecycle activity:
its command admission closes, the existing lifecycle disposes it, and its actual
post-disposal completion must prove shutdown with zero accepted media. Only then
can a fresh owner/channel/session be created and adopted under the shutdown gate.

Saves during this replacement affect the following Start. Repeated Start cannot
create duplicate owners. Shutdown joins admitted replacement/cleanup. Executed
generations retain the existing `HasProvenTerminalCleanup` conditions unchanged;
a failed cleanup never authorizes another generation.

## Initial PCP control information

Validated options create one immutable `ChannelMetadataSnapshot` used by the
publishing tracker and the relay server. Every successful downstream handshake,
including a late join, writes CHAN/id/info after the successful reply and before
releasing handshake readiness. Consequently retained HEAD and later DATA cannot
overtake the initial info.

The existing atom writer, wire codec, control parser and state adapter define
the format: id; type/name/genre/description/comment/URL/extension; optional
bitrate and existing stream type. There is no track information. Each text is
strict UTF-8 with terminal NUL; the whole control document is limited to 32 KiB.
The existing byte-writer seam exercises metadata timeouts without adding an
observer. Control bytes use the existing deadline and child cancellation; they
never enter media fanout, packet/byte/HEAD counters or stream position.

Legacy relay construction without metadata is unchanged. OBS onMetaData,
H.264/AAC and HTTP-FLV bytes are untouched. No live metadata update, YP, WAN,
NAT, original-channel relay, OBS route/key editor or Slice 3 track editor is added.

## Limited Windows acceptance plan — not executed

Obtain explicit run authorization and freeze source SHA/tree, Release hashes,
run manifest, operation budgets and permitted Computer Use route before any
launch. Identify an operable guest screen first; if unavailable, stop without
launching Nova. Keep secrets masked and do not save secret-bearing images.
Use a disposable guest with Networking Disable, ClipboardRedirection Enable,
read-only host shares, AudioInput/VideoInput/PrinterRedirection Disable.

| Gate | Future acceptance and evidence |
| --- | --- |
| A | Launch Nova once, open 配信設定…, open again to verify one window/focus. Inspect dark-blue appearance, Tab/Shift+Tab, a small viewport, scrolling and authorized DPI/resize cases. |
| B | Edit all fields; verify Start disabled while dirty. Cancel and reopen; repeat X discard. Main Finish/Interrupt remain modeless and operable under their existing conditions. |
| C | Try invalid name/URL/bitrate, verify field error and no saved mutation. Save one agreed Japanese/emoji fixture; retain only non-secret field/revision evidence. |
| D | Reopen and compare saved values. Confirm that saving/opening did not start publishing or allocate a new running generation. |
| E | First explicit Start after Save must use the saved revision; distinguish observed GUI state from OS listener/PID proof. Do not treat a configured value alone as runtime metadata evidence. |
| F | Compare actual local `ObsPublishChannelHost.Snapshot.Metadata` with the admitted fixture where an existing authorized readout is available. If that readout is unavailable, retain NOT_OBSERVABLE; managed same-instance tests remain separately identified. Do not add an observer for this gate. |
| G | With a separately authorized, prepared local PCP receiver/capture limited to the relay port, observe successful handshake → complete id/info → HEAD/DATA when media exists. Check metadata and a late join. With no OBS source, only handshake/info is physically testable. External PCP interoperability remains unverified until directly observed. |
| H | While running, save a distinct fixture. Saved revision changes; current generation and observed metadata must stay fixed. Confirm the notice that the current stream is unaffected. |
| I | Finish once, verify local completion/cleanup under existing proof conditions, then explicitly restart and verify the newer saved revision/fresh identity. Interrupt only within separately approved operation budgets. |
| J | Close the editor and Nova normally, verify focus/child lifetime, restart once and verify JSON restoration; clean up the disposable guest and preserve host evidence. |

The final operation budget must account for the distinct Cancel/X, validation,
save/restart and PCP late-join observations before authorization; this table
itself authorizes none of them. A gate lacking direct evidence stays
NOT_OBSERVABLE/NOT_EXECUTED. Do not fabricate PID, ExitCode or TCP proof.
OBS live streaming and PCRPlayer/audio/video acceptance are separate decisions
based on the change and existing a6 evidence, not prerequisites silently added
to this metadata GUI run. The independently observed black empty-window issue
from PR #326 remains UNKNOWN; inspect this new child's focus/lifetime without
claiming its cause fixed.

Windows GUI, DPI glyph rendering and external PeerCastStation interoperability
are pending. Managed tests and CI do not constitute physical acceptance.
`RUNTIME_LOAD_VERIFIED` and Publishing `PHYSICAL_PARTIAL` retain their prior scope.
