# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.8q — Bounded Scheduler Aggregate Byte Budget**

診断専用のH.264／AAC経路は、native context openからmanaged frame copy、format conversion、awaited sink callbackまで到達しています。本番decoder factoryからは独立しており、FFmpeg backend activationや配布承認を変更していません。

0.0.8qでは、decoderと既存schedulerを接続する前提として、`BoundedPlaybackScheduler`へaggregate owned-byte budgetを追加しました。

- frame数とowned byte数を独立して制限
- queued、reserved、in-flight frameをownership終了までaccount
- single-frame oversizeを`FrameTooLarge`で拒否・破棄
- countまたはbyte上限で既存full policyを適用
- `DropOldest`はreserved frameを保護しながら複数frameを破棄可能
- delivery、cancellation、failure、reservation completion、disposeでaccountingを解放
- snapshotへ`OwnedByteCount`を追加
- 既存の三引数snapshot constructionは維持

第二のdecoded queue、decoder ingress、duration cutoffは追加していません。

## 実装済みの主な領域

### PCP互換・Channel受信

- PeerCastStation互換HTTP relay entryとPCP handshake
- CHAN control decodeとbounded payload受信
- backpressure、有限timeout、停止、再接続
- official Stable／developmentとの互換性検証

### Relay

- bounded fan-outとslow child isolation
- downstream relay session／server
- owned relay coordinatorと有限drain
- controlled real-media relay verification

### 再生基盤

- verified FFmpeg runtime locator／leaseとABI gate
- native decoder context、parameter transfer、codec open、packet/frame境界
- callback-scoped metadata／plane／sample view
- owned managed frame copyとcanonical conversion
- serial awaited sink delivery
- count／byte bounded decoded-frame scheduler
- A/V timing、late-item、recovery、deadline-aware run policy
- Windows D3D11／WASAPI output境界
- finite continuous pump
- diagnostic H.264／AAC path through awaited sink delivery

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- bounded FLV／MP4 demux
- diagnostic／production decoderからschedulerへのmanaged-frame ingress
- decoded-buffer duration cutoff
- complete A/V synchronization、resampling、drift correction
- precise multimedia timerとin-flight preemption
- approved FFmpeg backend activation
- FFmpeg runtime同梱／再配布
- controlled audible／visible real-media playback
- GUI、window message loop、installer、recorder

FFmpeg backend activation、runtime bundling／distribution、license approvalは、ADRで明示的に承認されるまで未承認です。

## 次のマイルストーン

**0.0.8r — Diagnostic Managed Frame Scheduler Ingress Transaction**

0.0.8mで所有したmanaged frameを既存schedulerへ診断専用の固定順序で渡し、sequence、discontinuity、enqueue result、部分成功時のownershipを固定します。二streamのatomic enqueueは主張しません。

## 必要環境

- .NET SDK 10
- Windows 11はWindows output境界の対象
- Linux／Windows CI

## ビルドとテスト

```powershell
dotnet restore PeerCast-Nova.sln
dotnet build PeerCast-Nova.sln -c Release --no-restore
dotnet test PeerCast-Nova.sln -c Release --no-build --no-restore
dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore
```

## 設計原則

- 1回の公開操作を有限化する
- resident loopや無限retryを暗黙に導入しない
- countだけでなくresource量もboundする
- ownership transferとcleanupを明示する
- 成功した処理だけをconsume／commitする
- undefined enumや不正値はfail closed
- reset途中の失敗を成功として隠さない
- native pointer、handle、HRESULTをpublic contractへ漏らさない
- 実装していない機能を完了扱いしない
- 第三者assetはprovenanceとredistribution permissionを記録する

## ライセンス

PeerCast Novaは[Apache License 2.0](LICENSE)で公開されています。
