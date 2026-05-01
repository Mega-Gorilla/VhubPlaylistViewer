using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.StringLoading;
using VRC.Udon.Common.Interfaces;

namespace MegaGorilla.KawaPlayer.PlaylistViewer
{
    /// <summary>
    /// /api/vrc/playlists/popular?p={N} と /api/vrc/playlists/recent?p={N} のページ単位 GET、
    /// および /api/vrc/news?p=0 (vhub-playlist#97 / PR #99 v4 #3、V1 は p=0 のみサポート) の GET。
    /// 事前ベイクされた _popularPagePool / _recentPagePool / _newsUrl から VRCUrl を取得する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ListingClient : UdonSharpBehaviour
    {
        [Header("Controller")]
        [SerializeField] private PlaylistViewerController _controller;

        [Header("Pre-baked URLs (PoolGenerator が代入する)")]
        [SerializeField] private VRCUrl[] _popularPagePool = new VRCUrl[0];
        [SerializeField] private VRCUrl[] _recentPagePool = new VRCUrl[0];
        [Tooltip("News API は V1 で p=0 のみサポート (vhub-playlist#97)、配列ではなく単一 VRCUrl")]
        [SerializeField] private VRCUrl _newsUrl = VRCUrl.Empty;

        public VRCUrl[] PopularPagePool => _popularPagePool;
        public VRCUrl[] RecentPagePool => _recentPagePool;
        public VRCUrl NewsUrl => _newsUrl;

        private bool _isLoading;
        private VRCUrl _pendingUrl;
        private string _pendingKind = ""; // "popular" / "recent" / "news"、response 配信時に kind を controller へ渡す

        /// <summary>
        /// 受理: true、busy / 不正 (pool 未生成 / 範囲外 / VRCUrl 無効) で reject: false。
        /// 呼び出し元 (Controller) は **true 受理後にのみ** `_currentTab` / state を更新すること
        /// (PR #35 review: 連打 / 並行 client で response が誤 tab visual で render されるのを防ぐため)。
        /// </summary>
        public bool LoadPopular(int page) { return LoadInternal(_popularPagePool, page, "popular"); }
        public bool LoadRecent(int page) { return LoadInternal(_recentPagePool, page, "recent"); }

        /// <summary>
        /// /api/vrc/news?p=0 を fetch する (V1 single page、vhub-playlist#97)。
        /// 戻り値 semantics は LoadPopular/Recent と同じ。
        /// </summary>
        public bool LoadNews()
        {
            if (_isLoading)
            {
                ReportError("Listing already loading");
                return false;
            }
            if (!Utilities.IsValid(_newsUrl) || _newsUrl.Get().Length == 0)
            {
                ReportError("News URL not baked. Run PoolGenerator.");
                return false;
            }
            _isLoading = true;
            _pendingUrl = _newsUrl;
            _pendingKind = "news";
            VRCStringDownloader.LoadUrl(_newsUrl, (IUdonEventReceiver)this);
            return true;
        }

        private bool LoadInternal(VRCUrl[] pool, int page, string kind)
        {
            if (_isLoading)
            {
                ReportError("Listing already loading");
                return false;
            }
            if (pool == null || pool.Length == 0)
            {
                ReportError("Listing pool not generated. Run PoolGenerator.");
                return false;
            }
            if (page < 0 || page >= pool.Length)
            {
                ReportError("Page out of range");
                return false;
            }
            VRCUrl url = pool[page];
            if (!Utilities.IsValid(url) || url.Get().Length == 0)
            {
                ReportError("Listing pool entry invalid");
                return false;
            }

            _isLoading = true;
            _pendingUrl = url;
            _pendingKind = kind;
            VRCStringDownloader.LoadUrl(url, (IUdonEventReceiver)this);
            return true;
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            if (!Utilities.IsValid(_pendingUrl) || result.Url.Get() != _pendingUrl.Get()) return;
            _isLoading = false;
            string kind = _pendingKind;
            _pendingKind = "";
            if (_controller != null) _controller.OnListingResultReceived(result.Result, kind);
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            if (!Utilities.IsValid(_pendingUrl) || result.Url.Get() != _pendingUrl.Get()) return;
            _isLoading = false;
            _pendingKind = "";
            ReportError("Listing API error: " + result.Error);
        }

        private void ReportError(string msg)
        {
            Debug.LogWarning("[ListingClient] " + msg);
            if (_controller != null) _controller.OnApiError(msg);
        }
    }
}
