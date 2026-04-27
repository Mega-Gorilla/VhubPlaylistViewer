using UnityEditor;
using UnityEngine;

namespace MegaGorilla.KawaPlayer.PlaylistViewer.Editor
{
    /// <summary>
    /// PlaylistViewerController の Inspector に「VRChat 側で Allowed Domains を追加してください」
    /// 案内をチェックリスト形式で表示するカスタムインスペクター。
    /// </summary>
    [CustomEditor(typeof(PlaylistViewerController))]
    public class AllowedDomainsHelper : UnityEditor.Editor
    {
        private const string DOMAIN = "playlist.vrc-hub.com";
        private const string VRCHAT_WORLDS_URL = "https://vrchat.com/home/worlds";

        public override void OnInspectorGUI()
        {
            DrawSetupChecklist();
            EditorGUILayout.Space(8);
            DrawDefaultInspector();
            EditorGUILayout.Space(8);
            DrawPoolGeneratorShortcut();
        }

        private void DrawSetupChecklist()
        {
            EditorGUILayout.HelpBox(
                "セットアップ手順:\n" +
                "1. VRChat Web > My Worlds > 該当ワールド > Video Player Allowed Domains に\n" +
                "   '" + DOMAIN + "' を追加\n" +
                "2. Tools > KawaPlayer PlaylistViewer > Generate Pools で VRCUrl をベイク\n" +
                "3. シーンに本 prefab を配置 (KawaPlayer 等の動画プレイヤーは別途配置)\n" +
                "4. ワールドアップロード後、Viewer で検索 → URL を Copy → KawaPlayer に Paste",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open VRChat - My Worlds"))
            {
                Application.OpenURL(VRCHAT_WORLDS_URL);
            }
            if (GUILayout.Button("Copy 'playlist.vrc-hub.com'"))
            {
                EditorGUIUtility.systemCopyBuffer = DOMAIN;
                Debug.Log("[PlaylistViewer] Copied to clipboard: " + DOMAIN);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPoolGeneratorShortcut()
        {
            EditorGUILayout.LabelField("Pool Generation", EditorStyles.boldLabel);
            if (GUILayout.Button("Open Pool Generator"))
            {
                Selection.activeGameObject = ((PlaylistViewerController)target).gameObject;
                PoolGeneratorWindow.Open();
            }
        }
    }
}
