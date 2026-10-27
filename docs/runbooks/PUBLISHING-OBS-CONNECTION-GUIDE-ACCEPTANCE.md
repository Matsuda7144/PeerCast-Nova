# Publishing OBS connection guide — Windows acceptance plan

Status: **NOT RUN / GUI_PHYSICAL_ACCEPTANCE_PENDING**.

This plan covers the OBS guide, mask/reveal and explicit copy controls in the
existing GUI Next publishing tab. It is not authorization to start Nova, Sandbox
or OBS. Pin the PR source HEAD, Release binary hashes and run manifest before a
separately approved physical run. Keep the PR Draft and auto-merge disabled.

## Managed versus physical evidence

Managed tests cover planned/fixed and unbound/ephemeral ports, the real managed
loopback listener's bound port, TCP versus HEAD state, terminal dominance,
revision ordering, binding/window closure races, copy callback validation, privacy
and lifecycle non-interference. Fake clipboard writers do not prove the actual
Windows clipboard, UI display, focus, DPI behavior or OBS paste.

Connection authority is the current publishing owner's PublishingStatus.
Connection values use an explicit borrowed reader, not the ordinary diagnostic
snapshot. The stream key is an input route, not the ChannelId. GUI labels do not
claim OBS protocol success from TCP accept or playback success from a HEAD.

## Limited layout and key reacceptance (separate approval required)

The previous run PR327-GUI-691e6c5d-932f32bc was PARTIAL. Preserve its evidence
and its unavailable key/listener/process observations. The recovery run uses a
new HEAD and a new Run ID. Keep Release, WSB, manifest, results and screenshots
under that ID; use an explicitly run-specific path, never a reused REPL binding.
Hash and verify saved images. Validate the Release ProductVersion, executable
hash and source tree immediately before launch.

Use Networking=Disable, ClipboardRedirection=Enable, read-only Release sharing,
AudioInput=Disable, VideoInput=Disable and PrinterRedirection=Disable. No automatic
startup script. Identify and capture the unique Sandbox window through native
Computer Use and confirm benign guest Explorer operation before starting Nova.
Guest accessibility may be unavailable; do not infer failure solely from that,
or mistake host overlays in an unfocused capture for guest content. Activate and
reobserve the selected window. If guest operation cannot be established, stop.
No GUI terminal commands, command scripts, PowerShell or Windows-key shortcuts.

Operation limits: Sandbox 1, Nova 1, Start 1, Finish 1, key reveal/remask once each,
key copy 1, server copy at most 2 (only as needed), Interrupt at most 1 for safety.
Tab round-trip once. No restart, reconnect, retry, OBS streaming or PCRPlayer.
There is no arbitrary total duration cutoff; retain I/O/hang safety timeouts.

1. **A — Startup:** launch the pinned Release once; confirm usable main window
   and no automatic publishing Start.
2. **B — Layout:** open publishing; check guide, state, server/key controls,
   lifecycle buttons and feedback. Relay controls must be absent. Narrow wrapped
   instructions must remain readable by scrolling within their own read-only
   field; they must not cover feedback or actions.
3. **C — Placement and tabs:** the existing main-window resize guard disables
   user resizing/maximize; do not remove it or claim a physical resize succeeded.
   Managed rectangle tests cover changing logical widths and 100/150/200% scaling
   in the current DPI-virtualized shell, not physical font rendering. Switch to
   Channels and back once: Relay controls return on Channels, disappear on
   Publishing; shared text restores, publishing-only controls do not leak.
4. **D — Human reveal check:** before any reveal, identify a harmless local guest
   text field and arrange direct user observation. The user performs Reveal and
   confirms the actual display privately, then remasks. Suspend all screenshot
   and accessibility/text capture while a key could be visible. Save evidence
   only after the user confirms remasking and a fresh masked view is observed.
5. **E — Actual key clipboard check:** preflight privately identifies the expected
   key from the exact source's default SystemNovaObsPublishingRuntimeFactory →
   ObsPublishChannelOptions.Ingest → ObsRtmpIngestOptions.StreamName configuration
   (the owner's PublishingStatus.PublishName is derived from it).
   Confirm the production composition uses this factory without an override;
   do not change options for this run. Tie observations to the one current
   ChannelId/SessionId and do not rebind/restart. The user copies once and pastes
   only into the agreed disposable local guest text field, privately comparing
   the expected key, revealed value and pasted value. Capture remains suspended.
   Record only the user's equality result, never the values or a key hash.
   A copy success message alone is insufficient. If equality is not directly
   established, mark NOT_OBSERVABLE. Clear the temporary field without saving,
   replace clipboard with a harmless local string through GUI copy, and have
   the user confirm the screen is safe before capturing again. Clipboard sharing
   can propagate to the host: do not paste there or into a website, and do not
   claim host clipboard removal without observing it.
6. **F — Start:** inspect planned URL/state, then Start once. Confirm the bounded
   ready-only status; never claim OBS protocol/media success without an input.
   OS listener observation is optional only if an already validated safe GUI
   method is available. Otherwise record NOT_OBSERVABLE; managed real-bind tests
   and historical a6 evidence are separate, not this run's OS observation.
7. **G — Finish:** with no publisher, Finish once and check local termination,
   waiting stopped and disabled copy/reveal controls. Distinguish LocalCompleted,
   Cancelled and ObsNormalEos. If needed, Interrupt once for safety and record the
   actual classification. Never restart to repair the result.
8. **H — Cleanup:** close Nova normally and discard Sandbox after any required
   action-time confirmation. Record window disappearance, host remnants and only
   the guest PID/ExitCode/TCP facts actually observable. Keep the pre-existing
   blank black window issue separate; its cause remains UNKNOWN.

Direct human observations must be labelled as such. Agent screenshots cannot
prove the deliberately uncaptured key values. Non-default keys, Port=0 and
generation/binding/revision/window-lifetime races remain managed coverage.

If the dialog/surface or lifecycle behaves unexpectedly, preserve evidence and
stop rather than silently retrying/relaunching. Record the actual operation
counts and any unavailable observations.

## Acceptance boundary

A successful run accepts only the observed guide/reveal/copy/OBS-paste behavior
and any explicitly observed lifecycle integration on the pinned HEAD.
The historical a6 local A/V acceptance is retained without automatically
re-running or expanding it. HTTP EOF, HTTP request totals, WAN publication and
YP listing remain outside this run. RUNTIME_LOAD_VERIFIED and Publishing
PHYSICAL_PARTIAL are not upgraded by managed tests or CI.
