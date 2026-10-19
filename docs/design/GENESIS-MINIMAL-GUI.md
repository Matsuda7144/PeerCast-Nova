# PeerCast Nova Genesis Minimal GUI

Project Founder: 7144

This document records the current GUI design direction for the 0.1.0 Genesis
application surface. It is intentionally not an ADR number so production
architecture numbering can continue independently.

## Design principle

The default GUI combines a modern, restrained application shell with interaction
patterns familiar to existing PeerCast users.

- preserve familiar PeerCastStation / PeCaRecorder information placement;
- modernize spacing, scaling, selection visibility, and error presentation;
- keep ordinary viewing simple without hiding useful operational information;
- keep advanced and classic density as later modes rather than expanding the
  Genesis surface now.

## Main window

The main window is a channel browser, not an embedded video player.

It contains:

1. a compact PeerCast Nova application header;
2. a manual channel / source URL input and Play action;
3. a width-dominant PeCaRecorder-style channel table;
4. a manual Refresh action for the channel catalog;
5. a small application / playback status area;
6. `Normal`, `Video Only`, and `Fullscreen` playback presentation actions;
7. About / Legal navigation through the existing application boundary.

No permanent video area is embedded in the main window.

## Channel table

The default Genesis column order is:

1. `チャンネル名`
2. `[ジャンル - 詳細] コメント アーティスト/タイトル/アルバム`
3. `リスナー/リレー`
4. `ビットレート`
5. `配信時間`
6. `タイプ`

The table stays compact and row-oriented rather than becoming a card grid.
Numeric operational columns are right aligned. The details column receives most
of the available horizontal space.

Current interaction:

- manual source playback trims surrounding whitespace before updating the source
  field and requesting playback;
- while the source field has focus, `Enter` requests playback using the current
  field contents through the same manual Play path;
- while the source field has focus, `Esc` requests playback stop through the
  same bounded Stop path used by the channel list;
- while the source field has focus, `F5` requests another bounded channel-
  catalog refresh through the same session used by the channel list;
- manual source-field edits are mirrored into view-model state as they are typed
  without projecting the same text back into the native edit control;
- unrelated catalog, status, or playback-state refreshes do not overwrite manual
  source-field edits while the view-model source itself is unchanged;
- single click selects a channel and copies its source into the source field;
- double click selects the channel and requests playback;
- right click selects the row under the pointer and exposes `Play`, `Copy Source`,
  and `Refresh` actions with `Enter`, `Ctrl+C`, and `F5` shortcut labels;
- `Shift+F10` or the Menu / Application key opens the same context menu for the
  current selection, anchored below the selected row;
- source-specific context-menu actions are bound to the source that was selected
  when the menu opened, so a concurrent catalog refresh cannot redirect `Play`
  or `Copy Source` to a different row;
- clicking a column header toggles ascending / descending sorting while keeping
  selection by source where possible;
- catalog refresh or sorting that rebuilds native rows restores a preserved
  selection and scrolls that row back into view;
- `Refresh` requests another bounded channel-catalog read without racing an
  initial or already-running refresh;
- catalog refresh commands are tracked independently from playback commands, so
  an in-flight refresh does not suppress Play or Stop gestures from the source
  field, channel list, or channel context menu;
- while the channel list has focus, `Enter` plays the selected channel, `F5`
  refreshes the catalog, `Esc` requests playback stop, and `Ctrl+C` copies the
  selected channel source;
- `About` opens a finite owner-bound summary of the existing legal model; the
  complete embedded notice, provenance, and runtime-replacement text remains
  available through `Nova.App --about`;
- standard ListView column resizing remains available.

## Channel catalog boundary

Channel discovery is kept separate from the Win32 table and from production
playback.

Current data flow:

`IGenesisChannelCatalog -> GenesisChannelCatalogSession -> sanitizer -> GenesisMainViewModel -> ListView`

`NovaWindowsStartupOptions.ChannelCatalog` remains the application composition
seam. Genesis now uses SP as its default catalog at application startup. The SP
adapter reads `http://bayonet.ddo.jp/sp/index.txt`, parses its PeerCast `<>`
delimited channel metadata into `GenesisChannelItem` rows, and uses
`pcp://bayonet.ddo.jp:7146/` as a fallback tracker when a row does not provide a
usable tracker address. Callers can still replace the default catalog through
startup options without changing the channel table or playback interaction code.

The SP transport is intentionally isolated behind `IPeerCastIndexTextSource`.
The index parser and catalog mapping are tested without external network access.
The system text source uses one bounded HTTP/1.0 request over `TcpClient`, asks
for identity transfer encoding, accepts only successful responses, limits the
response to 4 MiB, and applies a 12-second request budget. A catalog transport or
parse failure is bounded to channel-refresh state and must not tear down the
Genesis application host.

SP response text decoding is also bounded. Declared UTF-8 is decoded strictly;
legacy Japanese declarations such as `Shift_JIS`, `windows-31j`, and `cp932`
use strict CP932 decoding. When no charset is declared, Genesis tries strict
UTF-8 first and falls back to CP932 only when the byte sequence is not valid
UTF-8. Unknown declared charsets and conflicting charset declarations are
rejected rather than guessed.

Catalog projection is bounded before reaching native controls:

- rows without a usable channel name or source are ignored;
- sources are trimmed and duplicate normalized sources are collapsed;
- negative listener, relay, bitrate, or uptime values become unknown values;
- externally supplied text fields are length bounded;
- no more than 10,000 rows are projected in one snapshot;
- already-valid channel objects retain their identity where no normalization is
  required.

The channel catalog session serializes refresh work and owns cancellation during
GUI shutdown. Genesis starts an initial refresh automatically and reuses the
same session for explicit manual Refresh requests, so repeated refresh gestures
cannot race the initial load or one another.

Status presentation is bounded and independent from playback errors:

- `Loading channels...` while a catalog refresh is active;
- `Channel refresh failed.` if refresh fails while retaining the previous rows;
- successful or cancelled catalog refresh returns the semantic idle status to
  `Ready` when playback is stopped; the Win32 idle presentation appends the
  loaded row count when non-zero (`Ready · 1 channel` or `Ready · N channels`);
- playback states such as `Playing` and `Stopping` take presentation priority
  while playback is active.

The SP Yellow Page catalog is the only network channel-directory integration in
this GUI slice. Production playback, PCP relay ownership, and decoded media
output remain separate and unchanged.

## Playback window

Successful playback opens a separate playback window. The main channel browser
remains available behind it.

The playback window has three presentation modes:

- `Normal`: ordinary framed playback window;
- `VideoOnly`: borderless, titleless video surface at a freely sized position;
- `Fullscreen`: monitor-filling video presentation.

The main window exposes all three presentation actions beside the status area.
They are disabled while no playback window is open, and the currently selected
mode is disabled so the active presentation remains visible at a glance. Mode
changes reuse the existing playback-window coordinator and replace only the
auxiliary surface; they do not request a playback stop.

Closing the playback window requests session stop. Stopping playback closes the
playback surface. Programmatic window replacement for presentation-mode changes
does not masquerade as a user close.

The Genesis implementation may use a placeholder video surface with a fake
playback controller. Actual decoded video is not required for GUI work.

## Application boundary

The GUI depends on the application-facing playback session boundary, not on
`Nova.Playback` implementation types.

Current GUI Next composition:

`Genesis GUI -> IPlaybackSessionController -> GuiNextDailyPlaybackSessionController -> NovaDailyHttpFlvPlaybackController`

The GUI adapter passes the selected channel's source, name, and catalog
ChannelId through the application boundary. The existing daily controller
parses the canonical channel-specific `pcp://host:port/channel/<id>` source and
continues to own PCP acquisition, the loopback HTTP-FLV host/session, and
PCRPlayer process lifetime. Manual source input uses the same boundary and is
accepted when it uses that existing PCP source form. Unsupported or malformed
sources fail closed.

Production construction is inert: the PCRPlayer path is resolved and the daily
network session is created only on Play. The adapter projects daily playback
completion and failure into the existing GUI playback snapshot. Tests continue
to inject `FakePlaybackSessionController` or a fake daily controller; they do
not start PCRPlayer or contact a real tracker.

The adapter does not connect managed decoding, fragmented MP4, or recording.
The existing auxiliary playback window remains a GUI-owned projection of the
application-facing snapshot.

## Windows ownership

The repository has one shared `Nova.Windows` owner thread, Win32 message loop,
application window, and dispatcher ownership model.

Genesis projects both its main controls and its auxiliary playback window on the
existing shared application dispatcher / owner thread. It does not create an
unrelated second message loop. The playback window is an owned auxiliary native
surface and remains separate from production playback ownership.

Additional playback integrations must preserve the shared Windows ownership
boundary rather than introducing a competing window host.

## Deferred

The Genesis GUI does not currently include:

- physical or native playback validation;
- additional production Yellow Page aggregation beyond the default SP catalog;
- recorder controls;
- fragmented MP4 controls;
- installer, signing, or updater UI;
- favorites or history;
- advanced statistics;
- animation framework;
- large theme framework;
- advanced or classic mode implementation.
