using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class FolderBatchTextureLinker : EditorWindow
{
    private DefaultAsset rootTextureFolder;
    private Material singleMaterial;
    private bool useProjectSelection = true;

    [MenuItem("Tools/Batch Link Textures from Subfolders")]
    public static void ShowWindow()
    {
        GetWindow<FolderBatchTextureLinker>("Batch Texture Linker");
    }

    private void OnGUI()
    {
        GUILayout.Label("Targeted Texture Linker (URP)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Select the root texture folder, then choose which material(s) to process.", MessageType.Info);

        EditorGUILayout.Space();

        // 1. Texture Folder
        rootTextureFolder = (DefaultAsset)EditorGUILayout.ObjectField("Root Textures Folder", rootTextureFolder, typeof(DefaultAsset), false);

        EditorGUILayout.Space();

        // 2. Selection Mode Toggle
        useProjectSelection = EditorGUILayout.Toggle("Use Selected Materials in Project", useProjectSelection);

        if (!useProjectSelection)
        {
            singleMaterial = (Material)EditorGUILayout.ObjectField("Target Material", singleMaterial, typeof(Material), false);
        }
        else
        {
            EditorGUILayout.HelpBox("Highlight one or more Material assets in your Project window before clicking run.", MessageType.None);
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Link Textures to Target Material(s)", GUILayout.Height(35)))
        {
            if (rootTextureFolder == null)
            {
                EditorUtility.DisplayDialog("Error", "Please assign the main Textures folder first!", "OK");
                return;
            }

            ProcessTargetMaterials();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.Space();

        GUILayout.Label("Material Asset Utilities", EditorStyles.boldLabel);
        if (GUILayout.Button("Remove 'mat' from Selected Material File Names", GUILayout.Height(30)))
        {
            RenameMaterialsRemoveMat();
        }
    }

    private void ProcessTargetMaterials()
    {
        // Force Unity to index any newly added or renamed PNG files on disk first
        AssetDatabase.Refresh();

        List<Material> materialsToProcess = new List<Material>();

        if (useProjectSelection)
        {
            Material[] selected = Selection.GetFiltered<Material>(SelectionMode.Assets);
            materialsToProcess.AddRange(selected);

            if (materialsToProcess.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "No materials selected! Highlight the material asset(s) in your Project panel.", "OK");
                return;
            }
        }
        else
        {
            if (singleMaterial == null)
            {
                EditorUtility.DisplayDialog("Error", "Please assign a Target Material!", "OK");
                return;
            }
            materialsToProcess.Add(singleMaterial);
        }

        string rootPath = AssetDatabase.GetAssetPath(rootTextureFolder);
        if (!Directory.Exists(rootPath))
        {
            Debug.LogError($"[TextureLinker] Invalid folder path: {rootPath}");
            return;
        }

        string fullRootPath = Path.GetFullPath(rootPath);
        string[] subfolders = Directory.GetDirectories(fullRootPath);
        Dictionary<string, string> subfolderMap = new Dictionary<string, string>();

        foreach (string subfolderPath in subfolders)
        {
            string folderName = Path.GetFileName(subfolderPath);
            string key = CleanKey(folderName);

            if (!string.IsNullOrEmpty(key) && !subfolderMap.ContainsKey(key))
            {
                subfolderMap.Add(key, subfolderPath);
            }
        }

        int linkedCount = 0;

        foreach (Material mat in materialsToProcess)
        {
            if (mat == null) continue;

            if (AssetDatabase.IsSubAsset(mat))
            {
                Debug.LogError($"[READ-ONLY ERROR] Material '{mat.name}' is embedded inside an FBX file! Extract materials first.");
                continue;
            }

            string matKey = CleanKey(mat.name);

            if (subfolderMap.TryGetValue(matKey, out string folderPath))
            {
                string[] filePaths = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly);
                bool matUpdated = false;

                Undo.RecordObject(mat, "Batch Link Textures");

                foreach (string fullFilePath in filePaths)
                {
                    string ext = Path.GetExtension(fullFilePath).ToLower();
                    if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".tga" && ext != ".tif" && ext != ".tiff")
                        continue;

                    string relativeAssetPath = FormatToUnityPath(fullFilePath);
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(relativeAssetPath);

                    if (tex == null)
                    {
                        // Import asset if Unity hasn't cached it yet
                        AssetDatabase.ImportAsset(relativeAssetPath, ImportAssetOptions.ForceUpdate);
                        tex = AssetDatabase.LoadAssetAtPath<Texture2D>(relativeAssetPath);
                    }

                    if (tex == null)
                    {
                        Debug.LogError($"[TextureLinker] Failed to load texture asset at '{relativeAssetPath}'. Ensure it is inside the Assets folder.");
                        continue;
                    }

                    string texName = Path.GetFileNameWithoutExtension(fullFilePath).ToLower();

                    // 1. Base Map / Diffuse / Albedo
                    if (texName.Contains("diffuse") || texName.Contains("base") || texName.Contains("albedo") || texName.Contains("color") || texName.Contains("col"))
                    {
                        SetTextureOnMaterial(mat, tex, new[] { "_BaseMap", "_BaseColorMap", "_MainTex" });
                        matUpdated = true;
                        Debug.Log($"<color=green>[+] Set Base Map</color> -> '{tex.name}' on '{mat.name}'");
                    }
                    // 2. Normal Map
                    else if (texName.Contains("normal") || texName.Contains("bump") || texName.Contains("nrm") || texName.Contains("nor"))
                    {
                        TextureImporter importer = AssetImporter.GetAtPath(relativeAssetPath) as TextureImporter;
                        if (importer != null && importer.textureType != TextureImporterType.NormalMap)
                        {
                            importer.textureType = TextureImporterType.NormalMap;
                            importer.SaveAndReimport();
                        }

                        SetTextureOnMaterial(mat, tex, new[] { "_BumpMap", "_NormalMap", "_DetailNormalMap" });
                        mat.EnableKeyword("_NORMALMAP");
                        matUpdated = true;
                        Debug.Log($"<color=green>[+] Set Normal Map</color> -> '{tex.name}' on '{mat.name}'");
                    }
                    // 3. Roughness / Metallic / Mask Map
                    else if (texName.Contains("rough") || texName.Contains("metal") || texName.Contains("mask") || texName.Contains("gloss") || texName.Contains("spec"))
                    {
                        SetTextureOnMaterial(mat, tex, new[] { "_MetallicGlossMap", "_MaskMap", "_SpecGlossMap", "_MetallicMap" });
                        mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                        mat.EnableKeyword("_MASKMAP");
                        matUpdated = true;
                        Debug.Log($"<color=green>[+] Set Metallic/Mask Map</color> -> '{tex.name}' on '{mat.name}'");
                    }
                }

                if (matUpdated)
                {
                    EditorUtility.SetDirty(mat);
                    linkedCount++;
                }
                else
                {
                    Debug.LogWarning($"[TextureLinker] Found subfolder for '{mat.name}', but no matching PNG names were recognized.");
                }
            }
            else
            {
                Debug.LogWarning($"[NO MATCH] No matching subfolder found for Material '{mat.name}' (Searched Key: '{matKey}')");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Force Unity's Inspector window to repaint so slots update visually immediately
        ActiveEditorTracker.sharedTracker.ForceRebuild();

        EditorUtility.DisplayDialog("Complete", $"Linked textures for {linkedCount} of {materialsToProcess.Count} target material(s)!", "OK");
    }

    private static void SetTextureOnMaterial(Material mat, Texture2D tex, string[] possiblePropertyNames)
    {
        foreach (string propName in possiblePropertyNames)
        {
            if (mat.HasProperty(propName))
            {
                mat.SetTexture(propName, tex);
                return;
            }
        }
        // Fallback: set main texture directly
        mat.mainTexture = tex;
    }

    private void RenameMaterialsRemoveMat()
    {
        Material[] selectedMaterials = Selection.GetFiltered<Material>(SelectionMode.Assets);

        if (selectedMaterials.Length == 0)
        {
            EditorUtility.DisplayDialog("Error", "Please select the Material asset(s) in your Project window that you want to rename.", "OK");
            return;
        }

        int renamedCount = 0;

        foreach (Material mat in selectedMaterials)
        {
            if (mat == null) continue;

            string oldName = mat.name;
            string newName = oldName;

            if (newName.EndsWith("_mat", System.StringComparison.OrdinalIgnoreCase))
            {
                newName = newName.Substring(0, newName.Length - 4);
            }
            else if (newName.EndsWith("mat", System.StringComparison.OrdinalIgnoreCase))
            {
                newName = newName.Substring(0, newName.Length - 3);
            }

            if (newName != oldName && !string.IsNullOrEmpty(newName))
            {
                string assetPath = AssetDatabase.GetAssetPath(mat);
                string error = AssetDatabase.RenameAsset(assetPath, newName);

                if (string.IsNullOrEmpty(error))
                {
                    renamedCount++;
                    Debug.Log($"[Renamed Material] '{oldName}' -> '{newName}'");
                }
                else
                {
                    Debug.LogError($"[Rename Error] Could not rename '{oldName}': {error}");
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Complete", $"Renamed {renamedCount} material(s) by removing 'mat'!", "OK");
    }

    private static string FormatToUnityPath(string fullDiskPath)
    {
        string normalized = fullDiskPath.Replace('\\', '/');
        int assetsIndex = normalized.IndexOf("/Assets/", System.StringComparison.OrdinalIgnoreCase);
        if (assetsIndex != -1)
        {
            return normalized.Substring(assetsIndex + 1);
        }
        return normalized;
    }

    private static string CleanKey(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";

        string cleaned = Regex.Replace(input, @"[^a-zA-Z0-9]", "").ToLower();

        if (cleaned.EndsWith("mat") && cleaned.Length > 3)
        {
            cleaned = cleaned.Substring(0, cleaned.Length - 3);
        }

        return cleaned;
    }
}