using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MegaGorilla.KawaPlayer.PlaylistViewer.Editor
{
    /// <summary>
    /// 本パッケージを fresh import した際に必要な 2 種類の UdonSharp メタアセットを自動生成する:
    ///
    ///   (1) U# Assembly Definition (.asset)  — Runtime asmdef を「U# が認識する asm」として登録。
    ///       これがないと UdonSharp は Runtime/Scripts/*.cs を「U# assembly に属していない」と
    ///       拒否する。KawaPlayer も同パターンで .asmdef と同名の .asset を同梱している。
    ///   (2) UdonSharpProgramAsset (.asset) — 各 UdonSharpBehaviour 派生 .cs に対する program asset。
    ///       Add Component 時に UdonSharpEditorUtility.RunBehaviourSetup から参照される。
    ///
    /// 実行: Unity 起動時 / asmdef リコンパイル後に [InitializeOnLoadMethod] で自動。
    ///       Tools > KawaPlayer PlaylistViewer > Create Missing UdonSharp Program Assets で手動再実行。
    /// </summary>
    public static class UdonSharpProgramAssetCreator
    {
        const string MenuPath = "Tools/KawaPlayer PlaylistViewer/Create Missing UdonSharp Program Assets";
        const string TargetNamespace = "MegaGorilla.KawaPlayer.PlaylistViewer";
        const string RuntimeAsmdefName = "MegaGorilla.KawaPlayer.PlaylistViewer.Runtime";
        const string PackageRuntimePath = "Packages/com.vhub.kawaplayer-playlistviewer/Runtime";

        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            EditorApplication.delayCall += () =>
            {
                bool createdAsmDef = EnsureUdonSharpAssemblyDefinition(silent: true);
                if (createdAsmDef)
                {
                    // U# Assembly Definition を作った直後はまだ UdonSharp の認識が追いつかないので、
                    // 一度 AssetDatabase.Refresh + 次の delayCall で program asset を作成する
                    EditorApplication.delayCall += () => CreateMissingProgramAssets(silent: true);
                }
                else
                {
                    CreateMissingProgramAssets(silent: true);
                }
            };
        }

        [MenuItem(MenuPath)]
        public static void CreateMissingMenu()
        {
            int asmCreated = EnsureUdonSharpAssemblyDefinition(silent: false) ? 1 : 0;
            int progCreated = CreateMissingProgramAssets(silent: false);

            EditorUtility.DisplayDialog(
                "KawaPlayer PlaylistViewer",
                "U# Assembly Definitions created: " + asmCreated + "\n"
                + "U# Program Assets created: " + progCreated + "\n\n"
                + "If counts are 0, all assets were already in place.",
                "OK");
        }

        // ===== (1) U# Assembly Definition =====

        /// <summary>
        /// Runtime asmdef を指す UdonSharpAssemblyDefinition (.asset) を必要に応じて生成。
        /// 戻り値: 新規作成したら true。
        /// </summary>
        private static bool EnsureUdonSharpAssemblyDefinition(bool silent)
        {
            try
            {
                string asmdefPath = PackageRuntimePath + "/" + RuntimeAsmdefName + ".asmdef";
                AssemblyDefinitionAsset asmdef = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(asmdefPath);
                if (asmdef == null)
                {
                    if (!silent) Debug.LogError("[PlaylistViewer] AssemblyDefinitionAsset not found at: " + asmdefPath);
                    return false;
                }

                string usharpDefPath = PackageRuntimePath + "/" + RuntimeAsmdefName + ".asset";
                if (AssetDatabase.LoadAssetAtPath<UdonSharpAssemblyDefinition>(usharpDefPath) != null)
                {
                    return false; // 既存
                }

                UdonSharpAssemblyDefinition def = ScriptableObject.CreateInstance<UdonSharpAssemblyDefinition>();
                def.sourceAssembly = asmdef;
                AssetDatabase.CreateAsset(def, usharpDefPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[PlaylistViewer] Created U# Assembly Definition: " + usharpDefPath);
                return true;
            }
            catch (Exception ex)
            {
                if (!silent) Debug.LogException(ex);
                return false;
            }
        }

        // ===== (2) UdonSharpProgramAsset (per UdonSharpBehaviour subclass) =====

        /// <summary>
        /// Runtime アセンブリ内の UdonSharpBehaviour 派生クラスごとに .asset を生成。
        /// 戻り値: 新規作成数。
        /// </summary>
        private static int CreateMissingProgramAssets(bool silent)
        {
            int created = 0;
            try
            {
                Assembly runtimeAsm = typeof(PlaylistViewerController).Assembly;
                Type usbType = typeof(UdonSharpBehaviour);

                Type[] udonSharpTypes = runtimeAsm.GetTypes()
                    .Where(t => t != null
                                && t.IsSubclassOf(usbType)
                                && !t.IsAbstract
                                && t.Namespace != null
                                && t.Namespace.StartsWith(TargetNamespace))
                    .ToArray();

                foreach (Type t in udonSharpTypes)
                {
                    UdonSharpProgramAsset existing = UdonSharpEditorUtility.GetUdonSharpProgramAsset(t);
                    if (existing != null) continue;

                    MonoScript script = FindMonoScriptForType(t);
                    if (script == null)
                    {
                        Debug.LogWarning("[PlaylistViewer] MonoScript not found for type: " + t.FullName);
                        continue;
                    }

                    string scriptPath = AssetDatabase.GetAssetPath(script);
                    if (string.IsNullOrEmpty(scriptPath)) continue;
                    string assetPath = Path.ChangeExtension(scriptPath, ".asset");

                    if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath) != null) continue;

                    UdonSharpProgramAsset asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                    SerializedObject so = new SerializedObject(asset);
                    SerializedProperty sourceProp = so.FindProperty("sourceCsScript");
                    if (sourceProp == null)
                    {
                        Debug.LogError("[PlaylistViewer] UdonSharpProgramAsset.sourceCsScript not found — UdonSharp internal layout may have changed.");
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
                    Debug.Log("[PlaylistViewer] Created " + created + " new UdonSharp Program Assets.");
                }
            }
            catch (Exception ex)
            {
                if (!silent) Debug.LogException(ex);
            }
            return created;
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
