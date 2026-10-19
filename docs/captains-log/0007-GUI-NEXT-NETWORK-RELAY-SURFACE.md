# Captain's Log #007

Genesis GUI Next now exposes the native `Network / Relay...` settings route and its
status-only window on a physical Windows run.

The validated surface reports unsupported backend capabilities truthfully:
`Listener: Unavailable`, no applied bound endpoint, zero relay connections,
Direct Play unavailable, IPv4 reachability unknown, and IPv6 unavailable. The
configured/default port value is not presented as an active listening endpoint.

The current native surface deliberately remains non-editable while the active
backend reports no shared-listener capability. Future editable controls must be
gated by real controller capabilities and applied/runtime evidence rather than by
draft/default values.

> Network state shown in the GUI must describe applied/runtime evidence, not intent.
