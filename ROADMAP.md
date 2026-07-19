# Roadmap

## 0.0.1-0.0.4 Network Foundation — Verified

- Foundation／Core lifecycleとcontrol/data plane分離
- bounded TCP transport、PCP framing、Channel session
- official PeerCastStation compatibility
- Relay fan-out、downstream server、owned coordinator
- controlled real-media relay verification

## 0.0.5 Playback Foundation — Verified

- separate `Nova.Playback` project
- bounded encoded inputとmedia classification
- replaceable decoder lifecycle
- canonical decoded output contract
- backend provenance、selection、audit、artifact boundaries

## 0.0.6 Decoder and Output Boundaries — Implemented; activation pending

- verified FFmpeg runtime locator／leaseとABI gate
- native context、parameter transfer、codec open、packet/frame boundaries
- callback-scoped metadata／plane／sample access
- managed copy、conversion、awaited sink delivery
- playback policy、scheduler、Windows D3D11／WASAPI output
- finite continuous pump

## 0.0.7 Timing and Late-aware Dispatch — Implemented

- monotonic A/V timing
- bounded wait／redecision
- late-item and reservation policy
- late-aware finite pumping

## 0.0.8 Recovery and Diagnostic Integration — In Progress

### Completed sequence

- 0.0.8a-0.0.8c Recovery and Run Budget — Implemented
- 0.0.8d-0.0.8j Diagnostic Gate through Packet/Frame — Implemented
- 0.0.8k Diagnostic Frame Metadata Transaction — Implemented
- 0.0.8l Diagnostic Frame Data Transaction — Implemented
- 0.0.8m Diagnostic Managed Frame Copy Transaction — Implemented
- 0.0.8n Diagnostic Managed Format Conversion Transaction — Implemented
- 0.0.8o Diagnostic Awaited Sink Delivery Transaction — Verified
- 0.0.8p Playback Cutover Architecture Review — Verified
- 0.0.8q Bounded Scheduler Aggregate Byte Budget — Verified

0.0.8qは既存schedulerへframe-count capacityとは独立したowned-byte budgetを追加します。queued、reserved、in-flight ownershipをaccountし、countまたはbyte制約に既存full policyを適用します。reserved frameはevictionから保護されます。

### Next

- 0.0.8r Diagnostic Managed Frame Scheduler Ingress Transaction

0.0.8mで所有したvideo／audio frameを既存schedulerへ固定順序で渡し、sequence、discontinuity、enqueue outcome、部分成功時のownershipを固定します。atomic two-stream enqueueは主張しません。

### Deferred

- bounded FLV／MP4 demux
- production decoder ingress
- duration-based decoded-buffer cutoff
- flush／EOS completionとcontinuous decode loop
- complete synchronization、resampling、drift correction
- controlled audible／visible playback

## Platform and dependency policy

- `net10.0`を維持する
- CI／releaseはsupported servicing updateを使用する
- production dependencyを安易に追加しない
- FFmpegはhost-installed／dynamic loadingを維持する
- third-party assetはprovenance、license、redistribution permissionを記録する

## 0.1.0 Genesis gate

- bounded demux and production decoder composition
- approved runtime／distribution decision
- finite decoded buffering and output lifecycle
- controlled audible／visible real-media verification
- user-facing application、packaging、documentation
