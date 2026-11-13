# ADR-0494: Opt In Smart Relay Recovery

## Status and scope

Smart Relay Phase C, based on main `2d6d45edb563a56c25241157dc02e313f76c1d9a`
after #366; main CI run 43347829589 succeeded. Managed validation is recorded
in [PCN-0504](../reviews/PCN-0504-OPT-IN-SMART-RELAY-RECOVERY.md).

## Decision and lifetime

An optional PcpChannelSessionOptions.SmartRelay selects the new recovery policy.
Null preserves the existing finite reconnect/initial RedirectPlanner behavior,
including GUI Relay's MaxReconnectAttempts=0. The opt-in loop lives inside the
existing session and calls the same RunAttemptAsync, connector, HTTP/HELO codec,
PipeReader and stream decoder. It is not a second PCP engine or Owner registry.

The same Coordinator, Owner identity, admission registration, fanout and bound
listener survive recoverable upstream failures. The upstream task now remains
alive during discovery/backoff; the Coordinator's existing task-ending drain
still runs only on terminal failure/Stop. Each TCP attempt has its own reader,
negotiation timer and transport cleanup. Media upstreams never overlap.

Stop cancels connection-slot waits, timers and receive reads. Retirement of a
Smart GUI Owner schedules Stop on that exact Coordinator, outside the Owner
lock. Owner-current/directory activity is checked before attempts and again
after connection-slot acquisition. Old tasks retain only their own objects.

Transport disposal runs on one bounded cleanup task per attempt; recovery
awaits both actual Dispose and CleanupCompletion, with a default five-second
deadline. Timeout/failure terminates this Owner and records unconfirmed cleanup;
it does not acquire another transport. Coordinator ShutdownCompleted is false
when Smart transport cleanup or retained fanout workers remain unconfirmed.

## Discovery and retry budgets

Reuse Phase B's owner-scoped PeerDirectory (256 records, 120-second HOST TTL),
address policy, matching cid, self/direct-peer/known-loop exclusions and local
failure cooldown. Successful DATA observations add one timestamp per existing
record, not an unbounded history. Candidate priority is most recent accepted
media, Receiving, RelayAvailable, local negotiation success, freshness and Hops.
Unknown is not success/availability; Agent names do not affect selection.

The original user-supplied bootstrap remains an explicit fallback, including
authorized local addresses. External HOST candidates still require permitted
global IPv4. One discovery wave has at most eight targets, including bootstrap,
and 30 seconds of monotonic time. Slot waiting, connect and HELO share this
budget. Endpoint deduplication persists across newly learned HOSTs in that wave;
new referrals never reset it. A process-wide semaphore allows at most four
Smart connect/negotiation attempts at once and releases its slot at negotiation
completion, not after an indefinitely live media stream. Waiting owns no socket.

Each exhausted wave backs off 1/2/4/8/16/30 seconds, with +/-20 percent jitter.
Limited mode stops after its configured recovery limit (default five minutes).
Persistent mode changes after five minutes to 60/120/300-second backoff, capped
at 300 nominal seconds with the same jitter; each low-frequency wave attempts
one target. It can continue until Stop/Retire. No reconnect-counter inflation,
network-restoration bypass, manual-retry bypass or parallel media source is used.
Thirty seconds of advancing DATA, rather than HELO/HEAD/control traffic, resets
the consecutive failure/backoff history. Status counters saturate and retain no
attempt list. Delays and budgets use the selected TimeProvider.

## Triggers and protocol compatibility

- EOF/reset/retryable I/O and read-idle failures recover after cleanup.
- 503/RelayFull and QUIT 1003 preserve A-1/B negotiation, learning HOST before
  trying another target. Legacy Nova 503/OLEH/QUIT 0 stays a capacity refusal.
- Explicit BADAGENT/NOTIDENTIFIED and other non-capacity protocol/HTTP errors
  terminate instead of being converted to Unavailable or retried forever.
- Normal QUIT 1000 terminates. An unspecified zero-code close does not prove
  global broadcast end; Smart mode can retry within its policy.
- Standard OFFAIR 1008/SHUTDOWN 1009 from one relay are not proof that the
  broadcaster is globally gone; they use the same budgets/backoff. Phase C
  introduces no authoritative broadcast-termination message or custom atom.
- Slow downstream children stay isolated by existing bounded queues.

Existing PCP wire codecs, GUIDs, BCID, ChannelSessionId/PublishingSessionId and
YP equality checks are unchanged. PeerCastStation's standard QUIT codes and
32-bit media positions were checked against the source pinned in ADR-0493.
Its request position is a long; emitted CHAN positions use the low 32 bits.

Media-stall observation is separate from byte/control activity. An explicitly
configured continuous-media profile can degrade at ten seconds and recover at
twenty seconds; valid nonempty, accepted DATA advances its clock. It observes
the same reader through a cancellable timer, not a second reader. The default
MediaStallTimeout is null, preserving sparse RAW and existing read-idle/profile
settings rather than imposing a universal twenty-second cutoff.

## Media continuity and downstream generations

The resume cursor is the end of DATA accepted by the real consumer/fanout,
not Core's last observed packet start. HEAD does not add its payload length to
the cursor. Matching wire positions expand against this 64-bit end across wrap.
Only a byte-identical previous DATA packet can be dropped as a duplicate. The
proof is bounded to one MiB, expires on use after 120 seconds and is discarded
on low-frequency backoff/terminal exit. Header identity uses a SHA-256 digest.

Every upstream outage conservatively closes old media consumers and clears
the fanout's retained HEAD. The existing fanout object is temporarily paused,
not replaced/disposed. Its workers must really finish, then the downstream
server waits for transport/admission cleanup. Listener/port/Owner remain stable.
If any cleanup cannot be proven, the entire Owner fails closed.

At a clean packet boundary, downstreams receive standard QUIT 1003 before close.
After a canceled partial packet write, transport close is used instead: a QUIT
must never be appended inside an unfinished DATA payload. Reconnected clients
can attach after a fresh HEAD; changed HEAD or a recovery boundary has an
explicit internal discontinuity/media-generation observation. Existing playback
and recording leases end with the old generation and never receive mixed media.

Gap, backward position or conflicting duplicate DATA is not appended to an old
HEAD. It ends that attempt, clears its cursor, requests position zero and requires
a fresh HEAD. Recovery DATA without a HEAD similarly restarts from zero. A HEAD
change within a connection performs the same downstream boundary before new
media. A TCP/HELO success is not recovery completion: Smart status/GUI first
readiness require actual advancing DATA. No seamless playback promise is made.

## GUI activation and remaining scope

Default GUI behavior and saved connection settings are unchanged. An explicit
process environment setting `NOVA_SMART_RELAY_RECOVERY=limited` or `persistent`
enables Smart recovery for the existing GUI Relay command. Invalid/missing/off
values leave it disabled. Setting it or constructing the application never
starts a relay; the user must still press Relay. It does not enable recovery for
the separate playback/OBS/recording Owners. The existing status snapshot exposes
recovery/degraded/backoff state. No native UI redesign or saved-setting migration
is included; environment mode is read at application construction.

When enabled, GUI initial readiness can wait through recovery until valid DATA
or Stop/Retire/terminal failure; the normal mode retains its 30-second readiness
timeout. Stop never schedules a restart. Listener remains loopback/ephemeral.

Node-wide SID migration, wide BCST forwarding, public multiplexed-listener
binding, WAN/NAT, bandwidth reservation, healthy-source quality switching,
multi-source mixing and relay-tree/manual-disconnect UI remain out of scope.
Phase W is an independent mandatory public-listener/reachability gate. Managed
loopback tests and green CI do not establish actual PeerCastStation/GUI A/V,
router/WAN or physical recording compatibility.
