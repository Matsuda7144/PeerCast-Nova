# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.9a — Timestamp-aware Packet Submission Architecture Review**

診断専用のH.264／AAC経路は、native context openからowned managed frame copy、既存bounded schedulerへの有限ingress、late-aware timed dispatchの有限transactionまで到達しています。本番decoder factoryからは独立しており、FFmpeg backend activationや配布承認を変更していません。

0.0.8xでbounded FLV parser、0.0.8zでexact source-session identityを持つdecoder-parameter bindingを実装しました。0.0.9aでは、real FLV eventをopened decoderへ渡すためのownershipとABIの順序を決定しました。

- packet submissionより先にsource-bound opened decoder sessionを作る
- old diagnostic native leaseはsource identityを持たないためreal FLV pathへ使わない
- 0.0.9bでvideo contextをcreate／configure／openした後にaudioを同じ順序で処理
- video／audio contextsはdistinct gate／runtime ownership chainsを持つ
- parameter bindingと両gate leasesは全resultでconsume
- partial configured／opened stateはsuccessとして返さない
- current packet boundaryはpayload-onlyでPTS／DTS／duration／time base／keyframe／sequenceを保持できない
- AVPacket field writeは0.0.9c ABI reviewへ分離
- `NeedFrameDrain`中はactive parser completionを唯一のpending encoded ownerとして保持
- EOSはempty packetではなくexplicit null-packet flushとbounded drain
- second encoded queue、hidden retry loop、resident decoder loopを作らない

0.0.9aはarchitecture reviewであり、runtime source、tests、native ABI、packet submission、PCP ingress、backend activationを変更していません。

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
- diagnostic managed-frame scheduler ingress
- finite diagnostic late-aware scheduler dispatch
- A/V timing、late-item、recovery、deadline-aware run policy
- Windows D3D11／WASAPI output境界
- finite continuous pump
- bounded diagnostic fixture and adapter
- pure managed bounded demux output contract
- pure managed bounded FLV v1 parser
- FLV session decoder-parameter binding
- timestamp-aware packet submission architecture review

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- source-bound opened native decoder session
- AVPacket metadata／flush ABI review and implementation
- finite one-event timestamp-aware submission
- controlled retained FLV fixture validation
- PCP-to-parser composition
- MP4／fragmented MP4 parser
- production decoder ingress
- decoded-buffer duration cutoff
- continuous decode、flush、EOS drain
- complete A/V synchronization、resampling、drift correction
- precise multimedia timerとin-flight preemption
- approved FFmpeg backend activation
- FFmpeg runtime同梱／再配布
- controlled audible／visible real-media playback
- GUI、window message loop、installer、recorder

FFmpeg backend activation、runtime bundling／distribution、license approvalは、ADRで明示的に承認されるまで未承認です。

## 次のマイルストーン

**0.0.9b — Source-bound Opened Native Decoder Session**

liveな0.0.8z bindingとdistinct video／audio ABI gate leasesをconsumeし、exact source-session reference、stream IDs、time basesと、configured＋opened状態のH.264／AAC contextsを1個のowned sessionへまとめます。

routine Ubuntu CIではinjected operationsを使い、ordering、partial failure、source disposal、gate／runtime ownership、audio→video cleanupを検証します。

## 必要環境

- .NET SDK 10
- Windows 11はWindows output境界の対象
- routine CIはUbuntu

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
