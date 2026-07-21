# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.9x — Approved External Runtime and Windows Lifecycle Activation Review**

0.0.9wで承認したexternal LGPL FFmpeg runtimeと、0.0.6t〜0.0.6yで分離したWindows 11 D3D11／WASAPI output境界を、application lifecycleへ接続する正確なownershipと順序をレビューしました。

- applicationがwindow、message loop、output dispatcher／owner threadを所有
- playback lifecycleはadapter probe、COM／D3D11／WASAPI activation、binding、stream、buffer、production compositionを所有
- windowはopaque positive tokenとして借用し、`Nova.Playback`では作成／破棄しない
- activation-owned native resourceへの全アクセスをapplication dispatcher経由に限定
- playback libraryはdispatcher thread、command queue、message pump、timer、resident loopを作らない
- current production deliveryは0.0.9q generation-matched ownerを継続使用
- 旧0.0.6y scheduler adapterをproduction経路へ重ねない
- exact parser stream description取得後に初回binding／stream initializationを実行
- Windows output generationはactive decoder generationと一致させる
- discontinuity replacementではdeviceを再作成せずoutput stateだけをnew decoder generationへreconfigure
- `WouldBlock`／wait／binding／initialization／resizeはscheduler itemを保持したままcaller-visible
- resizeは同一generationの明示操作、device lossは初回実装ではcomplete lifecycleのterminal result
- device loss後の自動recreate／source reconnect／generation revival／fallbackは禁止
- stopはproduction compositionを先に停止し、buffer → stream → binding → activationの順にowner threadで解放
- FFmpegは引き続きexternal、explicit-directory、checksum／ABI／symbol verified、unbundled

このmilestoneはarchitecture reviewです。Windows lifecycle runtime class、dispatcher adapter、physical device activation、controlled real-media outputはまだ実装していません。

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
- Windows D3D11／WASAPI probe、activation、binding、stream、buffer境界
- bounded demux output contract
- pure managed bounded FLV v1 parser
- exact FLV session decoder-parameter binding
- source-bound opened native decoder session
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
- policy-gated production decoder generation factory boundary
- finite production generation ingress／replacement composition boundary
- FFmpeg candidate license／registration／distribution review
- external-only LGPL runtime compliance policy
- built-in／host-supplied FFmpeg runtime manifest
- checksum-preserving LGPL user replacement boundary
- approved external FFmpeg application-composition-root entry
- GPLv3／LGPLv3 notice and license packaging
- approved runtime／Windows lifecycle activation architecture review

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

- finite approved-runtime Windows lifecycle implementation
- application-provided owner-thread dispatcher adapter
- controlled retained-fixture production activation validation
- controlled real-media decode and output validation
- Windows application window／message-loop implementation
- legal／about surfaceからのFFmpeg notice表示
- MP4／fragmented MP4 parser
- decoded-buffer duration cutoff
- discontinuity-time native reset semantics
- complete A/V synchronization、resampling、drift correction
- controlled audible／visible real-media playback
- GUI、installer、recorder

FFmpeg binary bundling／redistributionは既定方針として禁止です。別runtime packを将来検討する場合は、新しいADRとexact corresponding source／build／notice compliance archiveが必要です。

## 次のマイルストーン

**0.0.9y — Finite Approved External Runtime and Windows Lifecycle Activation Boundary**

application-owned dispatcherとwindow tokenを借用し、probe、activation、binding、stream、buffer、approved external runtime production compositionを一つの有限ownerとして実装します。exact stream description後のinitial output configuration、discontinuity generation reconfiguration、retained resize／backpressure、terminal device loss、production-first stopとowner-thread teardownをcross-platform injected testsで固定します。

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
- Windows native resourceはowner threadで所有しproduction decode pathはdispatcher-backed portだけを借用する

## ライセンス

PeerCast Novaは[Apache License 2.0](LICENSE)で公開されています。FFmpegはNovaのApache License 2.0には含まれず、外部runtimeとして別のlicense termsに従います。
