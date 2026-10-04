# NAT port mapping GUI control and diagnostics

This slice adds controls to the existing daily Win32 playback/relay window.
The adopted GUI design baseline calls for channel-first navigation and consistent
status semantics. The current main has a small native channel/relay shell, with
no settings page, theme framework or persistent settings store. This change
preserves its channel-first layout and adds a checkbox, separate router/connectivity
status, lease evidence and a scrollable, selectable read-only diagnostic panel.
It does not implement the larger dark-blue navigation/theme redesign.

## Settings and lifecycle

`NovaOptions.AutoPortMapping` defaults to false. The daily application persists
the existing options model in
`%LOCALAPPDATA%\PeerCast\Nova\settings.json`. Saving preserves the other model
fields and replaces the file after writing a same-directory temporary document.
An invalid settings document is reported rather than silently enabling anything.

At startup, a nonempty `NOVA_AUTO_PORT_MAPPING=on|off` overrides the saved setting;
otherwise the saved value applies, then the model's default OFF. Invalid environment
values retain the foundation's rejection behavior. GUI changes immediately update
the current desired setting and persist the choice. On the next startup an explicit
environment value again wins. The UI explains this when an environment override
was used. There is no second protocol-level or independently scheduled GUI policy.

`NovaPortMappingControl` calls only `Manager.SetEnabled` and the settings store.
Desired changes run synchronously in event order without I/O. Save operations are
serialized and perform I/O on a worker; a save error does not undo the active
desired state. The GUI reports that persistence failed. The composition drains
outstanding saves before disposing the controller.

The runtime continues to pump the existing reconciler independently of the GUI.
Existing listener tokens are reevaluated after ON. OFF supplies no demand and
cleanup uses the existing reconciliation/shutdown budgets. No GUI cancellation
token is passed to Map, Renew or Unmap. The GUI never invokes those operations.

## Projection

`Manager.ReadState()` takes one coherent capture under the existing state lock:
setting, generation, exact network identity, registered ports, mapping entries,
pending operation type and bounded diagnostic observations. Reading does not
reconcile, dispatch, advance generation or modify lease evidence.

`NatPortMappingStatusProjection` is a pure consumer. Current endpoint/lease data
comes only from an entry matching the capture's entire current network identity,
including epoch. Historical observations are retained as timestamped diagnostics.
Every old-generation/old-network diagnostic row is explicitly labeled historical
and excluded from the current diagnostic summary. Old routes cannot supply a
current external port or confirmed lease. The one daily
public TCP listener is the current displayed mapping; registered ports remain
available even while the setting is OFF.

States distinguish Disabled, Discovering, Idle, Mapping, Mapped, Renewing,
Unmapping, ResultUnknown, UnmapUnconfirmed and Rejected. OFF is a setting, so
outstanding cleanup remains visible rather than being hidden by Disabled.
Reachability has a separate display model and stays NotChecked in this slice.
An ACK means router evidence, not Internet reachability. External address remains
unknown because the backend does not query it. The displayed external port is
the returned result, and can remain last-observed evidence during unknown cleanup.
Requested lifetime, router-confirmed lifetime and estimated expiry are labeled
separately. No remaining-time countdown is inferred from wall-clock arithmetic.

The application formats Japanese summaries. It polls read-only state every 500 ms;
the native window receives posted updates on its own message loop. Immediate
settings updates also refresh the projection. Lower-generation display frames
are discarded so an older poll cannot undo the visible checkbox after a click.
This is presentation ordering, not adoption of router results.

## Diagnostic codes

The panel retains the last 32 projected code rows from the manager's bounded
256-observation history. Stable codes include:

- RouterNotFound: no unambiguous local IPv4 gateway candidate; does not prove that
  a physical router is absent or unsupported.
- DiscoveryTimeout: supported projection of a discovery-timeout observation.
  The existing system source only enumerates local routes and emits no timed
  protocol discovery observation; a missing candidate never fabricates this code.
- MapInProgress / MappingObserved / MappingEstablished / MapResultUnknown.
- RenewInProgress / RenewResultUnknown.
- UnmapInProgress / UnmapObserved / UnmapUnconfirmed.
- ResponseTimeout: an operation wait ended, with unknown router-side result.
- NetworkChanged / StaleObservation: historical generation or network evidence.
- ExternalPortRemapped / LifetimeShortened: returned evidence differs from request.
- RouterRejected / SettingChanged / Disabled.

Router refusal is distinct from timeout. Old MappingEstablished diagnostics are
not presented as current after a network change. Raw exception messages, packets
and protocol logs are not dumped into the diagnostic panel.

## Validation and bounds

Deterministic projection/settings/composition tests reuse the foundation's virtual
clock and fake router. They cover default OFF; existing-listener ON/OFF demand;
rapid OFF/ON and ordered persistence; ACK without reachability; Map/Unmap/Renew
timeouts; remapped ports; shortened leases; epoch changes; read-only capture;
save/reload, failed persistence, startup precedence and actual composition event
wiring. The foundation's original 37 cases remain intact.

Native GUI visual/accessibility acceptance, actual NAT-PMP router behavior and
Internet connectivity are UNVERIFIED. No actual router packets or Internet
reachability checks are part of these tests. New backends, address queries,
firewall changes, alternate routes/ports and a broad GUI redesign are excluded.
Stop after this slice; a separately authorized bounded real-router validation
and native GUI usability check are the recommended next slice.
