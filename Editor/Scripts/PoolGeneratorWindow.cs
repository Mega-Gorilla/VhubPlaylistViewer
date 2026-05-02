using UnityEditor;
using UnityEngine;

namespace MegaGorilla.KawaPlayer.PlaylistViewer.Editor
{
    /// <summary>
    /// Tools > VHub PlaylistViewer > Generate Pools から開く Editor Window。
    /// シーン中の PlaylistViewerController を選択して 4 種 pool を一括ベイク生成する。
    /// </summary>
    public class PoolGeneratorWindow : EditorWindow
    {
        private PlaylistViewerController _controller;
        private ListingClient _listingClient;
        private PlaylistResolver _resolver;
        private ThumbnailLoader _thumbnailLoader;
        private SearchClient _searchClient;

        private string _baseUrl = PoolGenerator.DEFAULT_BASE_URL;
        private string _resolvePoolId = PoolGenerator.POOL_ID_RESOLVE;
        private int _resolvePoolSize = 1024;
        private int _listingPageCount = 50;

        private string _statusMessage = "";
        private MessageType _statusType = MessageType.Info;

        [MenuItem("Tools/VHub PlaylistViewer/Generate Pools")]
        public static void Open()
        {
            PoolGeneratorWindow w = GetWindow<PoolGeneratorWindow>("PlaylistViewer Pool Generator");
            w.minSize = new Vector2(420, 480);
            w.AutoFillFromSelection();
        }

        private void AutoFillFromSelection()
        {
            GameObject go = Selection.activeGameObject;
            if (go == null) return;
            PlaylistViewerController c = go.GetComponentInChildren<PlaylistViewerController>(true);
            if (c == null) c = go.GetComponentInParent<PlaylistViewerController>();
            if (c == null) return;
            _controller = c;
            ResolveChildren();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("VHub PlaylistViewer — Pool Generator", EditorStyles.boldLabel);
            EditorGUILayout.Space(8);

            EditorGUI.BeginChangeCheck();
            _controller = (PlaylistViewerController)EditorGUILayout.ObjectField(
                "Controller", _controller, typeof(PlaylistViewerController), true);
            if (EditorGUI.EndChangeCheck()) ResolveChildren();

            using (new EditorGUI.DisabledGroupScope(true))
            {
                _listingClient = (ListingClient)EditorGUILayout.ObjectField(
                    "  Listing Client", _listingClient, typeof(ListingClient), true);
                _resolver = (PlaylistResolver)EditorGUILayout.ObjectField(
                    "  Playlist Resolver", _resolver, typeof(PlaylistResolver), true);
                _thumbnailLoader = (ThumbnailLoader)EditorGUILayout.ObjectField(
                    "  Thumbnail Loader", _thumbnailLoader, typeof(ThumbnailLoader), true);
                _searchClient = (SearchClient)EditorGUILayout.ObjectField(
                    "  Search Client", _searchClient, typeof(SearchClient), true);
            }

            if (_controller == null)
            {
                EditorGUILayout.HelpBox("PlaylistViewerController を割り当ててください。", MessageType.Error);
                return;
            }
            if (_listingClient == null || _resolver == null || _thumbnailLoader == null)
            {
                EditorGUILayout.HelpBox("Controller の Children (ListingClient / PlaylistResolver / ThumbnailLoader) が未設定です。Inspector で割り当ててから再度開いてください。", MessageType.Warning);
            }
            if (_searchClient == null)
            {
                EditorGUILayout.HelpBox("Controller の SearchClient が未設定のため、search prefix の Inspector 自動同期 (#23 polish) はスキップされます。他 4 件の pool は生成されます。", MessageType.Warning);
            }

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Server Settings", EditorStyles.boldLabel);
            _baseUrl = EditorGUILayout.TextField("Base URL", _baseUrl);
            _resolvePoolId = EditorGUILayout.TextField("Resolve Pool ID", _resolvePoolId);

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Pool Sizes", EditorStyles.boldLabel);
            _resolvePoolSize = EditorGUILayout.IntField("Resolve Pool Size", _resolvePoolSize);
            _listingPageCount = EditorGUILayout.IntField("Listing Pages (each)", _listingPageCount);

            EditorGUILayout.HelpBox(
                "yt-thumb-direct pool は server から /api/vrc/yt-thumb-direct-baking で全件 fetch するため、サイズ指定不要です (vhub-playlist#92 v4)。",
                MessageType.Info);

            int totalEntries = _resolvePoolSize + (_listingPageCount * 2);
            float estimatedMB = totalEntries * 54f / (1024f * 1024f);
            EditorGUILayout.LabelField("Total VRCUrls (excl. yt-thumb)", totalEntries.ToString());
            EditorGUILayout.LabelField("Estimated Size (excl. yt-thumb)", "~" + estimatedMB.ToString("F2") + " MB");

            EditorGUILayout.Space(12);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Validate Pool IDs"))
            {
                ValidateAll();
            }
            GUI.enabled = (_controller != null && _listingClient != null && _resolver != null && _thumbnailLoader != null);
            if (GUILayout.Button("Generate"))
            {
                Generate();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox(_statusMessage, _statusType);
            }
        }

        private void ResolveChildren()
        {
            _listingClient = null;
            _resolver = null;
            _thumbnailLoader = null;
            _searchClient = null;
            if (_controller == null) return;

            // Inspector で SerializedProperty 経由で取得
            SerializedObject so = new SerializedObject(_controller);
            _listingClient = so.FindProperty("_listingClient")?.objectReferenceValue as ListingClient;
            _resolver = so.FindProperty("_resolver")?.objectReferenceValue as PlaylistResolver;
            _thumbnailLoader = so.FindProperty("_thumbnailLoader")?.objectReferenceValue as ThumbnailLoader;
            _searchClient = so.FindProperty("_searchClient")?.objectReferenceValue as SearchClient;
        }

        private void ValidateAll()
        {
            string msg;
            if (!PoolGenerator.ValidatePoolId(_baseUrl, _resolvePoolId, out msg))
            {
                _statusMessage = "Resolve pool: " + msg;
                _statusType = MessageType.Error;
                return;
            }
            _statusMessage = "Resolve pool ID validated successfully";
            _statusType = MessageType.Info;
        }

        private void Generate()
        {
            if (_resolvePoolSize <= 0 || _listingPageCount <= 0)
            {
                _statusMessage = "Pool sizes must be positive";
                _statusType = MessageType.Error;
                return;
            }
            bool proceed = EditorUtility.DisplayDialog(
                "Generate Pools",
                "既存の VRCUrl[] を上書きして生成します。続行しますか？\n(yt-thumb-direct pool は server から fetch)",
                "Generate", "Cancel");
            if (!proceed) return;

            PoolGenerator.GenerateOptions opts = new PoolGenerator.GenerateOptions
            {
                BaseUrl = _baseUrl,
                ResolvePoolId = _resolvePoolId,
                ResolvePoolSize = _resolvePoolSize,
                ListingPageCount = _listingPageCount
            };

            try
            {
                PoolGenerator.Result r = PoolGenerator.GenerateAll(
                    _controller, _listingClient, _resolver, _thumbnailLoader, _searchClient, opts);
                if (r.Ok)
                {
                    _statusMessage = r.Message;
                    _statusType = MessageType.Info;
                    EditorUtility.DisplayDialog("Success", r.Message, "OK");
                }
                else
                {
                    _statusMessage = r.Message;
                    _statusType = MessageType.Error;
                }
            }
            catch (System.Exception ex)
            {
                _statusMessage = "Exception: " + ex.Message;
                _statusType = MessageType.Error;
                Debug.LogException(ex);
            }
        }
    }
}
