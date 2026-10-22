# GUI Next connection settings — phase 1

The Settings → 接続 / 帯域 / リレー数 surface saves next-start configuration.
The existing Settings → Network / Relay entry is retained unchanged. The editor's
実行状態 button also opens that existing status/NAT-PMP surface through its current
adapter and host. Viewing it does not toggle NAT; the existing explicit NAT control
remains independent of connection preference Save.

| Setting | Default | Validation |
| --- | --- | --- |
| ListenPort | Existing NovaOptions default, 7144 | Required ASCII integer, 1–65535 |
| UseIpv6 | false, consistent with existing unavailable controller | Checkbox |
| TotalUpstreamIpv4Kbps / TotalUpstreamIpv6Kbps | null | Empty means unlimited; integer 0–Int32.MaxValue |
| MaxRelaysTotal | null | Same |
| MaxRelaysPerBroadcastChannel / MaxRelaysPerViewingChannel | null | Same |

Zero is distinct from unlimited. Leading/trailing whitespace is trimmed for parsing;
signs, decimals, exponent notation, non-ASCII digits and overflow are rejected.
The native editor retains the entered text on failure and reports a Japanese field-specific reason.
Persistence failure leaves the saved and startup snapshots unchanged.

`GenesisGuiNextApplicationState` loads `connection-preferences.json` from the same
LocalApplicationData/PeerCast Nova root as Favorites/YP/notification/playback preferences.
The JSON store reuses the existing settings pattern: create-new temporary file,
flush, then replace by rename; no settings framework or migration of other domains.
Missing data uses defaults. Malformed, invalid or unknown-schema data raises the
application load-failure flag and uses defaults without passively overwriting the file.
An explicit valid Save can recover it. No connection data is written on shutdown.
Validation Profile uses its own preferences/connection-preferences.json, with the
existing path/reparse guard checked at load/save boundaries and before replacement.
It never defaults to the production connection preference store.

`Connection.Startup` is an immutable application configuration read at startup.
`Connection.Saved` is updated only after successful persistence. It becomes the next
`Startup` on the next load. Saving has no networking controller dependency and does
not rebind a listener, map ports, disconnect clients, or change relay admission.
This phase does not create a public listener or enforce IPv6, bandwidth or relay
limits. The UI states that runtime application is a follow-up; the startup snapshot
is not advertised as evidence of an active listener or enforced limits.

The new window uses the current-main shared owned `GenesisWindowUiFont` from #305,
with system message font selection, Japanese glyph checks, verified fallback,
DPI refresh and destruction lifetime. Its implementation and existing coverage
(including 選択) are retained; only coverage for the additional connection labels
is appended. The old standalone utility from #289 is not copied over main.
The new editor reuses the existing class-registration/partial-initialization safety
helpers, marshals off-thread Close to its owning thread with a generation token,
and retains cleanup failures instead of reopening or closing a reused foreign HWND.
The #302 Network Settings window, adapter, host and native cleanup remain unchanged.

Tab/Shift+Tab navigate focusable controls; Esc closes without saving. Enter activates
only a focused push button, so entering numeric text cannot accidentally save.

Managed tests cover persistence/reload, unlimited versus zero, invalid input,
rollback, failed loads, coexistence with other domains, ordered saves/shutdown,
startup forwarding, and font ownership/DPI/wiring. No Windows physical acceptance
is claimed. Later acceptance should inspect Japanese labels/layout, keyboard,
close/reopen, Save errors, and restart restoration in an isolated environment.
The integration base is main 5d0eca5301681e5ea19fd976daa953cd9bbcf65e;
source #289 is c5dd8006eaa94d1a53f59b80877471a377f299b2 (two commits).

Recommended next slice: application startup integration of these immutable values
with relay admission and bandwidth policy, including deterministic policy tests.
Integrate with the existing network ownership/capability boundary before enabling
a listener. Keep reachability and router mapping evidence separate.
