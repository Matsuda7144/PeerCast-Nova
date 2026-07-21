# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.9s — Policy-gated Production Decoder Generation Factory Boundary**

0.0.9rで確定した契約をruntimeへ実装し、policyからexact FLV binding、独立したvideo／audio runtime・ABI gate、source-bound native session、decode、bounded scheduler、timing、0.0.9q deliveryまでを一つのcomplete generation productへ構築する有限factoryを追加しました。

- candidate provenanceとbackend policyをbinding／filesystem observation／native workより先に評価
- pinned FFmpeg candidateは引き続き`LicenseReview.Pending`
- public production factoryは現在`LicenseReviewPending`を返し、runtime pathへ触れない
- approvedな注入条件でもthird-party／native-code policyの許可を必須化
- callerが明示した固定ドライブ上のexact pinned inventoryだけを使用
- video／audioへ独立した2本のruntime→ABI gate→context ownership chainを構築
- shared runtime／shared gateを参照同一性で拒否
- FLV binding→native session→decode→fresh scheduler→timing→0.0.9q deliveryを順序固定
- partial productを返さず、失敗時は未移譲ownershipだけを逆順cleanup
- source sessionとWindows output buffer boundaryはapplication ownerからborrow
- returned generationはfinite transfer／one-item delivery／reset／stopを公開
- factory disposalはactive creationをcancel／awaitし、completed generationはdisposeしない
- fallback runtime、download、bundling、resident loop、hidden retry、second queueなし

現在の法的状態ではdefault production operationsがFFmpeg native resourceを取得することはありません。通常backend登録、production PCP ingress composition、generation replacement orchestration、Windows device activation、runtime配布、audible／visible playbackはまだ実施していません。

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
- policy-gated production decoder generation factory boundary

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- production generation ingress and replacement composition
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

**0.0.9t — Production Generation Ingress and Replacement Composition Review**

0.0.9s factory productを既存のawaited PCP／FLV compositionへどう注入し、decoded completionをschedulerへtransferし、caller-driven finite deliveryを実行するかをレビューします。old generation停止、source／parser discontinuity、new generation作成、失敗時のterminal state、application-owned Windows lifecycleとの境界を確定し、runtime composition実装は後続へ分離します。

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
