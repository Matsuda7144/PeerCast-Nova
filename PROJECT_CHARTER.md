# PeerCast Nova Project Charter

## Purpose

PeerCast Nova は、既存 PeerCast コミュニティの利用習慣とネットワークを尊重し、シンプルで軽量な P2P 配信クライアントと relay を育てるプロジェクトです。新しさや機能数そのものではなく、長く使える互換性、安定性、開かれた開発を優先します。

## Project principles

- **P2P First:** 中央のサービスやアカウントを必須にせず、ピア間の接続と relay を中核にする。
- **Simplicity / Lightweight:** MVP の利用者価値に直接つながる機能から作り、依存関係、設定、資源使用を抑える。
- **Compatibility:** PeerCastStation との互換性を最優先する。MVP では Nova 固有の必須 wire protocol を導入しない。
- **Open / Community:** Apache-2.0 を採用し、公開可能な source と設計資料を明確にして、既存利用者と貢献者が参加しやすい運営を目指す。
- **Transparency:** 実装・検証・未対応の境界と、長期的な設計判断を記録する。
- **Core / UI Separation:** Core と protocol/network の責務を UI framework から分離し、UI は composition host として扱う。

## MVP (Version 0.1.0) — Complete

Version 0.1.0 の MVP は完成し、main に統合済みです。受入済みの scope は、既存 PeerCast／PeerCastStation network 上の playback と relay です。

- チャンネルを取得・選択し、再生を開始・停止できる。
- 選択した配信を他の peer へ relay できる。
- 接続状態と利用者が対処できるエラーを確認できる。
- 基本経路の PeerCastStation compatibility、bounded resource use、finite stop／cleanup を検証済みである。

受入状態:

- MVP PLAYBACK: ESTABLISHED / INTEGRATED INTO MAIN
- MVP RELAY: ESTABLISHED / INTEGRATED INTO MAIN
- MVP OVERALL: ESTABLISHED / INTEGRATED INTO MAIN
- PEERCAST NOVA MVP IMPLEMENTATION: COMPLETE

main `8caf7069c54c879fd1a7eb2683ab214a337aa883` には PR #245/#246 が landing 済みです。resulting-main CI run `36020495159` / job `107703845407` の Restore、Build、Test、Format はすべて SUCCESS で、実機受入も別途完了しています。

採用済みの playback path は PCP 入力を loopback-only HTTP-FLV で PCRPlayer／DSPlayer などの対応 external player に handoff する経路であり、MVP の再生要件を満たします。PCP Relay が relay 要件を満たします。録画、advanced UI modes、広範な codec support、mandatory Nova-only protocol は MVP の範囲外です。Nova-managed decoder／scheduler／Windows output の追加 playback 経路、managed regular MP4、Windows output の拡張、installer／code signing／updater、将来の productization と post-MVP hardening は MVP 完了後の作業であり、MVP blocker ではありません。詳細は [ROADMAP.md](ROADMAP.md) を参照してください。

## Architecture and decisions

Core は UI framework に依存しません。Application host は Core、Network、Playback、Windows host を組み合わせ、UI や OS resource の ownership を明示します。

長期的な protocol、ownership、lifecycle の判断は ADR に記録します。既存 PeerCastStation の wire behavior を変更・拡張する提案は、legacy interoperability と capability negotiation を説明し、必要な RFC と互換性検証を経てから導入します。

## Motto

**Code for Community. Design for the Future.**
