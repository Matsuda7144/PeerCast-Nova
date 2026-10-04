# Bounded headless PCP listener acceptance harness

This launch path exists only to unblock NAT-PMP router acceptance. It starts
the production public IPv4 PCP TCP downstream listener without a GUI or an
upstream channel/publisher. It does not test media relay, external reachability,
or GUI operation. Normal startup and saved GUI settings retain their existing
behavior. This command requires an explicit ON/OFF value and does not save it;
saved settings and `NOVA_AUTO_PORT_MAPPING` do not override this command.

After building Release, run from the repository root:

```powershell
& ./src/Nova.App/bin/Release/net10.0/Nova.App.exe --acceptance-headless-relay --duration 30s --port 0 --auto-port-mapping off --run-id NAT-PMP-OFF-EXAMPLE
```

All four inputs are mandatory. Duration is an integer from 1 to 300 seconds;
port is 0 (an OS-selected port) through 65535. Run ID is 1 to 96 ASCII letters,
digits, dots, underscores or hyphens. Duplicate, unknown, mixed GUI/acceptance,
and incomplete options fail before launching a listener. For a later explicitly
authorized router acceptance, use `--auto-port-mapping on` and a fresh Run ID.
ON can change router mappings. This slice's dry-run uses OFF only.

Ctrl+C requests cancellation through the existing application token. Duration
starts before startup, including startup work. Startup has a five-second wait
budget; shutdown has a separate ten-second budget. Graceful stop always attempts
listener cleanup before the existing NAT runtime's two-second reconciler cleanup.
Forced process termination cannot guarantee router cleanup; confirmed mappings
still have finite leases.

Each stdout line is one JSON object with schema
`Nova.HeadlessRelayAcceptance.v1`. Retain the entire stream together with the
commit SHA, arguments, run ID, exit code and timestamps. `ListenerStarted`
reports the actual bound endpoint (`0.0.0.0` is the wildcard bind address;
`PortMapping.Network.InternalAddress` is the selected route's local address).
`PublicWanEligible` means the IPv4 listener qualifies for public mapping demand,
not that a router is compatible or the port is reachable externally.

`Status` records appear every second. `PortMapping` reuses the Network layer's
coherent read-only projection, including network epoch/device/interface/address,
state, returned external port, requested/confirmed/estimated lease evidence,
reachability, timestamps, and diagnostic codes. External address is currently
unavailable; reachability remains `NotChecked`. `MappingObservations` preserves
retired-network and late-operation evidence without adopting it as current state.
`OperationDispatchCount` counts dispatches retained in the bounded diagnostics
window; it is not a lifetime packet counter. OFF runs have zero dispatches.

The production chain is GUI Relay event -> daily relay controller -> relay
coordinator -> `PcpDownstreamRelayServer` -> `TcpTransportListener`. The harness
reuses `PcpDownstreamRelayServer` plus its caller-owned fanout and NAT runtime.
Only the server binds and acquires the public mapping owner after successful
binding. Server shutdown closes the socket, releases the owner, and drains child
sessions; runtime shutdown then stops the pump and reconciles cleanup. The harness
never sends Map/Unmap directly or changes manager ownership/network/desired state.

Terminal codes distinguish `HarnessStartupFailure`, `ListenerBindFailure`,
`ListenerStartupTimeout`, `ListenerEndedUnexpectedly`, `DurationElapsed`,
`CallerCanceled`, and `ApplicationShutdownTimeout`. Mapping discovery/pending
and unknown results are separate mapping states. The terminal `Summary` contains
`Completed`, `CleanupUnconfirmed`, or `ApplicationCleanupIncomplete`.
`ProcessCompleted.Summary` contains the exit code: 0 for completed cleanup, 1 for
startup/listener failure, 2 for invalid inputs or unconfirmed/incomplete shutdown.
`ListenerStarted` retains the fact that startup succeeded; use `ListenerState`
to determine whether the listener is still running. `ApplicationTasksDrained`
is conservative and false for unconfirmed cleanup because submitted backend work
may still retain a late-observation task beyond the shutdown budget. Router
uncertainty is not reported as definite deletion or definite failure.

An OFF dry-run proves process/listener lifecycle only. Real NAT-PMP discovery,
physical Map/Renew/Unmap, external reachability, and physical GUI operation remain
unverified until a separate authorized acceptance run.
