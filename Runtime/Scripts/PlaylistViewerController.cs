using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Data;

namespace MegaGorilla.KawaPlayer.PlaylistViewer
{
    /// <summary>
    /// PlaylistViewer のメインコントローラー。状態機械、ビュー切替、子コンポーネント協調を担う。
    /// 子コンポーネント (ListingClient / SearchClient / PlaylistResolver / ThumbnailLoader / Keypad3D)
    /// は Inspector でアサインする。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PlaylistViewerController : UdonSharpBehaviour
    {
        // ----- 状態定数 -----
        public const int STATE_IDLE = 0;
        public const int STATE_LOADING = 1;
        public const int STATE_SEARCH_VIEW = 2;
        public const int STATE_DETAIL_VIEW = 3;
        public const int STATE_ERROR = 4;

        // ----- 設定 -----
        [Header("Server")]
        [SerializeField] private string _baseUrl = "https://playlist.vrc-hub.com";

        [Header("Children (アサイン必須)")]
        [SerializeField] private ListingClient _listingClient;
        [SerializeField] private SearchClient _searchClient;
        [SerializeField] private PlaylistResolver _resolver;
        [SerializeField] private ThumbnailLoader _thumbnailLoader;

        [Header("Behavior")]
        [SerializeField] private bool _autoLoadPopularOnStart = true;
        [SerializeField] private float _autoLoadDelay = 2f;
        [SerializeField] private int _pageSize = 20;

        [Header("i18n")]
        [Tooltip("CSV: lang,word,lang,word,... (\"en\" / \"ja\" など)")]
        [SerializeField] private string _trackCountUnits = "en,tracks,ja,曲";

        // ----- Hierarchy 自動バインド要素 (#-prefix) -----
        // SearchView
        private GameObject _searchView;
        private GameObject _detailView;
        private GameObject _resultListContent;
        private GameObject _resultTemplate;
        private GameObject _trackListContent;
        private GameObject _trackTemplate;
        private GameObject _loadingOverlay;
        private GameObject _errorOverlay;
        private TextMeshProUGUI _errorMessage;
        private TextMeshProUGUI _pageLabel;
        private TextMeshProUGUI _detailPlaylistName;
        private TextMeshProUGUI _detailOwnerName;
        private TextMeshProUGUI _detailTotalTracks;
        private TMP_InputField _detailUrlField;
        private Animator _animator;

        // ----- Runtime state -----
        private int _state;
        private int _currentPage;
        private string _currentTab;       // "popular" / "recent" / "search"
        private string _currentPlaylistId;
        private string _pendingOwnerName; // SelectResult 時に listing item から carry over
        private string _trackCountUnit;

        // 検索結果のキャッシュ (DataDictionary 直保持)
        private DataList _currentItems;

        // ----- Lifecycle -----

        void Start()
        {
            BindHierarchy();
            UpdateLanguageStrings();
            SetState(STATE_IDLE);

            if (_autoLoadPopularOnStart)
            {
                SendCustomEventDelayedSeconds(nameof(_AutoLoadPopular), _autoLoadDelay);
            }
        }

        public void _AutoLoadPopular()
        {
            RequestPopular(0);
        }

        // ----- Hierarchy バインド (VIB 流儀: #-prefix) -----

        private void BindHierarchy()
        {
            _animator = GetComponent<Animator>();

            Transform[] trans = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < trans.Length; i++)
            {
                Transform t = trans[i];
                if (t.name.Length == 0 || t.name[0] != '#') continue;
                switch (t.name)
                {
                    case "#SearchView": _searchView = t.gameObject; break;
                    case "#DetailView": _detailView = t.gameObject; break;
                    case "#ResultListContent": _resultListContent = t.gameObject; break;
                    case "#ResultTemplate": _resultTemplate = t.gameObject; break;
                    case "#TrackListContent": _trackListContent = t.gameObject; break;
                    case "#TrackTemplate": _trackTemplate = t.gameObject; break;
                    case "#LoadingOverlay": _loadingOverlay = t.gameObject; break;
                    case "#ErrorOverlay": _errorOverlay = t.gameObject; break;
                    case "#ErrorMessage": _errorMessage = t.GetComponent<TextMeshProUGUI>(); break;
                    case "#PageLabel": _pageLabel = t.GetComponent<TextMeshProUGUI>(); break;
                    case "#PlaylistName": _detailPlaylistName = t.GetComponent<TextMeshProUGUI>(); break;
                    case "#OwnerName": _detailOwnerName = t.GetComponent<TextMeshProUGUI>(); break;
                    // 注: detail view では #TotalTracks を使う。per-row 側の #TrackCount との衝突回避のため
                    case "#TotalTracks": _detailTotalTracks = t.GetComponent<TextMeshProUGUI>(); break;
                    case "#UrlField": _detailUrlField = t.GetComponent<TMP_InputField>(); break;
                }
            }
        }

        // ----- 状態遷移 -----

        private void SetState(int newState)
        {
            _state = newState;

            if (_loadingOverlay != null) _loadingOverlay.SetActive(newState == STATE_LOADING);
            if (_errorOverlay != null) _errorOverlay.SetActive(newState == STATE_ERROR);

            if (_animator != null)
            {
                _animator.SetBool("IsDetailView", newState == STATE_DETAIL_VIEW);
            }
        }

        // ----- Public API: タブ / ページング操作 -----

        public void OnTabPopular() { RequestPopular(0); }
        public void OnTabRecent() { RequestRecent(0); }
        public void OnTabSearch() { RequestSearch(); }
        public void OnNextPage()
        {
            if (_state == STATE_SEARCH_VIEW || _state == STATE_IDLE)
            {
                LoadPage(_currentTab, _currentPage + 1);
            }
        }
        public void OnPrevPage()
        {
            if (_currentPage > 0)
            {
                LoadPage(_currentTab, _currentPage - 1);
            }
        }
        public void OnBackToSearch()
        {
            SetState(STATE_SEARCH_VIEW);
        }

        public void RequestPopular(int page)
        {
            if (_listingClient == null) { ReportError("ListingClient not assigned"); return; }
            _currentTab = "popular";
            _currentPage = page;
            SetState(STATE_LOADING);
            _listingClient.LoadPopular(page);
        }

        public void RequestRecent(int page)
        {
            if (_listingClient == null) { ReportError("ListingClient not assigned"); return; }
            _currentTab = "recent";
            _currentPage = page;
            SetState(STATE_LOADING);
            _listingClient.LoadRecent(page);
        }

        public void RequestSearch()
        {
            if (_searchClient == null) { ReportError("SearchClient not assigned"); return; }
            _currentTab = "search";
            _currentPage = 0;
            SetState(STATE_LOADING);
            _searchClient.SubmitSearch();
        }

        private void LoadPage(string tab, int page)
        {
            if (tab == "popular") RequestPopular(page);
            else if (tab == "recent") RequestRecent(page);
            else if (tab == "search") { /* search のページングは別 issue で対応 */ }
        }

        // ----- 子からのコールバック -----

        /// <summary>
        /// ListingClient / SearchClient から呼ばれる。
        /// </summary>
        public void OnListingResultReceived(string json)
        {
            if (!ParseListingJson(json)) return;
            RenderResultList();
            SetState(STATE_SEARCH_VIEW);
        }

        /// <summary>
        /// PlaylistResolver から呼ばれる。
        /// </summary>
        public void OnPlaylistResolved(string json, string playlistId)
        {
            if (!ParseDetailJson(json, playlistId)) return;
            SetState(STATE_DETAIL_VIEW);
        }

        public void OnApiError(string message)
        {
            ReportError(message);
        }

        private void ReportError(string message)
        {
            Debug.LogWarning("[PlaylistViewer] " + message);
            if (_errorMessage != null) _errorMessage.text = message;
            SetState(STATE_ERROR);
        }

        // ----- JSON パース -----

        private bool ParseListingJson(string json)
        {
            DataToken root;
            if (!VRCJson.TryDeserializeFromJson(json, out root) || root.TokenType != TokenType.DataDictionary)
            {
                ReportError("Failed to parse listing response");
                return false;
            }
            DataDictionary rootDict = root.DataDictionary;

            DataToken okToken;
            if (rootDict.TryGetValue("ok", out okToken) && okToken.TokenType == TokenType.Boolean && !okToken.Boolean)
            {
                string err = "Server returned error";
                DataToken errToken;
                if (rootDict.TryGetValue("error", out errToken) && errToken.TokenType == TokenType.String)
                {
                    err = errToken.String;
                }
                ReportError(err);
                return false;
            }

            DataToken itemsToken;
            if (!rootDict.TryGetValue("items", out itemsToken) || itemsToken.TokenType != TokenType.DataList)
            {
                ReportError("Listing response missing items");
                return false;
            }

            _currentItems = itemsToken.DataList;

            // PageLabel 更新
            if (_pageLabel != null)
            {
                int total = -1;
                DataToken totalToken;
                if (rootDict.TryGetValue("totalEstimated", out totalToken) && totalToken.TokenType == TokenType.Double)
                {
                    total = (int)totalToken.Double;
                }
                _pageLabel.text = (_currentPage + 1).ToString() + (total > 0 ? " / " + ((total / _pageSize) + 1).ToString() : "");
            }

            return true;
        }

        private bool ParseDetailJson(string json, string playlistId)
        {
            DataToken root;
            if (!VRCJson.TryDeserializeFromJson(json, out root) || root.TokenType != TokenType.DataDictionary)
            {
                ReportError("Failed to parse playlist response");
                return false;
            }
            DataDictionary rootDict = root.DataDictionary;

            DataToken okToken;
            if (rootDict.TryGetValue("ok", out okToken) && okToken.TokenType == TokenType.Boolean && !okToken.Boolean)
            {
                ReportError("Playlist not found");
                return false;
            }

            // name
            string name = "(no name)";
            DataToken nameToken;
            if (rootDict.TryGetValue("name", out nameToken) && nameToken.TokenType == TokenType.String) name = nameToken.String;

            // tracks
            DataToken tracksToken;
            DataList tracks = null;
            if (rootDict.TryGetValue("tracks", out tracksToken) && tracksToken.TokenType == TokenType.DataList)
            {
                tracks = tracksToken.DataList;
            }

            // 詳細ビュー描画
            if (_detailPlaylistName != null) _detailPlaylistName.text = name;
            if (_detailOwnerName != null) _detailOwnerName.text = _pendingOwnerName != null ? _pendingOwnerName : "";
            if (_detailTotalTracks != null) _detailTotalTracks.text = (tracks != null ? tracks.Count.ToString() : "0") + " " + _trackCountUnit;
            if (_detailUrlField != null) _detailUrlField.text = _baseUrl + "/r/default/" + playlistId;

            _currentPlaylistId = playlistId;
            RenderTrackList(tracks);

            return true;
        }

        // ----- UI 描画 (テンプレートクローン式) -----

        private void RenderResultList()
        {
            if (_resultListContent == null || _resultTemplate == null) return;

            ClearChildrenExceptTemplate(_resultListContent.transform, _resultTemplate);

            if (_currentItems == null || _currentItems.Count == 0) return;

            RectTransform parent = (RectTransform)_resultListContent.transform;
            RectTransform tmpl = (RectTransform)_resultTemplate.transform;
            float h = tmpl.sizeDelta.y;
            Vector2 origPos = tmpl.anchoredPosition;

            for (int i = 0; i < _currentItems.Count; i++)
            {
                if (_currentItems[i].TokenType != TokenType.DataDictionary) continue;
                DataDictionary item = _currentItems[i].DataDictionary;

                GameObject row = Instantiate(_resultTemplate);
                RectTransform rt = (RectTransform)row.transform;
                rt.SetParent(parent, false);
                rt.anchoredPosition = origPos - new Vector2(0, h * i);
                row.SetActive(true);
                row.name = i.ToString();

                BindRowFields(row.transform, item);
            }
            parent.sizeDelta = new Vector2(parent.sizeDelta.x, h * _currentItems.Count);
        }

        private void BindRowFields(Transform row, DataDictionary item)
        {
            Transform[] children = row.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform c = children[i];
                if (c.name.Length == 0 || c.name[0] != '#') continue;

                switch (c.name)
                {
                    case "#Name":
                        SetTextFromItem(c, item, "name");
                        break;
                    case "#Owner":
                        SetTextFromItem(c, item, "ownerName");
                        break;
                    case "#TrackCount":
                        {
                            int n = TryGetInt(item, "trackCount", 0);
                            TextMeshProUGUI tmp = c.GetComponent<TextMeshProUGUI>();
                            if (tmp != null) tmp.text = n.ToString() + " " + _trackCountUnit;
                        }
                        break;
                    case "#Thumbnail":
                        {
                            int thumbIdx = TryGetInt(item, "thumbIndex", -1);
                            RawImage ri = c.GetComponent<RawImage>();
                            if (_thumbnailLoader != null && ri != null && thumbIdx >= 0)
                            {
                                _thumbnailLoader.LoadThumbnail(thumbIdx, ri);
                            }
                        }
                        break;
                    case "#SelectButton":
                        // Button の onClick から OnSelectResult が呼ばれる想定
                        // 行 index は GameObject 名 (= row.name) から逆引き
                        break;
                }
            }
        }

        private static void SetTextFromItem(Transform c, DataDictionary item, string key)
        {
            DataToken token;
            string val = "";
            if (item.TryGetValue(key, out token) && token.TokenType == TokenType.String) val = token.String;
            TextMeshProUGUI tmp = c.GetComponent<TextMeshProUGUI>();
            if (tmp != null) tmp.text = val;
        }

        /// <summary>
        /// 結果カードの SelectButton から呼ばれる。row index を引数 (string) で取り出すために、
        /// 各 Button の onClick で SendCustomEvent("OnSelectResultIndex0") などで分岐する想定だが、
        /// それは Udon の制約上面倒なので、prefab 側で Button の onClick から GameObject 名読みのアプローチ。
        /// この実装は v2 で改善可。
        /// </summary>
        public void OnSelectResultByName(string indexName)
        {
            int idx;
            if (!int.TryParse(indexName, out idx)) return;
            OnSelectResultByIndex(idx);
        }

        public void OnSelectResultByIndex(int rowIndex)
        {
            if (_currentItems == null || rowIndex < 0 || rowIndex >= _currentItems.Count) return;
            if (_currentItems[rowIndex].TokenType != TokenType.DataDictionary) return;
            DataDictionary item = _currentItems[rowIndex].DataDictionary;

            DataToken idToken;
            string playlistId = "";
            if (item.TryGetValue("id", out idToken) && idToken.TokenType == TokenType.String) playlistId = idToken.String;

            // ownerName は detail API のレスポンスに含まれないので、ここで listing item から拾って保存
            DataToken ownerToken;
            _pendingOwnerName = "";
            if (item.TryGetValue("ownerName", out ownerToken) && ownerToken.TokenType == TokenType.String) _pendingOwnerName = ownerToken.String;

            int resolveIndex = TryGetInt(item, "resolveIndex", -1);
            if (resolveIndex < 0 || playlistId.Length == 0)
            {
                ReportError("Invalid playlist entry");
                return;
            }

            SetState(STATE_LOADING);
            if (_resolver != null) _resolver.Resolve(resolveIndex, playlistId);
        }

        private void RenderTrackList(DataList tracks)
        {
            if (_trackListContent == null || _trackTemplate == null) return;

            ClearChildrenExceptTemplate(_trackListContent.transform, _trackTemplate);

            if (tracks == null || tracks.Count == 0) return;

            RectTransform parent = (RectTransform)_trackListContent.transform;
            RectTransform tmpl = (RectTransform)_trackTemplate.transform;
            float h = tmpl.sizeDelta.y;
            Vector2 origPos = tmpl.anchoredPosition;

            for (int i = 0; i < tracks.Count; i++)
            {
                if (tracks[i].TokenType != TokenType.DataDictionary) continue;
                DataDictionary tr = tracks[i].DataDictionary;

                GameObject row = Instantiate(_trackTemplate);
                RectTransform rt = (RectTransform)row.transform;
                rt.SetParent(parent, false);
                rt.anchoredPosition = origPos - new Vector2(0, h * i);
                row.SetActive(true);

                Transform[] cs = row.GetComponentsInChildren<Transform>(true);
                for (int j = 0; j < cs.Length; j++)
                {
                    Transform c = cs[j];
                    if (c.name == "#Position")
                    {
                        TextMeshProUGUI tmp = c.GetComponent<TextMeshProUGUI>();
                        if (tmp != null) tmp.text = (i + 1).ToString();
                    }
                    else if (c.name == "#Title")
                    {
                        DataToken titleToken;
                        string title = "";
                        if (tr.TryGetValue("title", out titleToken) && titleToken.TokenType == TokenType.String) title = titleToken.String;
                        TextMeshProUGUI tmp = c.GetComponent<TextMeshProUGUI>();
                        if (tmp != null) tmp.text = title;
                    }
                }
            }
            parent.sizeDelta = new Vector2(parent.sizeDelta.x, h * tracks.Count);
        }

        // ----- ヘルパー -----

        private static void ClearChildrenExceptTemplate(Transform parent, GameObject template)
        {
            int count = parent.childCount;
            for (int i = count - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                if (child == template) continue;
                Destroy(child);
            }
        }

        private static int TryGetInt(DataDictionary dict, string key, int defaultVal)
        {
            DataToken t;
            if (!dict.TryGetValue(key, out t)) return defaultVal;
            if (t.TokenType == TokenType.Double) return (int)t.Double;
            return defaultVal;
        }

        // ----- i18n -----

        public override void OnLanguageChanged(string language)
        {
            UpdateLanguageStrings();
        }

        private void UpdateLanguageStrings()
        {
            string lang = VRCPlayerApi.GetCurrentLanguage();
            string[] units = _trackCountUnits.Split(',');
            string unit = units.Length >= 2 ? units[1] : "tracks";
            for (int i = 0; i + 1 < units.Length; i += 2)
            {
                if (lang.IndexOf(units[i]) >= 0)
                {
                    unit = units[i + 1];
                    break;
                }
            }
            _trackCountUnit = unit;
        }
    }
}
