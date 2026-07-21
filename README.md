# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.9z — Controlled Retained-Fixture Production Activation Validation**

0.0.9fのcontrolled FLV fixtureを0.0.9y finite application lifecycleへ接続し、real bounded parser、real production composition、source-bound decode、decoded-completion transfer、retained scheduler ownership、generation-matched output admissionを一つのcross-platform integration harnessで検証しました。

- controlled real FLV bootstrapからexact parser source sessionとdecoder generation 0を作成
- production snapshotから320×240 videoと48 kHz stereo audio構成を導出
- lifecycle outputをactive decoder generation 0へ構成して`Ready`へ移行
- interleaved video／audio packetでaudio submit `EAGAIN`を保持
- one receive → one managed audio frame → one scheduler transfer → one retained retry
- second encoded／decoded queue、hidden loop、automatic retryなし
- first deliveryの`RetainedWouldBlock`でscheduler ownershipを維持
- caller-driven second deliveryで`AudioDelivered`をcommit
- discontinuity bootstrapでgeneration 0 stop完了後にgeneration 1を作成
- output-ready generationを0から1へreconfigureして再び`Ready`
- normal stopはproduction-firstで、borrowed dispatcherとwindow tokenを維持
- physical Windows activation、real FFmpeg loading、real native decodeは別gateとして未検証
- routine CircleCIでrestore、Release build with analyzers、full tests、format verification成功

このmilestoneはcomplete managed production seamのcontrolled validationです。physical COM／D3D11／WASAPI、実FFmpeg runtime、application window／message loop、audible／visible playbackはまだ検証していません。

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
- controlled retained-fixture production lifecycle integration validation

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

- controlled real external-runtime decode activation review
- controlled real FFmpeg native decode validation
- controlled Windows video／audio output validation
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

**0.0.10a — Controlled Real External-Runtime Decode Activation Review**

exact approved built-in runtimeをcontrolled Windows環境で実際にactivateする前に、fixture provenance、runtime-directory custody、Windows runner、native decode evidence、cleanup、artifact非配布、failure evidenceをレビューします。physical video rendering／audio outputは後続gateとして分離します。

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
- controlled fixture validationをphysical native activationの証拠へ読み替えない

## ライセンス

PeerCast Novaは[Apache License 2.0](LICENSE)で公開されています。FFmpegはNovaのApache License 2.0には含まれず、外部runtimeとして別のlicense termsに従います。built-in candidateのNova reviewはexact identityだけに限定され、host-supplied runtimeはhost declaration／attestationでありNova approvalではありません。
