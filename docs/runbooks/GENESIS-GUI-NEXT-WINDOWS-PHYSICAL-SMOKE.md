# Genesis GUI Next Windows Physical Smoke

This runbook is the physical Windows smoke boundary for PR #230 (`feature/gui-next`). It complements the branch's automated contract tests and GitHub Actions CI. It does not replace them.

## Scope and workstream boundary

This smoke validates the Windows-native GUI Next surface as a user can actually see and operate it:

- main-window layout and fixed-size behavior;
- main-window `Tab` / `Shift+Tab` focus traversal;
- `Channels` / `New` / `Favorites` / `Playing` tab switching;
- native list replacement without obviously stale rows;
- `Search`, `YP: All`, and `Refresh List` interaction;
- favorite mutation and persisted GUI Next state;
- `Settings` notification toggles and `Test Sound`;
- `Yellow Pages...` editor presentation and persistence;
- `Network / Relay...` truthful status-only presentation;
- keyboard focus/navigation and clean shutdown;
- exact restoration of pre-smoke GUI Next user state.

This smoke MUST NOT be used to claim any of the following:

- production PCP playback success;
- H.264 or AAC decode success;
- Windows video or audio delivery success;
- relay acceptance;
- port reachability success;
- MVP physical acceptance owned by PR #229 or PR #232.

Do not merge, rebase, cherry-pick, transplant commits/history, force-push, enable auto-merge, or touch PR #229 / PR #232 while following this runbook.

Live-channel double-click or Enter activation is not required here. Automated GUI Next contracts already own activation routing, and starting real playback solely for this GUI smoke would cross into the playback/validation workstreams.

## Acceptance rule

The physical verdict is fail-closed.

`ACCEPTED — GUI NEXT WINDOWS PHYSICAL SMOKE COMPLETE` is allowed only when:

1. the tested local commit exactly matches the then-current remote PR #230 head;
2. the working tree is clean before and after the run;
3. build and launch succeed on Windows;
4. every required physical check below is `PASS`;
5. any data-dependent item explicitly marked `N/A` meets its stated N/A condition;
6. pre-existing `%LOCALAPPDATA%\PeerCast Nova` state is restored exactly, or the directory is absent again if it was absent before the smoke.

Any required `FAIL` or `BLOCKED` produces:

`NOT ACCEPTED — GUI NEXT WINDOWS PHYSICAL SMOKE INCOMPLETE`

with the exact failed or blocked items recorded. Do not infer success from missing evidence.

A truthful `Network / Relay` message saying a backend capability or current status is unavailable is not itself a failure. The smoke checks that the GUI reports the limitation truthfully.

If `Test Sound` cannot be heard because the Windows output device is unavailable or muted, record `BLOCKED`; do not convert silence into a pass.

## 1. Capture exact test identity

Run from the repository root in PowerShell.

```powershell
git status --porcelain=v1
git branch --show-current
git fetch origin refs/heads/feature/gui-next:refs/remotes/origin/feature/gui-next
git rev-parse HEAD
git rev-parse origin/feature/gui-next
dotnet --version
```

Required before continuing:

- `git status --porcelain=v1` prints nothing;
- branch is `feature/gui-next`;
- local `HEAD` equals `origin/feature/gui-next` exactly.

The explicit fetch refspec is intentional: it refreshes the remote-tracking ref used by this runbook. A plain `git fetch origin feature/gui-next` can update only `FETCH_HEAD` in some worktree situations and leave `origin/feature/gui-next` stale.

If the SHAs differ, stop. Update only through the normal clean fast-forward-only workflow, then repeat this section. Do not rebase, merge unrelated history, cherry-pick, or force-reset another workstream into this branch.

Record:

```text
Tested SHA:
Windows version:
dotnet version:
```

## 2. Protect existing GUI Next user state

GUI Next persists user state below:

`%LOCALAPPDATA%\PeerCast Nova`

Current state files include:

- `favorites.json`
- `notification-preferences.json`
- `yellow-pages.json`

Close every running PeerCast Nova process before backup or restore.

Keep the same PowerShell window open for the whole smoke so the variables below remain available.

```powershell
$state = Join-Path $env:LOCALAPPDATA 'PeerCast Nova'
$backupRoot = Join-Path $env:TEMP ('PeerCast-Nova-GuiNext-Smoke-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$backupState = Join-Path $backupRoot 'PeerCast Nova'
$hadState = Test-Path -LiteralPath $state

New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
"hadState=$hadState" | Set-Content -LiteralPath (Join-Path $backupRoot 'manifest.txt')

function Get-StateHashes([string]$Root) {
    Get-ChildItem -LiteralPath $Root -File -Recurse |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($Root.Length).TrimStart('\')
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            "$hash  $relative"
        }
}

if ($hadState) {
    Copy-Item -LiteralPath $state -Destination $backupState -Recurse
    Get-StateHashes $state | Set-Content -LiteralPath (Join-Path $backupRoot 'before.sha256.txt')
}

$backupRoot
```

Required:

- record `$backupRoot` in the evidence record;
- do not delete the backup until cleanup verification passes;
- if backup fails, stop the smoke before modifying GUI state.

## 3. Build the exact tested commit

```powershell
dotnet restore src/Nova.App/Nova.App.csproj
dotnet build src/Nova.App/Nova.App.csproj -c Release --no-restore
```

Both commands must succeed.

Record `Build: PASS` only after the Release build completes successfully.

## 4. Launch

```powershell
dotnet run --project src/Nova.App/Nova.App.csproj -c Release --no-build
```

Do not launch a different installed copy for this smoke. The visible application must come from the exact tested working tree.

## 5. Main-shell physical and keyboard check

Required `PASS` observations:

- the Genesis main window opens and remains responsive;
- the tab buttons are visible in this order: `Channels`, `New`, `Favorites`, `Playing`;
- `Search`, `YP: All`, `Refresh List`, and `Settings` are visible and usable;
- the favorite action button is visible (`Add Favorite` or `Remove Favorite` according to selection/state);
- controls are not materially clipped or overlapping at the initial window size;
- an ordinary border-drag resize attempt does not materially resize the main window; the fixed-size behavior is intentional and is not a failure;
- the fixed-size surface keeps the primary controls usable and the list visible;
- no settings-initialization warning appears during a normal successful surface initialization. If one appears, record `FAIL` with its exact text.

### Main-window keyboard navigation

With Settings subwindows closed:

1. click an enabled main-shell focusable control such as `Search` or the source edit;
2. press `Tab` repeatedly and confirm focus advances through visible, enabled main-shell controls rather than remaining trapped on the original control;
3. continue far enough to confirm traversal can cross between the original Genesis controls and GUI Next controls such as the tab strip, Search/YP controls, refresh/settings controls, or favorite action when those controls are enabled;
4. hold `Shift` and press `Tab` repeatedly and confirm focus moves backward;
5. confirm `Tab` / `Shift+Tab` alone does not activate Play, start playback, refresh the catalog, open Settings, or mutate Favorites.

Disabled controls may be skipped. The exact set of enabled controls is state-dependent, so acceptance is based on coherent forward/backward traversal across the currently enabled main-shell controls rather than a hard-coded control count.

Record `Main-window Tab / Shift+Tab: PASS` only when both directions work without an unintended command activation.

Capture a screenshot of the main shell after initial refresh settles or reaches its truthful bounded error/status state.

## 6. Catalog, search, selection, filter, and refresh

Wait for the initial catalog refresh attempt to settle.

Network/catalog success is not mandatory for the GUI shell itself, but the interaction checks that require a visible channel cannot be inferred when no channel exists.

When at least one channel is visible:

1. On `Channels`, select one row and record its displayed name.
2. Type a substring in `Search` that leaves a known matching row.
3. Confirm the native list updates without an obviously stale row from the previous projection.
4. Change the search so the previously selected row is excluded. Confirm there is no visibly stale selection left on a different row merely because it reused the old row index.
5. Clear `Search` and confirm the normal channel list returns.
6. Open the YP selector. Select a specific available YP entry, confirm the list reflects that filter, then return to `YP: All`.
7. Click `Refresh List`; after the attempt completes, confirm the window remains responsive and the displayed rows/status are coherent.
8. Press `F5` once and confirm it produces the same bounded manual-refresh behavior.

If no channel is available for steps 1–5, record this section `BLOCKED` rather than fabricating channel evidence.

If only `YP: All` is available and no specific YP entry exists, record the YP-specific step `N/A — no selectable enabled YP entry`; this does not excuse the other required checks.

Do not deliberately hammer Refresh while an attempt is running. This is a physical smoke, not a load test.

## 7. Cross-tab physical check

Switch through all four tabs in order and back again:

1. `Channels`
2. `New`
3. `Favorites`
4. `Playing`

Required observations:

- the native list redraws for each selected tab;
- no obviously stale selected row survives merely from the previous tab's row index;
- empty `New`, `Favorites`, or `Playing` projections are acceptable when current application state truthfully has no rows for them;
- `Playing` must not be interpreted as proof of decoder, renderer, audio, or video success.

On `Channels`, click a column header and confirm normal Channels sorting remains usable. A comparable non-`Channels` tab with rows may be clicked to confirm it does not unexpectedly rewrite Channels sorting; if no such rows exist, record this secondary comparison `N/A — no comparable non-Channels rows`.

## 8. Favorite + notification + Yellow Page persistence mutation

This section intentionally creates only temporary GUI Next state. The full original state will be restored from the backup in section 12.

### 8.1 Favorite

When a visible live channel exists:

1. select it on `Channels`;
2. click `Add Favorite`;
3. switch to `Favorites` and confirm the saved row is represented there;
4. record the channel name used for the smoke.

If no live channel exists, record `Favorite persistence: BLOCKED`.

### 8.2 Notification settings

Open `Settings` and record the initial checked state of:

- `Notify new channels`
- `Notify favorite channels`
- `Play notification sound`

Toggle exactly one of those settings and reopen `Settings`. Confirm its check mark reflects the new value.

Use `Test Sound` once:

- audible system notification sound: `PASS`;
- no usable/unmuted Windows audio output: `BLOCKED`;
- GUI error or unhandled failure: `FAIL`.

`Test Sound` is a GUI notification check only. It is not evidence for media playback audio.

### 8.3 Yellow Pages

From `Settings`, open `Yellow Pages...`.

Required presentation:

- window title `Yellow Pages`;
- fields `ID`, `Name`, `Index URL`, `Fallback tracker`, `Namespace`;
- `Enabled` checkbox;
- buttons `New`, `Save`, `Delete`, `Close`;
- text indicating that changes apply on the next catalog refresh.

Create one unique disabled smoke entry. Use a unique ID so an interrupted prior run cannot collide, for example:

```text
ID: gui-next-smoke-YYYYMMDD-HHMMSS
Name: GUI Next Smoke Disabled
Index URL: https://gui-next-smoke.invalid/index.txt
Fallback tracker: <blank>
Namespace: gui-next-smoke
Enabled: unchecked
```

The entry MUST remain disabled. Do not enable it. The `.invalid` index URL is deliberately non-production and the disabled definition must not be used to initiate catalog network traffic.

Click `Save` and confirm the entry appears in the Yellow Pages list without a validation/persistence error. Close the Yellow Pages window.

## 9. Restart persistence check

Close PeerCast Nova normally. Wait until the process is fully gone before relaunching the same exact build:

```powershell
dotnet run --project src/Nova.App/Nova.App.csproj -c Release --no-build
```

Required after relaunch:

- the favorite added in section 8.1 is still present when that section was not blocked;
- the notification toggle changed in section 8.2 still reflects the changed value;
- `Settings` → `Yellow Pages...` still contains the unique disabled smoke entry;
- the disabled smoke YP remains disabled.

This restart proves physical persistence wiring only. Cleanup will restore the operator's original files byte-for-byte by hash.

## 10. Settings subwindows and keyboard check

### Yellow Pages keyboard behavior

With `Yellow Pages` open:

- `Tab` advances focus through controls;
- `Shift+Tab` moves focus backward;
- `Enter` activates a focused action button such as `Close`;
- `Escape` closes the window.

Reopen it if necessary after testing close behavior.

### Network / Relay

Open `Settings` → `Network / Relay...`.

Required:

- title is `Network / Relay`;
- the window presents status lines for listener/bound endpoint/relay/direct-listener/IP reachability/external-address state as available;
- the availability note truthfully says when editing, port checks, or current backend status are unavailable;
- no editable listener/port control is invented when the backend does not expose that capability;
- `Close`, `Enter` on the focused close button, or `Escape` closes the window cleanly.

Record the exact availability/status note shown. Do not translate an `Unavailable`, `Unknown`, or `Not checked` state into success.

Capture a screenshot of the Network / Relay window.

## 11. Optional data-dependent noninteractive-row observation

If an offline favorite or a truthful noninteractive `Playing` row naturally exists during the smoke, verify that double-clicking it does not start an activation from that display-only row.

If no such row naturally exists, record:

`N/A — noninteractive row not naturally available; covered by automated GUI Next contract`

Do not manufacture a production playback state merely to satisfy this optional observation.

## 12. Restore original user state

Close PeerCast Nova completely before restoring files.

First verify the backup path exists:

```powershell
if (-not (Test-Path -LiteralPath $backupRoot)) {
    throw "GUI Next smoke backup is missing: $backupRoot"
}
```

Then restore exactly:

```powershell
if (Test-Path -LiteralPath $state) {
    Remove-Item -LiteralPath $state -Recurse -Force
}

if ($hadState) {
    Copy-Item -LiteralPath $backupState -Destination $state -Recurse
}
```

If state existed before the smoke, verify hashes:

```powershell
if ($hadState) {
    Get-StateHashes $state | Set-Content -LiteralPath (Join-Path $backupRoot 'after.sha256.txt')
    $before = Get-Content -LiteralPath (Join-Path $backupRoot 'before.sha256.txt')
    $after = Get-Content -LiteralPath (Join-Path $backupRoot 'after.sha256.txt')
    $diff = Compare-Object $before $after
    if ($diff) {
        $diff | Format-Table -AutoSize
        throw 'GUI Next user-state restoration hash mismatch.'
    }
}
```

If state did not exist before the smoke, verify it is absent again:

```powershell
if (-not $hadState -and (Test-Path -LiteralPath $state)) {
    throw 'GUI Next state directory should be absent after restoration.'
}
```

Do not delete `$backupRoot` until all restoration checks pass. After acceptance evidence is safely recorded, the operator may delete that temporary backup manually.

## 13. Final repository safety check

Refresh the exact remote-tracking ref before comparing the final identity:

```powershell
git status --porcelain=v1
git fetch origin refs/heads/feature/gui-next:refs/remotes/origin/feature/gui-next
git rev-parse HEAD
git rev-parse origin/feature/gui-next
```

Required:

- working tree still clean;
- local HEAD still equals the recorded tested SHA;
- freshly updated `origin/feature/gui-next` still equals that tested SHA. If the remote branch advanced during the physical run, record that fact and do not claim the older run as validation of the newer head.

## Evidence record

Copy and fill this block for the physical result:

```text
Genesis GUI Next Windows Physical Smoke

Tested SHA:
Remote PR #230 head at start:
Remote PR #230 head at end:
Windows version:
dotnet version:
State existed before: yes / no
Backup path:

Release build: PASS / FAIL
Main shell/layout + fixed-size behavior: PASS / FAIL
Main-window Tab / Shift+Tab: PASS / FAIL
Initial refresh/status: PASS / FAIL
Search/selection: PASS / FAIL / BLOCKED
YP filter: PASS / FAIL / N/A
Refresh List + F5: PASS / FAIL
Cross-tab projection: PASS / FAIL
Channels sorting: PASS / FAIL
Non-Channels sort comparison: PASS / FAIL / N/A
Favorite mutation: PASS / FAIL / BLOCKED
Notification toggle: PASS / FAIL
Test Sound: PASS / FAIL / BLOCKED
Yellow Pages presentation: PASS / FAIL
Disabled YP save: PASS / FAIL
Restart persistence: PASS / FAIL / BLOCKED
Yellow Pages keyboard: PASS / FAIL
Network / Relay truthful presentation: PASS / FAIL
Optional noninteractive row: PASS / FAIL / N/A
Normal shutdown: PASS / FAIL
State restore/hash: PASS / FAIL
Final working tree clean: PASS / FAIL

Screenshots/evidence:
- main shell:
- Favorites/persistence:
- Yellow Pages:
- Network / Relay:

Notes:

Verdict:
```

## Verdict vocabulary

Use exactly one:

- `ACCEPTED — GUI NEXT WINDOWS PHYSICAL SMOKE COMPLETE`
- `NOT ACCEPTED — GUI NEXT WINDOWS PHYSICAL SMOKE INCOMPLETE`

The accepted verdict applies only to the GUI Next physical smoke boundary described here. It does not merge PR #230 and does not satisfy or alter PR #229 / PR #232 production playback or MVP physical acceptance requirements.
