# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.8z — FLV Session Decoder-Parameter Binding**

診断専用のH.264／AAC経路は、native context openからowned managed frame copy、既存bounded schedulerへの有限ingress、late-aware timed dispatchの有限transactionまで到達しています。本番decoder factoryからは独立しており、FFmpeg backend activationや配布承認を変更していません。

0.0.8xでpure managed bounded FLV v1 parserを実装し、0.0.8yのreviewを受けて、0.0.8zではparser sessionを既存managed codec-parameter ownership境界へ接続しました。

- liveなFLV demux sessionだけを受理
- video 0 H.264／audio 1 AACとexact `1/1000` time baseを検証
- parser由来width／height／avcCからvideo parameter leaseを作成
- parser由来sample rate／channel count／AudioSpecificConfigからaudio parameter leaseを作成
- existing factoryがcodec dataを再検証し、FFmpeg padding付き独立copyを所有
- exact source-session referenceをnon-owningで保持
- stream IDsとtime basesをimmutable metadataとして保持
- video作成後にaudioを作成し、partial failureではvideoを破棄
- binding成功後にsource sessionが破棄されてもparameter copiesは維持
- bindingはparser／session／packet completionを所有しない
- audio→video順のidempotent cleanup

0.0.8zはnative runtime、context、parameter transfer、codec open、packet submission、PCP ingress、scheduler、deviceを所有しません。

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
- parser-to-decoder composition architecture review
- FLV session decoder-parameter binding

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- timestamp-aware packet submission
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

**Timestamp-aware Packet Submission Architecture Review**

0.0.8vの1 event completionと0.0.8z bindingのexact source identityを照合し、parser order、context selection、PTS／DTS／time base、keyframe、sequence、decoder backpressure、EOS flush／drainを情報損失なく扱う有限境界を決定します。

既存payload-only packet APIやvideo→audio固定transactionをそのまま再利用せず、active parser completionを必要な間だけ保持し、第2encoded-packet queueを作らない設計にします。

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
