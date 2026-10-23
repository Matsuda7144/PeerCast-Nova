# GUI Next publishing composition Phase 1

## Dependency scope

This independent integration starts at main
`1e85f378b50aa827ef0070d703b23c1c3772b95d`, with #313 publishing and #314
borrowed status foundations already merged. Source #271 is
`f0ae19170c7d7cb36dbcea9b253549571c6a695d`; only its final GUI-specific commit
(15 files relative to `e3748378131247c7b5747797a022ab13fdeb6786`) is used as a
reference. Its dependency-inclusive 65-file history is not imported.

Current-main Logs, recording, connection settings, root network ownership,
validation-profile and diagnostics paths take precedence over old source code.
The default and validation GUI state loaders both bind only the safe status
interface. The existing root network shutdown order is retained; the publishing
owner is released after GUI/host and other root cleanup. Daily startup/window
files and #313/#314 foundation implementation remain unchanged. No old source
branch, #271 or old GUI foundation is modified.

## Application ownership

Normal `RunGenesisAsync` creates one existing `INovaObsPublishingRuntime`
through `SystemNovaObsPublishingRuntimeFactory`. Construction does not bind a
listener, register an active publishing source or start OBS ingest. Phase 1
never calls its Start method. The application retains this unstarted owner
for the GUI lifetime; the GUI receives only `INovaObsPublishingReadOnlyStatus`.

After GUI/host teardown, application composition awaits owner disposal once,
including startup rejection/failure. Repeated disposal awaits the same task;
cleanup failure maps to the existing Faulted startup result. Disposal of this
unstarted owner cancels before its run loop can start listeners. This cleanup
is not evidence of graceful completion of an active broadcast.

## Read-only screen

The existing left navigation enables 配信. The existing dark list and details
pane show state, ChannelId, SessionId and typed rejection/termination categories.
IDs describe the actual owner's configured identity, not accepted media.
Normal startup reports 未起動（待受未開始） with actual owner IDs.
An absent/unreadable owner reports 利用不可 without invented IDs. The borrowed
boundary retains the distinct NotStarted, Observed and Stopped states and its
generation/revision/terminal guards.

F5 and shell Refresh read the snapshot without catalog refresh. The existing
GUI status clock also reads while the publishing view is selected; no new
timer or collector is introduced. Unchanged snapshots retain the row collection
and disposed tabs reject late reads. Publishing rows cannot dispatch channel
playback, favorites, recording or channel context-menu commands.

Connection URLs, routes/keys, metadata and raw exception text are excluded.
There are no publishing Start/Stop, OBS, WAN or NAT commands.

## Validation boundary

Managed tests exercise ownership, actual production-owner construction and
unstarted disposal, factory/cleanup failure, navigation/schema, privacy and
refresh isolation. The production-owner test never invokes Start. Existing
status tests cover observed/terminal states and safe failure classification.
Release build, solution tests and format verification are required.

Windows visual/lifecycle acceptance and real publishing remain unperformed.
Starting/stopping an active broadcast requires a later, separately approved
lifecycle slice.
