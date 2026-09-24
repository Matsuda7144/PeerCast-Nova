# Control Plane and Data Plane

PeerCast Nova keeps user-visible state and commands separate from media payload transfer.

~~~text
Control: Nova.App / Nova.Cli (composition roots)
             |-> Nova.Core    -> lifecycle, channel state, immutable status
             +-> Nova.Network -> session and relay control

Data:    Peer TCP -> PCP session -> bounded Network packet flow
                                  |-> PCP relay coordinator -> downstream peer
                                   |-> playback input -> loopback HTTP-FLV -> PCRPlayer / DSPlayer
                                   +-> managed Playback pipeline (additional post-MVP path)
~~~

## Rules

1. Media payload bytes do not travel through Core status or generic application events.
2. Every consumer owns an explicit packet/byte bound and backpressure policy.
3. Cancellation and shutdown propagate from the owning session.
4. Discontinuities and terminal outcomes are explicit data, not hidden log messages.
5. UI status is an immutable control-plane snapshot and is rate-limited independently from payload flow.

## Current relay flow

`PcpRelayPacket` exposes one callback-scoped packet with its sequence, protocol position when available, numeric packet type, payload memory, and explicit discontinuity details. It contains no channel, peer, or session identifier. Child status and termination use separate fixed-field snapshots. Slow children are isolated through bounded queues.

## Current playback flows

The accepted MVP playback flow in the daily Windows app receives a selected PCP channel, exposes a loopback-only HTTP-FLV endpoint, and starts a compatible external player, including PCRPlayer and DSPlayer. This external-player path satisfies the MVP playback requirement.

`Nova.Playback` also contains bounded input ownership and managed/native decode, scheduler, and Windows output boundaries. Integrating that managed path into the daily app, adding managed regular-MP4 production playback, and expanding Windows output are post-MVP work. A completed parser, decoder slice, or controlled output validation is evidence for that additional route only; it does not narrow or reopen the accepted MVP path.
