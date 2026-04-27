using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace MegaGorilla.KawaPlayer.PlaylistViewer.Editor
{
    /// <summary>
    /// 本パッケージ内の UdonSharpBehaviour 派生クラスに対し、欠けている UdonSharpProgramAsset
    /// (`.asset`) ファイルを一括生成する。KawaPlayer 等他の UdonSharp パッケージは .cs と並んで
    /// .asset を repo に同梱しており、初回 import 後に Add Component 等で AddComponent 可能になる。
    /// 我々は .asset を初回 import 時に自動生成する方針 ([InitializeOnLoadMethod])
    /// + 手動再実行用 MenuItem の二段構え。
    ///
    /// Unity が .cs を見つけても U# Program Asset が無いと
    /// `UdonSharpEditorUtility.RunBehaviourSetup` 内で
    /// "Unable to find valid U# program asset associated with script" エラーが出る。
    /// </summary>
    public static class UdonSharpProgramAssetCreator
    {
        const string MenuPath = "Tools/KawaPlayer PlaylistViewer/Create Missing UdonSharp Program Assets";
        const string TargetNamespace = "MegaGorilla.KawaPlayer.PlaylistViewer";

        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            // Unity 起動時 / asmdef リコンパイル後に欠落分を自動生成
            EditorApplication.delayCall += () =>
            {
                CreateMissingInternal(silent: true);
            };
        }

        [MenuItem(MenuPath)]
        public static void CreateMissingMenu()
        {
            CreateMissingInternal(silent: false);
        }

        private static void CreateMissingInternal(bool silent)
        {
            try
            {
                Assembly runtimeAsm = typeof(PlaylistViewerController).Assembly;
                Type usbType = typeof(UdonSharpBehaviour);

                var udonSharpTypes = runtimeAsm.GetTypes()
                    .Where(t => t != null
                                && t.IsSubclassOf(usbType)
                                && !t.IsAbstract
                                && t.Namespace != null
                                && t.Namespace.StartsWith(TargetNamespace))
                    .ToArray();

                int created = 0;
                int skipped = 0;
                foreach (Type t in udonSharpTypes)
                {
                    // 既存 program asset があればスキップ
                    UdonSharpProgramAsset existing = UdonSharpEditorUtility.GetUdonSharpProgramAsset(t);
                    if (existing != null) { skipped++; continue; }

                    // 対応する MonoScript を AssetDatabase から検索
                    MonoScript script = FindMonoScriptForType(t);
                    if (script == null)
                    {
                        Debug.LogWarning("[PlaylistViewer] MonoScript not found for type: " + t.FullName);
                        continue;
                    }

                    string scriptPath = AssetDatabase.GetAssetPath(script);
                    if (string.IsNullOrEmpty(scriptPath)) continue;
                    string assetPath = Path.ChangeExtension(scriptPath, ".asset");

                    if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath) != null)
                    {
                        // 既に物理 file がある (cache 未反映)
                        skipped++;
                        continue;
                    }

                    UdonSharpProgramAsset asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                    SerializedObject so = new SerializedObject(asset);
                    SerializedProperty sourceProp = so.FindProperty("sourceCsScript");
                    if (sourceProp == null)
                    {
                        Debug.LogError("[PlaylistViewer] UdonSharpProgramAsset has no 'sourceCsScript' property — UdonSharp internal layout may have changed.");
                        UnityEngine.Object.DestroyImmediate(asset);
                        continue;
                    }
                    sourceProp.objectReferenceValue = script;
                    so.ApplyModifiedPropertiesWithoutUndo();

                    AssetDatabase.CreateAsset(asset, assetPath);
                    created++;
                    Debug.Log("[PlaylistViewer] Created U# program asset: " + assetPath);
                }

                if (created > 0)
                {
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    Debug.Log("[PlaylistViewer] Created " + created + " new UdonSharp Program Assets (skipped " + skipped + " already-present).");
                }
                else if (!silent)
                {
                    EditorUtility.DisplayDialog(
                        "KawaPlayer PlaylistViewer",
                        "All " + skipped + " UdonSharp Program Assets are already in place. No new assets were created.",
                        "OK");
                }
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    Debug.LogException(ex);
                }
            }
        }

        private static MonoScript FindMonoScriptForType(Type t)
        {
            string[] guids = AssetDatabase.FindAssets("t:MonoScript " + t.Name);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                MonoScript ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (ms != null && ms.GetClass() == t) return ms;
            }
            return null;
        }
    }
}
