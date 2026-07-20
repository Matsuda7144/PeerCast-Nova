# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.9k — Source-bound One-frame Receive and EOS Drain Boundary**

exact FLV source sessionとopened native decoder sessionを1世代として所有し、成功したnative frameを失わず1つのmanaged decoded completionへ移す有限boundaryを実装しました。

- 1 public receive operationは最大1回のcallback-scoped native receive／managed copy
- state-only receive後のsecond receiveを廃し、同じcallback内でframe ownershipを確立
- decoded completionはexact source、decoder／discontinuity generation、stream、media、independent sequence、managed bytesを保持
- generation全体でactive decoded completionは最大1件
- ordinary、pending packet／flush relief、accepted-flush drainを固定modeで分離
- retained packet／flush retryはproduced relief frame completion解放後の別操作で最大1回
- repeated `EAGAIN`は次の1-frame receiveと次の1-retryを要求
- ordinary `NeedMoreInput`は正常な有限終了
- relief／flush drain中の`NeedMoreInput`はno-progress failure
- accepted video／audio flushを独立して1回1frameでdecoder EOFまでdrain
- per-frame limitとreusable frame-count／managed-byte budgetを実装
- cancellation、undefined／contradictory result、frame identity、native failureをfail closed
- active decoded frame→submission→native audio→videoのcleanup順
- second decoded queue、resident loop、production activation、scheduler／output wiringなし

診断専用のmanaged conversion、bounded scheduler、late-aware dispatch、Windows output境界は引き続きproduction decoder factoryから独立しています。FFmpeg backend activationやruntime配布も未承認のままです。

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
- bounded decode iteration and independent EOS drain review
- source-bound one-frame managed receive and EOS drain boundary

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- 0.0.9k boundaryとawaited PCP-to-FLV generationのcomposition
- real FLV decoded completionのconversion／scheduler ingress／delivery
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

**0.0.9l — Awaited Decoded Completion Composition Review**

0.0.9iのawaited PCP-to-FLV composition generationを0.0.9kのsource-bound receive authorityへ接続する設計をレビューします。callback内のretained-send relief、callback外のordinary receive／flush drain、decoded completion consumer backpressure、既存conversion／bounded schedulerへの所有権移譲、shared budget、cancellation、discontinuity replacement、cleanup順を確定し、runtime composition実装は次のmilestoneへ分離します。

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
