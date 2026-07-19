# PCN-0108: Playback Cutover Architecture Review

## Scope

0.0.8p reviews the completed diagnostic path through 0.0.8o before any
connection to the existing bounded scheduler, timing, recovery, or Windows
output boundaries.

## Evidence reviewed

- `PlaybackDiagnosticAwaitedSinkDeliveryTransaction`
- `PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary`
- `BoundedPlaybackScheduler`
- `PlaybackPipelineIntegrationProbe`
- project-reference direction in all production projects
- `Directory.Build.props`, `Directory.Packages.props`, and `global.json`
- current navigation, roadmap, README, and changelog state

## Required conclusions

- The verified PCP/Channel/Relay compatibility contracts remain unchanged.
- The diagnostic H.264/AAC envelope is not treated as an FLV or MP4 demux claim.
- Converted decoded payload remains callback-scoped and is not a scheduler input.
- The existing scheduler-owned managed frame is the future cutover seam.
- No second decoded-frame queue is introduced.
- Frame-count capacity alone is insufficient before decoder integration.
- Aggregate owned-byte budgeting is the next mandatory implementation step.
- Unknown media timing prevents a mandatory duration cutoff at this stage.
- `Nova.Playback` remains one project until a concrete platform or packaging
  boundary requires extraction.
- `net10.0` remains the single target; no speculative multi-targeting is added.
- No new production package or third-party asset is introduced.
- FFmpeg activation, bundling, redistribution, and approval remain unchanged.

## Documentation alignment

This milestone updates `NAVIGATION.md`, `ROADMAP.md`, `README.md`, and
`CHANGELOG.md` through 0.0.8p and identifies 0.0.8q as aggregate scheduler byte
budgeting.

The active changelog is compacted around the current playback-integration
sequence. Its previous detailed contents remain immutable in repository history
at commit `f078dab5372a45a8c577c1c802a697ab72b45c02` and in the milestone ADR/PCN
records. No implementation source or validation evidence is removed.

## Automated validation

CI #265 passed restore, Release build, the full test suite, and format
verification on both Windows and Ubuntu.

Status: Verified.
