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

        public void LoadPopular(int page) { LoadInternal(_popularPagePool, page); }
        public void LoadRecent(int page) { LoadInternal(_recentPagePool, page); }

        /// <summary>
        /// /api/vrc/news?p=0 を fetch する (V1 single page、vhub-playlist#97)。
        /// </summary>
        public void LoadNews()
        {
            if (_isLoading)
            {
                ReportError("Listing already loading");
                return;
            }
            if (!Utilities.IsValid(_newsUrl) || _newsUrl.Get().Length == 0)
            {
                ReportError("News URL not baked. Run PoolGenerator.");
                return;
            }
            _isLoading = true;
            _pendingUrl = _newsUrl;
            VRCStringDownloader.LoadUrl(_newsUrl, (IUdonEventReceiver)this);
        }

        private void LoadInternal(VRCUrl[] pool, int page)
        {
            if (_isLoading)
            {
                ReportError("Listing already loading");
                return;
            }
            if (pool == null || pool.Length == 0)
            {
                ReportError("Listing pool not generated. Run PoolGenerator.");
                return;
            }
            if (page < 0 || page >= pool.Length)
            {
                ReportError("Page out of range");
                return;
            }
            VRCUrl url = pool[page];
            if (!Utilities.IsValid(url) || url.Get().Length == 0)
            {
                ReportError("Listing pool entry invalid");
                return;
            }

            _isLoading = true;
            _pendingUrl = url;
            VRCStringDownloader.LoadUrl(url, (IUdonEventReceiver)this);
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            if (!Utilities.IsValid(_pendingUrl) || result.Url.Get() != _pendingUrl.Get()) return;
            _isLoading = false;
            if (_controller != null) _controller.OnListingResultReceived(result.Result);
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            if (!Utilities.IsValid(_pendingUrl) || result.Url.Get() != _pendingUrl.Get()) return;
            _isLoading = false;
            ReportError("Listing API error: " + result.Error);
        }

        private void ReportError(string msg)
        {
            Debug.LogWarning("[ListingClient] " + msg);
            if (_controller != null) _controller.OnApiError(msg);
        }
    }
}
