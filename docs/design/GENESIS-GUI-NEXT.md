# PeerCast Nova Genesis GUI Next

Project Founder: 7144

This document defines the next GUI workstream after the completed Genesis minimal
GUI baseline. GUI Next deliberately branches from the GUI-only Genesis baseline
and remains independent from production PCP playback integration while the two
workstreams are still under review.

The goal is to retain the fast, information-dense daily workflow that made
PeCaRecorder useful while keeping PeerCast Nova's default surface restrained,
modern, and testable.

## Initial Scope v1 (PR #230)

This section fixes the first-delivery completion scope. The remaining sections
and `GENESIS-GUI-NEXT-NETWORK.md` retain the longer-term product design; a future
capability described there is not automatically a first-delivery requirement.
This scope freeze does not remove existing code, change other PR dependencies,
or waive existing regression, data-protection, or physical acceptance conditions.

### Required first-delivery features

1. Channel list and selected-channel details.
2. Search and Yellow Page filtering.
3. F5 and Refresh through the shared refresh command, including focus handling.
4. Startup and periodic Yellow Page refresh with the default 60-second interval,
   serialized with manual refresh and cancelled during shutdown.
5. Channel sorting with stable selection restoration.
6. New and Favorites views, including persistent favorites and offline entries.
7. Playing view showing only state proven by the application playback contract.
8. New/favorite notifications, sound on/off, and the explicit Test Sound action.
9. Multiple Yellow Page add/edit/remove/enable/disable and save/reload, including
   retention of disabled definitions without reading them as enabled providers.
10. Selected-channel playback GUI connection to the existing playback controller.
11. Existing manual recording GUI and the Recording page's active manual
    recording projection, selection, and Stop action.
12. Truthful, status-only Network / Relay presentation, including explicit
    unavailable/unknown states and safe window navigation and closure.
13. Save/reload of existing required GUI settings and exact protection/recovery
    of pre-existing user state, including restoration of original absence.
14. Normal GUI shutdown and release of owned asynchronous work and resources.

For Network / Relay, first-delivery completion does not require native editing,
configuration persistence, runtime reconfiguration, or active port checks.
Unavailable backend capabilities must remain explicit; saved drafts or local
binding must never be presented as applied settings or Internet reachability.
The existing status-only checks in the formal Windows smoke remain mandatory.

### Independent future extensions

- Network / Relay editing, network configuration persistence, and production
  backend application, including transactional live-listener changes.
- Active port-reachability checks and UPnP / NAT-PMP port mapping.
- User-facing automatic-refresh interval editing and persistence of that value.
- Recording-rule, history, and file-library extensions beyond the existing
  manual recording and active Recording-page acceptance scope.
- The Downloads implementation.
- Internal-player/decoder completion and fragmented MP4 playback.
- A publishing engine.

Some future extensions already have implementation and automated tests in this
branch, notably recording rules, history, and the library. Their presence does
not make their full feature acceptance an initial completion gate, and this
freeze does not remove or relocate them. Existing regression and safety
requirements still apply. The PR description must distinguish implementation
presence from the initial acceptance scope rather than imply that this code is
absent.

Playback GUI routing belongs to PR #230; decoder, rendering, media audio, and
protocol/relay physical acceptance remain with their owning playback/network
workstreams, including PR #229 where applicable. The existing manual-recording
and Recording-navigation acceptance records remain valid within their recorded
SHA and scope; they do not prove acceptance of every feature at the final HEAD.

### Acceptance state and remaining merge gates

At this freeze's baseline, HEAD is
`5e4ebc81d8190f0c295b926bd9226349781d7581`; exact-HEAD GitHub Actions run
`39169179617` succeeded. Code and automated-test confirmation is separate from
final-HEAD Windows physical acceptance, which remains incomplete:

- R3: `SECTION7_FIRST_GUI_INCOMPLETE` / `NOT ACCEPTED`.
- R4: `BLOCKED` / no physical verification performed.
- PR #230: OPEN / Draft / unmerged; auto-merge disabled.

Before considering Draft release or merge:

1. Align the PR description with this scope without discarding historical
   acceptance records or changing other PR responsibilities.
2. Resolve confirmed blockers within the initial scope, if any, and retain
   relevant regression coverage and successful final exact-HEAD CI.
3. Fix the final-HEAD Windows Release artifact identity and hashes; do not reuse
   an older-HEAD artifact as proof of the final build.
4. Establish a verified User Host operation and settings-protection/recovery
   route before any separately authorized physical run. R3/R4 are not resumed
   or reclassified by this document.
5. Complete the existing required GUI physical checks, permitted N/A conditions,
   normal shutdown, and directly verified exact user-state restoration under
   [the formal Windows smoke](../runbooks/GENESIS-GUI-NEXT-WINDOWS-PHYSICAL-SMOKE.md).
   A narrower F5-only check does not replace the full GUI acceptance boundary.
6. Confirm that dependencies remain within their agreed workstreams, then seek
   separate authorization for Draft release or merge. CI success alone is not
   physical acceptance or merge authorization.

## Product direction

GUI Next is still a channel browser first. It is not a second application and it
does not replace the existing Genesis playback boundary.

The primary views are:

1. `チャンネル一覧` - all currently available channels from enabled Yellow Pages;
2. `新着` - channels newly observed or restored after having genuinely ended;
3. `お気に入り` - the user's saved channels;
4. `視聴中` - sessions that Nova is currently playing or reconnecting.

A future `ダウンロード` view is an explicit extension point, but download
implementation is not part of the first GUI Next delivery.

The current Genesis table-oriented interaction remains the default. Double-click,
Enter, F5, source entry, sorting, selection preservation, and the channel context
menu should continue to behave consistently across the applicable channel views.

## Yellow Page management

GUI Next supports more than one Yellow Page (YP). YP configuration is persistent
and user-editable.

Each YP definition has at least:

- a stable local identifier;
- a display name;
- an index/catalog URL;
- enabled/disabled state;
- source/namespace metadata needed to distinguish origins where applicable.

The settings UI supports:

- add;
- edit;
- remove;
- enable/disable;
- manual refresh.

The existing SP catalog remains a valid built-in/default source, but the GUI and
catalog pipeline must not assume that SP is the only source.

A failure in one YP must not discard successful data from another YP or terminate
the application host. Per-YP failure state is retained for diagnostics and the
last usable data may remain visible until normal stale-data policy removes it.

## Automatic catalog refresh

Automatic YP refresh is required. A YP browser that only refreshes manually will
quickly present stale channel information.

The default behavior is:

- refresh enabled YPs at application startup;
- continue refreshing in the background while the GUI session is alive;
- allow manual F5 / Refresh to request an immediate refresh;
- serialize refresh work so automatic and manual refreshes do not race;
- never run overlapping refreshes for the same application catalog session;
- cancel finite refresh work during application shutdown;
- expose the last attempted and last successful refresh state to the GUI.

The initial automatic-refresh interval is 60 seconds. It is an application
setting rather than a protocol constant. Initial Scope v1 retains the existing
startup-configuration boundary; user-facing interval editing and persistence are
future extensions and do not require redesigning the catalog boundary.

The refresh scheduler belongs to the application/catalog layer. The native Win32
surface must not own a timer that performs network work directly.

In the active main shell, unmodified F5 uses the same current-view refresh command
as the Refresh button, including when focus is on Search, the YP selector,
navigation, a button, or the channel list. It does not move focus or edit text.
One key press dispatches once; held-key repeats are ignored, and F5 / Refresh
button requests share a pending-command guard. The main-shell hook does not
intercept modified F5 gestures or input for other windows, including settings
dialogs.
If the shared Refresh bridge is not registered, F5 is left to the existing
control handlers. A registered bridge still consumes F5 while a refresh is
pending; a rejected request is not treated as an absent bridge.

## Channel presence and stale-data handling

A single YP snapshot is not sufficient evidence that a channel has really ended.
Historically, YP publication can flap: a live channel can temporarily disappear
from one or more refreshes and then return. Treating every one-snapshot absence as
an end would repeatedly reclassify the channel as new and repeatedly notify the
user.

GUI Next therefore tracks channel presence across successful refresh generations.
The initial policy is:

- a newly observed identity becomes active immediately;
- an active identity is not considered ended after one missing successful refresh;
- an identity becomes ended after three consecutive successful refresh generations
  in which it is absent from all enabled YPs that previously supplied it;
- a channel that returns after reaching ended state may be classified as new again;
- failed or cancelled refreshes do not increment absence counters.

This mirrors the robustness lesson from PeCaRecorder without coupling Nova to its
implementation.

Channel identity must be based on stable channel/source identity, not list row
index or display name alone. Aggregation must preserve origin information so
multiple YPs can report the same channel without creating unstable duplicates.

## New channels

`新着` is a first-class view over the shared channel catalog, not a separately
owned copy of channel objects.

A channel qualifies as new when it transitions from unknown/ended to active after
a successful catalog update. A refresh that only changes metadata must not make an
already-active channel new again.

New-state retention and dismissal should remain independent from list sorting and
selection. The first implementation may retain the new marker for the active
appearance lifetime; later UX work may add explicit acknowledgement without
changing the underlying presence tracker.

## Favorites

`お気に入り` is also a view over the shared catalog. Favorites are persistent
user data and must not depend on a row currently being visible.

Required behavior:

- add/remove favorite from the channel context menu;
- show favorite channels in the Favorites tab;
- preserve a favorite while its channel is offline;
- match a returning channel using stable identity rather than current row index;
- allow favorites to participate in notification rules.

An offline favorite may remain represented as saved metadata in the Favorites
view, but playback actions are only enabled when a usable current source is known.

## Notifications and sound

GUI Next includes notification state driven by catalog transitions, not by native
control events.

The minimum notification behavior is:

- notification for a newly active channel when the corresponding option is
  enabled;
- notification for an enabled favorite when it becomes active;
- optional system notification sound;
- sound on/off setting;
- a test-sound action in settings;
- no repeated notification for metadata-only refreshes;
- no repeated notification caused by a single transient YP disappearance.

Notification generation consumes the same presence transitions that drive the
`新着` view. This keeps tab state and notification state consistent.

Custom sound-file selection may be added later. The first implementation should
prefer the Windows system notification sound so Nova does not require bundled
media solely for alerts.

## Playing view

`再生中` presents Nova-owned playback sessions separately from catalog
availability. It is the appropriate place for detailed runtime state that would
make the ordinary channel list noisy.

The view should be able to expose, as the production application boundary makes
information available:

- channel/source identity;
- Connecting / Receiving / Reconnecting / Playing / Stopping / Failed state;
- received packet/count or equivalent progress evidence;
- decoder/runtime initialization state;
- media information such as codec when known.

The GUI must not infer decoder/render success merely because a playback window is
open. Playback diagnostics are projections of the application playback contract,
not guesses based on native window state.

The selected-channel Phase 1 connection uses the existing daily HTTP-FLV and
PCRPlayer path through the application-facing playback contract. It does not
connect managed decoding, fragmented MP4, recording, or physical playback
validation. New runtime fields are added only when the application-facing
playback contract can provide them truthfully.

## GUI Design Baseline v1

The main window uses a dark blue shell. Its fixed left navigation order is:

1. `チャンネル一覧`
2. `新着`
3. `お気に入り`
4. `視聴中`
5. `録画`
6. `検索`
7. `配信`
8. `設定`
9. `ログ`
10. `について`

The upper status area presents connection state, P2P node count, upload rate,
download rate, port reachability, and search. Metrics must be shown as unknown
until an application-owned projection can report them truthfully. The main
channel list remains the dominant area. Its selected row uses a background
highlight and a blue left accent. The primary row actions are Play, Record,
Favorite, and Details; advanced actions stay in the right-click menu.

Settings use tabs for General, Playback, Downloads, Notifications, Network, and
Yellow Pages. Recording and log views use a list above a detail pane. Red marks
recording, stop, and dangerous actions; orange marks warnings and failures;
purple marks debugging and special functions. Green means healthy or complete,
and blue means normal, selected, or playing.

The shell is conceptually:

```text
PeerCast Nova
┌────────────────┬─────────────────────────────────────────────────────┐
│ Fixed navigation│ Connection | P2P | Up | Down | Port | Search        │
│                 ├─────────────────────────────────────────────────────┤
│                 │ Play | Record | Favorite | Details                  │
│                 ├─────────────────────────────────────────────────────┤
│                 │ Channel | Details | Listeners/Relays | Rate | Type  │
│                 │ ...                                                 │
│                 ├─────────────────────────────────────────────────────┤
│                 │ Status / refresh detail                             │
└─────────────────┴─────────────────────────────────────────────────────┘
```

The first shell slice may expose unsupported destinations as disabled entries
until their views exist. It must retain the existing Channels, New, Favorites,
and Playing projections and their catalog behavior.

The source URL field and playback actions already present in Genesis are retained
until an explicit UX slice demonstrates a better placement. GUI Next must not
remove a proven interaction merely to make the first tab mock-up cleaner.

Advanced YP configuration, notification settings, and later download settings
belong in Settings rather than permanently occupying the channel browser.

## Architecture boundaries

GUI Next extends the existing separation instead of moving network and persistence
logic into Win32 controls.

Target data flow:

```text
Yellow Page definitions
        |
        v
per-YP catalogs -----> aggregating catalog
                            |
                            v
                  refresh/presence session
                     |      |       |
                     |      |       +--> notification transitions
                     |      +----------> favorite projection
                     +-----------------> channel/new projections
                            |
                            v
                     Genesis view model
                            |
                            v
                       Win32 surface
```

The existing production-independent playback boundary remains:

```text
Genesis GUI -> IPlaybackSessionController -> application adapter
```

GUI Next must not directly reference production PCP, FFmpeg, decoder, or renderer
implementation types merely to populate the Playing view.

Persistence should be behind application-facing stores/interfaces so favorites,
YP definitions, refresh preferences, and notification preferences can be tested
without invoking native controls.

## Delivery slices

Implementation should remain small, reviewable, and GREEN after each slice.

1. **GUI Next contract and shell**
   - introduce tab/view model concepts without changing catalog networking;
   - retain existing Genesis channel-list behavior on the Channels view.
2. **YP definitions and persistence boundary**
   - built-in SP definition;
   - add/edit/remove/enable model and tests;
   - no network races introduced.
3. **Multi-YP aggregation**
   - read enabled YPs independently;
   - deterministic deduplication and origin tracking;
   - bounded per-YP failure.
4. **Automatic refresh**
   - lifecycle-owned scheduler;
   - startup + periodic + manual refresh serialization;
   - configurable interval and shutdown cancellation.
5. **Presence/new-state tracking**
   - successful-generation tracking;
   - three-miss ended threshold;
   - New projection and flap regression tests.
6. **Favorites**
   - persistent favorite identities;
   - context-menu add/remove;
   - Favorites projection including offline saved entries.
7. **Notifications**
   - transition-driven notifications;
   - favorite/new rules;
   - system sound enable/disable and test action.
8. **Playing projection**
   - tab and truthful application-contract state only;
   - no fake decoder/render evidence.
9. **GUI polish**
   - settings surfaces, YP filter, search/filter, selection/focus/accessibility
     regression coverage.
10. **Future: Downloads**
   - add only after the core browsing/playback workflow is stable;
   - keep download lifecycle, progress, retry, destination, and history separate
     from the ordinary channel catalog.

## Explicit non-goals for the first GUI Next slices

- merging PR #228 or the production playback integration PR;
- enabling auto-merge;
- rewriting the existing PCP/FFmpeg playback pipeline;
- claiming physical playback validation from GUI tests;
- implementing downloads before the browsing/YP/favorites/notification path is
  stable;
- large theme or animation frameworks;
- copying PeCaRecorder source code or binary implementation details.

PeCaRecorder is a behavioral and workflow reference only. PeerCast Nova keeps its
own architecture, tests, naming, and implementation.
