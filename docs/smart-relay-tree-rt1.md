# Smart Relay RT-1: observed relay tree

Modern の Channels でチャンネルを選択し、詳細カードの「リレーツリー」を開く。
説明欄が縦スクロール可能な読み取り専用ツリーに切り替わり、右側に状態・容量・速度を表示する。
同じボタンで通常の説明へ戻る。録画・停止操作の配置と Classic は変更しない。

## 観測範囲

- 現在の ChannelId / Owner 世代だけを投影する。
- 上流は `PcpChannelSession` の実 TCP 接続と PCP 交渉結果。
- 下流は既存サーバーが所有し、HTTP 要求と ChannelId を検証した session。
  HELO 受信前は交渉中、既存の established 判定後だけ接続済みになる。
- 視聴 loopback、配信 loopback、共有公開 listener を区別する。
  loopback は実際の bind アドレスから確認し、その他のチャンネル専用待受は経路 Unknown とする。
- OBS の入力状態は既存 RTMP ingest の観測値。PCP Peer として数えない。
  汎用 Broadcast で入力を観測できなければ入力ノードを作らない。
- HOST 紹介候補、過去の Peer、別 Peer の下流、接続先での再生成功は投影しない。

`up-` / `down-` はプロセス内の実接続 ID から作る表示用識別子であり、
IP アドレスやグローバルな Peer identity ではない。
同じ接続は Owner 世代内で安定した識別子を持つ。異なる実接続は別に表示する。
下流の投影上限は 64 件。上限超過は省略数を表示し、架空のノードで補わない。

## 状態と数値

接続試行・PCP 交渉・接続済み・終了処理・切断・Unknown を文字で区別する。
Recovery は既存 Smart Relay 状態から投影し、再接続で実接続 ID が変わっても Owner は維持する。
過去の接続履歴は保存しないため、Owner がなく観測できない場合は「情報未取得」。
既存の終了失敗または未解放の Admission lease があれば「終了処理未確認」とする。

接続数と予約数は別の値。容量は既存 Admission のチャンネル予約 / チャンネル上限。
速度は Phase E のアプリケーション書込み完了速度であり、受信確認や映像・音声の証明ではない。
個別 child の速度はその child の計測値だけを使い、未計測なら Unknown。
全体速度を個別接続へ流用しない。ペーシングは既存共有 pacer の設定状態を表示する。

## 所有権と安全性

既存 Owner 登録に観測元の弱参照を結び付ける。GUI は接続や Owner を保持せず、
秘密値・生 IP / URL・Peer の任意文字列・atom・例外全文を含まない immutable な投影だけを使う。
観測元の読み取りは registry のロック外で行い、取得後に現在の Owner と ID を再確認する。
世代が変わった読み取り結果は破棄する。

更新は既存 1 秒 status clock と選択変更を使う。新規 timer、socket、探索、制御 engine は追加しない。
ツリーを開く操作に Relay 開始・接続変更・切断・WAN/NAT 設定変更の権限はない。

## 検証範囲

managed snapshot / 世代交代テストと既存 fixture の loopback TCP で、
複数チャンネル、local / 共有 listener、HEAD/DATA、HOST 候補の除外、終了を照合する。
既存 Recovery テストでは同一 Owner の上流交換を、OBS テストでは実 RTMP 入力状態を確認する。
最小サイズの配置と Classic 非適用は managed テストで確認する。
Windows 実表示、PeerCastStation 実機、PCRPlayer 映像・音声、WAN 到達性は別の確認項目。
