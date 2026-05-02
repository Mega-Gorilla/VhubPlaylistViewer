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
    /// prefix 同期: PoolGenerator (Editor) が `Generate` 実行時に **3 箇所** を
    /// `baseUrl + /api/vrc/playlists/search?q=` で同期する (docs §13.6 step 4 + §12 #5):
    ///   1. `_expectedUrlPrefix` (string SerializeField、本クラス。runtime 検証用)
    ///   2. バインドされた `_searchInputField` (`VRCUrlInputField`) の `m_Text` (Inspector backing)
    ///   3. その `m_TextComponent` が指す child の `UnityEngine.UI.Text` (legacy) または `TMP_Text` の `m_Text`
    ///
    /// なぜ 3 箇所必要か: `VRCUrlInputField` は `TMP_InputField` を継承せず `Selectable` 直系の
    /// 独自実装で、runtime では `m_TextComponent` (子の Text/TMP) の text を URL ソースとして読む。
    /// VRCUrlInputField.m_Text 単独設定だと Play Mode 入場時に空の TextComponent.text で m_Text が
    /// 上書きされ、prefix が消える (#37 user-reported bug)。
    ///
    /// runtime からは `VRCUrlInputField.text` setter は Udon 非公開 (§12 #4) のため、
    /// ユーザーが VRChat キーボードで prefix を消した場合の自動復元は不能 →
    /// `SubmitSearch` は推奨 prefix を含む actionable error を出して再入力を促す。
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
