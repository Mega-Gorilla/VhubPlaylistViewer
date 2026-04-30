# VHub PlayList — VRChat 向け検索/閲覧 API 仕様

> **ステータス**: ドラフト v3 (改訂後)
> **対象バージョン**: vhub-playlist (Next.js 16 + Hasura v2.44 + PostgreSQL 16) の追加実装。
> **クライアント**: VRChat ワールド内 Udon (本リポジトリ `KawaPlayer_PlaylistViewer`)。
> **改訂履歴**:
> - 2026-04-27 v1: 初版ドラフト
> - 2026-04-28 v2: maintainer review 反映 — `/thumb` 廃止 (`/vrcurl/default-thumb` で代替)、`ALLOWED_POOLS` 拡張、playlist pool 永久化、ownerName フォールバック規則、NFKC 正規化、`/r/...` レスポンスに `id` 追加 (defensive 用)
> - 2026-04-29 v3: vhub-playlist#88 セカンドレビュー反映 — `_validate` を新規実装と訂正、`playlist` pool の削除禁止 (LRU 無効化に加えて) を明文化、`ownerName` フォールバックを `display_name?.trim() || misskey_username || ""` に修正、`recent` / `search` のソート末尾に `id ASC` tie-breaker 追加、`dest_url` 構築を `process.env.NEXT_PUBLIC_SITE_URL` ベース化、検索の制御文字処理を「trim 後、内部に残ったら 400」に統一、運用メトリクスは v1 はログ出力のみ (ダッシュボード化は別 issue)

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
| Pool 連携 | 既存 `pool_slots` / `pool_url_index` 機構を `playlist` / `default-thumb` 等の pool ID で再利用。**`playlist` pool は LRU 無効化 + 削除禁止 (永久保持)**。 |
| エラー形式 | `{ ok: false, error: "..." }` (既存 `/r/...` と同じ規約) |
| ページサイズ | 1 ページ = 20 件固定 |
| Hasura アクセス | API Routes は admin secret 経由 (Hasura permission の追加変更なし) |
| 検索クエリ正規化 | NFKC + 小文字化 (`q_norm = q.normalize("NFKC").toLowerCase()`) を**バリデーション後・SQL 実行前・キャッシュキー算出前**に適用 |
| URL 構築 | `dest_url` 等の絶対 URL は **`process.env.NEXT_PUBLIC_SITE_URL`** をベースに組み立てる (staging/dev/本番で同コード) |
| ソート安定性 | listing/search のすべてのソートは末尾に `id ASC` を tie-breaker として加える (ページング揺れ防止) |

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

加えて、既存 `GET /r/{poolId}/{playlistId}` のレスポンスに **`id` フィールドを追加** する小修正 (§4.6、defensive 検証用)、および Editor 用の **新規 `GET /r/{poolId}/_validate` ルート追加** (§4.7、v3 改訂で「既存」表記を訂正)。

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
      "ownerName": "Mega Gorilla",     // display_name?.trim() || misskey_username || ""
      "trackCount": 12,
      "resolveIndex": 4321,            // /vrcurl/playlist/{i} の index (§5)
      "thumbIndex": 7,                 // /vrcurl/default-thumb/{i} の index (-1 なら未登録)
      "isPublic": true,
      "updatedAt": "2026-04-15T03:21:00Z"
    }
  ]
}
```

**`ownerName` のフォールバック規則** (実装で必須):

```ts
const ownerName =
  (user.display_name?.trim() || null) ?? user.misskey_username ?? "";
```

> 単純な `??` チェーンだと `display_name === ""` が "値あり" 扱いになり空文字が返ってしまうため、`trim() || null` を挟んで「空白のみ/未設定」をフォールバック対象にする。`display_name` が NULL/空白のみ/空文字いずれの場合も `misskey_username` にフォールバック、それも NULL なら最終的に空文字 `""`。

**`resolveIndex` の役割**:

クライアント側で `/r/{poolId}/{playlistId}` を叩くには playlistId 文字列を URL に埋め込む必要があり、Udon では runtime で `VRCUrl` を作れない。そこで**サーバー側で `playlistId → poolIndex` マッピング**を提供し、クライアントは `_resolvePool[resolveIndex]` というベイク済 VRCUrl で叩けるようにする。

具体的には、本仕様では `/r/{poolId}/{playlistId}` を**廃止せず**、新たに以下を追加する:

- `pool_slots` の **`pool_id = "playlist"`** (新規 pool, **LRU 無効化 + 削除禁止 = 永久保持**) を導入。`dest_url` には **`${process.env.NEXT_PUBLIC_SITE_URL}/r/default/{playlistId}`** を格納。
- listing/search のレスポンスを返す際、各 item の `playlistId` に対して `register("playlist", "${SITE_URL}/r/default/${playlistId}")` を実行し、得られた index を `resolveIndex` として埋め込む。
- クライアントは `_resolvePool[resolveIndex]` (= ベイク済の `${SITE_URL}/vrcurl/playlist/{i}`) を叩く。サーバーは `/vrcurl/playlist/{i}` で 302 を返し、ブラウザ/クライアントは `/r/default/{playlistId}` に redirect されて元の resolve API が実行される。
- ⚠️ **`playlist` pool は LRU 無効化 + 削除禁止必須** (§5.1)。クライアントのベイク済 VRCUrl[] は配布物として固定なので、index → playlistId の対応がランタイムで上書き or 削除されると配布済ワールド全体で resolve がズレる。

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

- **ソート**:
  1. `playlists.resolve_count DESC`
  2. `updated_at DESC`
  3. **`id ASC`** (安定 tie-breaker)
- **フィルタ**: `is_public = true`
- **レスポンス**: §3.1 共通スキーマ

### 4.2 `GET /api/vrc/playlists/recent`

| 名前 | 必須 | 型 | デフォルト | 説明 |
|---|---|---|---|---|
| `p` | × | int | 0 | ページ index。最大 49 |

- **ソート**:
  1. `created_at DESC`
  2. **`id ASC`** (安定 tie-breaker)
- **フィルタ**: `is_public = true`
- **レスポンス**: §3.1 共通スキーマ

### 4.3 `GET /api/vrc/playlists/search`

| 名前 | 必須 | 型 | デフォルト | 説明 |
|---|---|---|---|---|
| `q` | ○ | string | - | 検索キーワード (URL エンコード済)。**1 文字以上 64 字以下** |
| `p` | × | int | 0 | ページ index。最大 49 |

#### バリデーション (実行順)

1. URL デコード後の `q` を取得
2. **前後の空白文字 (`\s` 相当) を trim**
3. trim 後の長さが 0 → `400`
4. trim 後の長さが 64 字超 → `400`
5. **trim 後の文字列の内部に制御文字 (`\p{Cc}`、改行・タブ・NUL 等) が含まれる → `400`**
6. NFKC 正規化 + 小文字化 → `q_norm = q_trimmed.normalize("NFKC").toLowerCase()`
7. SQL / キャッシュキーには `q_norm` を使用

正規化の効果:
- 全角/半角の差を吸収 (`カラオケ` ≡ `ｶﾗｵｹ`、互換漢字を統一)
- 大文字小文字を吸収 (`Karaoke` ≡ `karaoke`、ASCII 範囲)

```ts
// ステップ 1〜6 の実装イメージ
const qDecoded = decodeURIComponent(rawQ);
const qTrimmed = qDecoded.trim();
if (qTrimmed.length === 0 || qTrimmed.length > 64) return 400;
if (/\p{Cc}/u.test(qTrimmed)) return 400;       // trim 後内部に制御文字 → 400
const qNorm = qTrimmed.normalize("NFKC").toLowerCase();
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
  updated_at DESC,
  id ASC
```

#### その他のフィルタ

- `is_public = true`
- `p < 0` または `p > 49` → `400`

#### レスポンス

§3.1 共通スキーマ。

### 4.4 (廃案) `GET /thumb/{poolId}/{index}`

⚠️ **不採用**。当初は専用エンドポイント `/thumb/...` を想定していたが、既存 `/vrcurl/{poolId}/{index}` が既に同じ「pool slot lookup → 302 redirect」機構を提供しているため、ホワイトリスト拡張のみで賄える (§4.5)。新規パス追加なしで運用シンプル化。

### 4.5 既存 `GET /vrcurl/{poolId}/{index}` のホワイトリスト拡張 (重要)

#### 現状

`src/app/vrcurl/[poolId]/[index]/route.ts:14` で `poolId !== DEFAULT_POOL_ID` (= `"default"`) を 404 で蹴っている。

#### 修正

`/vrcurl` 専用のホワイトリスト定数として 3 つの pool ID を許可:

```ts
// src/app/vrcurl/[poolId]/[index]/route.ts
const ALLOWED_VRCURL_POOLS = new Set(["default", "playlist", "default-thumb"]);
if (!ALLOWED_VRCURL_POOLS.has(poolId)) {
  return new NextResponse(null, { status: 404 });
}
```

これにより:
- `/vrcurl/default/{i}` — 既存、動画 URL (KawaPlayer 等が利用)
- `/vrcurl/playlist/{i}` — 新規、playlist resolve URL → 302 → `/r/default/{playlistId}`
- `/vrcurl/default-thumb/{i}` — 新規、サムネ画像 URL (302 redirect)

#### `/r/{poolId}/{playlistId}` は `default` のみ維持 (共通化しない)

⚠️ **重要**: `/vrcurl` のホワイトリストを `/r/{poolId}/{playlistId}` にも流用してはならない。resolve API は意味的に `default` pool のみで利用される (`playlist` / `default-thumb` の resolve は意味なし) ため、**専用の別ホワイトリスト**として `default` のみを許可する:

```ts
// src/app/r/[poolId]/[playlistId]/route.ts
const ALLOWED_RESOLVE_POOLS = new Set(["default"]);
if (!ALLOWED_RESOLVE_POOLS.has(poolId)) {
  return new NextResponse(null, { status: 404 });
}
```

定数を `/vrcurl` 側と共通化 (lib に切り出して両方から参照) すると、`/r/playlist/...` や `/r/default-thumb/...` を意図せず受理してしまい v3 contract に反するため、**別名・別 Set として明示的に分離**すること。

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

### 4.7 新規 `GET /r/{poolId}/_validate`

⚠️ **訂正 (v3)**: 当初は「既存エンドポイント」と記載していたが、現行コード (`src/app/r/[poolId]/[playlistId]/route.ts`) には `_validate` 専用ロジックは存在しない。`/r/playlist/_validate` は `poolId !== "default"` チェックで先に 404 を返す上、仮にチェックを通っても path param `[playlistId] === "_validate"` として Hasura クエリへ流される。したがって `_validate` は**新規ルートとして追加**する必要がある。

#### 実装方針

専用ルート `src/app/r/[poolId]/_validate/route.ts` を新設:

```ts
// src/app/r/[poolId]/_validate/route.ts
const VALIDATE_ALLOWED_POOLS = new Set(["default", "playlist", "default-thumb"]);
const POOL_SIZE = 100_000;

export async function GET(
  _req: NextRequest,
  { params }: { params: Promise<{ poolId: string }> }
) {
  const { poolId } = await params;
  if (!VALIDATE_ALLOWED_POOLS.has(poolId)) {
    return NextResponse.json({ ok: false, error: "Unknown pool" }, { status: 404 });
  }
  return NextResponse.json({ ok: true, pool: poolId, poolSize: POOL_SIZE });
}
```

Next.js App Router のルート優先順位では、静的セグメント `[poolId]/_validate/` が動的セグメント `[poolId]/[playlistId]/` より優先されるため、`/r/default/_validate` は新ルートにヒットし、`/r/default/{playlistId}` は既存ルートに流れる。

#### レスポンス例

```http
GET /r/playlist/_validate          → 200 { ok: true, pool: "playlist", poolSize: 100000 }
GET /r/default-thumb/_validate     → 200 { ok: true, pool: "default-thumb", poolSize: 100000 }
GET /r/unknown/_validate           → 404 { ok: false, error: "Unknown pool" }
```

クライアント側 Editor (`PoolGenerator`) はこれを叩いて Pool ID 検証する。

## 5. Pool 設計

### 5.1 Pool ID とサイズ (LRU 無効化 + 削除禁止)

| Pool ID | 用途 | サイズ | LRU 上書き | 削除許可 |
|---|---|---|---|---|
| `default` | 既存。動画 URL の pool | 100,000 | ✅ **有効** (既存挙動) | ✅ `releaseByUrls()` で削除可 |
| `playlist` | **新規**。playlist resolve URL の pool | 100,000 | 🔴 **無効** ⚠️ | 🔴 **削除禁止** (永久保持) |
| `default-thumb` | **新規**。サムネ画像 URL の pool | 100,000 | ✅ **有効** (動画 URL と同様) | ✅ 削除可 |

すべて既存の `pool_slots` / `pool_url_index` テーブルで一元管理。`pg_advisory_xact_lock(hashtext(poolId))` の機構もそのまま流用。

#### `playlist` pool の永久保持が必要な理由

クライアントは `_resolvePool[N]` (= ベイク済 `/vrcurl/playlist/N`) を**ワールドのアセットとして永続的に持つ**。つまり「ベイク時に N → playlistId X」だった対応がランタイムで「N → playlistId Y」に上書きされる、もしくは slot 自体が削除されると:

- 配布済ワールド全員に対して、X を選んだつもりが Y が解決される (上書き) / 404 になる (削除)
- 削除後に同じ playlist が再登録されると別 index が割り当たり、過去のベイクと整合しない
- 全 VRChat ワールドが影響範囲 (グローバルな副作用、永久に直らない)

そのため `playlist` pool は **LRU 上書き禁止** + **slot 削除禁止** を両立させる必要がある (v3 改訂)。

#### (a) LRU 上書き禁止 (`register()` の変更)

容量超過時は LRU 上書きせずエラーを投げる:

```ts
const NO_LRU_POOLS = new Set(["playlist"]);

async function register(poolId: string, url: string): Promise<number> {
  // ...重複チェック...
  if (count >= POOL_SIZE) {
    if (NO_LRU_POOLS.has(poolId)) {
      throw new Error(`Pool '${poolId}' is full (LRU disabled)`);
    }
    // 既存 LRU 上書きロジック
  }
  // ...新規割り当て...
}
```

API は `500 Internal Server Error` + 運用アラート (ログレベル `error`、§8 参照)。

#### (b) slot 削除禁止 (削除パスの保護)

`playlist` pool slot は **プレイリスト削除/非公開化/cleanup-orphans 実行時にも保持**する。LRU 無効化だけでは不足するため、以下の削除パスを **`PROTECTED_POOLS` で除外**する:

| 呼び出し元 | 場所 (server 側) | 対応 |
|---|---|---|
| `releaseByUrls(urls)` | `src/lib/pool.ts` | **`pool_id = 'playlist'` を除外** (`PROTECTED_POOLS`) |
| `cleanupOrphanedVideos` | `src/app/actions.ts` | `releaseByUrls` 経由で playlist URL は対象外 (関数仕様として保証) |
| `deletePlaylist` | `src/app/actions.ts` | 現状 `pool_slots[playlist]` は触っていない (✅ 永久保持済) — 受け入れ基準で**明示的に「触らない」を保証** |
| プレイリスト非公開化 (`is_public = false`) | `actions.ts` 各所 | 同上 |
| `cleanup-orphans.ts` バッチ | `src/scripts/cleanup-orphans.ts` | `playlist` pool に拡張**しない**ことを明文化 |

実装例 (`releaseByUrls` 改修):

```ts
const PROTECTED_POOLS = new Set(["playlist"]);

export async function releaseByUrls(urls: string[]): Promise<void> {
  if (urls.length === 0) return;
  const db = getDb();
  await db.begin(async (tx) => {
    await tx`
      DELETE FROM pool_slots
      WHERE (pool_id, index) IN (
        SELECT pool_id, index FROM pool_url_index
        WHERE url = ANY(${urls})
          AND pool_id NOT IN (${[...PROTECTED_POOLS]})
      )
    `;
    await tx`
      DELETE FROM pool_url_index
      WHERE url = ANY(${urls})
        AND pool_id NOT IN (${[...PROTECTED_POOLS]})
    `;
  });
}
```

これにより `playlist` pool slot は将来どんな削除パスを通っても**残り続ける**ことが保証される。

#### LRU 容量試算

- プレイリスト総数 (公開) 想定: 〜数千〜数万。LRU 無効でも 100k で当面十分
- 動画サムネ: 動画数と同等。100k で OK
- 容量超過時の運用フローは §13 を参照

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

## 8. メトリクス / ロギング

**v1 ではログ出力のみ** (構造化ログ)。監視ダッシュボード化は別 issue で扱う。

`console.log` / `console.warn` / `console.error` で以下を構造化出力:

- `popular_request_count` / `recent_request_count` / `search_request_count` (per-request log)
- `search_query` (per-request log、`q_norm` のみ。生 `q` は記録しない)
- `vrcurl_redirect_404` per pool (404 発生時のみ)
- **`playlist` pool 容量警告**: `register()` 時に `count >= 80,000` で `console.warn`、`count >= 95,000` で `console.error`
- `popular_p99_latency_ms` 等のレイテンシは既存 `/r/...` メトリクス機構と同形式

ダッシュボード化 (`pool_slots_count_by_pool` の Grafana 連携、`search_query_top_50` の集計等) は本仕様のスコープ外。**別 issue で扱う**。

## 9. テスト

### 9.1 単体テスト

- `popular(p=0)` が pageSize 件返す
- `recent(p=49)` が空でも 200 で `items: []` が返る
- **`recent` で同 `created_at` の playlist 2 件のページング順序が `id ASC` で安定** (tie-breaker テスト)
- `search(q="")` → 400
- `search(q="   ")` (空白のみ) → 400 (trim 後空)
- `search(q="aaa..." (65字))` → 400
- `search(q="あいうえおかきくけこさしすせそたちつてとなにぬねの")` (50字) → 200
- **`search(q="abc\ndef")` (内部に改行) → 400** (trim 後内部に制御文字)
- **`search(q="カラオケ")` と `search(q="ｶﾗｵｹ")` が同じ結果セット** (NFKC 正規化テスト)
- **`search(q="Karaoke")` と `search(q="karaoke")` が同じ結果** (lowercase テスト)
- `/vrcurl/playlist/0` (空) → 404
- `/vrcurl/default-thumb/0` (空) → 404
- `/vrcurl/unknown-pool/0` → 404 (whitelist 動作)
- `/r/default/_validate` → 200 `{ ok: true, pool: "default", poolSize: 100000 }`
- `/r/playlist/_validate` → 200
- `/r/unknown/_validate` → 404
- `/r/default/{playlistId}` (resolve) は引き続き正常動作 (`_validate` ルート追加で壊れていないことの回帰テスト)
- `popular` 同一クエリを 65 回連続叩いて 65 回目で 429
- `playlist` pool 100,001 件目の register でエラー応答 (LRU 無効化テスト)
- **`releaseByUrls([playlistDestUrl])` が `pool_id='playlist'` の slot を削除しないこと** (削除禁止テスト)
- `default` pool が 100,000 に達した状態で `register("default", ...)` 呼出 → LRU 上書き成功 (既存挙動の回帰テスト)
- `users.display_name === null` のとき `ownerName === misskey_username`
- **`users.display_name === ""` のとき `ownerName === misskey_username`** (空文字フォールバックテスト)
- **`users.display_name === "  "` (空白のみ) のとき `ownerName === misskey_username`**

### 9.2 結合テスト

- listing → クライアントが `resolveIndex` 経由で `/vrcurl/playlist/{i}` を叩く → 302 で `/r/default/{playlistId}` → 既存 resolve flow → レスポンスに `id` が含まれる
- listing → `thumbIndex` 経由で `/vrcurl/default-thumb/{i}` を叩く → 302 で実サムネ URL
- 同一 playlist を複数回 listing 取得 → `resolveIndex` が変わらないこと (永久保持の保証)
- **プレイリスト削除後も `/vrcurl/playlist/{old_index}` が 302 を返し続けること** (削除禁止の保証)

## 10. ロールアウト

1. PR 作成 → maintainer レビュー
2. ステージング環境デプロイ
3. クライアント (本パッケージ) のステージング向けビルドで結合確認
4. 本番デプロイ
5. クライアントを VPM にリリース

## 11. 受け入れ基準

### 新規エンドポイント

- [ ] `/api/vrc/playlists/popular?p={page}` 実装 (ページサイズ 20、最大 page 49、`resolve_count DESC` → `updated_at DESC` → **`id ASC`** 順)
- [ ] `/api/vrc/playlists/recent?p={page}` 実装 (`created_at DESC` → **`id ASC`** 順)
- [ ] `/api/vrc/playlists/search?q={query}&p={page}` 実装 (q trim 後 1〜64 字、内部に制御文字があれば 400、NFKC + lower 正規化、4 段ソート末尾に **`id ASC`**)

### 既存エンドポイントの拡張

- [ ] `/vrcurl/{poolId}/{index}` の `ALLOWED_VRCURL_POOLS = {"default","playlist","default-thumb"}` ホワイトリスト化
- [ ] `/r/{poolId}/{playlistId}` の `ALLOWED_RESOLVE_POOLS = {"default"}` を**別名・別 Set として明示的に分離** (`/vrcurl` 側と共通化しない、resolve は default pool のみ意味を持つ)
- [ ] `/r/{poolId}/{playlistId}` レスポンスに `id` フィールド追加 (defensive 検証用、§4.6)
- [ ] **新規 `/r/{poolId}/_validate` ルート追加** (`src/app/r/[poolId]/_validate/route.ts`、§4.7)。許可 pool: `{"default","playlist","default-thumb"}`

### Pool 機構

- [ ] `pool_slots` への `pool_id = playlist` / `default-thumb` 対応 (DB スキーマは現状で OK、書き込み開始のみ)
- [ ] **`playlist` pool の LRU 無効化**: `register()` 内に `NO_LRU_POOLS = {"playlist"}` を追加、容量超過時はエラー
- [ ] **`playlist` pool slot の削除禁止**: `releaseByUrls()` 内で `PROTECTED_POOLS = {"playlist"}` を除外
- [ ] **`deletePlaylist` / 非公開化処理が `pool_slots[playlist]` を触らない** ことを明示的に検証 (現状の挙動を保証する回帰テスト)
- [ ] **`cleanup-orphans.ts` スクリプトが `pool_slots[playlist]` を触らない** ことを明示的に検証
- [ ] `register()` のテストに永久 pool ケース追加 (LRU 無効 + 削除禁止)
- [ ] **`playlist` pool 件数のログ警告** (80% で `console.warn`、95% で `console.error`)。**ダッシュボード化は別 issue**

### 共通

- [ ] レート制限 60 req/min/IP の適用 (新エンドポイント、`/vrcurl/default-thumb/...` は緩め可)
- [ ] resolve cache 同様のインメモリキャッシュ TTL 60秒
- [ ] エラーケース・バリデーション (q 長さ、p 範囲、不正 pool 等)
- [ ] **`ownerName` フォールバック**: `display_name?.trim() || misskey_username || ""` (空文字列もフォールバック対象)
- [ ] **`totalEstimated`**: Hasura `_aggregate { count }` 経由 (admin secret) で取得 (Hasura permission 変更不要)
- [ ] **`thumbIndex`**: 該当動画のサムネ URL がない場合 `-1` を返す
- [ ] **絶対 URL は `process.env.NEXT_PUBLIC_SITE_URL` ベースで構築** (`dest_url` 等の hardcode 禁止、staging/dev/本番で同コード)
- [ ] **クエリ正規化**: NFKC + lowercase を `q` の trim/長さ/制御文字バリデーション後に適用、qHash も正規化後値 (`q_norm`) で計算
- [ ] §9 のテストケースを単体/結合テストとして追加
- [ ] 結合確認: kawaplayer-playlistviewer のテストワールドから popular → resolve → サムネ表示のフローが通る

## 12. 提案する実装スコープ分割 (任意)

実装する側で sub-issue / sub-PR に分割したい場合:

1. **基盤**: 既存 `/r` `/vrcurl` の poolId ホワイトリスト拡張 + **新規 `/r/{poolId}/_validate` ルート** + `register()` の `NO_LRU_POOLS` + `releaseByUrls()` の `PROTECTED_POOLS` + resolve API レスポンスへの `id` 追加
2. **listing**: `/api/vrc/playlists/popular` + `/recent` (resolveIndex / thumbIndex / ownerName / totalEstimated / `id ASC` tie-breaker を含む)
3. **search**: `/api/vrc/playlists/search` (NFKC + lower 正規化、4 段ソート、制御文字 400)
4. **横断**: キャッシュ TTL / レート制限 / 構造化ログ (`playlist` pool 容量警告含む)

`/thumb/...` は (1) で `/vrcurl/default-thumb/...` 流用に決定済 → 独立 PR 不要。

ご相談ベースで、分割 PR にしてもまとめ PR にしても OK。

## 13. オープン課題 (Discussion)

- 検索の `pg_trgm` / GIN インデックス導入条件 (本仕様では ILIKE Seq Scan を許容、公開プレイリスト数 〜10k 程度まで)
  - 移行判断基準: search の p99 が 200ms を超えたら導入検討、もしくは公開プレイリスト数 5,000 件突破時
- **`playlists.normalized_name` を generated column 化** して `lower(name)` の毎回計算を避ける (検索高速化、`pg_trgm` 導入と同時に検討)
- listing にタグ/カテゴリでの絞り込みを追加するか？(現行 schema にタグカラムなし → 別 issue)
- thumb の WebP 配信 (CDN 経由) を入れるか？
- **`playlist` pool 容量超過時の運用フロー** (現行 100k で当面足りるが、超えた際の対応: pool サイズ拡大 / shard 化 / 別 pool への移行戦略)
- メトリクスダッシュボード化 (`pool_slots_count_by_pool`、`search_query_top_50` 等の集計と Grafana 連携) は別 issue で

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
