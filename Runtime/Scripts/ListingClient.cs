using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.StringLoading;
using VRC.Udon.Common.Interfaces;

namespace MegaGorilla.KawaPlayer.PlaylistViewer
{
    /// <summary>
    /// /api/vrc/playlists/popular?p={N} と /api/vrc/playlists/recent?p={N} のページ単位 GET。
    /// 事前ベイクされた _popularPagePool / _recentPagePool から index 引きで VRCUrl を取得する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ListingClient : UdonSharpBehaviour
    {
        [Header("Controller")]
        [SerializeField] private PlaylistViewerController _controller;

        [Header("Pre-baked URLs (PoolGenerator が代入する)")]
        [SerializeField] private VRCUrl[] _popularPagePool = new VRCUrl[0];
        [SerializeField] private VRCUrl[] _recentPagePool = new VRCUrl[0];

        public VRCUrl[] PopularPagePool => _popularPagePool;
        public VRCUrl[] RecentPagePool => _recentPagePool;

        private bool _isLoading;
        private VRCUrl _pendingUrl;

        public void LoadPopular(int page) { LoadInternal(_popularPagePool, page); }
        public void LoadRecent(int page) { LoadInternal(_recentPagePool, page); }

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
