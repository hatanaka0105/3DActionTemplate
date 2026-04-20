using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blocks.EditorTools
{
    /// <summary>
    /// Lists textures and models in the project that are NOT referenced by the currently open scene(s),
    /// sorted by file size in descending order. Supports pinging, selecting and safely moving
    /// unused assets to the OS trash via <see cref="AssetDatabase.MoveAssetToTrash"/>.
    ///
    /// Caveats (static analysis cannot detect these references):
    ///   - Assets loaded via Addressables / AssetBundles
    ///   - Assets under Resources/ folders loaded via Resources.Load
    ///   - Assets referenced only from other scenes listed in Build Settings
    ///   - Assets referenced by code paths (e.g. hardcoded paths, reflection)
    ///   - Assets under Packages/, Editor/, StreamingAssets/ (excluded from the scan)
    /// Review the list carefully before deleting.
    /// </summary>
    public class AssetUsageAnalyzer : EditorWindow
    {
        #region Types

        private enum AssetKind
        {
            Texture,
            Model,
        }

        [Flags]
        private enum ScanFilter
        {
            None = 0,
            Textures = 1 << 0,
            Models = 1 << 1,
            All = Textures | Models,
        }

        private class AssetEntry
        {
            public string Guid;
            public string AssetPath;
            public string AbsolutePath;
            public long FileSizeBytes;
            public AssetKind Kind;
            public bool Selected;
        }

        #endregion

        #region Fields

        private const string k_MenuPath = "Tools/Blocks/Asset Usage Analyzer";
        private const string k_ResourcesFolderToken = "/Resources/";
        private const string k_StreamingAssetsFolderToken = "/StreamingAssets/";
        private const string k_EditorFolderToken = "/Editor/";

        private ScanFilter m_Filter = ScanFilter.All;
        private bool m_IncludeInactiveSceneObjects = true;
        private bool m_ExcludeResourcesFolder = true;
        private bool m_ExcludeStreamingAssetsFolder = true;
        private bool m_ExcludeEditorFolder = true;
        private string m_PathFilter = string.Empty;

        private List<AssetEntry> m_UnusedEntries = new List<AssetEntry>();
        private HashSet<string> m_UsedGuids = new HashSet<string>();
        private Vector2 m_ScrollPos;
        private long m_TotalUnusedBytes;
        private bool m_HasScanned;
        private string m_LastScanSummary = string.Empty;

        #endregion

        #region Menu

        [MenuItem(k_MenuPath)]
        public static void Open()
        {
            var window = GetWindow<AssetUsageAnalyzer>("Asset Usage Analyzer");
            window.minSize = new Vector2(720, 420);
            window.Show();
        }

        #endregion

        #region GUI

        private void OnGUI()
        {
            DrawHeader();
            DrawOptions();
            DrawActions();
            DrawWarning();
            DrawResults();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Asset Usage Analyzer", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Finds textures / models in the project that are not referenced by the currently open scene(s).",
                EditorStyles.miniLabel);
            EditorGUILayout.Space(4);
        }

        private void DrawOptions()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Scan Options", EditorStyles.boldLabel);
                m_Filter = (ScanFilter)EditorGUILayout.EnumFlagsField("Asset types", m_Filter);
                m_IncludeInactiveSceneObjects = EditorGUILayout.Toggle(
                    new GUIContent("Include inactive scene objects",
                        "Also collect dependencies from GameObjects that are disabled in the open scene(s)."),
                    m_IncludeInactiveSceneObjects);

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Exclusions (treated as 'in use' and skipped)", EditorStyles.miniBoldLabel);
                m_ExcludeResourcesFolder = EditorGUILayout.Toggle("Exclude Resources/", m_ExcludeResourcesFolder);
                m_ExcludeStreamingAssetsFolder = EditorGUILayout.Toggle("Exclude StreamingAssets/", m_ExcludeStreamingAssetsFolder);
                m_ExcludeEditorFolder = EditorGUILayout.Toggle("Exclude Editor/", m_ExcludeEditorFolder);

                EditorGUILayout.Space(2);
                m_PathFilter = EditorGUILayout.TextField(
                    new GUIContent("Path contains", "Only show results whose asset path contains this substring (case-insensitive). Leave empty for no filter."),
                    m_PathFilter);
            }
        }

        private void DrawActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Scan open scene(s)", GUILayout.Height(26)))
                {
                    Scan();
                }

                using (new EditorGUI.DisabledScope(!m_HasScanned || m_UnusedEntries.Count == 0))
                {
                    if (GUILayout.Button("Select all visible", GUILayout.Height(26)))
                    {
                        foreach (var e in GetVisibleEntries()) e.Selected = true;
                    }

                    if (GUILayout.Button("Deselect all", GUILayout.Height(26)))
                    {
                        foreach (var e in m_UnusedEntries) e.Selected = false;
                    }

                    if (GUILayout.Button("Export CSV...", GUILayout.Height(26)))
                    {
                        ExportCsv();
                    }

                    GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);
                    if (GUILayout.Button("Move selected to Trash", GUILayout.Height(26)))
                    {
                        DeleteSelected();
                    }
                    GUI.backgroundColor = Color.white;
                }
            }
        }

        private void DrawWarning()
        {
            EditorGUILayout.HelpBox(
                "Static analysis cannot detect references made at runtime (Addressables, Resources.Load, AssetBundles, other scenes, reflection). " +
                "Always verify the listed assets before deleting. Deletion uses the OS trash so you can restore files if needed.",
                MessageType.Warning);
        }

        private void DrawResults()
        {
            if (!m_HasScanned)
            {
                EditorGUILayout.HelpBox("Press 'Scan open scene(s)' to list unused assets.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(m_LastScanSummary, EditorStyles.miniLabel);

            var visible = GetVisibleEntries().ToList();
            long visibleBytes = 0;
            for (int i = 0; i < visible.Count; i++) visibleBytes += visible[i].FileSizeBytes;

            EditorGUILayout.LabelField(
                $"Visible: {visible.Count} assets, {FormatBytes(visibleBytes)}  |  Total unused: {m_UnusedEntries.Count} assets, {FormatBytes(m_TotalUnusedBytes)}",
                EditorStyles.boldLabel);

            using (var scroll = new EditorGUILayout.ScrollViewScope(m_ScrollPos))
            {
                m_ScrollPos = scroll.scrollPosition;

                for (int i = 0; i < visible.Count; i++)
                {
                    DrawEntryRow(visible[i]);
                }
            }
        }

        private void DrawEntryRow(AssetEntry entry)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                entry.Selected = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(18));

                GUILayout.Label(entry.Kind.ToString(), GUILayout.Width(60));
                GUILayout.Label(FormatBytes(entry.FileSizeBytes), GUILayout.Width(90));

                EditorGUILayout.LabelField(entry.AssetPath, EditorStyles.label);

                if (GUILayout.Button("Ping", GUILayout.Width(50)))
                {
                    var obj = AssetDatabase.LoadMainAssetAtPath(entry.AssetPath);
                    if (obj != null) EditorGUIUtility.PingObject(obj);
                }

                if (GUILayout.Button("Select", GUILayout.Width(60)))
                {
                    var obj = AssetDatabase.LoadMainAssetAtPath(entry.AssetPath);
                    if (obj != null) Selection.activeObject = obj;
                }

                GUI.backgroundColor = new Color(1f, 0.75f, 0.75f);
                if (GUILayout.Button("Trash", GUILayout.Width(60)))
                {
                    if (EditorUtility.DisplayDialog("Move to Trash",
                            $"Move this asset to the OS trash?\n\n{entry.AssetPath}\n\nYou can restore from the trash if needed.",
                            "Move", "Cancel"))
                    {
                        if (AssetDatabase.MoveAssetToTrash(entry.AssetPath))
                        {
                            m_UnusedEntries.Remove(entry);
                            RecalculateTotal();
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                GUI.backgroundColor = Color.white;
            }
        }

        #endregion

        #region Scan

        private void Scan()
        {
            m_UnusedEntries.Clear();
            m_UsedGuids.Clear();
            m_TotalUnusedBytes = 0;
            m_HasScanned = false;

            int sceneCount = EditorSceneManager.sceneCount;
            if (sceneCount == 0 || !EditorSceneManager.GetSceneAt(0).IsValid())
            {
                EditorUtility.DisplayDialog("No Scene", "No scene is currently open.", "OK");
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("Asset Usage Analyzer", "Collecting scene dependencies...", 0f);
                CollectUsedDependencies();

                EditorUtility.DisplayProgressBar("Asset Usage Analyzer", "Enumerating project assets...", 0.4f);
                var allCandidates = EnumerateCandidateAssets();

                EditorUtility.DisplayProgressBar("Asset Usage Analyzer", "Computing sizes...", 0.75f);
                BuildUnusedList(allCandidates);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            m_UnusedEntries = m_UnusedEntries.OrderByDescending(e => e.FileSizeBytes).ToList();
            RecalculateTotal();

            var openScenes = new List<string>();
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var s = EditorSceneManager.GetSceneAt(i);
                if (s.IsValid()) openScenes.Add(Path.GetFileNameWithoutExtension(s.path));
            }
            m_LastScanSummary = $"Scanned scene(s): {string.Join(", ", openScenes)}  |  Used dependencies: {m_UsedGuids.Count}";
            m_HasScanned = true;
            Repaint();
        }

        private void CollectUsedDependencies()
        {
            var rootObjects = new List<GameObject>();
            for (int s = 0; s < EditorSceneManager.sceneCount; s++)
            {
                var scene = EditorSceneManager.GetSceneAt(s);
                if (!scene.IsValid() || !scene.isLoaded) continue;

                var roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    if (!m_IncludeInactiveSceneObjects && !roots[r].activeInHierarchy) continue;
                    rootObjects.Add(roots[r]);
                }
            }

            if (rootObjects.Count == 0) return;

            var deps = EditorUtility.CollectDependencies(rootObjects.ToArray());
            for (int i = 0; i < deps.Length; i++)
            {
                var obj = deps[i];
                if (obj == null) continue;
                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;
                var guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid)) m_UsedGuids.Add(guid);
            }

            // Also add transitive dependencies of the scene files themselves.
            for (int s = 0; s < EditorSceneManager.sceneCount; s++)
            {
                var scene = EditorSceneManager.GetSceneAt(s);
                if (!scene.IsValid() || string.IsNullOrEmpty(scene.path)) continue;
                var sceneDeps = AssetDatabase.GetDependencies(scene.path, true);
                for (int d = 0; d < sceneDeps.Length; d++)
                {
                    var guid = AssetDatabase.AssetPathToGUID(sceneDeps[d]);
                    if (!string.IsNullOrEmpty(guid)) m_UsedGuids.Add(guid);
                }
            }
        }

        private List<string> EnumerateCandidateAssets()
        {
            var result = new List<string>(4096);

            if ((m_Filter & ScanFilter.Textures) != 0)
            {
                var guids = AssetDatabase.FindAssets("t:Texture", new[] { "Assets" });
                for (int i = 0; i < guids.Length; i++) result.Add(guids[i]);
            }
            if ((m_Filter & ScanFilter.Models) != 0)
            {
                // t:Model matches imported model assets (fbx/obj/blend/etc.).
                var guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets" });
                for (int i = 0; i < guids.Length; i++) result.Add(guids[i]);
            }

            return result.Distinct().ToList();
        }

        private void BuildUnusedList(List<string> candidateGuids)
        {
            for (int i = 0; i < candidateGuids.Count; i++)
            {
                var guid = candidateGuids[i];
                if (m_UsedGuids.Contains(guid)) continue;

                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;

                if (m_ExcludeResourcesFolder && path.Contains(k_ResourcesFolderToken)) continue;
                if (m_ExcludeStreamingAssetsFolder && path.Contains(k_StreamingAssetsFolderToken)) continue;
                if (m_ExcludeEditorFolder && path.Contains(k_EditorFolderToken)) continue;

                var kind = ClassifyAsset(path);
                if (!kind.HasValue) continue;

                var absolute = ToAbsolutePath(path);
                long size = 0;
                try
                {
                    var info = new FileInfo(absolute);
                    if (info.Exists) size = info.Length;
                }
                catch
                {
                    size = 0;
                }

                m_UnusedEntries.Add(new AssetEntry
                {
                    Guid = guid,
                    AssetPath = path,
                    AbsolutePath = absolute,
                    FileSizeBytes = size,
                    Kind = kind.Value,
                });
            }
        }

        private static AssetKind? ClassifyAsset(string path)
        {
            var importer = AssetImporter.GetAtPath(path);
            if (importer is TextureImporter) return AssetKind.Texture;
            if (importer is ModelImporter) return AssetKind.Model;
            return null;
        }

        #endregion

        #region Actions

        private void DeleteSelected()
        {
            var selected = m_UnusedEntries.Where(e => e.Selected).ToList();
            if (selected.Count == 0)
            {
                EditorUtility.DisplayDialog("Nothing Selected", "No entries are selected.", "OK");
                return;
            }

            long totalBytes = selected.Sum(s => s.FileSizeBytes);
            bool ok = EditorUtility.DisplayDialog(
                "Move to Trash",
                $"Move {selected.Count} asset(s) ({FormatBytes(totalBytes)}) to the OS trash?\n\n" +
                "You can restore from the trash if needed, but Unity .meta files and references may still need cleanup.",
                "Move", "Cancel");
            if (!ok) return;

            var paths = selected.Select(s => s.AssetPath).ToList();
            var failed = new List<string>();
            AssetDatabase.MoveAssetsToTrash(paths.ToArray(), failed);

            var failedSet = new HashSet<string>(failed);
            m_UnusedEntries.RemoveAll(e => e.Selected && !failedSet.Contains(e.AssetPath));

            if (failed.Count > 0)
            {
                Debug.LogWarning($"[AssetUsageAnalyzer] Failed to trash {failed.Count} asset(s):\n{string.Join("\n", failed)}");
            }

            RecalculateTotal();
            Repaint();
        }

        private void ExportCsv()
        {
            var path = EditorUtility.SaveFilePanel("Export Unused Assets", Application.dataPath, "UnusedAssets", "csv");
            if (string.IsNullOrEmpty(path)) return;

            var sb = new StringBuilder();
            sb.AppendLine("Kind,SizeBytes,SizeReadable,AssetPath,GUID");
            for (int i = 0; i < m_UnusedEntries.Count; i++)
            {
                var e = m_UnusedEntries[i];
                sb.Append(e.Kind).Append(',')
                  .Append(e.FileSizeBytes).Append(',')
                  .Append(FormatBytes(e.FileSizeBytes)).Append(',')
                  .Append(EscapeCsv(e.AssetPath)).Append(',')
                  .Append(e.Guid).AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            EditorUtility.RevealInFinder(path);
        }

        #endregion

        #region Helpers

        private IEnumerable<AssetEntry> GetVisibleEntries()
        {
            if (string.IsNullOrEmpty(m_PathFilter))
            {
                return m_UnusedEntries;
            }
            var filter = m_PathFilter;
            return m_UnusedEntries.Where(e => e.AssetPath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void RecalculateTotal()
        {
            m_TotalUnusedBytes = 0;
            for (int i = 0; i < m_UnusedEntries.Count; i++) m_TotalUnusedBytes += m_UnusedEntries[i].FileSizeBytes;
        }

        private static string ToAbsolutePath(string assetPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return Path.Combine(projectRoot, assetPath).Replace('\\', '/');
        }

        private static string FormatBytes(long bytes)
        {
            const double KB = 1024d;
            const double MB = KB * 1024d;
            const double GB = MB * 1024d;
            if (bytes >= GB) return $"{bytes / GB:0.00} GB";
            if (bytes >= MB) return $"{bytes / MB:0.00} MB";
            if (bytes >= KB) return $"{bytes / KB:0.00} KB";
            return $"{bytes} B";
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        #endregion
    }
}
