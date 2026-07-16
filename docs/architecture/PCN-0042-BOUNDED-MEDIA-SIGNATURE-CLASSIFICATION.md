# PCN-0042 Bounded Media Signature Classification

## Purpose

Milestone 0.0.5b adds a finite container-classification boundary between the
owned playback input and a future replaceable decoder. It does not add a
decoder or renderer.

```text
PcpChannelSession
  -> PcpPlaybackLatencyBuffer
     -> disposable PlaybackPacketLease
        -> PlaybackMediaClassifier
           -> fixed content-free classification snapshot
              -> future decoder selection
```

Core, Network, and UI do not receive the retained prefix or depend on the
classifier.

## Recognition boundary

| Container | Minimum evidence | Fixed basis |
|---|---|---|
| FlashVideo | valid FLV header/version/flags/data offset | `FileHeader` |
| Ogg | `OggS` plus structure version zero | `CapturePattern` |
| MPEG-TS | three `0x47` bytes separated by 188 bytes | `RepeatedPacketSync` |
| ASF | ASF Header Object GUID at prefix start | `FileHeader` |
| WebM | bounded EBML Header with DocType `webm` | `EbmlDocumentType` |
| Matroska | bounded EBML Header with DocType `matroska` | `EbmlDocumentType` |

Insufficient, malformed, unsupported, or limit-exhausted input stays
`Unknown`. Metadata and filename extensions are not fallback evidence.

## Bounds and lifetime

The default prefix bound is 64 KiB and the configurable hard maximum is
1 MiB. Prefix storage comes from `ArrayPool<byte>` and is cleared on every
discontinuity and terminal path, then returned with clearing on disposal.

Every observed packet increments a counter. Only copied bytes increment the
examined-byte counter. The result records a fixed terminal state and optional
fixed limit reason; it never returns a byte sample, hash, identifier, stream
position, metadata value, endpoint, peer text, or exception.

## Probe ownership and shutdown

The CLI probe owns the Network session, latest-wins input, classifier, and
finite timeout. Recognition requests cooperative session stop. Before owner
cleanup it snapshots skips that occurred while classification was active;
after cleanup it separately verifies that no pending packet or outstanding
lease remains. This prevents deliberate post-recognition cleanup from being
misreported as decoder lag while preserving real latency-skip detection.

The default probe is 15 seconds, uses no reconnect, limits the prefix to
64 KiB, and permits at most four discontinuities. Its output is fixed fields
only and omits the channel ID, host, media bytes, signature values, metadata,
positions, and peer-provided strings.

## Deferred boundary

- codec discovery inside the recognized container;
- replaceable decoder construction and finite teardown;
- decoder reset on source/latency discontinuity;
- audio clock, video scheduling, and A/V synchronization;
- audible/visible controlled playback verification.

