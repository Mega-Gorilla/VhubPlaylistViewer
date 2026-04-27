# VHub PlayList — VRChat 向け検索/閲覧 API 仕様 (ドラフト)

> **ステータス**: ドラフト — `kisaragi-official/vhub-playlist` への PR 元ネタ。
> **対象バージョン**: vhub-playlist (Next.js 16 + Hasura v2.44 + PostgreSQL 16) の追加実装。
> **クライアント**: VRChat ワールド内 Udon (本リポジトリ `KawaPlayer_PlaylistViewer`)。
> **作成日**: 2026-04-27

## 0. 動機

現状 `vhub-playlist` の VRChat 向け公開 API は以下の 2 つのみで、これだけでは VRChat ワールド内で**プレイリストを探す**ことができない:

- `GET /r/{poolId}/{playlistId}` — playlistId を**既知**として resolve
- `GET /vrcurl/{poolId}/{index}` — pool index → 動画 URL 302 redirect

ユーザーは PC ブラウザで `playlist.vrc-hub.com` を開いて URL をコピーしてくる必要があり、ワールド内 UX が分断されている。本仕様は**ワールド内で検索/閲覧を完結させる**ための追加 API を定義する。

## 1. 設計方針

| 方針 | 内容 |
|---|---|
| 認証 | 不要 (公開プレイリストのみ対象) |
| レスポンス形式 | JSON (`Content-Type: application/json`)、UTF-8、改行なし |
| レート制限 | per-IP (既存 `/r/...` と同基盤を使用) |
| キャッシュ | レスポンスをインメモリ短期キャッシュ (60秒) |
| Pool 連携 | 既存 `pool_slots` / `pool_url_index` 機構を `default-thumb` 等の pool ID で再利用 |
| エラー形式 | `{ ok: false, error: "..." }` (既存 `/r/...` と同じ規約) |
| ページサイズ | 1 ページ = 20 件固定 |

### 1.1 クライアント側制約 (Udon 側の事情)

サーバー設計に直接影響する Udon の制約:

1. **`VRCUrl` はランタイムで文字列から構築不可** — クライアントは `VRCUrl[]` をビルド時にベイクしておく必要がある。エンドポイントの URL は **「pool 化できる構造」** であることが望ましい。
2. **HTTP メソッドは GET のみ** — `VRCStringDownloader.LoadUrl()` は GET 専用。POST やボディの送信は不可。
3. **URL クエリパラメータは pool 単位での pre-bake で対応** — ページ番号や検索クエリは URL 末尾に含まれるため、ベイク時に列挙できる範囲しか叩けない。
4. **検索クエリは VRCUrlInputField 経由で動的取得可能** — ユーザーが `VRCUrlInputField` に文字列を打ち込んだ場合、Udon 側で `GetUrl()` から VRCUrl が取れる (VRChat 側の URL 検証を経て)。これが**唯一の動的 VRCUrl 取得手段**。

これらを踏まえ、各エンドポイントは:
- **ページング閲覧 (popular/recent)** → URL に `?p={page}` を含めて、ページ index 列挙でベイク
- **検索 (search)** → クライアントが `VRCUrlInputField` で動的に URL を組み立てる前提
- **サムネイル取得** → 既存の URL pool パターンを流用

## 2. エンドポイント一覧

| Path | Method | Cache | Rate limit | 用途 |
|---|---|---|---|---|
| `GET /api/vrc/playlists/popular` | GET | 60s | 60 req/min/IP | 人気順プレイリスト (ページング) |
| `GET /api/vrc/playlists/recent` | GET | 60s | 60 req/min/IP | 最近作成順プレイリスト (ページング) |
| `GET /api/vrc/playlists/search` | GET | 60s | 60 req/min/IP | キーワード検索 |
| `GET /thumb/{poolId}/{index}` | GET | - | 緩め | サムネイル画像への 302 redirect |

## 3. 共通レスポンス形式

### 3.1 Listing/Search 共通スキーマ (popular / recent / search)

```jsonc
{
  "ok": true,
  "page": 0,                  // 0-origin
  "pageSize": 20,
  "totalEstimated": 1234,     // 概算件数 (検索/ソート結果の総件数。ページング上限の指針)
  "items": [
    {
      "id": "V1StGXR8_Z5jdHi6B-myT",   // playlists.id (nanoid 21 字)
      "name": "お気に入りカラオケ",
      "ownerName": "Mega Gorilla",
      "trackCount": 12,
      "resolveIndex": 4321,             // VRChat クライアントが /r/... を叩く際の VRCUrl[] index (= 後述)
      "thumbIndex": 7,                  // /thumb/{thumbPoolId}/{thumbIndex} で取得できる
      "isPublic": true,
      "updatedAt": "2026-04-15T03:21:00Z"
    }
  ]
}
```

**`resolveIndex` の役割**:

クライアント側で `/r/{poolId}/{playlistId}` を叩くには playlistId 文字列を URL に埋め込む必要があり、Udon では runtime で `VRCUrl` を作れない。そこで**サーバー側で `playlistId → poolIndex` マッピング**を提供し、クライアントは `_resolvePool[resolveIndex]` というベイク済 VRCUrl で叩けるようにする。

具体的には、本仕様では `/r/{poolId}/{playlistId}` を**廃止せず**、新たに以下を追加する:

- `pool_slots` の **`pool_id = "playlist"`** (新規 pool) を導入。`dest_url` には `https://playlist.vrc-hub.com/r/default/{playlistId}` を格納。
- listing/search のレスポンスを返す際、各 item の `playlistId` に対して `register("playlist", "https://playlist.vrc-hub.com/r/default/{playlistId}")` を実行し、得られた index を `resolveIndex` として埋め込む。
- クライアントは `_resolvePool[resolveIndex]` (= ベイク済の `https://playlist.vrc-hub.com/vrcurl/playlist/{i}`) を叩く。サーバーは `/vrcurl/playlist/{i}` で 302 を返し、ブラウザ/クライアントは `/r/default/{playlistId}` に redirect されて元の resolve API が実行される。

→ つまり**既存の `/vrcurl/{poolId}/{index}` redirect 機構をそのまま流用**してプレイリストの動的 URL 解決を実現する。

### 3.2 エラーレスポンス

```json
{ "ok": false, "error": "Rate limit exceeded" }
```

HTTP ステータスは:
- 200: 正常
- 400: 不正なクエリ (例: `q` が長すぎる、`p` が負)
- 404: 該当なし (未知の pool / playlist)
- 429: レート制限超過
- 500: サーバーエラー

エラー時もボディは JSON で返す。

## 4. 詳細仕様

### 4.1 `GET /api/vrc/playlists/popular`

**クエリ**:
| 名前 | 必須 | 型 | デフォルト | 説明 |
|---|---|---|---|---|
| `p` | × | int | 0 | ページ index (0-origin)。最大値は 49 (= 1000 件まで) |

**ソート**: `playlists.resolve_count DESC` (resolve 回数が多い順)。同点の場合 `updated_at DESC`。

**フィルタ**: `is_public = true` のみ。

**レスポンス**: §3.1 のスキーマに準拠。

**例**:
```http
GET /api/vrc/playlists/popular?p=0
```

```json
{
  "ok": true,
  "page": 0,
  "pageSize": 20,
  "totalEstimated": 234,
  "items": [
    {
      "id": "V1StGXR8_Z5jdHi6B-myT",
      "name": "お気に入りカラオケ",
      "ownerName": "Mega Gorilla",
      "trackCount": 12,
      "resolveIndex": 0,
      "thumbIndex": 0,
      "isPublic": true,
      "updatedAt": "2026-04-15T03:21:00Z"
    }
  ]
}
```

---

### 4.2 `GET /api/vrc/playlists/recent`

**クエリ**:
| 名前 | 必須 | 型 | デフォルト | 説明 |
|---|---|---|---|---|
| `p` | × | int | 0 | ページ index (0-origin)。最大値は 49 |

**ソート**: `created_at DESC`。

**フィルタ**: `is_public = true`。

**レスポンス**: §3.1 のスキーマに準拠。

---

### 4.3 `GET /api/vrc/playlists/search`

**クエリ**:
| 名前 | 必須 | 型 | デフォルト | 説明 |
|---|---|---|---|---|
| `q` | ○ | string | - | 検索キーワード (URL エンコード済)。**1 文字以上 64 字以下** |
| `p` | × | int | 0 | ページ index (0-origin)。最大値は 49 |

**検索アルゴリズム**: 第一段階として PostgreSQL の `ILIKE '%{q}%'` を `playlists.name` に適用。後日 `pg_trgm` / `tsvector` への移行を検討。

**ソート**: 完全一致 → 前方一致 → 部分一致 の順、各群内で `resolve_count DESC`。

**フィルタ**: `is_public = true`。

**バリデーション**:
- `q` が空 / 64 字超 → `400` エラー
- 制御文字を含む → `400` エラー (改行・タブ等は trim)

**レスポンス**: §3.1 のスキーマに準拠。

**例**:
```http
GET /api/vrc/playlists/search?q=%E3%82%AB%E3%83%A9%E3%82%AA%E3%82%B1&p=0
```

---

### 4.4 `GET /thumb/{poolId}/{index}`

**Path Params**:
| 名前 | 説明 |
|---|---|
| `poolId` | 現状 `default-thumb` のみ許可 |
| `index` | 0 以上の整数 |

**処理**: `pool_slots` テーブルから `(poolId, index)` で `dest_url` (サムネ画像の実 URL) を引き、HTTP 302 redirect。

**レスポンス**:
- 成功: `302 Location: https://i.ytimg.com/vi/.../hqdefault.jpg`
- 未登録: `404 Not Found` (ボディなし)

**Pool 登録**: `videos.thumbnail_url` (oEmbed で取得済) を listing/search レスポンス生成時に `register("default-thumb", thumbUrl)` する。同一 URL は再利用 (既存 `register()` の挙動)。

**注意**: `videos.thumbnail_url` が NULL の場合、サムネプールには登録しない。クライアントには `thumbIndex: -1` を返す。

---

### 4.5 既存 `GET /r/{poolId}/_validate` (再利用)

新仕様ではないが、Editor の Pool ID 検証で使用する既存エンドポイント。本仕様の追加 pool (`default-thumb`, `playlist`) も同じ機構で `_validate` できるようにする。

**レスポンス例**:
```http
GET /r/playlist/_validate
```
```json
{ "ok": true, "pool": "playlist", "poolSize": 100000 }
```

## 5. Pool 設計

### 5.1 Pool ID の追加

| Pool ID | 用途 | サイズ |
|---|---|---|
| `default` | 既存。動画 URL の pool | 100,000 |
| `playlist` | **新規**。 playlist resolve URL の pool | 100,000 |
| `default-thumb` | **新規**。サムネ画像 URL の pool | 100,000 |

すべて既存の `pool_slots` / `pool_url_index` テーブルで一元管理。`pg_advisory_xact_lock(hashtext(poolId))` の機構もそのまま流用。

### 5.2 LRU 容量試算

- プレイリスト総数 (公開) 想定: 〜数万。1 ユーザーが 1 ワールドで閲覧する範囲は数百〜千。LRU 100,000 で十分。
- サムネ: 動画数 × 解像度 = 同上。100,000 で OK。

### 5.3 `_validate` レスポンス形式

新規 pool でも `/r/{poolId}/_validate` で `{ ok, pool, poolSize }` を返すよう拡張。Editor 側の Pool Generator が叩く。

## 6. レート制限

| エンドポイント | 制限 | 既存 `/r/...` との関係 |
|---|---|---|
| `/api/vrc/playlists/popular` | 60 req/min/IP | 同 bucket でも分離でも可。実装シンプルさで同 bucket 推奨 |
| `/api/vrc/playlists/recent` | 60 req/min/IP | 〃 |
| `/api/vrc/playlists/search` | 60 req/min/IP | 〃 |
| `/thumb/{poolId}/{index}` | 制限緩めまたは無し | 動画再生中に頻繁にアクセスする可能性 |

## 7. キャッシュ

### 7.1 listing/search のレスポンスキャッシュ

- キー: `popular:p={page}` / `recent:p={page}` / `search:q={qHash}:p={page}` (qHash は URL-decoded q を sha1)
- TTL: **60 秒**
- 保存先: インメモリ (Next.js プロセス内、Redis 不要)

### 7.2 invalidation

以下のミューテーションで対応する listing/search キャッシュを無効化:
- `playlists` テーブルの insert / update / delete (popular / recent / search すべて)
- `playlist_tracks` の変更 (trackCount が変わる、popular のみ影響)

ただし完全な invalidation は複雑なので、**v1 では TTL 60秒任せでよい** (resolve API と同方針)。

## 8. メトリクス

運用監視のために以下を記録:
- `popular_request_count` / `recent_request_count` / `search_request_count`
- `search_query_top_50` (上位 50 検索クエリ)
- `thumb_redirect_404_count` (per pool)
- `popular_p99_latency_ms` 等

`/r/...` の既存メトリクス機構と同じ形式で。

## 9. テスト

### 9.1 単体テスト

- `popular(p=0)` が pageSize 件返す
- `recent(p=49)` が空でも 200 で `items: []` が返る
- `search(q="")` → 400
- `search(q="あいうえおかきくけこさしすせそたちつてとなにぬねの")` (50字) → 200
- `search(q="aaa..." (65字))` → 400
- `thumb/default-thumb/0` (空) → 404
- `popular` の同一クエリを 65 回連続叩いて 65 回目で 429 が返る

### 9.2 結合テスト

- listing → クライアントが `resolveIndex` 経由で `/vrcurl/playlist/{i}` を叩く → 302 で `/r/default/{playlistId}` → 既存の resolve flow

## 10. ロールアウト

1. PR 作成 → maintainer レビュー
2. ステージング環境デプロイ
3. クライアント (本パッケージ) のステージング向けビルドで結合確認
4. 本番デプロイ
5. クライアントを VPM にリリース

## 11. オープン課題 (Discussion)

- 検索の N-gram / 全文インデックス対応 (`pg_trgm`) は v2 で？
- listing にタグ/カテゴリでの絞り込みを追加するか？(現行 schema にタグカラムなし → 別 issue)
- thumb の WebP 配信 (CDN 経由) を入れるか？

---

## Appendix A. クライアント側の流れ (参考)

```
[VRChat World]                                  [playlist.vrc-hub.com]
1. ワールド入室
   └─ 自動で popular?p=0 を叩く          ────▶  /api/vrc/playlists/popular?p=0
                                                  │ JSON: items[].resolveIndex, thumbIndex
2. resolve URL の baked VRCUrl 経由で詳細取得
   └─ _resolvePool[resolveIndex] (= /vrcurl/playlist/{i})
                                          ────▶  /vrcurl/playlist/{i}
                                                  │ 302 → /r/default/{playlistId}
                                          ────▶  /r/default/{playlistId}
                                                  │ JSON: tracks[].index, title, mode
3. サムネ表示
   └─ _thumbPool[thumbIndex]              ────▶  /thumb/default-thumb/{i}
                                                  │ 302 → 動画サムネ JPEG
4. ユーザーが詳細画面で URL をコピー
   └─ TMP_InputField に表示された
       https://playlist.vrc-hub.com/r/default/{playlistId}
       を VRChat キーボードの Copy → KawaPlayer に Paste
```
