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
        [Tooltip("Error overlay 表示後、この秒数経過で自動的に SearchView に復帰する")]
        [SerializeField] private float _errorAutoDismissSeconds = 5f;
        [SerializeField] private int _pageSize = 20;

        [Header("Detail view text length limits (TMP overflow bug 回避、docs/unity-architecture.md §13.1 参照)")]
        [Tooltip("詳細表示の playlist 名最大文字数。超過分は末尾を「…」で省略。rect 728 + fontSize 56 で全角 13")]
        [SerializeField] private int _detailNameMaxChars = 15;
        [Tooltip("詳細表示の owner 名最大文字数。超過分は末尾を「…」で省略。rect 384 + fontSize 28 で全角 13.7")]
        [SerializeField] private int _detailOwnerMaxChars = 15;
        [Tooltip("track 一覧の各 title 最大文字数。超過分は末尾を「…」で省略。rect 658 + fontSize 36 で全角 18 + margin")]
        [SerializeField] private int _trackTitleMaxChars = 22;

        [Header("Result rows (Pre-allocated, 20 行を prefab に物理配置して各行に ResultRow をアタッチ)")]
        [Tooltip("固定 20 行の ResultRow 参照。prefab で 0..19 の順に並べる")]
        [SerializeField] private ResultRow[] _resultRows = new ResultRow[0];

        [Header("i18n")]
        [Tooltip("CSV: lang,word,lang,word,... (\"en\" / \"ja\" など)")]
        [SerializeField] private string _trackCountUnits = "en,tracks,ja,曲";

        // ----- Theme (#23 §0) -----
        [Header("Theme (#23 §0)")]
        [Tooltip("選択中タブ / アクションボタン色 (default: #4A90E2)")]
        [SerializeField] private Color _primaryColor = new Color(0.29f, 0.56f, 0.89f, 1f);
        [Tooltip("カード / 非選択タブの背景 tint (white α=0.08)")]
        [SerializeField] private Color _surfaceColor = new Color(1f, 1f, 1f, 0.08f);
        [Tooltip("ResultRow hover 時の背景 tint (white α=0.16)")]
        [SerializeField] private Color _surfaceHoverColor = new Color(1f, 1f, 1f, 0.16f);
        [Tooltip("Loading / Error overlay の背景色 (dark navy α=0.85)")]
        [SerializeField] private Color _overlayColor = new Color(13f / 255f, 18f / 255f, 30f / 255f, 0.85f);
        [Tooltip("主要 text 色 (white)")]
        [SerializeField] private Color _textPrimaryColor = Color.white;
        [Tooltip("補足 text 色 (white α=0.6)")]
        [SerializeField] private Color _textMutedColor = new Color(1f, 1f, 1f, 0.6f);
        [Tooltip("エラー表示色 (#E55353)")]
        [SerializeField] private Color _errorColor = new Color(0.9f, 0.33f, 0.33f, 1f);

        [Header("Theme apply targets")]
        [Tooltip("Tab background Image。active 時に Primary、inactive 時に Surface へ tint")]
        [SerializeField] private Image _tabPopularBg;
        [SerializeField] private Image _tabRecentBg;
        [SerializeField] private Image _tabSearchBg;
        [Tooltip("Surface 色を当てる panel Image 群 (例: card 背景、UrlField 背景 など)")]
        [SerializeField] private Image[] _surfacePanels;
        [Tooltip("Overlay 色を当てる Image 群 (#LoadingOverlay / #ErrorOverlay の半透明背景)")]
        [SerializeField] private Image[] _overlayPanels;
        [Tooltip("Error icon (Phosphor warning) の Image。errorColor で tint")]
        [SerializeField] private Image _errorIcon;

        // 公開アクセサ (ResultRow 等が読む)
        public Color PrimaryColor => _primaryColor;
        public Color SurfaceColor => _surfaceColor;
        public Color SurfaceHoverColor => _surfaceHoverColor;
        public Color TextPrimaryColor => _textPrimaryColor;
        public Color TextMutedColor => _textMutedColor;

        // ----- Hierarchy 自動バインド要素 (#-prefix) -----
        // SearchView (#ResultListContent / #ResultTemplate は廃止: ResultRow Pre-allocated 方式)
        private GameObject _searchView;
        private GameObject _detailView;
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
        private RawImage _detailPlaylistThumbnail; // Phase A-4: cover art (#PlaylistThumbnail)
        private Animator _animator;

        // ----- Runtime state -----
        private int _state;
        private int _currentPage;
        private string _currentTab;       // "popular" / "recent" / "search"
        private int _activeTabIndex = -1; // 0=Popular / 1=Recent / 2=Search、UpdateTabVisuals が読む
        private string _currentPlaylistId;
        private string _pendingOwnerName;     // SelectResult 時に listing item から carry over
        private int _pendingYtThumbIndex = -1; // 同上、Phase A-4 で DetailView の cover art に使用
        private string _trackCountUnit;

        // 検索結果のキャッシュ (DataDictionary 直保持)
        private DataList _currentItems;

        // ----- Lifecycle -----

        void Start()
        {
            BindHierarchy();
            UpdateLanguageStrings();
            ApplyThemeOnStart();
            SetState(STATE_IDLE);

            if (_autoLoadPopularOnStart)
            {
                SendCustomEventDelayedSeconds(nameof(_AutoLoadPopular), _autoLoadDelay);
            }
        }

        /// <summary>
        /// Inspector で wire された各 Image / panel を、Theme color group の値で初期 tint する。
        /// active tab の visual も併せて更新 (#23 Phase A)。
        /// </summary>
        private void ApplyThemeOnStart()
        {
            if (_surfacePanels != null)
            {
                for (int i = 0; i < _surfacePanels.Length; i++)
                {
                    if (_surfacePanels[i] != null) _surfacePanels[i].color = _surfaceColor;
                }
            }
            if (_overlayPanels != null)
            {
                for (int i = 0; i < _overlayPanels.Length; i++)
                {
                    if (_overlayPanels[i] != null) _overlayPanels[i].color = _overlayColor;
                }
            }
            if (_errorIcon != null) _errorIcon.color = _errorColor;
            UpdateTabVisuals();
        }

        /// <summary>
        /// _activeTabIndex に基づき 3 タブの背景色を Primary / Surface に振り分ける (#23 Phase A)。
        /// _activeTabIndex == -1 (起動直後、まだどのタブも触っていない) の場合は全タブ Surface。
        /// </summary>
        private void UpdateTabVisuals()
        {
            if (_tabPopularBg != null) _tabPopularBg.color = (_activeTabIndex == 0) ? _primaryColor : _surfaceColor;
            if (_tabRecentBg != null)  _tabRecentBg.color  = (_activeTabIndex == 1) ? _primaryColor : _surfaceColor;
            if (_tabSearchBg != null)  _tabSearchBg.color  = (_activeTabIndex == 2) ? _primaryColor : _surfaceColor;
        }

        public void _AutoLoadPopular()
        {
            RequestPopular(0);
        }

        // ----- Hierarchy バインド (VIB 流儀: #-prefix) -----

        private void BindHierarchy()
        {
            _animator = GetComponent<Animator>();

            // Controller GameObject が #-prefix children の親に居ない構成 (例: Canvas の sibling として
            // PlaylistViewer/Controller に配置され、#SearchView 等が PlaylistViewer/Canvas/... の子)
            // でも bind できるよう、transform.parent (= prefab root) から全スキャンする。
            // Controller が prefab root (parent==null) の場合は自身から (=従来挙動)。
            Transform searchRoot = transform.parent != null ? transform.parent : transform;
            Transform[] trans = searchRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < trans.Length; i++)
            {
                Transform t = trans[i];
                if (t.name.Length == 0 || t.name[0] != '#') continue;
                switch (t.name)
                {
                    case "#SearchView": _searchView = t.gameObject; break;
                    case "#DetailView": _detailView = t.gameObject; break;
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
                    case "#PlaylistThumbnail": _detailPlaylistThumbnail = t.GetComponent<RawImage>(); break;
                }
            }
        }

        // ----- 状態遷移 -----

        private void SetState(int newState)
        {
            _state = newState;

            if (_loadingOverlay != null) _loadingOverlay.SetActive(newState == STATE_LOADING);
            if (_errorOverlay != null) _errorOverlay.SetActive(newState == STATE_ERROR);

            // View 排他切替: LOADING/ERROR は overlay でカバーするので前の view を保持。
            // Animator (#13) 配置後も SetActive で表示自体を制御し、Animator は α fade 等の演出を担当する想定。
            if (newState != STATE_LOADING && newState != STATE_ERROR)
            {
                bool detailVisible = (newState == STATE_DETAIL_VIEW);
                if (_searchView != null) _searchView.SetActive(!detailVisible);
                if (_detailView != null) _detailView.SetActive(detailVisible);
            }

            if (_animator != null)
            {
                _animator.SetBool("IsDetailView", newState == STATE_DETAIL_VIEW);
            }

            // ERROR overlay は数秒後に自動で消す (UX 改善: ユーザー操作不要で recovery)。
            // 重複 schedule は state チェックで no-op になるため安全。
            if (newState == STATE_ERROR)
            {
                SendCustomEventDelayedSeconds(nameof(_AutoDismissError), _errorAutoDismissSeconds);
            }
        }

        /// <summary>
        /// SendCustomEventDelayedSeconds から呼ばれる。STATE_ERROR から自動で SearchView へ復帰する。
        /// 既に他 state に遷移済なら no-op (新エラー発生時の重複呼出に安全)。
        /// </summary>
        public void _AutoDismissError()
        {
            if (_state != STATE_ERROR) return;
            SetState(STATE_SEARCH_VIEW);
        }

        /// <summary>
        /// VRChat の TMP で overflow=Ellipsis / Truncate が頂点ゼロ bug を起こすため、
        /// C# 側で text を最大文字数で切り捨てて末尾に「…」(U+2026) を付加する。
        /// 詳細: docs/unity-architecture.md §13.1
        /// </summary>
        private string TruncateString(string s, int maxChars)
        {
            if (s == null) return "";
            if (maxChars <= 0 || s.Length <= maxChars) return s;
            return s.Substring(0, maxChars) + "…";
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
            _activeTabIndex = 0;
            UpdateTabVisuals();
            _currentPage = page;
            SetState(STATE_LOADING);
            _listingClient.LoadPopular(page);
        }

        public void RequestRecent(int page)
        {
            if (_listingClient == null) { ReportError("ListingClient not assigned"); return; }
            _currentTab = "recent";
            _activeTabIndex = 1;
            UpdateTabVisuals();
            _currentPage = page;
            SetState(STATE_LOADING);
            _listingClient.LoadRecent(page);
        }

        public void RequestSearch()
        {
            if (_searchClient == null) { ReportError("SearchClient not assigned"); return; }
            _currentTab = "search";
            _activeTabIndex = 2;
            UpdateTabVisuals();
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

            // Defensive id 検証 (server-api-spec.md §4.6 / §5.2、strict mode):
            // /r/default/{playlistId} レスポンスは id を**必ず**返す。listing で得た
            // playlistId と不一致なら playlist pool 衝突 (永久化されているはずだが
            // 二重防衛として) を検出してエラー扱いにする。
            // pre-release 時点では server bug の早期発見を優先するため strict。
            DataToken idToken;
            if (!rootDict.TryGetValue("id", out idToken) || idToken.TokenType != TokenType.String)
            {
                Debug.LogWarning("[PlaylistViewer] Resolve response missing 'id' field — server not conformant to spec §4.6");
                ReportError("Resolve response missing playlist id");
                return false;
            }
            string responseId = idToken.String;
            if (responseId != playlistId)
            {
                Debug.LogWarning("[PlaylistViewer] id mismatch: expected=" + playlistId + " got=" + responseId);
                ReportError("Playlist id mismatch");
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
            if (_detailPlaylistName != null) _detailPlaylistName.text = TruncateString(name, _detailNameMaxChars);
            if (_detailOwnerName != null) _detailOwnerName.text = TruncateString(_pendingOwnerName, _detailOwnerMaxChars);
            if (_detailTotalTracks != null) _detailTotalTracks.text = (tracks != null ? tracks.Count.ToString() : "0") + " " + _trackCountUnit;
            if (_detailUrlField != null) _detailUrlField.text = _baseUrl + "/r/default/" + playlistId;

            // Phase A-4: cover art (i.ytimg.com 経由)。-1 / 範囲外なら ThumbnailLoader 側で dummy fallback
            if (_detailPlaylistThumbnail != null && _thumbnailLoader != null)
            {
                _thumbnailLoader.LoadYtThumbnail(_pendingYtThumbIndex, _detailPlaylistThumbnail);
            }

            _currentPlaylistId = playlistId;
            RenderTrackList(tracks);

            return true;
        }

        // ----- UI 描画 -----

        /// <summary>
        /// Pre-allocated 20 行方式: _resultRows[] (固定) を順に走査し、count まで SetData、
        /// 余剰は Hide。動的 Instantiate しない (docs/unity-architecture.md §5.3 参照)。
        /// </summary>
        private void RenderResultList()
        {
            if (_resultRows == null || _resultRows.Length == 0) return;

            int itemCount = _currentItems != null ? _currentItems.Count : 0;
            int slotCount = _resultRows.Length;

            for (int i = 0; i < slotCount; i++)
            {
                ResultRow row = _resultRows[i];
                if (row == null) continue;

                if (i >= itemCount)
                {
                    row.Hide();
                    continue;
                }

                if (_currentItems[i].TokenType != TokenType.DataDictionary)
                {
                    row.Hide();
                    continue;
                }
                DataDictionary item = _currentItems[i].DataDictionary;

                string name = TryGetString(item, "name", "");
                string owner = TryGetString(item, "ownerName", "");
                int trackCount = TryGetInt(item, "trackCount", 0);
                // ytThumbIndex は yt-thumb-direct pool (i.ytimg.com 直接 URL) の index (vhub-playlist#92 v4)。
                // 旧 thumbIndex (default-thumb pool 経由) は redirect 不可で動かないため使わない。
                int ytThumbIndex = TryGetInt(item, "ytThumbIndex", -1);

                row.SetTrackCountSuffix(" " + _trackCountUnit);
                row.SetData(name, owner, trackCount, ytThumbIndex);
            }
        }

        /// <summary>
        /// ResultRow.OnSelect から呼ばれる。Pre-allocated 各行が自身の固定 _index を渡してくる。
        /// </summary>
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

            // Phase A-4: DetailView の cover art に使う ytThumbIndex も同様に carry-over
            // (resolve API は ytThumbIndex を返さないため、listing item から確保しておく)
            _pendingYtThumbIndex = TryGetInt(item, "ytThumbIndex", -1);

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
                        if (tmp != null) tmp.text = TruncateString(title, _trackTitleMaxChars);
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

        private static string TryGetString(DataDictionary dict, string key, string defaultVal)
        {
            DataToken t;
            if (!dict.TryGetValue(key, out t)) return defaultVal;
            if (t.TokenType == TokenType.String) return t.String;
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
