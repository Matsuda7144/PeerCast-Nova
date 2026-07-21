# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.10a — Approved FFmpeg Candidate Manifest Refresh**

`ApprovedForManifestImplementation`となったBtbN 2026-07-21 win64 LGPL shared候補へbuilt-in manifestを更新し、旧2026-07-15候補をsuperseded履歴として保持しました。archive、complete inventory、5 load-role DLL、build configuration、PE dependency、ABI／symbol条件をtext evidenceとtestsで固定しています。

- active build ID `btbn.ffmpeg.20260721.win64.lgpl-shared`
- fixed archive size／SHA-256とcomplete 223-file expanded inventory
- exact ten-file `bin` integrity inventory、load対象は5 DLLのみ
- GPL／nonfree disabled、shared enabled、static disabledをmanifest条件化
- `d2d1.dll`、`dwrite.dll`、`iphlpapi.dll`、`usp10.dll`をWindows system dependencyとして分類
- verbatim `-buildconf`／`-L` text evidenceを保持
- FFmpeg archive／EXE／DLLはGit、build、publish、release artifactへ含めない
- native load、ABI／symbol runtime execution、decoder context、packet、decodeは未実施

このmilestoneはapproved manifest implementationです。physical COM／D3D11／WASAPI、実FFmpeg runtime activation、native decode、application window／message loop、audible／visible playbackはまだ検証していません。

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
- approved 2026-07-21 FFmpeg candidate manifest and superseded-candidate history
- complete candidate inventory／buildconf／license text evidence

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

**0.0.10b — Controlled Real External-Runtime Native Decode Validation**

0.0.10aで固定したexact approved built-in runtimeとcontrolled FLV fixtureを明示Windows環境でのみ使用し、runtime verificationからH.264／AAC context、finite packet／frame、cleanupまでを証跡化します。D3D11／WASAPIとphysical audio／video outputは後続gateとして分離します。

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
