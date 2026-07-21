# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.9w — External LGPL Runtime Compliance and User Replacement Boundary**

0.0.9vで確定した外部LGPL runtime方針を実装し、reviewed built-in candidateと利用者が明示承認するcompatible modified buildを、同じfail-closed runtime境界へ接続しました。

- immutableなbuilt-in／host-supplied runtime manifestを実装
- accepted licenseは`LGPL-3.0-or-later`だけ
- `avcodec-62.dll`、`avformat-62.dll`、`avutil-60.dll`、`swresample-6.dll`、`swscale-9.dll`を固定role／filename／ABI majorで検証
- 各DLLのpositive lengthとSHA-256を必須化
- GPL、nonfree、LGPL-2.1、不明license、重複／欠落role、rename、path traversal、wrong ABI、invalid digestを拒否
- host manifestによるmodified LGPL buildの差し替えを許可しつつchecksum検証を維持
- runtimeは一つのexplicit directoryからだけ取得
- 既存のretained handle、file inventory、PE dependency、ABI、required-symbol gateを再利用
- production entryはgeneric backend factoryではなく`PlaybackFfmpegExternalRuntimeComposition`
- pinned BtbN candidateを`LicenseReview.Approved`へ変更
- GPLv3／LGPLv3本文、FFmpeg notice、runtime replacement手順、text hashを追加
- `Nova.Cli`のbuild／publish outputへlegal directory全体をコピー
- FFmpeg binaryのbundling／mirror／rehost／download／extract／install／updateは引き続き禁止
- runtime discovery、fallback、resident loop、second queue、Windows resource ownershipは追加しない

これでlicense reviewによるproduction fail-closed状態は解除されました。ただしWindows application／window／device lifecycleを所有して実際のoutput環境へ接続する作業と、controlled real-media decode／output validationはまだ完了していません。

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
- FFmpeg candidate license／registration／distribution architecture review
- external-only LGPL runtime compliance policy
- built-in／host-supplied FFmpeg runtime manifest
- checksum-preserving LGPL user replacement boundary
- approved external FFmpeg application-composition-root entry
- GPLv3／LGPLv3 notice and license packaging

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)
- [FFmpeg external runtime compliance policy](docs/legal/FFMPEG-EXTERNAL-RUNTIME-COMPLIANCE.md)
- [FFmpeg distribution legal material](THIRD-PARTY-LICENSES/FFmpeg)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- approved external runtimeとapplication-owned Windows lifecycleの接続
- Windows application／window／adapter／COM／device lifecycle composition
- controlled retained-fixture production activation validation
- controlled real-media decode and output validation
- legal／about surfaceからのFFmpeg notice表示
- MP4／fragmented MP4 parser
- decoded-buffer duration cutoff
- discontinuity-time native reset semantics
- complete A/V synchronization、resampling、drift correction
- controlled audible／visible real-media playback
- GUI、window message loop、installer、recorder

FFmpeg binary bundling／redistributionは既定方針として禁止です。別runtime packを将来検討する場合は、新しいADRとexact corresponding source／build／notice compliance archiveが必要です。

## 次のマイルストーン

**0.0.9x — Approved External Runtime and Windows Lifecycle Activation Review**

承認済みbuilt-in／host manifestと一つのexplicit runtime directoryを、application-owned Windows adapter selection、COM／window activation、resource binding、stream initialization、buffer processingへ接続する順序をレビューします。production compositionがWindows resourceを借用する境界、stop／reset／device loss／discontinuity replacementのownership、controlled Windows validation計画を固定し、実装前に二重所有・隠れたpump・fallback経路を排除します。

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
- LGPL replacement freedomとruntime integrityを明示manifestで両立する
- Windows output resourceはapplication lifecycleが所有しproduction decode pathは借用する

## ライセンス

PeerCast Novaは[Apache License 2.0](LICENSE)で公開されています。FFmpegはNovaのApache License 2.0には含まれず、外部runtimeとして別のlicense termsに従います。