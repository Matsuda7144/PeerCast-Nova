# PeerCast Nova Navigation

## 現在地

**0.0.8q — Bounded Scheduler Aggregate Byte Budget — Verified**

`BoundedPlaybackScheduler`はframe数とaggregate owned byte数を独立して制限します。queued、reserved、in-flight frameをownership終了までaccountし、countまたはbyte上限で既存full policyを適用します。

- oversize frame: `FrameTooLarge`
- `DropOldest`: unreserved frameだけを必要数破棄
- `DropIncoming`: incomingを破棄
- `Reject`: queueを変更せず拒否
- snapshot: `OwnedByteCount`

CI #275はWindows／Ubuntuでrestore、Release build、full tests、formatに成功しました。

## 最新の入口

- `src/Nova.Playback/BoundedPlaybackScheduler.cs`
- `tests/Nova.Playback.Tests/BoundedPlaybackSchedulerTests.cs`
- `docs/adr/ADR-0101-bounded-scheduler-aggregate-byte-budget.md`
- `docs/reviews/PCN-0109-BOUNDED-SCHEDULER-BYTE-BUDGET-VALIDATION.md`

## 次の作業

**0.0.8r — Diagnostic Managed Frame Scheduler Ingress Transaction**

0.0.8mで所有したvideo／audio managed frameを既存schedulerへ固定順序で渡し、sequence、discontinuity、enqueue result、部分成功時のownershipとcleanupを固定します。二streamのatomic enqueueは主張しません。

## 未完了

- FFmpeg backend activation、同梱、再配布承認
- bounded FLV／MP4 demux
- diagnostic／production decoder ingress
- duration cutoffと完全なA/V同期
- controlled audible／visible playback
- GUI、installer、recorder
