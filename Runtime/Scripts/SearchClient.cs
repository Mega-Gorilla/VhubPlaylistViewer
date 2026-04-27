using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.SDK3.StringLoading;
using VRC.Udon.Common.Interfaces;

namespace MegaGorilla.KawaPlayer.PlaylistViewer
{
    /// <summary>
    /// 動的に組み立てた検索 URL を VRCStringDownloader で叩く。
    /// VRCUrl はランタイム生成不可のため、ユーザーが VRCUrlInputField に入力した文字列
    /// (VRChat 内蔵キーボード経由、Copy/Paste も可) を GetUrl() で取得する経路を使う。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SearchClient : UdonSharpBehaviour
    {
        [Header("Controller")]
        [SerializeField] private PlaylistViewerController _controller;

        [Header("UI")]
        [Tooltip("ユーザーが VRChat 内蔵キーボードで入力する VRCUrlInputField。Inspector の text にプレフィックス (API URL) をプリセットしておく")]
        [SerializeField] private VRCUrlInputField _searchInputField;

        [Tooltip("URL がこの文字列で始まらない場合は不正とみなす")]
        [SerializeField] private string _expectedUrlPrefix = "https://playlist.vrc-hub.com/api/vrc/playlists/search?q=";

        private bool _isLoading;
        private VRCUrl _pendingUrl;

        public void SubmitSearch()
        {
            if (_isLoading)
            {
                ReportError("Search already in progress");
                return;
            }
            if (_searchInputField == null)
            {
                ReportError("Search input field not assigned");
                return;
            }

            VRCUrl url = _searchInputField.GetUrl();
            if (!Utilities.IsValid(url))
            {
                ReportError("Search URL is invalid");
                return;
            }
            string raw = url.Get();
            if (raw.Length <= _expectedUrlPrefix.Length || !raw.StartsWith(_expectedUrlPrefix))
            {
                ReportError("Search URL prefix mismatch — query empty or tampered");
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
            ReportError("Search API error: " + result.Error);
        }

        private void ReportError(string msg)
        {
            Debug.LogWarning("[SearchClient] " + msg);
            if (_controller != null) _controller.OnApiError(msg);
        }
    }
}
