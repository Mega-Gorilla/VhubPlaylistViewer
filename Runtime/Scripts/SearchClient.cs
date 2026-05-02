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
    ///
    /// prefix 同期: PoolGenerator (Editor) が `Generate` 実行時に `_expectedUrlPrefix` (string) と
    /// バインドされた `_searchInputField` の `m_Text` (TMP_InputField の Inspector backing) の
    /// 両方を `baseUrl + /api/vrc/playlists/search?q=` で同期する (docs §13.6 step 4)。
    /// runtime からは `VRCUrlInputField.text` setter は Udon 非公開 (§12 #4) のため復元不能。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SearchClient : UdonSharpBehaviour
    {
        [Header("Controller")]
        [SerializeField] private PlaylistViewerController _controller;

        [Header("UI")]
        [Tooltip("ユーザーが VRChat 内蔵キーボードで入力する VRCUrlInputField。PoolGenerator (Tools > VHub PlaylistViewer > Generate Pools) が text プレフィックスを baseUrl から自動同期するため、Inspector 手動編集は不要")]
        [SerializeField] private VRCUrlInputField _searchInputField;

        [Tooltip("URL がこの文字列で始まらない場合は不正とみなす。PoolGenerator が baseUrl から自動同期するため、Inspector 手動編集は不要 (Generate 未実行時の fallback default のみ意味あり)")]
        [SerializeField] private string _expectedUrlPrefix = "https://playlist.vrc-hub.com/api/vrc/playlists/search?q=";

        private bool _isLoading;
        private VRCUrl _pendingUrl;

        /// <summary>
        /// 受理: true、busy / input 不正で reject: false。呼び出し元 (Controller) は
        /// **true 受理後にのみ** `_currentTab` / state を更新する (PR #35 review)。
        /// </summary>
        public bool SubmitSearch()
        {
            if (_isLoading)
            {
                ReportError("Search already in progress");
                return false;
            }
            if (_searchInputField == null)
            {
                ReportError("Search input field not assigned");
                return false;
            }

            VRCUrl url = _searchInputField.GetUrl();
            if (!Utilities.IsValid(url))
            {
                ReportError("Search URL is invalid");
                return false;
            }
            string raw = url.Get();
            if (raw.Length <= _expectedUrlPrefix.Length || !raw.StartsWith(_expectedUrlPrefix))
            {
                // VRCUrlInputField.text setter は Udon 非公開 (docs §12 #4) のため、ここで自動復元はできない。
                // ユーザーに具体的な prefix を見せて再入力を促す actionable error を出す。
                ReportError("検索 URL が無効です。検索欄をタップして '" + _expectedUrlPrefix + "' から始まる URL を入力してください");
                return false;
            }

            _isLoading = true;
            _pendingUrl = url;
            VRCStringDownloader.LoadUrl(url, (IUdonEventReceiver)this);
            return true;
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            if (!Utilities.IsValid(_pendingUrl) || result.Url.Get() != _pendingUrl.Get()) return;
            _isLoading = false;
            // SearchClient は search 専用、kind は固定 (PR #35 review、kind tagging で stale 判定)
            if (_controller != null) _controller.OnListingResultReceived(result.Result, "search");
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
