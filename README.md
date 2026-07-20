# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.8v — Pure Managed Bounded Demux Output Contract**

診断専用のH.264／AAC経路は、native context openからowned managed frame copy、既存bounded schedulerへの有限ingress、late-aware timed dispatchの有限transactionまで到達しています。本番decoder factoryからは独立しており、FFmpeg backend activationや配布承認を変更していません。

0.0.8vでは、real container parserより先に必要なparser-neutral input contractをpure managedで実装しました。

- H.264 video 1本＋AAC audio 1本のimmutable owned stream description
- stable distinct stream id、bounded codec data、video／audio configuration、positive rational time base
- FLV／MP4／fragmented MP4のfixed container enum
- parser orderを保つfinite packet／EOS event
- separate known／unknown PTSとDTS、negative timestamp preservation、optional duration
- video keyframe／non-keyframe／unknown、audio NotApplicable
- per-stream monotonic sequenceとindependent EOS state
- input、packet、codec data、packet count、aggregate bytes、skipped records、timestamp magnitudeのhard bounds
- checked rational timestamp converter
- active completionをdisposeするまで次readを拒否するcaller-driven backpressure
- packet payloadを先にclearしてからaudio／video codec dataをclearするidempotent cleanup

0.0.8vはcontainer bytesを解析せず、stream、parser、decoder、scheduler、native lease、deviceを所有しません。FLV／MP4 parser、decoder packet submission、decode loop、production activationは後続milestoneです。

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

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- first concrete bounded container parser
- bounded FLV／MP4 parser
- parser-to-codec-parameter composition
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

0.0.8v contractの上に最初のconcrete bounded container parserを置く前に、FLVとMP4／fragmented MP4のどちらを先に実装するかarchitecture reviewで決定します。

PeerCast／HTTP relayで必要なnon-seekable input、codec configuration、PTS／DTS、keyframe、EOS、finite observation boundsを比較し、production activationやreal-media playbackの承認とは分離します。

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
