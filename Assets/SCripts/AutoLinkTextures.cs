using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class AutoLinkTextures : EditorWindow
{
    [MenuItem("Tools/Auto-Link Selected Scene Objects")]
    public static void LinkHierarchyObjects()
    {
        GameObject[] selectedObjects = Selection.gameObjects;

        if (selectedObjects == null || selectedObjects.Length == 0)
        {
            Debug.LogError("[AutoLink] NO GAMEOBJECTS SELECTED! Highlight your main parent FBX in the Hierarchy window first.");
            return;
        }

        // 1. Map physical disk folders
        string assetsPath = Application.dataPath;
        string[] allFolderPaths = Directory.GetDirectories(assetsPath, "*", SearchOption.AllDirectories);

        Dictionary<string, string> folderMap = new Dictionary<string, string>();

        foreach (string fullPath in allFolderPaths)
        {
            string folderName = Path.GetFileName(fullPath);
            string key = CleanKey(folderName);
            string relativePath = "Assets" + fullPath.Substring(assetsPath.Length).Replace('\\', '/');

            if (!string.IsNullOrEmpty(key) && !folderMap.ContainsKey(key))
            {
                folderMap.Add(key, relativePath);
            }
        }

        int linkedCount = 0;
        int checkedObjects = 0;

        foreach (GameObject rootGo in selectedObjects)
        {
            MeshRenderer[] renderers = rootGo.GetComponentsInChildren<MeshRenderer>(true);

            foreach (MeshRenderer mr in renderers)
            {
                checkedObjects++;
                Material mat = mr.sharedMaterial;
                if (mat == null) continue;

                string rawGOName = mr.gameObject.name;
                string exactKey = CleanKey(rawGOName);

                // Base Key fallback: "pCube141.001" -> "pCube141"
                string baseGOName = Regex.Replace(rawGOName, @"[\._]\d+$", "");
                string baseKey = CleanKey(baseGOName);

                string matchingFolderPath = "";

                // Try Exact Key first, then Fallback Base Key
                if (folderMap.TryGetValue(exactKey, out matchingFolderPath) ||
                    folderMap.TryGetValue(baseKey, out matchingFolderPath))
                {
                    // Search textures inside matched folder
                    string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { matchingFolderPath });
                    bool linkedAny = false;

                    foreach (string tGuid in textureGuids)
                    {
                        string tPath = AssetDatabase.GUIDToAssetPath(tGuid);
                        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(tPath);
                        if (tex == null) continue;

                        string texName = Path.GetFileNameWithoutExtension(tPath).ToLower();

                        // Diffuse / Base Color
                        if (texName.Contains("diffuse") || texName.Contains("base") || texName.Contains("albedo") || texName.Contains("color"))
                        {
                            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                            else if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
                            linkedAny = true;
                        }
                        // Normal Map
                        else if (texName.Contains("normal") || texName.Contains("bump"))
                        {
                            if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", tex);

                            TextureImporter importer = AssetImporter.GetAtPath(tPath) as TextureImporter;
                            if (importer != null && importer.textureType != TextureImporterType.NormalMap)
                            {
                                importer.textureType = TextureImporterType.NormalMap;
                                importer.SaveAndReimport();
                            }
                        }
                        // Roughness / Metallic
                        else if (texName.Contains("roughness") || texName.Contains("metallic"))
                        {
                            if (mat.HasProperty("_MetallicGlossMap")) mat.SetTexture("_MetallicGlossMap", tex);
                        }
                    }

                    if (linkedAny)
                    {
                        EditorUtility.SetDirty(mat);
                        linkedCount++;
                        Debug.Log($"<color=green>[AutoLink SUCCESS]</color> Linked Object '{rawGOName}' -> Folder '{matchingFolderPath}'");
                    }
                }
                else
                {
                    Debug.LogWarning($"<color=red>[NO BAKE FOLDER]</color> '{rawGOName}' has no folder on disk (Checked: '{exactKey}' & '{baseKey}')");
                }
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"<b>[AutoLink Complete]</b> Linked {linkedCount} / {checkedObjects} objects in scene!");
    }

    private static string CleanKey(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return Regex.Replace(input, @"[^a-zA-Z0-9]", "").ToLower();
    }
}