# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.9u — Finite Production Generation Ingress and Replacement Composition Boundary**

0.0.9tで確定した契約をruntimeへ実装し、awaited PCP／FLV input、source-bound decode、one-shot scheduler transfer、caller-driven delivery、explicit reset、stop-old-before-parser-reset replacementを一つのproduction専用有限境界へ統合しました。

- 既存`PlaybackPcpFlvAwaitedDecodeCompositionBoundary`は診断・比較用として変更しない
- production専用`PlaybackProductionPcpFlvCompositionBoundary`を追加
- parser、generation creator、最大1個のcomplete generationを一つのownerへ統合
- Windows output buffer boundaryはapplication ownerからborrow
- decode snapshot、one-event submission、one-frame receiveだけをassembly-internalにforward
- raw decode、native context、ABI gate、runtime lease、Windows resourceを公開しない
- packet、finalization、receive／drain、transfer、delivery、reset、replacement、stopを一つのoperation gateで直列化
- decoded completionは事前検証後に一度だけtransfer
- `Transferred`後はcompletionへ再アクセス／disposeしない
- deliveryはPCP callback外から一項目ずつcaller-drivenで実行
- retained output状態はscheduler reservationを保持
- scheduler admission rejectionはterminalで、capacity polling／hidden retry／second queueなし
- discontinuityはold generationの`StopAsync`完了後だけparser resetへ進む
- old／new overlap、old revival、rollback、partial packet commitなし
- asynchronous stopはgeneration→parser／source→factoryの順にcleanup
- pinned FFmpeg candidateは引き続き`LicenseReview.Pending`

public default compositionはstream description後のgeneration作成時に`LicenseReviewPending`でfail closedします。通常backend登録、FFmpeg配布、Windows device activation、audible／visible playbackはまだ実施していません。

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
- production generation ingress／replacement composition architecture review
- finite production generation ingress／replacement composition boundary

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- FFmpeg candidate license approval and normal backend registration
- FFmpeg runtime distribution decision
- production decoder ingress activation under approved provenance
- Windows application／window／device lifecycle composition
- MP4／fragmented MP4 parser
- decoded-buffer duration cutoff
- discontinuity-time native reset semantics
- complete A/V synchronization、resampling、drift correction
- controlled audible／visible real-media playback
- GUI、window message loop、installer、recorder

FFmpeg backend activation、runtime bundling／distribution、license approvalは、ADRで明示的に承認されるまで未承認です。

## 次のマイルストーン

**0.0.9v — FFmpeg Candidate License, Backend Registration, and Distribution Review**

現在`LicenseReview.Pending`のpinned external FFmpeg candidateについて、正確なbuild provenance、LGPL／optional component条件、dynamic linking、source／notice提供、host-installed運用、通常backend登録、bundling／redistributionの可否をレビューします。法的・配布判断をarchitectureと分離して記録し、承認されない経路は引き続きfail closedに保ちます。

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
