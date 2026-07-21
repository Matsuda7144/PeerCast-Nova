# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.9y — Finite Approved External Runtime and Windows Lifecycle Activation Boundary**

0.0.9wで承認したexact built-in external LGPL FFmpeg runtimeまたは明示的にhostがattestするunreviewed replacement routeを、0.0.6t〜0.0.6xのWindows 11 D3D11／WASAPI output境界へ接続する有限application lifecycleを実装しました。

- application-owned output dispatcher／owner threadとpositive opaque window tokenを借用
- playback側ではwindow、dispatcher thread、queue、message pump、timer、resident loopを作成しない
- manifest／backend policy／runtime-directory syntax／window token／canonical formatsをnative access前に検証
- resource-free probe後、activation／binding／stream／bufferの全native operationをdispatcher経由に限定
- exact parser stream descriptionとdecoder generation取得後にvideo bind → audio bind → video initialize → audio initialize
- binding／stream／buffer generationがactive decoder generationと一致するまでdeliveryを拒否
- 0.0.9q generation-matched scheduler deliveryを唯一のproduction scheduler-output ownerとして維持
- async video／audio processingとgeneration resetをdispatcher-backed production pathへ接続
- discontinuity replacementで固定reset／reconfiguration orderを実行しdeviceは再作成しない
- `WouldBlock`／wait／binding／initialization／resize等はcaller-visibleでscheduler ownershipを保持
- retained resizeは明示same-generation resize後にcallerがdeliveryを再試行
- device lossはterminalで、production composition停止後にbuffer → stream → binding → activationをowner thread上で解放
- normal stop、active-operation cancellation、partial construction cleanupをidempotentに固定
- borrowed dispatcherとwindow tokenはcleanup後もapplication ownershipを維持
- cross-platform injected tests 10件とroutine CircleCI gateで検証

このmilestoneは有限lifecycle implementationです。physical Windows device、application window／message loop、controlled retained real fixture、real-media decode／output、audible／visible playbackはまだ検証していません。

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
- project-reviewed built-in／host-declared host-attested unreviewed FFmpeg runtime manifest
- checksum-preserving LGPL user replacement boundary
- built-in-approved／explicit-host-attested application-composition-root entry
- readable GPLv3／LGPLv3 notice, provenance, replacement, and release-terms guard packaging
- approved runtime／Windows lifecycle activation architecture review
- explicit application owner-thread dispatcher contract
- dispatcher-backed production output process／reset path
- finite approved-runtime Windows lifecycle activation boundary
- generation-matched initial／replacement output configuration
- explicit retained resize and terminal device-loss teardown

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

- controlled retained-fixture production activation validation
- controlled real-media decode and output validation
- Windows application window／message-loop implementation
- application-provided dispatcher implementation
- legal／about surfaceからのFFmpeg notice表示
- MP4／fragmented MP4 parser
- decoded-buffer duration cutoff
- discontinuity-time native reset semantics
- complete A/V synchronization、resampling、drift correction
- controlled audible／visible real-media playback
- GUI、installer、recorder

FFmpeg binary bundling／redistributionは既定方針として禁止です。別runtime packを将来検討する場合は、新しいADRとexact corresponding source／build／notice compliance archiveが必要です。

## 次のマイルストーン

**0.0.9z — Controlled Retained-Fixture Production Activation Validation**

保持済みcontrolled FLV fixtureと明示的なverified external runtime環境を使い、0.0.9y lifecycleのpolicy admission、runtime verification、exact stream description、production generation creation、output-configuration admissionまでを有限手順で検証します。physical Windows outputとaudible／visible playbackは後続gateとして分離します。

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
- exact built-in candidateだけをNova review approvalとし、host attestationをapprovalへ読み替えない
- Windows native resourceはowner threadで所有しproduction decode pathはdispatcher-backed portだけを借用する
- output generationはactive decoder generationと一致させる
- device lossは旧generation revivalなしのterminal lifecycle resultとして扱う

## ライセンス

PeerCast Novaは[Apache License 2.0](LICENSE)で公開されています。FFmpegはNovaのApache License 2.0には含まれず、外部runtimeとして別のlicense termsに従います。built-in candidateのNova reviewはexact identityだけに限定され、host-supplied runtimeはhost declaration／attestationでありNova approvalではありません。
