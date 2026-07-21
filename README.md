# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.9r — Production Decoder Generation Factory and Activation Review**

0.0.9qで完成した有限decode／scheduler delivery generationを、verified FFmpeg runtime、ABI gate、FLV parameter binding、source-bound native session、timing、application-owned Windows outputへ安全に構築・置換するproduction factory契約を確定しました。runtime実装は0.0.9sへ分離しています。

- production policyはruntime path確認やnative loadより先に評価
- pinned FFmpeg candidateは現在も`LicenseReview.Pending`で、production creationはfail closed
- provenance、license review、backend registrationをこのレビューでは変更しない
- runtimeはcallerが明示した固定ドライブ上のexact pinned inventoryだけを使用
- `PATH`、registry、隣接directory、network search、download、展開、bundlingは行わない
- video／audioは独立した2本のruntime→ABI gate→context ownership chainを使用
- context disposalが親gateとruntime leaseを消費するため、runtime lease共有を禁止
- exact FLV parameter bindingからcomplete H.264／AAC native sessionを作り、partial productを返さない
- decode boundary、fresh bounded scheduler、generation timing、0.0.9q deliveryを一つのgeneration productへ統合
- source demux sessionはparser／composition ownerからborrowし、generationはdisposeしない
- Windows activation／binding／stream initialization／buffer processingはapplication ownerが保持
- replacementはold generationを完全停止・cleanupしてからnew generationを作成
- resident pump、hidden retry、fallback runtime、second queueを追加しない

現在の法的状態ではproduction factoryがnative resourceを取得することはありません。0.0.9rはownershipとactivation policyのレビューであり、FFmpeg承認、backend activation、runtime配布、Windows device activation、audible／visible playbackを実施していません。

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
- finite generation-matched scheduler delivery／output lifecycle boundary
- production generation factory／activation architecture review

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- policy-gated production generation factory implementation
- FFmpeg candidate license approval and normal backend registration
- production decoder ingress activation
- Windows application／window／device lifecycle composition
- MP4／fragmented MP4 parser
- decoded-buffer duration cutoff
- discontinuity-time native reset semantics
- complete A/V synchronization、resampling、drift correction
- FFmpeg runtime同梱／再配布
- controlled audible／visible real-media playback
- GUI、window message loop、installer、recorder

FFmpeg backend activation、runtime bundling／distribution、license approvalは、ADRで明示的に承認されるまで未承認です。

## 次のマイルストーン

**0.0.9s — Policy-gated Production Decoder Generation Factory Boundary**

license／backend policyをfilesystem observationより先に適用し、approvedな注入条件下だけでexact FLV binding、独立したvideo／audio runtime・ABI gate、source-bound native session、decode boundary、bounded scheduler、timing、0.0.9q deliveryをcomplete generation productへ構築します。production candidateの`Pending`状態、Windows outputのapplication ownership、runtime非同梱方針は維持します。

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
