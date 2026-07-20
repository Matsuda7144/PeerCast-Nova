# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.8w — First Bounded Container Parser Selection Review**

診断専用のH.264／AAC経路は、native context openからowned managed frame copy、既存bounded schedulerへの有限ingress、late-aware timed dispatchの有限transactionまで到達しています。本番decoder factoryからは独立しており、FFmpeg backend activationや配布承認を変更していません。

0.0.8vでparser-neutralなbounded demux output contractを実装し、0.0.8wでは最初のconcrete parserとしてFLV v1を選択しました。

- PeerCast ingressに合うcaller-owned callback chunkを有限push
- consumed byte countと最大1個のactive 0.0.8v completion
- split recordだけを保持するone bounded carry buffer
- FLV header、tag、PreviousTagSizeのstrict validation
- media前にAVC／AAC sequence headerを要求
- script metadataはbounded skipのみでcodec configurationには使わない
- AVCDecoderConfigurationRecordとbounded SPS parsingで映像寸法を取得
- AVCC NAL length walkを行い、Annex Bへ変換しない
- FLV timestampをDTS、signed composition offsetを加えた値をPTS
- AudioSpecificConfigからAAC-LC sample rate／channel countを取得
- exact duplicate sequence headerはbounded skip、変更はfail closed
- explicit discontinuityでcarry／bootstrapをreset
- final inputでtruncationとper-stream EOSを明示
- 0.0.8v completion構築成功後にだけparser stateをcommit

MP4／fragmented MP4は、init segment、box hierarchy、track／sample／fragment stateの範囲が大きいため後続reviewへ延期しました。0.0.8wはarchitecture reviewであり、FLV parser runtime、PCP composition、decoder submission、production activationは追加していません。

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
- bounded FLV parser architecture selection

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- pure managed bounded FLV v1 parser
- controlled FLV fixture validation
- PCP-to-parser composition
- parser-to-codec-parameter composition
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

**0.0.8x — Pure Managed Bounded FLV v1 Demux Parser**

0.0.8wで固定したcaller-chunk、consumed-byte progress、bounded carry、FLV structural validation、AVC／AAC bootstrap、bounded SPS／AudioSpecificConfig解析、packet／EOS出力、discontinuity resetをpure managed runtimeとfocused testsで実装します。

PCP consumerへの接続、decoder codec-parameter transfer、packet submission、decode loop、production activation、Windows output activationは含めません。

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
