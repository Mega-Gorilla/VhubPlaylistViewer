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
    /// (= /vrcurl/playlist/{i}) 経由で叩く。
    /// vhub-playlist#91 (v4) 以降、サーバーは 302 redirect ではなく **200 JSON 直接** を返す
    /// (VRCStringDownloader が redirect follow しないため)。レスポンスの id field は呼び出し元で
    /// 検証する (PlaylistViewerController.ParseDetailJson 参照)。
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

        /// <summary>
        /// resolveIndex / playlistId に対する resolve を開始する。
        /// **戻り値**: 受理して LoadUrl を発行できたら true、busy / プール未生成 / 範囲外 /
        /// 不正な VRCUrl で reject したら false。呼び出し元 (Controller.OnSelectResultByIndex)
        /// は **true 受理時にのみ** `_pendingOwnerName` / `_pendingYtThumbIndex` の更新を行うこと
        /// (一覧で別行を連打した際に in-flight resolve と pending メタが desync するのを防ぐため、
        /// レビュー指摘で導入、PR #34)。
        /// </summary>
        public bool Resolve(int resolveIndex, string playlistId)
        {
            if (_isLoading)
            {
                ReportError("Resolve already in progress");
                return false;
            }
            if (_resolvePool == null || _resolvePool.Length == 0)
            {
                ReportError("Resolve pool not generated. Run PoolGenerator.");
                return false;
            }
            if (resolveIndex < 0 || resolveIndex >= _resolvePool.Length)
            {
                ReportError("Resolve index out of range");
                return false;
            }
            VRCUrl url = _resolvePool[resolveIndex];
            if (!Utilities.IsValid(url) || url.Get().Length == 0)
            {
                ReportError("Resolve pool entry invalid");
                return false;
            }

            _isLoading = true;
            _pendingUrl = url;
            _pendingPlaylistId = playlistId;
            VRCStringDownloader.LoadUrl(url, (IUdonEventReceiver)this);
            return true;
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
