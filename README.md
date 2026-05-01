# VHub PlaylistViewer

VRChat ワールド向けの **VHub PlayList プレイリスト検索 / 閲覧 UI** を提供する unitypackage（開発中）。

## 概要

[VHub PlayList](https://playlist.vrc-hub.com) のプレイリストデータを VRChat ワールド内で検索・閲覧し、選択したプレイリストの URL を [KawaPlayer](https://github.com/Mega-Gorilla/KawaPlayer) などの動画プレイヤーに渡すための View 専用パッケージ。

## ステータス

**Alpha (開発中)** — Runtime / Editor 実装完了 + サーバー API v4 対応済 (2026-05)。prefab 同梱前の実機検証段階。

- ✅ Runtime UdonSharp スクリプト群 (検索 / 詳細表示 / サムネ表示) 実装完了
- ✅ Editor の Pool Generator / AllowedDomainsHelper 実装完了
- ✅ サーバー API v4 (resolve 200 JSON / yt-thumb-direct pool) サポート
- ✅ 実機 VR でサムネ表示確認済 (Allow Untrusted URLs OFF プレイヤーでも動作)
- ⏳ Prefab / Animator / Material 同梱 → [#12](../../issues/12) / [#13](../../issues/13)
- ⏳ 完成版 README + 導入手順 → [#14](../../issues/14)
- ⏳ ライセンス確定 → [#15](../../issues/15)
- ⏳ VPM listing 登録 → [#17](../../issues/17)

VCC からの正式インストール提供は #17 完了後を予定。

## 関連プロジェクト

- [KawaPlayer](https://github.com/Mega-Gorilla/KawaPlayer) — VRChat 向け動画プレイヤー（YamaPlayer ベース）
- VHub PlayList — プレイリスト管理サーバー (private)

## ライセンス

未定 (検討中: [#15](../../issues/15))。
