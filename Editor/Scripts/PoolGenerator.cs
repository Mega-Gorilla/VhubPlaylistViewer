using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;

namespace MegaGorilla.KawaPlayer.PlaylistViewer.Editor
{
    /// <summary>
    /// 4 種類の VRCUrl[] をベイク生成する Editor ロジック。KawaPlayer PlaylistLoaderEditor の
    /// リフレクション代入パターンを踏襲。
    /// </summary>
    public static class PoolGenerator
    {
        public const string DEFAULT_BASE_URL = "https://playlist.vrc-hub.com";
        public const string POOL_ID_RESOLVE = "playlist";

        public class GenerateOptions
        {
            public string BaseUrl = DEFAULT_BASE_URL;
            public string ResolvePoolId = POOL_ID_RESOLVE;
            public int ResolvePoolSize = 1024;
            public int ListingPageCount = 50;     // popular / recent 共通
        }

        public class Result
        {
            public bool Ok;
            public string Message;
        }

        // ---------- バリデーション ----------

        public static bool ValidatePoolId(string baseUrl, string poolId, out string message)
        {
            message = "";
            if (string.IsNullOrEmpty(baseUrl) || !(baseUrl.StartsWith("http://") || baseUrl.StartsWith("https://")))
            {
                message = "Base URL must start with http:// or https://";
                return false;
            }
            if (string.IsNullOrEmpty(poolId))
            {
                message = "Pool ID must not be empty";
                return false;
            }

            try
            {
                HttpWebRequest request = WebRequest.Create(baseUrl + "/r/" + poolId + "/_validate") as HttpWebRequest;
                if (request == null) { message = "Failed to create request"; return false; }
                request.Timeout = 5000;
                request.UserAgent = "KawaPlayerPlaylistViewer-PoolGenerator/1.0";

                using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                {
                    string body = reader.ReadToEnd();
                    if (body.Contains("Unknown pool"))
                    {
                        message = "Pool ID '" + poolId + "' does not exist on server " + baseUrl;
                        return false;
                    }
                    return true;
                }
            }
            catch (WebException wex)
            {
                if (wex.Response is HttpWebResponse httpRes)
                {
                    int code = (int)httpRes.StatusCode;
                    message = "Server returned " + code + " for /r/" + poolId + "/_validate";
                }
                else
                {
                    message = "Network error: " + wex.Message;
                }
                return false;
            }
            catch (Exception ex)
            {
                message = "Validation failed: " + ex.Message;
                return false;
            }
        }

        // ---------- 生成 ----------

        public static VRCUrl[] BuildPool(string urlTemplate, int count)
        {
            VRCUrl[] urls = new VRCUrl[count];
            for (int i = 0; i < count; i++)
            {
                urls[i] = new VRCUrl(urlTemplate.Replace("{i}", i.ToString()));
            }
            return urls;
        }

        /// <summary>
        /// vhub-playlist#92 v4: /api/vrc/yt-thumb-direct-baking?cursor=N で
        /// 全 (index, url) を fetch し、URL は i.ytimg.com 直接 (trusted host)。
        /// 失敗時は null + message。成功時は VRCUrl[] (i.ytimg.com 直接 URL の baked array)。
        ///
        /// runtime は listing JSON の items[i].ytThumbIndex を _ytThumbPool[ytThumbIndex] への
        /// 直接 index として使うので、server から返された index を **そのスロットに保存** する必要がある
        /// (response の到着順ではない)。server pool が sparse、1 始まり、register 順 != index 順 等の
        /// ケースで wrong thumb / gray fallback を防ぐためのキー実装。欠番は new VRCUrl("") で埋める。
        /// </summary>
        public static VRCUrl[] FetchYtThumbDirectPool(string baseUrl, out string message)
        {
            message = "";
            var indexedUrls = new Dictionary<int, string>();
            int maxIndex = -1;
            int cursor = 0;
            const int MAX_PAGES = 100; // 最大 100 page × pageSize 1000 = 100k 件、暴走防止
            int safety = MAX_PAGES;
            bool finishedNaturally = false;

            while (safety-- > 0)
            {
                string endpoint = baseUrl + "/api/vrc/yt-thumb-direct-baking?cursor=" + cursor + "&pageSize=1000";
                try
                {
                    HttpWebRequest req = WebRequest.Create(endpoint) as HttpWebRequest;
                    if (req == null) { message = "Failed to create request for " + endpoint; return null; }
                    req.Timeout = 10000;
                    req.UserAgent = "KawaPlayerPlaylistViewer-PoolGenerator/1.0";

                    using (HttpWebResponse res = req.GetResponse() as HttpWebResponse)
                    using (StreamReader reader = new StreamReader(res.GetResponseStream()))
                    {
                        string body = reader.ReadToEnd();
                        var jObj = Newtonsoft.Json.Linq.JObject.Parse(body);
                        var items = jObj["items"] as Newtonsoft.Json.Linq.JArray;
                        if (items != null)
                        {
                            foreach (var item in items)
                            {
                                var indexToken = item["index"];
                                var urlToken = item["url"];
                                if (indexToken == null || urlToken == null) continue;

                                int idx = indexToken.ToObject<int>();
                                string url = urlToken.ToString();
                                if (idx < 0)
                                {
                                    message = "Server returned negative index " + idx + " at cursor " + cursor;
                                    return null;
                                }
                                if (string.IsNullOrEmpty(url)) continue;

                                string existing;
                                if (indexedUrls.TryGetValue(idx, out existing) && existing != url)
                                {
                                    message = "Duplicate index " + idx + " with conflicting URLs: '" +
                                              existing + "' vs '" + url + "'";
                                    return null;
                                }
                                indexedUrls[idx] = url;
                                if (idx > maxIndex) maxIndex = idx;
                            }
                        }
                        var nextCursor = jObj["nextCursor"];
                        if (nextCursor == null || nextCursor.Type == Newtonsoft.Json.Linq.JTokenType.Null)
                        {
                            finishedNaturally = true;
                            break;
                        }
                        cursor = nextCursor.ToObject<int>();
                    }
                }
                catch (Exception ex)
                {
                    message = "Fetch failed at cursor " + cursor + ": " + ex.Message;
                    return null;
                }
            }

            // safety 枯渇 + nextCursor 残 = partial pool。silent return ではなく fail closed。
            // (server 側 ensure re-throw と整合する fail-closed pattern。本来発生し得ないが defense-in-depth)
            if (!finishedNaturally)
            {
                message = "Safety limit reached (" + MAX_PAGES + " pages × pageSize=1000 = " +
                          (MAX_PAGES * 1000) + " items) but server still returned nextCursor=" +
                          cursor + ". Refusing to bake a partial pool.";
                return null;
            }

            // index 0..maxIndex の dense array を作る。欠番は new VRCUrl("") (runtime 側で empty 判定 → dummy)。
            VRCUrl[] result = new VRCUrl[maxIndex + 1];
            for (int i = 0; i <= maxIndex; i++)
            {
                string url;
                result[i] = indexedUrls.TryGetValue(i, out url) ? new VRCUrl(url) : new VRCUrl("");
            }
            return result;
        }

        // ---------- リフレクション代入 ----------

        public static void AssignPrivateField(UnityEngine.Object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
            {
                throw new Exception("Field not found: " + fieldName + " on " + target.GetType().Name);
            }
            field.SetValue(target, value);
            EditorUtility.SetDirty(target);

            // Proxy 上の SerializeField 書き込みを **背後の UdonBehaviour にも sync** する。
            // これがないと runtime (Play Mode / VR build) では UdonBehaviour の publicVariables が
            // 古い値のままで、proxy 経由でセットした VRCUrl[] や string が反映されない (docs §13.6 参照)。
            // 既存 5 pool 代入 (resolve / yt-thumb / popular / recent / news) すべてに必要 (#37 review)。
            if (target is UdonSharpBehaviour usb)
            {
                UdonSharpEditorUtility.CopyProxyToUdon(usb);
            }
        }

        // ---------- バッチ生成 ----------

        public static Result GenerateAll(
            PlaylistViewerController controller,
            ListingClient listingClient,
            PlaylistResolver resolver,
            ThumbnailLoader thumbnailLoader,
            GenerateOptions opts)
        {
            Result r = new Result();

            if (controller == null) { r.Message = "Controller is null"; return r; }
            if (listingClient == null) { r.Message = "ListingClient is null"; return r; }
            if (resolver == null) { r.Message = "PlaylistResolver is null"; return r; }
            if (thumbnailLoader == null) { r.Message = "ThumbnailLoader is null"; return r; }

            string baseUrl = opts.BaseUrl.TrimEnd('/');

            // 1. Resolve pool: baseUrl/vrcurl/{ResolvePoolId}/{i}
            VRCUrl[] resolveUrls = BuildPool(baseUrl + "/vrcurl/" + opts.ResolvePoolId + "/{i}", opts.ResolvePoolSize);
            AssignPrivateField(resolver, "_resolvePool", resolveUrls);

            // 2. yt-thumb-direct pool: baseUrl/api/vrc/yt-thumb-direct-baking?cursor=N で全 (index, url) を fetch
            //    (vhub-playlist#92 v4: i.ytimg.com 直接 URL を baked、VRCImageDownloader で trusted host から redirect なしで取得)
            //    旧 thumbIndex (default-thumb pool 経由 /vrcurl/default-thumb/{i}) は redirect 不可で動作しないため廃止
            VRCUrl[] ytThumbUrls = FetchYtThumbDirectPool(baseUrl, out string ytThumbMessage);
            if (ytThumbUrls == null)
            {
                r.Message = "yt-thumb-direct fetch failed: " + ytThumbMessage;
                return r;
            }
            AssignPrivateField(thumbnailLoader, "_ytThumbPool", ytThumbUrls);

            // 3. Popular page pool: baseUrl/api/vrc/playlists/popular?p={i}
            VRCUrl[] popularUrls = BuildPool(baseUrl + "/api/vrc/playlists/popular?p={i}", opts.ListingPageCount);
            AssignPrivateField(listingClient, "_popularPagePool", popularUrls);

            // 4. Recent page pool: baseUrl/api/vrc/playlists/recent?p={i}
            VRCUrl[] recentUrls = BuildPool(baseUrl + "/api/vrc/playlists/recent?p={i}", opts.ListingPageCount);
            AssignPrivateField(listingClient, "_recentPagePool", recentUrls);

            // 5. News URL: baseUrl/api/vrc/news?p=0 (vhub-playlist#97 / PR #99 v4 #3、V1 は p=0 のみ)
            //    pool ではなく単一 VRCUrl (paging guard により p=1 以降は server が 400 を返す)
            VRCUrl newsUrl = new VRCUrl(baseUrl + "/api/vrc/news?p=0");
            AssignPrivateField(listingClient, "_newsUrl", newsUrl);

            // #38: Search prefix sync (旧 section 6) を撤去。
            // SearchClient と in-VRChat free-form search は Web 誘導 UI に置換 (#SearchView/#SearchBar
            // 内の `#WebSearchHintButton` + `#WebSearchUrlField`)。Web URL は Controller.Start で
            // `_baseUrl + "/"` を runtime sync するため、PoolGenerator-bake 不要。

            AssetDatabase.SaveAssets();

            r.Ok = true;
            r.Message = "Generated: resolve=" + opts.ResolvePoolSize +
                ", yt-thumb-direct=" + ytThumbUrls.Length + " (fetched from server)" +
                ", popular pages=" + opts.ListingPageCount +
                ", recent pages=" + opts.ListingPageCount +
                ", news=1 (single page, vhub-playlist#97)";
            return r;
        }
    }
}
