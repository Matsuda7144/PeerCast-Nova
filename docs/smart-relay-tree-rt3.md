# Smart Relay RT-3: 独立画面とHOST申告ツリー

## 取得経路と境界

既存の上流HELO/OLEH、HTTP/PCP境界、PipeReader所有権、媒体パケット処理は維持する。
上流handshakeのトップレベルHOST、受信中のincremental HOST、下流controlのHOSTを再利用する。
受信中の上流と下流controlに、**受信専用**のBCST/HOST観測を追加する。
BCSTを送信・転送・探索せず、BCST由来のHOSTは紹介・Recovery候補へ追加しない。
handshake完了前のBCST観測、CHAN等を含む汎用BCST処理は追加しない。

BCST観測は以下を全て満たす場合だけ採用する。不適合な観測を捨てても媒体処理は維持する。

- ChannelId一致、空でないfrom、自己from除外、destなし又は現行wire SessionId宛て。
- TTL 1〜32、Hops 0〜31、合計32以下、既知のgroup bits（1〜7）。
- 16KiB、300 atoms、最上位48フィールド、HOST最大8件、深さはBCST→HOST→leafのみ。
- HOST毎1024bytes・32フィールド。既知フィールドの重複・不正長・Channel不一致を拒否。
- 宣言や未知フィールドを含めてサイズを評価。未知の入れ子や別BCSTは観測対象にしない。

送信元SIDもHOSTも遠隔の申告であり、認証済みの実接続の証明とはしない。
同一SID・endpoint組の再報告は同じ観測IDを更新する。再転送しないため転送用重複cacheは作らない。
全ノードのHOSTが届く保証、自動的な全網ツリー構築、汎用BCST転送は今回の対象外。

## Owner内の有限な表示用保持

PcpPeerDirectory内で紹介用と表示用の集合を分ける。紹介ポリシーと既存のloop除外は変更しない。
表示用には、有効なChannel/SIDと単一送信先IPv4/portを持つHOSTを保持する。
private/loopbackを表示用に扱えても、それを紹介候補の許可へ転用しない。
表示用上限256件・毎秒64更新・単調時計で最終受信から120秒、追加Timer/Socketなし。
同じendpointの異なるSID、同じSIDの異なるendpointを無条件に統合しない。
Owner退役で破棄し、immutableなコピーだけを返す。Registryは既存の弱参照と世代再確認を維持する。

IP/portは新設のローカルGUI専用の読み取り値契約を通す。既存のpublicなredacted観測・status契約は変更しない。
アセンブリ境界を越えるためこの独立したデータ契約はpublicだが、remote APIには接続しない。
IPv4は数値へコピーし、可変なIPAddressや通信オブジェクトを共有しない。更新・終了などの操作能力は持たない。
Nova.AppへのInternalsVisibleToは追加せず、既存のYP Announce内部実装への分離契約を維持する。
任意のPeer文字列、秘密URL、認証情報、例外本文は追加しない。

## 関係の構築と表示

Nova / 実上流 / 実下流 / RTMP入力は従来の実観測。HOST PeerはReportedPeerで、ConnectionIdを持たない。
実接続とHOSTの結合にはwire SIDとendpointの両方を使う。endpointだけでは同一Peerと判定しない。
有効なupip/upptが一意な親endpointと対応した場合だけ、Reportedな親子関係を作る。
親不明・SID矛盾・endpoint競合・自己参照は根へ分離する。循環では申告の辺だけを除去し、実所有の辺を維持する。
実上流より先の報告された祖先、直接下流より先の遠隔Peerも、情報が届いた分だけ多段表示できる。
固定の階層数制限ではなく、有限なノード数（実接続最大64＋HOST最大256＋Nova/入力）で処理を制限する。

Modernの左ペインにリレーツリーを追加し、中央に独立画面を設ける。
既存チャンネルの選択欄・ローカル観測の更新・IP表示切替、広い標準SysTreeView32、下部詳細を配置する。
Channelsの一覧と詳細カードは維持し、以前のカード内のツリー切替ボタンは置き換える。
Classicは従来の10項目と表示を維持する。Modernのみ行間を調整し最小サイズにも全項目を収める。

役割・実接続/申告・状態・経路・個別書込み完了速度・取得元・最終観測を区別する。
HOST numlは申告された直接視聴数、numrは申告されたリレー数。Novaの実下流数と混同しない。
数値ver/vevpを表示し、未取得項目はUnknown。EX等の任意prefix文字列の表示は追加しない。
IPは既定マスクで、切替は表示だけを変える。マスク文字列を識別キーに使用しない。

既存1秒更新を使い、状態・速度のみの更新ではitem handleを維持する。
構造変更時には同一Ownerの展開・選択・先頭表示を可能な範囲で復元する。
Channel / Owner変更、接続消滅、HOST期限切れでは古い選択を使わない。
右クリックはHitTestした対象の詳細表示・表示IDコピーだけ。空白にはメニューを出さない。

## RT-4の境界

選択キーはChannelId / OwnerSessionId / NodeKind / 実ConnectionId / 経路 / 表示用ReportId。
表示IDやIPを解析しない。HOSTノードのReportIdは接続IDではない。
HasDownstreamIdentityは現在の観測の識別材料であって切断許可ではない。
RT-4で切断可能なのはNovaに直接接続した下流だけ。実行時にはOwner管理下で
Channel / Owner / child同一性・接続状態・cleanupを原子的に再確認し、既存ChildStop/cleanup契約を使う。
確認ダイアログでは、そのchild配下にも間接的影響があることを説明する。
今回は切断コマンド・実行API・UIからのSocket.Close/Dispose/Cancelを追加しない。

## 検証の意味

既存のmanaged loopback fixtureで、実TCPの下流HOST/BCST受信→Owner観測→GUI投影、
3段以上と分岐、HEAD/DATAバイト一致、Admission解放、次Ownerへの旧申告非混入を確認する。
上流の実TCPでもBCST受信後の媒体順序と紹介集合の分離を確認する。
制御時計の期限・件数・回数、CID/SID矛盾、endpoint重複、循環、不正BCST等を別途検証する。

Windows 11 build26200・既存のネットワーク無効な隔離構成で、未接続チャンネル2件を使用。
左ペインの独立項目、一覧からの選択引継ぎ、選択欄の変更、濃色表示、右クリックとコピー項目実行、
更新時の選択維持、IP切替、通常/最小サイズ、一覧への復帰、Classic既存詳細を確認した。
多数HOSTの実描画・展開/折りたたみ・スクロールと維持、Owner交代時の実描画、追加DPIは未確認。
Windows実表示はcommit前のRelease。最後のHitTest余白除外は同画面での再操作未確認。
実TCPでの取得・投影の証拠と、Windowsでの限定した実表示は別の受入範囲として扱う。

## 参考

PeerCastStationの以下の公式ソースから、標準HOST/upip/uppt・申告カウント・BCSTの挙動を確認した。
GPLコードのコピーや移植は行わず、上記の制限とNovaの既存観測・Owner契約に合わせて独自実装した。

- [HostTree](https://github.com/kumaryu/peercaststation/blob/master/PeerCastStation/PeerCastStation.Core/HostTree.cs)
- [RelayTreeViewModel](https://github.com/kumaryu/peercaststation/blob/master/PeerCastStation/PeerCastStation.WPF/ChannelLists/RelayTrees/RelayTreeViewModel.cs)
- [PCPSourceStream](https://github.com/kumaryu/peercaststation/blob/master/PeerCastStation/PeerCastStation.PCP/PCPSourceStream.cs)
- [PCPOutputStream](https://github.com/kumaryu/peercaststation/blob/master/PeerCastStation/PeerCastStation.PCP/PCPOutputStream.cs)
