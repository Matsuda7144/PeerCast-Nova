# Native fMP4 decode fixture

`fmp4-avc1-aac.mp4` is a small, locally generated, non-encrypted fragmented MP4 used by the Windows x64 native decoder acceptance test.

| Property | Value |
| --- | --- |
| Size | 14,777 bytes |
| SHA-256 | `3780066C30D70C5063368D708C3850410CAD03840E0B302966206C94AA0425EC` |
| Top-level layout | `ftyp`, `moov`, two `moof` + `mdat` media fragments |
| Video | H.264 Main, `avc1`, 64×48, 10 fps, 12 encoded samples, media timebase `1/90000` |
| Audio | AAC-LC, 48 kHz, stereo, 58 encoded access units, media timebase `1/48000` |
| Duration | 1.221333 seconds per stream |
| Source media | Generated test pattern (`testsrc2`) and 440 Hz sine wave; no third-party recording or music |
| Fixture license/provenance | Original synthetic test data produced for PeerCast Nova and included under the repository Apache-2.0 license |

The file was generated with BtbN's Windows x64 `lgpl-shared` FFmpeg release `autobuild-2026-09-26-13-03`, version `n8.1.3-20260926`. It uses the build's `libopenh264` encoder for H.264 and FFmpeg's native `aac` encoder. The exact generation command was:

```powershell
ffmpeg -hide_banner -loglevel error -fflags +bitexact `
  -f lavfi -i 'testsrc2=size=64x48:rate=10' `
  -f lavfi -i 'sine=frequency=440:sample_rate=48000' `
  -t 1.2 -map 0:v:0 -map 1:a:0 -map_metadata -1 `
  -c:v libopenh264 -profile:v main -pix_fmt yuv420p -b:v 80k -g 10 -threads:v 1 `
  -c:a aac -profile:a aac_low -b:a 64k -ar 48000 -ac 2 -threads:a 1 `
  -video_track_timescale 90000 `
  -movflags +frag_keyframe+empty_moov+default_base_moof+skip_trailer `
  -y fmp4-avc1-aac.mp4
```

The acceptance test splits the initialization segment from the media fragments by top-level box lengths, then runs the bytes through the Nova fMP4 initialization parser, encoded-sample batch adapter, `PlaybackFmp4LiveDecoderSession`, the new FFmpeg backend, and the existing decoded-frame sink. The test fixture is embedded as a test resource; the FFmpeg runtime binaries are not.
