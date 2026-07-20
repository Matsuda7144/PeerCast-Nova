# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.8x — Pure Managed Bounded FLV v1 Demux Parser**

診断専用のH.264／AAC経路は、native context openからowned managed frame copy、既存bounded schedulerへの有限ingress、late-aware timed dispatchの有限transactionまで到達しています。本番decoder factoryからは独立しており、FFmpeg backend activationや配布承認を変更していません。

0.0.8vでparser-neutralなdemux output contractを作り、0.0.8wで選択した最初のreal container parserを0.0.8xでpure managed実装しました。

- caller-owned `ReadOnlyMemory<byte>` chunkを有限push
- consumed byte countと最大1個のactive 0.0.8v completion
- splitした1個のFLV recordだけを保持するbounded carry
- FLV signature／version／flags／data offset／tag／PreviousTagSizeのstrict validation
- AVC sequence headerの後にAAC sequence headerを要求し、media前にimmutable sessionを作成
- script tagはbounded skipのみ。AMF metadataは解析しない
- AVCDecoderConfigurationRecord、SPS／PPS、profile／level、NAL length sizeを検証
- bounded SPS bit parsingでinterlace／croppingを含む映像寸法を取得
- AVCC NAL length walkを行い、Annex Bへ変換しない
- FLV timestampをDTS、signed composition offsetを加えた値をPTS
- frame typeをkeyframe stateへ対応し、durationはunknownを維持
- AAC-LC AudioSpecificConfigからindexed／explicit sample rateとchannel countを取得
- exact duplicate sequence headerはbounded skip、変更はfail closed
- AVC end-of-sequenceとfinal inputをexplicit per-stream EOSへ対応
- active completion disposalをparser backpressureに利用
- explicit discontinuityでold session／carry／bootstrap／sequence／EOSをreset
- terminal malformed／codec／timestamp failuresをfault latch

0.0.8x parserはPCP ingress、decoder packet submission、scheduler、native lease、deviceを所有しません。ネットワークとdecoderへの接続は後続の明示review対象です。

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

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- parser-to-decoder composition
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

**Parser-to-Decoder Composition Architecture Review**

0.0.8x parserのimmutable stream descriptionとowned packet／EOS completionを、既存managed codec-parameter ownerとbounded native packet境界へ安全に渡す方法を決定します。

0.0.8v completion backpressure、event順序、payload lifetime、PTS／DTS、EOS、partial failure、cancellation、native lease cleanupを固定し、第2encoded-packet queue、PCP接続、production activationを同じmilestoneへ混ぜません。

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
