# GUI Next publishing commands — Phase 3

## Dependency and ownership

Independent integration from main
`45c9ef9e714ead2f1eb530ead7906f7385e0612f` (tree
`aa139a3aaa3d8df9797bda74d2e60c43bd0953ea`). Only old PR #273's eight-file
delta (`e91182d0d47de27ffc68662eba506cbfcbc658d7` to
`6c37b98d82988b5f5f543454544047ed7634e229`) is adapted. The lifecycle already
integrated by #316 is reused unchanged, including its unstarted-disposal
Completion/ShutdownCompleted checks and fail-closed semantics. No old #272
code/history, stacked merge, cherry-pick or later slice is imported.

Normal GUI composition binds borrowed status and `INovaGuiNextPublishingCommands`
to the tabs. Dedicated Publishing buttons call those application commands. The
GUI does not acquire a runtime, socket, process, or publishing owner.

`NovaGuiNextPublishingCommands` admits one Start off the native UI thread and
forwards to the existing `RequestStart`. Interrupt waits for an admitted Start
to settle, then forwards to `RequestStopAsync`. Application shutdown closes
command admission, drains admitted operations, and disposes the lifecycle once.
Navigation away does not cancel an owned publisher. Disposed tabs reject late
presentation updates, and native updates use the existing posted-message path.
The current startup root closes publishing command admission before waiting for
playback/network owner shutdown, then joins publishing disposal after host
teardown. Admission closure itself does not start or dispose a runtime.

The current #315 Publishing page, read-only details, Japanese UI font and dark
shell rendering are retained. Dedicated control IDs 1134/1135 avoid the current
Relay IDs 1131/1132/1133. The Publishing navigation guard remains intact; only
the two matching dedicated button handles bypass it on the Publishing tab.
Controls begin hidden/disabled and use the existing visibility/update path.
The Japanese glyph probe includes the new button scope/command diagnostic
characters, and its coverage test also scans the command adapter. This required
two supporting font files beyond the original eight-file source delta; font
selection, lifetime and DPI behavior remain unchanged.

## User-visible contract

- 配信開始 requests RTMP listening; acceptance does not prove listener readiness,
  OBS connection, or media reception.
- Existing lifecycle/source projection distinguishes acceptance with unconfirmed
  listening, OBS waiting (listener ready), OBS connected, and media HEAD received.
  These are source status facts, not physical broadcast acceptance.
- 配信中断 requests cancellation cleanup. It is not an OBS normal-EOS/graceful-stop
  command. Foundation-reported normal EOS retains its existing proof requirements.
- Start is unavailable after its admission in the same generation. Interrupt is
  unavailable while interruption/cleanup is pending or after termination.
  Loss of status observation does not block cancellation of an owned active start.
- Construction, status refresh, navigation and F5 do not start or interrupt.
- The existing dark shell controls are reused. Interrupt is marked red, unavailable
  buttons are dimmed, and only bounded safe diagnostics/identities are displayed.
  No URL, stream key, or exception body is added to the screen.

## Validation and exclusions

Managed tests cover explicit forwarding, duplicate clicks, blocked Start and
Interrupt ordering, shutdown races, terminal/no-restart behavior, safe errors,
missing boundaries, listener/connection/media distinctions, navigation, late UI
updates and lost observation. Existing Publishing refresh tests now bind commands
and verify refresh performs no catalog/network command work.

No OBS app launch/connection, physical publishing, WAN/NAT action or native GUI
acceptance is performed by this slice. Generation replacement/restart and a
production graceful-stop API remain separate follow-up work. Existing Recording,
playback, network settings, PR #269 and the Phase 3C scoped closeout are unchanged.

## Required later Windows Sandbox acceptance

Physical: NOT_RUN. Merge Readiness: NOT_ASSESSED. Managed results alone do not
accept native window behavior. A separately authorized bounded Sandbox run
should verify Japanese font/dark rendering, both labels and disabled states,
details/layout at the supported window size, navigation/F5/Refresh without Start,
one explicit Start (listener readiness distinct from OBS/media), duplicate-click
suppression, Interrupt as cancellation, closed-generation restart rejection,
navigation-away/back, and closing the window during Start/Interrupt. Verify no
late updates to a destroyed window and safe shutdown on incomplete cleanup.
Use only an isolated test profile, no real OBS publisher or WAN/NAT action.
Direct human GUI acceptance is separate from managed tests or internal counters.
Non-cooperative synchronous Start/Dispose/I/O still has no forced finite deadline;
this command adapter orders and joins operations rather than adding forced stop.
