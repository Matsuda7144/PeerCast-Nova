# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.9p — Generation-matched Scheduler Delivery and Output Lifecycle Review**

0.0.9oでschedulerへ移譲したreal decoded frameを、同じdecoder generationのcontextとoutput lifecycleへ安全に接続するownership契約を確定しました。runtime実装は0.0.9qへ分離しています。

- production deliveryはdestructiveな`RunOneAsync`ではなくscheduler reservation経路へ一本化
- 一回のcaller-driven delivery operationでreserve→generation検証→context選択→timing／reset admission→conversion→output→reservation completion
- video frameは同じnative sessionのvideo context、audio frameは同じsessionのaudio contextだけを使用
- source-bound native sessionを唯一のcontext ownerとし、二重のcontext lifetime ownershipを禁止
- result-aware canonical output seamで`WouldBlock`、initialization／binding／resize、device loss、recoveryを表現
- reservation completionを`Delivered / Dropped / Retained`へ分離し、明示dropをdeliveryとして数えない
- replacement generationの最初の`DecoderReset`を成功したreset admission後に一度だけacknowledge
- cancellationやretryable outputではreservationとreset markerを維持
- active deliveryをcancel／awaitしてからscheduler frame、decode boundary、native contextをcleanup
- Windows activation、binding、stream initialization、COM／OS handle ownershipはapplication owner側に維持
- second decoded queue、resident pump、hidden retry、二重output pathは追加しない

診断専用のawaited sink delivery、managed conversion、late-aware dispatch、Windows output境界は既に存在します。0.0.9pではそれらをそのまま混在させず、generation-matchedなreservation deliveryへ統合する契約を選択しました。production generation composite、backend activation、runtime配布、audible／visible playbackはまだ未実装です。

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
- generation-matched scheduler delivery／output lifecycle architecture review

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- finite generation-matched scheduler delivery and output lifecycle implementation
- production generation factory and decoder ingress activation
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

**0.0.9q — Finite Generation-matched Scheduler Delivery and Output Lifecycle Boundary**

exact source-bound decode generation、0.0.9o scheduler ingress、one-item reservation、matching FFmpeg context conversion、result-aware canonical output、one-shot `DecoderReset` admission、`Delivered / Dropped / Retained` completion、async cleanupを一つの有限generation boundaryへ実装します。Windows device activationとproduction factoryは後続へ残します。

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
