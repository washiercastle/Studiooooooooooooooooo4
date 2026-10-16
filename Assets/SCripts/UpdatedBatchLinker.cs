using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;

public class UpdatedBatchLinker : EditorWindow
{
    private DefaultAsset materialsFolder;
    private DefaultAsset texturesFolder;

    // Manual / Targeted list
    private List<Material> manualMaterials = new List<Material>();
    private Vector2 scrollPos;

    [MenuItem("Tools/Updated Batch Linker (Locked Base Map + Targeted)")]
    public static void ShowWindow()
    {
        GetWindow<UpdatedBatchLinker>("Updated Batch Linker (Targeted)");
    }

    private void OnGUI()
    {
        GUILayout.Label("HDRP Batch Linker & Targeted Tools", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Global Folders
        materialsFolder = (DefaultAsset)EditorGUILayout.ObjectField("Materials Folder (Global)", materialsFolder, typeof(DefaultAsset), false);
        texturesFolder = (DefaultAsset)EditorGUILayout.ObjectField("Textures Folder", texturesFolder, typeof(DefaultAsset), false);

        EditorGUILayout.Space();

        if (GUILayout.Button("1. Link ALL Materials in Folder", GUILayout.Height(30)))
        {
            if (materialsFolder == null || texturesFolder == null)
            {
                EditorUtility.DisplayDialog("Error", "Please assign both global folders.", "OK");
                return;
            }
            LinkAllMaterials();
        }

        EditorGUILayout.Space(10);
        GUI.backgroundColor = new Color(0.8f, 0.9f, 1f);
        if (GUILayout.Button("2. Enable Double-Sided (All)", GUILayout.Height(30)))
        {
            if (materialsFolder == null) return;
            EnableDoubleSidedHDRP();
        }

        EditorGUILayout.Space(10);
        GUI.backgroundColor = new Color(0.9f, 1f, 0.8f);
        if (GUILayout.Button("3. Change UV Set to UV1 (All)", GUILayout.Height(30)))
        {
            if (materialsFolder == null) return;
            SetAllMaterialsUV1();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(15);
        GUI.backgroundColor = new Color(1f, 0.9f, 0.8f);
        if (GUILayout.Button("4. LINK FBX MESHES TO THESE MATERIALS", GUILayout.Height(35)))
        {
            if (materialsFolder == null)
            {
                EditorUtility.DisplayDialog("Error", "Please assign the Materials folder.", "OK");
                return;
            }
            LinkSceneMeshesToExistingMaterials();
        }
        GUI.backgroundColor = Color.white;

        // --- TARGETED / MANUAL SECTION ---
        EditorGUILayout.Space(20);
        EditorGUILayout.LabelField("Targeted / Manual Material Linker", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Add specific materials below to update *only* them without risking other manual adjustments in your project.", MessageType.Info);

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(100));
        for (int i = 0; i < manualMaterials.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            manualMaterials[i] = (Material)EditorGUILayout.ObjectField($"Material {i + 1}", manualMaterials[i], typeof(Material), false);
            if (GUILayout.Button("X", GUILayout.Width(25)))
            {
                manualMaterials.RemoveAt(i);
                i--;
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Empty Slot"))
        {
            manualMaterials.Add(null);
        }
        if (GUILayout.Button("Add Selected in Project"))
        {
            foreach (Object obj in Selection.objects)
            {
                if (obj is Material mat && !manualMaterials.Contains(mat))
                {
                    manualMaterials.Add(mat);
                }
            }
        }
        if (GUILayout.Button("Clear List"))
        {
            manualMaterials.Clear();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);
        GUI.backgroundColor = new Color(0.8f, 1f, 0.9f);
        if (GUILayout.Button("Run Targeted Link for Listed Materials", GUILayout.Height(30)))
        {
            if (texturesFolder == null)
            {
                EditorUtility.DisplayDialog("Error", "Please assign the Textures Folder first.", "OK");
                return;
            }
            if (manualMaterials.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "Please add at least one material to the list.", "OK");
                return;
            }
            LinkTargetedMaterials();
        }
        GUI.backgroundColor = Color.white;
    }

    private void LinkAllMaterials()
    {
        string matFolderPath = AssetDatabase.GetAssetPath(materialsFolder);
        string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { matFolderPath });
        List<Material> matsToProcess = new List<Material>();

        foreach (string guid in matGuids)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (mat != null) matsToProcess.Add(mat);
        }

        int count = ProcessMaterials(matsToProcess);
        EditorUtility.DisplayDialog("Batch Linker Complete", $"Successfully processed {count} materials!", "OK");
    }

    private void LinkTargetedMaterials()
    {
        List<Material> validMats = new List<Material>();
        foreach (Material mat in manualMaterials)
        {
            if (mat != null && !validMats.Contains(mat)) validMats.Add(mat);
        }

        if (validMats.Count == 0)
        {
            EditorUtility.DisplayDialog("Error", "No valid materials found in the manual list.", "OK");
            return;
        }

        int count = ProcessMaterials(validMats);
        EditorUtility.DisplayDialog("Targeted Link Complete", $"Successfully updated {count} specified materials!", "OK");
    }

    private int ProcessMaterials(List<Material> materials)
    {
        string texFolderPath = AssetDatabase.GetAssetPath(texturesFolder);
        if (!Directory.Exists(texFolderPath)) return 0;

        string[] texSubdirectories = Directory.GetDirectories(texFolderPath, "*", SearchOption.AllDirectories);
        int processedCount = 0;

        foreach (Material mat in materials)
        {
            if (mat == null) continue;

            string bestMatchingDir = FindBestMatchingFolder(mat.name, texSubdirectories);
            if (string.IsNullOrEmpty(bestMatchingDir)) continue;

            string[] extensions = { "*.png", "*.jpg", "*.jpeg", "*.tga", "*.tif", "*.tiff" };
            List<string> imageFiles = new List<string>();
            foreach (string ext in extensions)
            {
                imageFiles.AddRange(Directory.GetFiles(bestMatchingDir, ext, SearchOption.TopDirectoryOnly));
            }

            bool modified = false;
            string lockedBaseMapPath = null;
            string alphaMapPath = null;
            string unlabelledFallbackPath = null;

            foreach (string filePath in imageFiles)
            {
                string assetPath = filePath.Replace("\\", "/");
                string cleanedFileName = CleanName(Path.GetFileNameWithoutExtension(filePath));

                if (IsNormalMap(cleanedFileName))
                {
                    SetTextureImporterReadableAndType(assetPath, TextureImporterType.NormalMap);
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                    if (tex != null && mat.HasProperty("_NormalMap"))
                    {
                        mat.SetTexture("_NormalMap", tex);
                        mat.EnableKeyword("_NORMALMAP");
                        modified = true;
                    }
                }
                else if (IsMaskMap(cleanedFileName))
                {
                    SetTextureImporterReadableAndType(assetPath, TextureImporterType.Default);
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                    if (tex != null && mat.HasProperty("_MaskMap"))
                    {
                        mat.SetTexture("_MaskMap", tex);
                        mat.EnableKeyword("_MASKMAP");
                        modified = true;
                    }
                }
                else if (IsAlphaMap(cleanedFileName))
                {
                    alphaMapPath = assetPath;
                }
                else if (IsDiffuseMap(cleanedFileName))
                {
                    if (lockedBaseMapPath == null)
                    {
                        lockedBaseMapPath = assetPath;
                    }
                }
                else if (IsSpecularMap(cleanedFileName) || IsMetallicMap(cleanedFileName) || IsRoughnessMap(cleanedFileName) || IsHeightMap(cleanedFileName))
                {
                    continue;
                }
                else
                {
                    if (unlabelledFallbackPath == null)
                    {
                        unlabelledFallbackPath = assetPath;
                    }
                }
            }

            string finalBaseMapPath = lockedBaseMapPath ?? unlabelledFallbackPath;

            if (finalBaseMapPath != null)
            {
                SetTextureImporterReadableAndType(finalBaseMapPath, TextureImporterType.Default);
                Texture2D baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(finalBaseMapPath);

                if (baseTex != null)
                {
                    if (alphaMapPath != null)
                    {
                        SetTextureImporterReadableAndType(alphaMapPath, TextureImporterType.Default);
                        Texture2D alphaTex = AssetDatabase.LoadAssetAtPath<Texture2D>(alphaMapPath);

                        if (alphaTex != null)
                        {
                            baseTex = CombineAlphaIntoTexture(baseTex, alphaTex);
                        }
                    }

                    if (mat.HasProperty("_BaseColorMap")) mat.SetTexture("_BaseColorMap", baseTex);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
                    modified = true;
                }
            }

            if (modified)
            {
                EditorUtility.SetDirty(mat);
                processedCount++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return processedCount;
    }

    private Texture2D CombineAlphaIntoTexture(Texture2D baseTex, Texture2D alphaTex)
    {
        try
        {
            int width = baseTex.width;
            int height = baseTex.height;
            Color32[] basePixels = baseTex.GetPixels32();
            Color32[] alphaPixels = alphaTex.GetPixels32();

            for (int i = 0; i < basePixels.Length; i++)
            {
                byte alphaVal = (i < alphaPixels.Length) ? alphaPixels[i].r : (byte)255;
                basePixels[i].a = alphaVal;
            }

            Texture2D combinedTex = new Texture2D(width, height, TextureFormat.RGBA32, true);
            combinedTex.SetPixels32(basePixels);
            combinedTex.Apply();
            return combinedTex;
        }
        catch
        {
            return baseTex;
        }
    }

    private void EnableDoubleSidedHDRP()
    {
        string matFolderPath = AssetDatabase.GetAssetPath(materialsFolder);
        string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { matFolderPath });
        int count = 0;

        foreach (string matGuid in matGuids)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(matGuid));
            if (mat != null)
            {
                if (mat.HasProperty("_DoubleSidedEnable")) mat.SetFloat("_DoubleSidedEnable", 1.0f);
                if (mat.HasProperty("_CullMode")) mat.SetInt("_CullMode", 0);
                if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", 0);
                mat.EnableKeyword("_DOUBLESIDED_ON");
                mat.doubleSidedGI = true;

                EditorUtility.SetDirty(mat);
                count++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Double-Sided Applied", $"Forced double-sided rendering on {count} materials!", "OK");
    }

    private void SetAllMaterialsUV1()
    {
        string matFolderPath = AssetDatabase.GetAssetPath(materialsFolder);
        string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { matFolderPath });
        int count = 0;

        foreach (string matGuid in matGuids)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(matGuid));
            if (mat != null && mat.HasProperty("_UVBase"))
            {
                mat.SetFloat("_UVBase", 1.0f);
                EditorUtility.SetDirty(mat);
                count++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("UV Channel Updated", $"Successfully switched _UVBase to UV1 for {count} materials!", "OK");
    }

    private void LinkSceneMeshesToExistingMaterials()
    {
        string matFolderPath = AssetDatabase.GetAssetPath(materialsFolder);
        string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { matFolderPath });

        Dictionary<string, Material> availableMaterials = new Dictionary<string, Material>();
        foreach (string guid in matGuids)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (mat != null && !availableMaterials.ContainsKey(mat.name))
            {
                availableMaterials.Add(mat.name, mat);
            }
        }

        // Updated to use FindObjectsByType with FindObjectsSortMode.None for optimal performance and no warnings
        MeshRenderer[] renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        int updatedCount = 0;

        foreach (MeshRenderer renderer in renderers)
        {
            Material[] sharedMats = renderer.sharedMaterials;
            bool rendererChanged = false;

            for (int i = 0; i < sharedMats.Length; i++)
            {
                if (sharedMats[i] != null && availableMaterials.ContainsKey(sharedMats[i].name))
                {
                    sharedMats[i] = availableMaterials[sharedMats[i].name];
                    rendererChanged = true;
                }
            }

            if (rendererChanged)
            {
                renderer.sharedMaterials = sharedMats;
                EditorUtility.SetDirty(renderer);
                updatedCount++;
            }
        }

        SkinnedMeshRenderer[] skinnedRenderers = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        foreach (SkinnedMeshRenderer renderer in skinnedRenderers)
        {
            Material[] sharedMats = renderer.sharedMaterials;
            bool rendererChanged = false;

            for (int i = 0; i < sharedMats.Length; i++)
            {
                if (sharedMats[i] != null && availableMaterials.ContainsKey(sharedMats[i].name))
                {
                    sharedMats[i] = availableMaterials[sharedMats[i].name];
                    rendererChanged = true;
                }
            }

            if (rendererChanged)
            {
                renderer.sharedMaterials = sharedMats;
                EditorUtility.SetDirty(renderer);
                updatedCount++;
            }
        }

        EditorUtility.DisplayDialog("Mesh Linker Complete", $"Successfully linked your adjusted materials to {updatedCount} objects in the scene!", "OK");
    }

    private string FindBestMatchingFolder(string matName, string[] directories)
    {
        string bestMatchDir = null;
        int highestScore = 0;
        HashSet<string> matWords = GetTokenSet(matName);

        foreach (string dir in directories)
        {
            string folderName = Path.GetFileName(dir);
            HashSet<string> folderWords = GetTokenSet(folderName);
            int score = CalculateWordOverlapScore(matWords, folderWords);

            string cleanedMat = CleanName(matName);
            string cleanedFolder = CleanName(folderName);
            if (cleanedMat.Contains(cleanedFolder) || cleanedFolder.Contains(cleanedMat)) score += 5;

            if (score > highestScore)
            {
                highestScore = score;
                bestMatchDir = dir;
            }
        }
        return highestScore > 0 ? bestMatchDir : null;
    }

    private HashSet<string> GetTokenSet(string input)
    {
        string cleaned = input.ToLowerInvariant();
        cleaned = Regex.Replace(cleaned, @"(_mat|mat|_material|material)$", "");
        cleaned = Regex.Replace(cleaned, @"[^a-z0-9]", " ");
        string[] words = cleaned.Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        HashSet<string> tokens = new HashSet<string>();

        foreach (string w in words)
        {
            if (w.Length > 0)
            {
                tokens.Add(w);
                if (w.EndsWith("ized")) tokens.Add(w.Substring(0, w.Length - 4));
                if (w.EndsWith("ed")) tokens.Add(w.Substring(0, w.Length - 2));
                if (w.EndsWith("es")) tokens.Add(w.Substring(0, w.Length - 2));
                if (w.EndsWith("s")) tokens.Add(w.Substring(0, w.Length - 1));
            }
        }
        return tokens;
    }

    private int CalculateWordOverlapScore(HashSet<string> setA, HashSet<string> setB)
    {
        int score = 0;
        foreach (string a in setA)
        {
            foreach (string b in setB)
            {
                if (a == b) score += 3;
                else if (a.Contains(b) || b.Contains(a)) score += 1;
            }
        }
        return score;
    }

    private string CleanName(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        string cleaned = input.ToLowerInvariant();
        cleaned = Regex.Replace(cleaned, @"(_mat|mat|_material|material)$", "");
        cleaned = Regex.Replace(cleaned, @"[^a-z0-9]", "");
        return cleaned;
    }

    private bool IsNormalMap(string name) => name.Contains("normal") || name.Contains("norm") || name.Contains("nrm") || name.Contains("bump");
    private bool IsMaskMap(string name) => name.Contains("mask") || name.Contains("aorm") || name.Contains("pack");
    private bool IsAlphaMap(string name) => name.Contains("alpha") || name.Contains("opacity") || name.Contains("transp") || name.Contains("transparency");
    private bool IsDiffuseMap(string name) => name.Contains("diffuse") || name.Contains("albedo") || name.Contains("basecolor") || name.Contains("color") || name.Contains("base") || name.Contains("col") || name.Contains("diff");
    private bool IsSpecularMap(string name) => name.Contains("specular") || name.Contains("spec") || name.Contains("spc");
    private bool IsMetallicMap(string name) => name.Contains("metallic") || name.Contains("metalness") || name.Contains("metal") || name.Contains("met");
    private bool IsRoughnessMap(string name) => name.Contains("roughness") || name.Contains("rough") || name.Contains("rgh") || name.Contains("gloss") || name.Contains("smooth");
    private bool IsHeightMap(string name) => name.Contains("height") || name.Contains("disp") || name.Contains("displacement") || name.Contains("parallax");

    private void SetTextureImporterReadableAndType(string path, TextureImporterType type)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            bool dirty = false;
            if (importer.textureType != type) { importer.textureType = type; dirty = true; }
            if (!importer.isReadable) { importer.isReadable = true; dirty = true; }
            if (dirty) { importer.SaveAndReimport(); }
        }
    }
}