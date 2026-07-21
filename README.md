# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.9q — Finite Generation-matched Scheduler Delivery and Output Lifecycle Boundary**

0.0.9pで確定したownership契約をruntimeへ実装し、scheduler-owned real decoded frameを同じsource-bound decoder generationのcontext、timing、conversion、result-aware Windows outputへ接続する有限境界を追加しました。

- exact source、decoder／discontinuity generation、scheduler sequence、media role、frame livenessをreservation中に再検証
- native video／audio context ownershipはsource-bound sessionだけに維持
- videoは同じsessionのH.264 context、audioは同じsessionのAAC contextだけを使用
- 一回のcaller-driven operationでreserve→validate→reset／timing→adapt／convert→output→completion
- scheduler reservation completionを`Retained / Delivered / Dropped`へ型付き拡張
- 旧`Complete(bool)` APIを互換維持
- 明示video dropはdeliveryではなくdropとしてaccounting
- `WouldBlock`、initialization／binding／resize、audio recovery、cancellationは同じframeを保持
- device lossはreservationを保持してreset-requiredへ遷移
- replacement generationの最初の`DecoderReset`をreset成功後に一度だけacknowledge
- active delivery中は後続completionをdetachしない
- `StopAsync`はactive deliveryをcancel／awaitしてからscheduler ownershipとdecode／native ownershipをcleanup
- second decoded queue、resident pump、hidden retry、二重context ownershipを追加しない

診断専用のawaited sink delivery、late-aware dispatch、Windows output境界と互換性を維持したまま、real decoded scheduler ownershipの有限delivery経路まで到達しました。production generation factory、ABI-gate acquisition、backend activation、device lifecycle、runtime配布、audible／visible playbackにはまだ接続していません。

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
- managed frame copy、conversion、awaited delivery
- count／byte bounded decoded-frame scheduler
- finite late-aware scheduler dispatch
- A/V timing、late-item、recovery、deadline-aware run policy
- Windows D3D11／WASAPI output境界
- bounded demux output contract
- pure managed bounded FLV v1 parser
- exact FLV session decoder-parameter binding
- source-bound opened native decoder session
- exact AVPacket metadata／flush ABI decision
- metadata-aware native packet／flush boundary
- finite one-event packet submission state machine
- controlled retained FLV fixture validation
- one-event final EOS parser façade
- finite awaited PCP-to-FLV composition boundary
- source-bound one-frame managed receive and independent EOS drain
- finite awaited PCP／FLV／decode composition boundary
- exact-active one-shot decoded completion detach
- generation-wide scheduler ingress ownership boundary
- generation-matched scheduler delivery／output lifecycle review
- finite generation-matched scheduler delivery／output lifecycle boundary

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- production generation factory and decoder ingress activation
- ABI-gate acquisition and backend policy activation
- Windows application／window／device lifecycle composition
- MP4／fragmented MP4 parser
- decoded-buffer duration cutoff
- discontinuity-time native reset semantics
- complete A/V synchronization、resampling、drift correction
- approved FFmpeg backend activation
- FFmpeg runtime同梱／再配布
- controlled audible／visible real-media playback
- GUI、window message loop、installer、recorder

FFmpeg backend activation、runtime bundling／distribution、license approvalは、ADRで明示的に承認されるまで未承認です。

## 次のマイルストーン

**0.0.9r — Production Decoder Generation Factory and Activation Review**

verified runtime locator／lease、video／audio ABI gate、FLV decoder parameter binding、source-bound native session、decode／scheduler generation、timing、Windows output lifecycleをproduction factoryがどう取得・所有・置換するかをレビューします。backend activation、license gate、failure rollback、discontinuity replacement、application-owned device resourcesの境界を確定し、runtime factory実装は後続へ分離します。

## 必要環境

- .NET SDK 10
- Windows 11はWindows output境界の対象
- routine Linux CIはCircleCI

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
