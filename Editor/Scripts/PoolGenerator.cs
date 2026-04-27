using System;
using System.IO;
using System.Net;
using System.Reflection;
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
        public const string POOL_ID_THUMB = "default-thumb";

        public class GenerateOptions
        {
            public string BaseUrl = DEFAULT_BASE_URL;
            public string ResolvePoolId = POOL_ID_RESOLVE;
            public string ThumbPoolId = POOL_ID_THUMB;
            public int ResolvePoolSize = 1024;
            public int ThumbPoolSize = 1024;
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

            // 2. Thumb pool: baseUrl/thumb/{ThumbPoolId}/{i}
            VRCUrl[] thumbUrls = BuildPool(baseUrl + "/thumb/" + opts.ThumbPoolId + "/{i}", opts.ThumbPoolSize);
            AssignPrivateField(thumbnailLoader, "_thumbPool", thumbUrls);

            // 3. Popular page pool: baseUrl/api/vrc/playlists/popular?p={i}
            VRCUrl[] popularUrls = BuildPool(baseUrl + "/api/vrc/playlists/popular?p={i}", opts.ListingPageCount);
            AssignPrivateField(listingClient, "_popularPagePool", popularUrls);

            // 4. Recent page pool: baseUrl/api/vrc/playlists/recent?p={i}
            VRCUrl[] recentUrls = BuildPool(baseUrl + "/api/vrc/playlists/recent?p={i}", opts.ListingPageCount);
            AssignPrivateField(listingClient, "_recentPagePool", recentUrls);

            AssetDatabase.SaveAssets();

            r.Ok = true;
            r.Message = "Generated: resolve=" + opts.ResolvePoolSize +
                ", thumb=" + opts.ThumbPoolSize +
                ", popular pages=" + opts.ListingPageCount +
                ", recent pages=" + opts.ListingPageCount;
            return r;
        }
    }
}
