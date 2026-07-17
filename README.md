# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、長年親しまれてきた PeerCast の思想を受け継ぎながら、
現代の技術で再設計するオープンソースの P2P ライブ配信プラットフォームです。

## 現在の段階

**Milestone 0.0.6g — Native Parameter Transfer Boundary**

0.0.6g transfers the owned H.264 or AAC parameters into the existing native
decoder context through a fixed Windows x64 boundary. It extends the verified
symbol gate, creates temporary `AVCodecParameters`, copies zero-padded
extradata, and fails closed on codec mismatch, missing symbols, rejected
transfer, or native failure. `avcodec_open2`, packet or frame processing,
demux, conversion, and FFmpeg redistribution remain out of scope.

既存PeerCastStationとの接続を確認する互換プローブを実装しました。

- `GET /channel/{channel-id}`のHTTPリレー要求
- `x-peercast-pcp`
- `x-peercast-pos`
- HTTP 200／503の処理
- HELO送信
- OLEH解析
- OK／QUIT判定
- NUL埋めされた短いAtom名
- PCP形式のGUID、文字列、整数、IPv4値
- 同一PipeReaderでHTTPとPCPを連続解析
- 管理下の疑似Peerとの統合テスト
- 実際のPeerCastStation向けCLIプローブ

0.0.2c Compatibility Probeは、公式Stable 4.1.0と公式master
development 7.1.1.0の両方で接続に成功し、Verifiedになりました。

0.0.3aではChannel IDとローカルSession IDを分離し、再接続を含む
明示的な状態遷移、メタデータ／ストリーム位置／revisionを持つ不変Snapshotを
Coreへ追加しました。メディアpayload、socket、Pipe、queueはCoreを通りません。
アプリケーション終了時はモジュールごとに有限の停止期限を適用し、1つが
停止しなくても後続モジュールのbest-effort停止を継続します。

0.0.3bでは管理下の実機で値を保存しないCHAN構造観測を行い、
`id`、`pkt`、`info`、`trck`のbounded control decoderを実装しました。
`pkt/data`は長さだけを扱い、control modelへpayloadをコピーしません。
未知fieldは無視し、既知fieldの重複、不正UTF-8、埋込みNUL、上限超過は
明示的に拒否します。

0.0.3cではProtocolのpayload-free CHAN modelをNetwork境界でCoreの
不変Snapshotへ反映します。部分メタデータは欠けた項目を保持し、空文字は
明示的な更新として扱います。メタデータとpacket位置は1つのrevisionで公開し、
32-bit位置の周回を単調な64-bit位置へ展開します。曖昧な大幅移動や後退は
暗黙に位置を戻さず、明示的なdiscontinuityとして報告します。

0.0.3dではhandshake後の共有PipeReaderからトップレベルAtomを1個ずつ処理し、
`pkt/data`を上限付きの所有バッファへ移すNetwork受信境界を追加しました。
packet consumerの非同期処理が完了するまで次のAtomをdecodeしないため、遅い
consumerへ明示的にbackpressureを伝播します。payloadはconsumer callback中だけ
有効で、Core Snapshotには入りません。取消、QUIT、位置discontinuityも同じ経路で
扱います。

0.0.3eではTCP接続、HTTP relay entry、HELO/OLEH/OK、同一PipeReaderの
post-handshake CHAN受信を1つのNetwork Sessionへ結びました。再接続回数、
handshake、idle、接続、停止は有限で、ローカルSession ID、Core位置、packet
sequenceは再接続をまたいで維持します。consumer失敗、protocol拒否、RelayFullは
暗黙に再試行しません。管理下のPeerCastStation 6.0.0.0でHTTP 200から実packet
4個の受信と停止まで確認済みです。

0.0.3fでは、Coreへ固定分類・固定段階・試行回数だけを持つprivacy-safeな
fault Snapshotを追加しました。自由文、例外、識別子、接続先、メタデータ、
payload、stream位置、protocol codeは保持しません。状態とfaultは原子的に公開し、
faultは停止後も診断用に残し、明示的な再接続開始時だけ消去します。公式Stable
4.1.0と公式development 7.1.1の隔離loopback環境でも、それぞれHTTP 200から
実packet 4個の受信と有限停止を確認しました。これにより0.0.3 Channelは
Verifiedです。

0.0.4aでは、上流packetを一度だけ有限のpool memoryへコピーし、子ごとの
packet数／byte数上限を持つrelay fan-outを追加しました。各子は独立workerで
順序どおり送信し、遅い子はqueue上限で、失敗した子はsink failureで、その子だけを
停止します。他の子と上流sessionは継続します。payload memoryは子callback中だけ
有効でCoreを通りません。停止に応答しない子も有限時間で待機を打ち切りますが、
実cleanupが終わるまでchild slotを保持するため、壊れた子の蓄積は上限を超えません。

0.0.4bでは、下流peerのHTTP relay entry、HELO／OLEH／OK、満員時の
503／OLEH／QUIT、bounded CHAN serializationを追加しました。CHANの72-byte prefixは
接続ごとに再利用し、payloadはfan-outの共有memoryから直接書くため、payload-sized copyを
増やしません。handshakeとwriteは有限時間で、remote QUIT、fan-out停止、slow child、
write failureを固定形式で終了します。停止に応答しないwriterは結果にcleanup未完了を示し、
実終了までbounded child slotを保持します。

公式Stable 4.1.0とdevelopment 7.1.1の両方がNovaへ下流接続し、それぞれ4 CHAN packet／
16 byteを0 discardで受信し、cleanupまで完了しました。0.0.4bのdownstream wire/sessionは
Verifiedです。

0.0.4cでは、bounded TCP listenerと複数のdownstream child sessionを所有する
`PcpDownstreamRelayServer`を追加しました。待機中accept、HTTP待機中child、通常childを
同じ停止要求で有限に終了し、全childのcleanupを待ってから完了します。TCP接続数と
fan-out child数は別々に有限化し、1 childの失敗は他のchildとaccept loopへ伝播しません。
診断Snapshotは固定状態と集計値だけを持ち、識別子、接続先、例外、metadata、payload、
位置、peer由来文字列を保持しません。

公式Stable 4.1.0とdevelopment 7.1.1の両方がこのserverへ接続し、それぞれ
1 handshake、4 CHAN packet／16 byte、0 discard、cleanup未完了0、内部失敗0を確認しました。
0.0.4cのaccept coordinatorはVerifiedです。

0.0.4dでは、上流Channel Session、bounded fan-out、下流relay serverを所有する
`PcpRelayCoordinator`を追加しました。自然な上流終了では新規packetを止めてから、
in-flightを含む全child queueが空になるまで有限時間drainし、その後に下流serverと
fan-outを順番に終了します。明示停止とcaller cancellationはdrainを省略して速やかに
両network境界を停止します。再接続時も同じfan-outと下流childを維持し、次の上流要求は
Coreが保持する最後の位置から再開します。

synthetic upstream wireからNovaを通して、公式Stable 4.1.0へ18 packet／72 byte、
development 7.1.1へ19 packet／76 byteを連続配信しました。両方とも0 discard、
cleanup未完了0、内部失敗0です。0.0.4d coordinatorはVerifiedです。ただし、公式上流から
実在メディアチャンネルをNova経由で下流へ通す試験は残っているため、0.0.4 Relay全体は
まだ完了していません。未観測のpacket typeやbootstrap値は推測しません。

0.0.4eでは、上流sessionが途中で例外終了してterminal resultを返せない場合でも、
fan-outが受理したpacket数／payload byte数を保持するcoordinator-owned ingress集計を
追加しました。完全なpacketを1件中継した直後に不完全Atomで上流を失敗させる統合試験で、
上流resultがnullでもingress 1 packet／2 byteと下流delivery 1 packet／2 byteが一致し、
有限drainとcleanupが完了することを確認しています。集計は固定数値だけで、識別子、接続先、
metadata、payload、位置、例外、peer由来文字列を保持しません。

0.0.4fでは、公式upstreamからNovaを経由してdownstream peerへ通す実機試験を
安全に行うため、loopback限定・最長10分・有限queueのCLI relay診断を追加しました。
合否はingress／delivery packet・byte数、accepted handshake、discard、cleanup、
internal failure、shutdown完了だけで判定し、channel IDや内容は表示しません。
隔離loopback試験では46 packet／184 byteを受理し、下流へ4 packet／16 byteを配信、
discard 0、cleanup未完了0、内部失敗0、終了コード0を確認しました。

0.0.4gでは、このrelay診断と下流Channel Session Probeを1コマンドで安全に同期する
End-to-End gateを追加しました。待受前の下流接続、使用中portへの誤接続、片側だけの
成功を拒否し、所有するbackground jobだけを有限時間で後片付けします。隔離試験では
成功時exit 0、上流不在時exit 1、使用中portの接続前拒否、Channel ID非表示、listener
残留なしを確認しました。

0.0.4hでは、Coordinatorがlistener bindの結果を`Ready`／`Stopped`／`Failed`の固定値で
公開するreadiness契約を追加しました。CLIはbind成功後だけReadyを表示し、1コマンドgateは
その出力を有限時間で待ってから下流を起動します。OSのTCP一覧取得は不要になり、使用中port
ではbind失敗後に下流を接続しないことを確認しています。readiness待ちの取消はCoordinatorを
停止せず、所有権とbackpressure／cleanup境界は変えていません。

0.0.4最終検証では、公式PeerCastStation 6.0.0.0のHTTP 200 responseに含まれる未知の
non-ASCII field valueを、response値だけのbounded legacy octetとして受理しました。
request、status line、header名、制御文字の厳格検証は維持しています。管理下の実在メディア
チャンネルを60秒中継し、1,338 packet／17,381,199 byteを受理、下流へ4 packet／
51,633 byteを配信しました。discard、cleanup未完了、内部失敗は0で、両方のacceptance
gateと終了コード0を確認しました。Channel ID、metadata値、payload内容、位置は記録して
いません。これによりMilestone 0.0.4 RelayはVerifiedです。

0.0.5aでは`Nova.Playback`を独立プロジェクトとして追加し、Networkからの
callback-scoped packetをplayer向けの有限owned memoryへ移す境界を実装しました。
packet数とbyte数を別々に制限し、満杯時は最も古い待機packetを捨てて最初の保持packetへ
`LatencySkip`を明示します。上流位置gapは`Source`として分離し、payload lease、
graceful drain、即時stop、貸出中memoryの固定counterを備えます。Core、UI、diagnosticへ
payloadを渡しません。これはplayer入力のFoundationであり、media format判定、decoder、
audio/video出力、壁時計latency保証はまだ実装していません。

0.0.5bでは、Playbackが所有するmedia prefixだけを最大64 KiB（設定上限1 MiB）で
一時観測し、FLV、Ogg、MPEG-TS、ASF、WebM、Matroskaを固定enumへ分類する
`PlaybackMediaClassifier`を追加しました。discontinuity、上限到達、入力完了、認識、
破棄の各経路でprefixを消去し、Coreや通常の診断へ内容を渡しません。管理下の
PeerCastStation 6.0.0.0に対する有限Probeでは、HTTP 200から1 packetでFlashVideoを
認識し、skip 0、未解放0、正常停止、classification gate合格を確認しました。
これはcontainer判定のVerifiedであり、codec判定、decoder、audio/video出力は未実装です。

0.0.5cでは、実decoderをまだ導入せず、同期factory、one-shot decoder runner、
最大64 KiBのbootstrap再生、通常packetの追加copyなしのlease handoff、
source／latency discontinuity時の`ResetAsync`、停止期限、取消を無視するcallbackの
遅延cleanup契約を追加しました。Release build、全214 test、13件×20回の反復、
format検証を通過し、decoder lifecycleはVerifiedです。

0.0.5dでは、管理下の実在メディア入力をChannel Sessionから分類、decoder runner、
CLI内部の協調的counting decoderまで通す有限Probeを追加しました。全233 test、
13件×20回の反復、format検証に加え、PeerCastStationからHTTP 200でFlashVideoを認識し、
runner／decoderの入力数とbyte数一致、factory生成1、dispose 1、skip 0、未解放lease 0、
deferred cleanupなし、正常停止、`Lifecycle Gate: Passed`を確認しました。実codec、
decoded frame、audio/video出力はまだ含みません。

0.0.5eでは、decoderが出力するcanonical frame契約を追加しました。音声は
Signed16／Float32 interleaved PCM、映像はpacked BGRA32とし、sequence、timestamp、
duration、decoder reset discontinuity、rate／dimension／strideだけを固定metadataとして
渡します。payload memoryはawaitされるsink callback中だけ有効で、保持するsinkはcallback
完了前にcopyします。sinkの待機はdecoderへbackpressureとして伝播します。実decoder、
decoded queue、renderer、audio device、clockはまだ含みません。

## 重要な修正

PCPのAtom名は必ず4文字ではありません。

```text
sid -> 73 69 64 00
ok  -> 6F 6B 00 00
```

1～4文字をNULで4バイトまで埋める仕様として修正しました。

## ビルドとテスト

.NET 10 SDKを導入したPowerShellから、リポジトリの場所に関係なく実行できます。

```powershell
& "C:\path\to\PeerCast-Nova\build.ps1"
```

このスクリプトはrestore、Release build、全テスト、Transport／PCP Framingの
セルフテストを順番に実行し、途中で失敗した場合は後続処理を開始しません。
また、`LIB`環境変数に存在しないディレクトリが含まれている場合は、
現在のビルドプロセスだけから除外し、終了時に元の値へ戻します。

## 実機互換プローブ

PeerCastStationでテスト用チャンネルを受信または配信した状態で実行します。

```powershell
dotnet run --project src/Nova.Cli -- `
  --pcp-compatibility-probe `
  --host 127.0.0.1 `
  --port 7144 `
  --channel 0123456789ABCDEF0123456789ABCDEF `
  --trace-atoms
```

PowerShell補助スクリプト:

```powershell
tools/Test-PeerCastStationCompatibility.ps1 `
  -ChannelId 0123456789ABCDEF0123456789ABCDEF `
  -TraceAtoms
```

値を表示しない有限CHAN構造観測は、管理下のテストチャンネルに限って
明示的に有効化します。識別子とIPは自動的に伏字になります。

```powershell
tools/Test-PeerCastStationCompatibility.ps1 `
  -ChannelId 0123456789ABCDEF0123456789ABCDEF `
  -ObservePostHandshake `
  -ObserveAtoms 4 `
  -ObserveTimeoutMilliseconds 2000 `
  -ObserveMaxAtomBytes 262144
```

成功例:

```text
Outcome: Accepted
HTTP: 200
Received Atom: oleh
Received Atom: ok
Remote Version: 1218
Terminal Atom: ok
Terminal Code: 1
```

リレー満員の場合はHTTP 503の後もHELO/OLEHが行われ、
最終的に`RelayFull`と`quit`が表示される場合があります。

## 実機メディア署名プローブ

既知の管理下チャンネルだけを対象に、15秒・64 KiB・再接続なしで実行します。
出力は固定分類とcounterだけで、Channel ID、metadata、payload、位置を表示しません。

```powershell
dotnet run --project src/Nova.Cli -- `
  --playback-signature-probe `
  --host 127.0.0.1 `
  --port 7144 `
  --channel 0123456789ABCDEF0123456789ABCDEF `
  --playback-probe-timeout-ms 15000 `
  --playback-idle-timeout-ms 10000 `
  --playback-reconnects 0 `
  --playback-probe-max-bytes 65536 `
  --playback-probe-max-discontinuities 4
```

## 実機Decoder Lifecycleプローブ

実codecを使わず、分類済みencoded inputがdecoder lifecycleへ渡り、有限停止と
lease解放まで完了することを固定counterだけで検証します。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File tools\Test-PeerCastStationDecoderLifecycle.ps1 `
  -ChannelId 0123456789ABCDEF0123456789ABCDEF `
  -InputLimit 4
```

`Lifecycle Gate: Passed`はmedia decodeや再生成功を意味しません。Channel ID、
endpoint、metadata、payload、prefix、position、例外文は結果へ出力しません。

## 実機Channel Sessionプローブ

管理下のテストチャンネルだけで、実packetを有限個受信して停止します。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File tools\Test-PeerCastStationChannelSession.ps1 `
  -ChannelId 0123456789ABCDEF0123456789ABCDEF `
  -PacketLimit 4 `
  -TimeoutMilliseconds 10000 `
  -IdleTimeoutMilliseconds 5000 `
  -Reconnects 0
```

出力は終了理由、試行回数、HTTP状態、CHAN／packet数、payload総バイト数、
最終状態だけです。チャンネルID、番組メタデータ、peer/session ID、IPアドレス、
payload内容、stream位置は表示しません。
時間上限へ達した場合は同じ限定情報を表示し、終了コード2を返します。
terminal faultがある場合だけ、固定されたcategory、stage、attempt、reconnect回数を
追加表示します。例外メッセージや外部由来の値は表示しません。

```text
End: Stopped
Stop Trigger: PacketLimit
Attempts: 1
Reconnects: 0
HTTP: 200
Channel Messages: 4
Packets: 4
Payload Bytes: 34743
Final State: Stopped
```

## 実機Relay診断

管理下の実在メディアチャンネルだけで実行します。PeerCastStationの設定やチャンネルを
Novaから列挙・変更することはありません。待受は`127.0.0.1`固定です。

推奨手順は1コマンドです。公式PeerCastStationを上流としてNova relayを起動し、
待受開始後に下流sessionを自動接続します。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File tools\Test-PeerCastStationRelayEndToEnd.ps1 `
  -ChannelId 0123456789ABCDEF0123456789ABCDEF `
  -HostName 127.0.0.1 `
  -Port 7144 `
  -ListenPort 17145 `
  -DurationMilliseconds 60000 `
  -MinimumPackets 4
```

最後が`End-to-End Gate: Passed`で終了コード0なら、relay側の完全なacceptanceと
下流側のpacket上限到達が両方成立しています。片側の失敗、待受開始／終了待ちの
時間切れ、使用中の待受portは終了コード1になります。下流接続はNova自身が出す
`Relay State: Ready`の後だけ開始され、OSのTCP一覧を走査しません。

手動で個別出力を観察する場合だけ、従来の2端末手順を使います。1つ目のPowerShellで
Nova relayを起動します。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File tools\Test-PeerCastStationRelay.ps1 `
  -ChannelId 0123456789ABCDEF0123456789ABCDEF `
  -HostName 127.0.0.1 `
  -Port 7144 `
  -ListenPort 17145 `
  -DurationMilliseconds 60000 `
  -MinimumPackets 4
```

`Relay State: Ready`が表示されたら、2つ目のPowerShellから下流sessionを接続します。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File tools\Test-PeerCastStationChannelSession.ps1 `
  -ChannelId 0123456789ABCDEF0123456789ABCDEF `
  -HostName 127.0.0.1 `
  -Port 17145 `
  -PacketLimit 4 `
  -TimeoutMilliseconds 30000 `
  -IdleTimeoutMilliseconds 15000 `
  -Reconnects 0
```

上流側の最終行が`Acceptance Gate: Passed`、下流側が`Stop Trigger: PacketLimit`なら、
少なくとも指定packet数が公式upstreamからNovaを経由して下流へ連続到達しています。
公開ログではコマンド行に含まれるChannel IDを削除してください。

## 最初の公開目標

Version 0.1.0 “Genesis” の公開条件:

1. P2Pリレー
2. ストリーミング受信と映像・音声再生

## Project Founder

**7144**

## License

Apache License 2.0

> **Never lose the excitement of Day One.**
> **For a Better Future.**
