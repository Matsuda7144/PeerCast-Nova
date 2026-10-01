# Live Windows presentation acceptance harness

This is acceptance/test code only. Production `src/` remains unchanged.

## Path and ownership

The opt-in `LiveWindowsPresentationAcceptanceTests.Paced_live_Pcp_native_decode_presents_through_existing_Windows_output_once`
calls the same `LiveEncodingNativeAcceptanceTests.RunAsync` used by native no-output acceptance.
It changes only the terminal output factory:

clock-paced 192-frame / 300-block source → native H264/AAC → 0.8s fMP4 →
PCP publishing → standard PCP atoms / bounded Pipe / production stream reader →
production fMP4 composition → one native decoder →
measuring/awaited decoded-output wrapper → existing Windows generation factory →
existing decoded-frame presentation adapter → DXGI/WASAPI.

No raw/PCM bypass, retained file/fixture, second decoder, browser/external player,
or alternate transport is used. This reuses controlled post-handshake PCP wire
acceptance; it does not establish TCP handshake, WAN or device capture.

The composition owns the native decoder and output generation, including adapter
disposal. The acceptance session owns the Windows runner, output resource graph
and timing factory. The runtime is borrowed by the shared live runner and owned
by the physical entry. The publisher retains its existing source/encoder/mux
ownership. Every decoded callback awaits the terminal before borrowing ends.

The producer's successful completion is followed by PCP QUIT, receiver completion,
decoder CompleteAsync, output CompleteAsync/drain, a completed snapshot, generation
disposal, Windows resource disposal, runner Stop, message-loop exit and runner
disposal. Cancellation/failure does not emit successful EOS or completion capture.
A teardown error preserves a simultaneous earlier run error as an aggregate.
There is no retry, reconnect, decoder recreation or output recovery.

## Non-physical preparation

Ordinary tests use fake presentation sessions/consumers. The pinned-runtime dry
gate constructs the existing runner in Created state and verifies runtime/decoder
ABI without calling StartAsync, creating output resources, or reserving a guard.
The native terminal regression forwards real decoded frames to a fake measurement
consumer; it opens no Windows output.

The shared native runner retains 25/40/427 first-publish assertions, 1 HEAD / 8 DATA,
8 fragments, 493 parsed samples, 192 video, 300 raw audio / 301 AAC packets / 2 AAC
flush packets / 308224 decoded PCM frames, payload identity, bounded pending
state, QUIT/EOS and disposal assertions.

## Separately authorized physical entry

**Do not run this entry as part of harness remediation or ordinary validation.**

Future execution requires all of:

- Windows 11 x64.
- Inventory-pinned `NOVA_FMP4_FFMPEG_TEST_RUNTIME`.
- `NOVA_LIVE_PHYSICAL_RUN_ONCE=RUN-ONCE`.
- Caller-supplied `NOVA_LIVE_PHYSICAL_TASK_ID`, `NOVA_LIVE_PHYSICAL_RUN_ID`,
  `NOVA_LIVE_PHYSICAL_HEAD` and `NOVA_LIVE_PHYSICAL_GUARD_DIRECTORY`.
- An existing absolute guard directory, reused for the same Task-ID, not replaced
  to bypass a consumed marker.
- Fresh external authority/CI/review and stale-window checks, before invocation.
  HEAD must equal local/origin/PR. The test records the caller SHA; it does not
  replace the external authority gate.
- One exact physical test filter, a durable evidence log, one initialized session,
  no retry, and direct human motion/audio observation.

The harness validates identifiers and source SHA, verifies runtime before the
physical boundary, and persists `<Task-ID>.physical-once.json` with CreateNew and
disk flush immediately before session creation/StartAsync. The marker counts a
reserved **initialization attempt**, not proof that native output initialized.
It is never removed by the real harness; a second launch for that task fails even
with a different Run-ID. Logs/snapshots determine actual initialization outcomes.
Window title contains Run-ID; guard and logs bind Task-ID/Run-ID/HEAD.

The non-physical guard tests use isolated `HARNESS-DRY-` identities and temporary
markers, and remove only their own test artifacts. They do not reserve a real run.

## Future evidence

Completion capture emits adapter and Windows native evidence snapshots before
composition disposal: submitted/presented video, late video drops, first/last PTS,
audio submissions/writes/accepted PCM frames, late audio delivery, native presents/
writes/audio-start acknowledgements, format/dimensions, device-loss counts, reset
count, in-flight counts and zero completion padding. Native acknowledges must
agree with adapter writes; submitted video must equal presented plus late drops.
Expected decoded counts and final AAC/PCM tail remain asserted.

Cleanup emits resource states and stopped/disposed runner/message-loop/window-token
snapshots. Counters not exposed by these snapshots remain NOT RECORDED, including
precision A/V sync, native recovery details and external stale/matching-window
inventory. User motion/audio reports are separate from automated counters.

**WINDOWS PHYSICAL LIVE PRESENTATION NOT YET PROVEN** until that separate run.
Real device capture, WAN, arbitrary late join, per-fragment independent decode,
precision sync and long-duration stability remain unproven.
