# VHub PlayList — VRChat 向け検索/閲覧 API 仕様

> **ステータス**: ドラフト v2 (maintainer review 反映済)
> **対象バージョン**: vhub-playlist (Next.js 16 + Hasura v2.44 + PostgreSQL 16) の追加実装。
> **クライアント**: VRChat ワールド内 Udon (本リポジトリ `KawaPlayer_PlaylistViewer`)。
> **改訂履歴**:
> - 2026-04-27 v1: 初版ドラフト
> - 2026-04-28 v2: maintainer review 反映 — `/thumb` 廃止 (`/vrcurl/default-thumb` で代替)、`ALLOWED_POOLS` 拡張、playlist pool 永久化、ownerName フォールバック規則、NFKC 正規化、`/r/...` レスポンスに `id` 追加 (defensive 用)

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
| Pool 連携 | 既存 `pool_slots` / `pool_url_index` 機構を `playlist` / `default-thumb` 等の pool ID で再利用 |
| エラー形式 | `{ ok: false, error: "..." }` (既存 `/r/...` と同じ規約) |
| ページサイズ | 1 ページ = 20 件固定 |
| Hasura アクセス | API Routes は admin secret 経由 (Hasura permission の追加変更なし) |

### 1.1 クライアント側制約 (Udon 側の事情)

サーバー設計に直接影響する Udon の制約:

1. **`VRCUrl` はランタイムで文字列から構築不可** — クライアントは `VRCUrl[]` をビルド時にベイクしておく必要がある。
2. **HTTP メソッドは GET のみ** — `VRCStringDownloader.LoadUrl()` は GET 専用。POST やボディの送信は不可。
3. **URL クエリパラメータは pool 単位での pre-bake で対応**。
4. **検索クエリは VRCUrlInputField 経由で動的取得可能** — ユーザーが `VRCUrlInputField` に文字列を打ち込んだ場合、Udon 側で `GetUrl()` から VRCUrl が取れる。これが**唯一の動的 VRCUrl 取得手段**。

これらを踏まえ、各エンドポイントは:
- **ページング閲覧 (popular/recent)** → URL に `?p={page}` を含めて、ページ index 列挙でベイク
- **検索 (search)** → クライアントが `VRCUrlInputField` で動的に URL を組み立てる前提
- **サムネイル取得** → 既存 `/vrcurl/{poolId}/{index}` redirect 機構を `poolId = default-thumb` で流用

## 2. エンドポイント一覧

| Path | Method | Cache | Rate limit | 用途 |
|---|---|---|---|---|
| `GET /api/vrc/playlists/popular` | GET | 60s | 60 req/min/IP | 人気順プレイリスト (ページング) |
| `GET /api/vrc/playlists/recent` | GET | 60s | 60 req/min/IP | 最近作成順プレイリスト (ページング) |
| `GET /api/vrc/playlists/search` | GET | 60s | 60 req/min/IP | キーワード検索 |

サムネ取得とプレイリスト解決は **既存 `GET /vrcurl/{poolId}/{index}`** を `poolId = default-thumb` / `playlist` のホワイトリスト拡張で再利用 (新エンドポイント追加なし)。詳細は §4.5 / §5。

加えて、既存 `GET /r/{poolId}/{playlistId}` のレスポンスに **`id` フィールドを追加** する小修正 (§4.6、defensive 検証用)。

## 3. 共通レスポンス形式

### 3.1 Listing/Search 共通スキーマ (popular / recent / search)

```jsonc
{
  "ok": true,
  "page": 0,                  // 0-origin
  "pageSize": 20,
  "totalEstimated": 1234,     // 概算件数 (Hasura _aggregate { count }、admin secret 経由)
  "items": [
    {
      "id": "V1StGXR8_Z5jdHi6B-myT",   // playlists.id (nanoid 21 字)
      "name": "お気に入りカラオケ",
      "ownerName": "Mega Gorilla",     // display_name ?? misskey_username ?? "" (フォールバック規則)
      "trackCount": 12,
      "resolveIndex": 4321,            // /vrcurl/playlist/{i} の index (§5)
      "thumbIndex": 7,                 // /vrcurl/default-thumb/{i} の index (-1 なら未登録)
      "isPublic": true,
      "updatedAt": "2026-04-15T03:21:00Z"
    }
  ]
}
```

**`ownerName` フォールバック規則** (重要):
1. `users.display_name` が NULL でなければ採用
2. NULL なら `users.misskey_username` を採用
3. それも NULL なら空文字 `""`

**`resolveIndex` の役割**:

クライアント側で `/r/{poolId}/{playlistId}` を叩くには playlistId 文字列を URL に埋め込む必要があり、Udon では runtime で `VRCUrl` を作れない。そこで**サーバー側で `playlistId → poolIndex` マッピング**を提供し、クライアントは `_resolvePool[resolveIndex]` というベイク済 VRCUrl で叩けるようにする。

具体的には、本仕様では `/r/{poolId}/{playlistId}` を**廃止せず**、新たに以下を追加する:

- `pool_slots` の **`pool_id = "playlist"`** (新規 pool) を導入。`dest_url` には `https://playlist.vrc-hub.com/r/default/{playlistId}` を格納。
- listing/search のレスポンスを返す際、各 item の `playlistId` に対して `register("playlist", "https://playlist.vrc-hub.com/r/default/{playlistId}")` を実行し、得られた index を `resolveIndex` として埋め込む。
- クライアントは `_resolvePool[resolveIndex]` (= ベイク済の `https://playlist.vrc-hub.com/vrcurl/playlist/{i}`) を叩く。サーバーは `/vrcurl/playlist/{i}` で 302 を返し、クライアントは `/r/default/{playlistId}` に redirect されて元の resolve API が実行される。
- ⚠️ **`playlist` pool は LRU 無効化必須** (§5.1)。クライアントのベイク済 VRCUrl[] は配布物として固定なので、index → playlistId の対応がランタイムで上書きされると配布済ワールド全体で resolve がズレる。

### 3.2 エラーレスポンス

```json
{ "ok": false, "error": "Rate limit exceeded" }
```

HTTP ステータス: `200` 正常 / `400` 不正クエリ / `404` 該当なし / `429` レート制限超過 / `500` サーバーエラー。エラー時もボディは JSON。

## 4. 詳細仕様

### 4.1 `GET /api/vrc/playlists/popular`

| 名前 | 必須 | 型 | デフォルト | 説明 |
|---|---|---|---|---|
| `p` | × | int | 0 | ページ index (0-origin)。最大 49 |

- **ソート**: `playlists.resolve_count DESC`、tie-break `updated_at DESC`
- **フィルタ**: `is_public = true`
- **レスポンス**: §3.1 共通スキーマ

### 4.2 `GET /api/vrc/playlists/recent`

| 名前 | 必須 | 型 | デフォルト | 説明 |
|---|---|---|---|---|
| `p` | × | int | 0 | ページ index。最大 49 |

- **ソート**: `created_at DESC`
- **フィルタ**: `is_public = true`
- **レスポンス**: §3.1 共通スキーマ

### 4.3 `GET /api/vrc/playlists/search`

| 名前 | 必須 | 型 | デフォルト | 説明 |
|---|---|---|---|---|
| `q` | ○ | string | - | 検索キーワード (URL エンコード済)。**1 文字以上 64 字以下** |
| `p` | × | int | 0 | ページ index。最大 49 |

#### クエリ正規化 (重要)

サーバー受信時に **以下の前処理**を経由してから比較:

1. URL デコード
2. **NFKC 正規化** (全角・半角を統一: `カラオケ` ≡ `ｶﾗｵｹ`、互換漢字を統一)
3. **lowercase 化** (`Karaoke` ≡ `karaoke`、ASCII 範囲)
4. 前後の whitespace trim

```ts
const qNorm = decodeURIComponent(rawQ).normalize("NFKC").toLowerCase().trim();
```

#### 検索アルゴリズム

正規化済み `qNorm` を `LOWER(playlists.name)` (ない場合は実行時 `LOWER()` キャスト) に対して `ILIKE` 比較:

```sql
WHERE is_public = true
  AND LOWER(playlists.name) ILIKE '%' || qNorm || '%'
```

将来的なパフォーマンス対策として `playlists.normalized_name` (generated column or trigger 維持) を検討 (§13)。

#### ソート

完全一致 → 前方一致 → 部分一致 の 3 段階、各群内で `resolve_count DESC`、tie-break `updated_at DESC`:

```sql
ORDER BY
  CASE
    WHEN LOWER(name) = qNorm THEN 0
    WHEN LOWER(name) LIKE qNorm || '%' THEN 1
    ELSE 2
  END,
  resolve_count DESC,
  updated_at DESC
```

#### フィルタ・バリデーション

- `is_public = true`
- `q` 空 / 64 字超 → `400`
- 制御文字を含む → `400` (改行・タブ等は事前 trim)
- `p < 0` または `p > 49` → `400`

#### レスポンス

§3.1 共通スキーマ。

### 4.4 (廃案) `GET /thumb/{poolId}/{index}`

⚠️ **不採用**。当初は専用エンドポイント `/thumb/...` を想定していたが、既存 `/vrcurl/{poolId}/{index}` が既に同じ「pool slot lookup → 302 redirect」機構を提供しているため、ホワイトリスト拡張のみで賄える (§4.5)。新規パス追加なしで運用シンプル化。

### 4.5 既存 `GET /vrcurl/{poolId}/{index}` のホワイトリスト拡張 (重要)

#### 現状

`src/app/vrcurl/[poolId]/[index]/route.ts:14` で `poolId !== DEFAULT_POOL_ID` (= `"default"`) を 404 で蹴っている。

#### 修正

ホワイトリスト定数化して 3 つの pool ID を許可:

```ts
const ALLOWED_POOLS = new Set(["default", "playlist", "default-thumb"]);
if (!ALLOWED_POOLS.has(poolId)) {
  return new NextResponse(null, { status: 404 });
}
```

これにより:
- `/vrcurl/default/{i}` — 既存、動画 URL (KawaPlayer 等が利用)
- `/vrcurl/playlist/{i}` — 新規、playlist resolve URL → 302 → `/r/default/{playlistId}`
- `/vrcurl/default-thumb/{i}` — 新規、サムネ画像 URL (302 redirect)

#### 同じ拡張を `/r/{poolId}/{playlistId}` にも

`src/app/r/[poolId]/[playlistId]/route.ts:26` でも同じ `poolId` ホワイトリストチェックがあるが、resolve API は意味的には `default` のみで利用される (playlist や default-thumb の resolve は意味なし)。**現状維持で OK** だが、ALLOWED_POOLS を共通定数として lib に切り出して両方から参照する方が保守性が良い。

### 4.6 `GET /r/{poolId}/{playlistId}` レスポンスに `id` 追加 (defensive 検証用)

#### 現状

```ts
const response = {
  ok: true,
  pool: poolId,
  name: playlist.name,
  tracks,
};
```

#### 修正

`id` フィールドを追加:

```ts
const response = {
  ok: true,
  id: playlist.id,        // ← 追加
  pool: poolId,
  name: playlist.name,
  tracks,
};
```

これによりクライアントは「listing API で得た playlistId」と「resolve API レスポンスの `id`」を比較し、不一致なら **playlist pool LRU 衝突または register() の race condition** を検知できる (`playlist` pool は永久化するので通常起きないが、クライアント側 defensive layer として安全)。

### 4.7 既存 `GET /r/{poolId}/_validate` の新 pool ID 対応

新規 pool でも `/r/{poolId}/_validate` で `{ ok, pool, poolSize }` を返すよう拡張:

```http
GET /r/playlist/_validate          → { ok: true, pool: "playlist", poolSize: 100000 }
GET /r/default-thumb/_validate     → { ok: true, pool: "default-thumb", poolSize: 100000 }
```

クライアント側 Editor (`PoolGenerator`) はこれを叩いて Pool ID 検証する。

## 5. Pool 設計

### 5.1 Pool ID とサイズ

| Pool ID | 用途 | サイズ | LRU 上書き |
|---|---|---|---|
| `default` | 既存。動画 URL の pool | 100,000 | **有効** (既存挙動) |
| `playlist` | **新規**。playlist resolve URL の pool | 100,000 | **無効 (永久保持)** ⚠️ |
| `default-thumb` | **新規**。サムネ画像 URL の pool | 100,000 | **有効** (動画 URL と同様) |

#### `playlist` pool の永久化が必要な理由

クライアントは `_resolvePool[N]` (= ベイク済 `/vrcurl/playlist/N`) を**ワールドのアセットとして永続的に持つ**。つまり「ベイク時に N → playlistId X」だった対応がランタイムで「N → playlistId Y」に上書きされると:

- 配布済ワールド全員に対して、X を選んだつもりが Y が解決される
- 全 VRChat ワールドが影響範囲 (グローバルな副作用)

これを防ぐため、**`playlist` pool では LRU 上書きを禁止**する。容量超過時は:
1. `register()` がエラー応答 (HTTP 500、運用アラート対象)
2. 運用側で pool サイズを増やすか、削除済みプレイリストの slot を回収するロジックを別途実装

実装例:

```ts
const PERMANENT_POOLS = new Set(["playlist"]);

async function register(poolId: string, url: string): Promise<number> {
  // ...重複チェック...
  if (count >= POOL_SIZE) {
    if (PERMANENT_POOLS.has(poolId)) {
      throw new PoolExhaustedError(`Pool ${poolId} is full and LRU is disabled`);
    }
    // 既存 LRU 上書きロジック
  }
  // ...新規割り当て...
}
```

#### LRU 容量試算

- プレイリスト総数 (公開) 想定: 〜数万。100k で当面十分
- 動画サムネ: 動画数 × 解像度 = 同上

### 5.2 LRU 衝突に対する client 側 defensive (strict mode)

`/r/.../{playlistId}` のレスポンスに `id` を含める (§4.6) ことで、クライアントは strict mode で検証する:

- **`id` フィールドが無い** → サーバーが spec §4.6 非準拠 → `ReportError` で error state に遷移
- **`id` が listing 時の `playlistId` と不一致** → playlist pool 衝突等の異常 → 同様に error state

```csharp
// クライアント側擬似コード (PlaylistViewerController.ParseDetailJson)
DataToken idToken;
if (!rootDict.TryGetValue("id", out idToken) || idToken.TokenType != TokenType.String)
{
    Debug.LogWarning("[PlaylistViewer] Resolve response missing 'id' field — server not conformant to spec §4.6");
    ReportError("Resolve response missing playlist id");
    return false;
}
string responseId = idToken.String;
if (responseId != playlistId)
{
    Debug.LogWarning("[PlaylistViewer] id mismatch: expected=" + playlistId + " got=" + responseId);
    ReportError("Playlist id mismatch");
    return false;
}
```

**Strict mode を採用する理由** (pre-release 期):
- Server bug を早期発見 (spec 違反が silent skip にならず、即 error state)
- 永久 pool (server-side primary defense) と一貫した責務分担 (両方とも strict)
- `Debug.LogWarning` で詳細情報をログに残し、`ReportError` 経由の UI 表示 (`#ErrorMessage`) は短文 (UX 配慮)

将来 VPM 配布後に互換性が必要になったら、その時点で lenient 化を検討する。

## 6. レート制限

| エンドポイント | 制限 | 備考 |
|---|---|---|
| `/api/vrc/playlists/popular` | 60 req/min/IP | 同 bucket 推奨 |
| `/api/vrc/playlists/recent` | 60 req/min/IP | 〃 |
| `/api/vrc/playlists/search` | 60 req/min/IP | 〃 |
| `/vrcurl/{poolId}/{index}` (default-thumb) | 緩めまたは無し | 動画再生中に頻繁にアクセスする可能性 |

## 7. キャッシュ

### 7.1 listing/search のレスポンスキャッシュ

- キー:
  - `popular:p={page}`
  - `recent:p={page}`
  - `search:q={qHash}:p={page}` (qHash は **正規化済み qNorm** を sha1)
- TTL: **60 秒**
- 保存先: インメモリ (Next.js プロセス内、Redis 不要)

### 7.2 invalidation

以下のミューテーションで対応するキャッシュを無効化:
- `playlists` テーブルの insert / update / delete (popular / recent / search すべて)
- `playlist_tracks` の変更 (trackCount が変わる、popular のみ影響)

完全 invalidation は複雑なので、**v1 では TTL 60秒任せでよい**。

## 8. メトリクス

- `popular_request_count` / `recent_request_count` / `search_request_count`
- `search_query_top_50` (上位 50 検索クエリ、qNorm 単位)
- `vrcurl_redirect_404_count` per pool (新 pool 含む)
- `popular_p99_latency_ms` 等
- `playlist_pool_capacity_used_pct` (LRU 無効 pool の運用監視)

## 9. テスト

### 9.1 単体テスト

- `popular(p=0)` が pageSize 件返す
- `recent(p=49)` が空でも 200 で `items: []` が返る
- `search(q="")` → 400
- `search(q="aaa..." (65字))` → 400
- `search(q="あいうえおかきくけこさしすせそたちつてとなにぬねの")` (50字) → 200
- **`search(q="カラオケ")` と `search(q="ｶﾗｵｹ")` が同じ結果セット** (NFKC 正規化テスト)
- **`search(q="Karaoke")` と `search(q="karaoke")` が同じ結果** (lowercase テスト)
- `/vrcurl/playlist/0` (空) → 404
- `/vrcurl/default-thumb/0` (空) → 404
- `/vrcurl/unknown-pool/0` → 404 (whitelist 動作)
- `popular` 同一クエリを 65 回連続叩いて 65 回目で 429
- `playlist` pool 100,001 件目の register でエラー応答 (永久 pool 検証)
- `display_name` が NULL のユーザーで `ownerName == misskey_username` を確認

### 9.2 結合テスト

- listing → クライアントが `resolveIndex` 経由で `/vrcurl/playlist/{i}` を叩く → 302 で `/r/default/{playlistId}` → 既存 resolve flow → レスポンスに `id` が含まれる

## 10. ロールアウト

1. PR 作成 → maintainer レビュー
2. ステージング環境デプロイ
3. クライアント (本パッケージ) のステージング向けビルドで結合確認
4. 本番デプロイ
5. クライアントを VPM にリリース

## 11. 受け入れ基準

### 新規エンドポイント

- [ ] `/api/vrc/playlists/popular?p={page}` 実装 (ページサイズ 20、最大 page 49、resolve_count DESC + updated_at DESC 順)
- [ ] `/api/vrc/playlists/recent?p={page}` 実装 (created_at DESC 順)
- [ ] `/api/vrc/playlists/search?q={query}&p={page}` 実装 (q 1〜64 字、NFKC + lower 正規化、3 段ソート)

### 既存エンドポイントの拡張

- [ ] `/vrcurl/{poolId}/{index}` の `ALLOWED_POOLS = {"default","playlist","default-thumb"}` ホワイトリスト化
- [ ] `/r/{poolId}/{playlistId}` レスポンスに `id` フィールド追加 (defensive 検証用、§4.6)
- [ ] `/r/{poolId}/_validate` が新 pool ID 2 種に対応

### Pool 機構

- [ ] `pool_slots` への `pool_id = playlist` / `default-thumb` 対応 (DB スキーマは現状で OK、書き込み開始のみ)
- [ ] `playlist` pool の **LRU 無効化** (`PERMANENT_POOLS` set 導入、容量超過時はエラー)
- [ ] `register()` のテストに永久 pool ケース追加

### 共通

- [ ] レート制限 60 req/min/IP の適用 (新エンドポイント、`/vrcurl/default-thumb/...` は緩め可)
- [ ] resolve cache 同様のインメモリキャッシュ TTL 60秒
- [ ] エラーケース・バリデーション (q 長さ、p 範囲、不正 pool 等)
- [ ] **`ownerName` フォールバック**: `display_name ?? misskey_username ?? ""`
- [ ] **クエリ正規化**: NFKC + lowercase + trim を `q` の事前処理として実装、qHash も正規化後値で計算
- [ ] §9 のテストケースを単体/結合テストとして追加
- [ ] 結合確認: kawaplayer-playlistviewer のテストワールドから popular → resolve → サムネ表示のフローが通る

## 12. 提案する実装スコープ分割 (任意)

実装する側で sub-issue / sub-PR に分割したい場合:

1. 既存ルートのホワイトリスト拡張 + `/r/...` レスポンスに `id` 追加 + `/r/.../_validate` 拡張 — **基盤** (まず先に)
2. `/api/vrc/playlists/popular` + `/recent` (ページング、resolveIndex 含む) — 主要機能
3. `/api/vrc/playlists/search` (q + p、NFKC 正規化) — 検索ロジック
4. Pool 機構の永久 pool 対応 (`PERMANENT_POOLS`)
5. キャッシュ + レート制限 + メトリクス + ownerName フォールバック

ご相談ベースで、分割 PR にしてもまとめ PR にしても OK。

## 13. オープン課題 (Discussion)

- 検索の N-gram / 全文インデックス対応 (`pg_trgm`) は v2 で？
- listing にタグ/カテゴリでの絞り込みを追加するか？(現行 schema にタグカラムなし → 別 issue)
- `playlists.normalized_name` (NFKC + lower 済み) を generated column / trigger 維持で導入するか? — 検索パフォーマンス向上見込み
- thumb の WebP 配信 (CDN 経由) を入れるか？
- `playlist` pool の容量超過運用 (アラート閾値、削除済 slot 回収バッチ等)

---

## Appendix A. クライアント側の流れ (参考)

```
[VRChat World]                                    [playlist.vrc-hub.com]
1. ワールド入室
   └─ 自動で popular?p=0 を叩く            ────▶  /api/vrc/playlists/popular?p=0
                                                    │ JSON: items[].resolveIndex, thumbIndex, ownerName
2. resolve URL の baked VRCUrl 経由で詳細取得
   └─ _resolvePool[resolveIndex]
       (= /vrcurl/playlist/{i})
                                            ────▶  /vrcurl/playlist/{i}
                                                    │ 302 → /r/default/{playlistId}
                                            ────▶  /r/default/{playlistId}
                                                    │ JSON: id, name, tracks[]
   └─ defensive: response.id == 期待 playlistId を検証
3. サムネ表示
   └─ _thumbPool[thumbIndex]                ────▶  /vrcurl/default-thumb/{i}
       (= /vrcurl/default-thumb/{i})                │ 302 → 動画サムネ JPEG
4. ユーザーが詳細画面で URL をコピー
   └─ TMP_InputField に表示された
       https://playlist.vrc-hub.com/r/default/{playlistId}
       を VRChat キーボードの Copy → KawaPlayer に Paste
```
