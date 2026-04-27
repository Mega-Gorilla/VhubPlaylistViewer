using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.StringLoading;
using VRC.Udon.Common.Interfaces;

namespace MegaGorilla.KawaPlayer.PlaylistViewer
{
    /// <summary>
    /// 詳細ビュー用に /r/{poolId}/{playlistId} を resolve する。
    /// Listing API が返す resolveIndex を使い、ベイク済 _resolvePool[index]
    /// (= /vrcurl/playlist/{i}) 経由で叩く。サーバーが 302 で /r/default/{playlistId} に redirect。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PlaylistResolver : UdonSharpBehaviour
    {
        [Header("Controller")]
        [SerializeField] private PlaylistViewerController _controller;

        [Header("Pre-baked URLs (PoolGenerator が代入する)")]
        [SerializeField] private VRCUrl[] _resolvePool = new VRCUrl[0];

        public VRCUrl[] ResolvePool => _resolvePool;

        private bool _isLoading;
        private VRCUrl _pendingUrl;
        private string _pendingPlaylistId;

        public void Resolve(int resolveIndex, string playlistId)
        {
            if (_isLoading)
            {
                ReportError("Resolve already in progress");
                return;
            }
            if (_resolvePool == null || _resolvePool.Length == 0)
            {
                ReportError("Resolve pool not generated. Run PoolGenerator.");
                return;
            }
            if (resolveIndex < 0 || resolveIndex >= _resolvePool.Length)
            {
                ReportError("Resolve index out of range");
                return;
            }
            VRCUrl url = _resolvePool[resolveIndex];
            if (!Utilities.IsValid(url) || url.Get().Length == 0)
            {
                ReportError("Resolve pool entry invalid");
                return;
            }

            _isLoading = true;
            _pendingUrl = url;
            _pendingPlaylistId = playlistId;
            VRCStringDownloader.LoadUrl(url, (IUdonEventReceiver)this);
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            if (!Utilities.IsValid(_pendingUrl) || result.Url.Get() != _pendingUrl.Get()) return;
            _isLoading = false;
            string id = _pendingPlaylistId;
            _pendingPlaylistId = null;
            if (_controller != null) _controller.OnPlaylistResolved(result.Result, id);
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            if (!Utilities.IsValid(_pendingUrl) || result.Url.Get() != _pendingUrl.Get()) return;
            _isLoading = false;
            _pendingPlaylistId = null;
            ReportError("Resolve API error: " + result.Error);
        }

        private void ReportError(string msg)
        {
            Debug.LogWarning("[PlaylistResolver] " + msg);
            if (_controller != null) _controller.OnApiError(msg);
        }
    }
}
