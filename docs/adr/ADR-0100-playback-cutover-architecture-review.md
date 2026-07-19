# ADR-0100: Playback Cutover Architecture Review

- Status: Accepted
- Milestone: 0.0.8p
- Production FFmpeg activation: Pending and unchanged

## Context

Milestones 0.0.8k through 0.0.8o completed a diagnostic-only finite path from
one H.264 packet and one AAC packet through native frame observation, managed
copy, managed conversion, and awaited decoded sink delivery.

The repository already contains separate bounded scheduling, timing, late-item,
recovery, and Windows output boundaries. The diagnostic path must not be joined
to those components by simply adding another queue or by retaining converted
callback payload.

## Findings

### Compatibility

Playback work remains isolated from PCP framing, Channel, and Relay wire
contracts. No playback milestone changes the verified PeerCastStation protocol
behavior. The diagnostic H.264/AAC envelope is not an FLV or MP4 compatibility
claim; a bounded demux contract is still required before real container input
can enter the decoder path.

### Composition seam

`PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary` keeps canonical payload valid
only for the awaited sink callback. Converted payload therefore cannot be
stored and later scheduled.

`BoundedPlaybackScheduler` already owns `PlaybackDecoderFfmpegManagedFrame`
instances and performs conversion plus awaited delivery when an item is run.
The future decode-to-playback cutover seam is consequently the owned managed
frame before conversion, not the converted callback payload and not a second
decoded-output queue.

### Buffering and cutoff

The scheduler currently limits frame count but not aggregate owned bytes. A
managed frame may contain a large bounded allocation, so frame count alone is
not a sufficient memory bound for decoder integration.

Before any diagnostic or production decoder can enqueue frames, the scheduler
must gain an aggregate byte budget in addition to its existing item capacity.
The budget must use checked accounting over owned plane/sample byte lengths,
reject and dispose a single frame that cannot fit, and apply the configured full
policy until both count and byte constraints are satisfied.

A media-duration cutoff is deferred because unknown timestamps and durations
are explicitly valid at existing decode boundaries. Byte accounting is always
available and is therefore the next mandatory cutoff. Timing-based late
handling remains the responsibility of the existing timing and late-item
boundaries.

### Module split

The existing dependency direction remains acceptable:

`Foundation <- Core <- Network <- Playback <- Cli`

`Protocol.Pcp` remains independently referenced by Network and Cli. No new
project split is approved in 0.0.8p. In particular, a `Nova.Playback.Windows`
project would add packaging and public-contract movement without solving the
current memory-bound prerequisite. Reconsider that split when a non-Windows
application host is introduced or when OS-specific package/runtime assets can
no longer remain internal implementation details.

### .NET 10

The repository keeps `net10.0`. It is the current project baseline, the SDK is
pinned through `global.json`, and no compatibility requirement presently
justifies multi-targeting. CI and release builds must remain on a supported
servicing update. Multi-targeting is deferred until an actual consumer requires
an older target.

### External libraries and assets

No new production NuGet dependency is approved for scheduler budgeting or
cutover composition; the BCL is sufficient. Test dependencies remain centrally
pinned and test-only.

FFmpeg remains host-installed and dynamically loaded. Nova does not bundle or
redistribute FFmpeg while license review is Pending.

No third-party media, logo, icon, font, or other binary asset is introduced by
this milestone. Future repository assets require recorded source, license, and
redistribution permission before inclusion. Existing PeerCast or
PeerCastStation branding must not be copied merely because it is visually
familiar.

## Decision

0.0.8p is a documentation and architecture-alignment milestone. It does not
add runtime wiring.

The next implementation milestone is 0.0.8q: add aggregate owned-byte budgeting
to `BoundedPlaybackScheduler`, including deterministic full-policy behavior,
accounting snapshots, overflow handling, reservation safety, disposal, and
focused tests.

Only after that prerequisite is verified may a later milestone connect copied
diagnostic frames to the scheduler. That connection must preserve explicit
ownership transfer and must not claim atomic two-stream enqueue unless the
scheduler provides a real transactional contract.

## Non-goals

No decoder backend activation, demux, packet loop, flush/EOS handling, scheduler
ingress wiring, resident task, new output queue, Windows device activation,
real-media playback claim, FFmpeg redistribution, or approval-state change is
included.
