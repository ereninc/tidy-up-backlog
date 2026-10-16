using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Temporary asset-only authoring; never edits the gameplay scene or network prefab.
internal static class CozyGameCaseAssetBuilder
{
    private const string Folder = "Assets/GameAssets/Model/CozyGameCase_v01";
    private const string Review = "ArtReview/CozyGameCase_v01_2026-10-16";

    [MenuItem("Tools/Game Cases/Temporary/Build Cozy Game Case Asset")]
    public static void Build()
    {
        string modelPath = Folder + "/CozyGameCase_v01.fbx";
        AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport);
        Texture2D tones = LoadTexture("CozyGameCase_ShellTones.png");
        Texture2D cover = LoadTexture("CozyGameCase_CoverPreview.png");
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Require(lit != null && lit.isSupported, "Supported URP/Lit shader is required.");
        Material shellMaterial = MaterialAsset("CozyGameCase_ShellPreview", lit);
        shellMaterial.SetTexture("_BaseMap", tones);
        shellMaterial.SetColor("_BaseColor", new Color32(120,156,168,255));
        shellMaterial.SetFloat("_Metallic", 0);
        shellMaterial.SetFloat("_Smoothness", .35f);
        Material coverMaterial = MaterialAsset("CozyGameCase_CoverPreview", lit);
        coverMaterial.SetTexture("_BaseMap", cover);
        coverMaterial.SetColor("_BaseColor", Color.white);
        coverMaterial.SetFloat("_Metallic", 0);
        coverMaterial.SetFloat("_Smoothness", .22f);
        AssetDatabase.SaveAssetIfDirty(shellMaterial);
        AssetDatabase.SaveAssetIfDirty(coverMaterial);
        var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        importer.isReadable = true;
        importer.generateSecondaryUV = false;
        importer.swapUVChannels = false;
        importer.importAnimation = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importNormals = ModelImporterNormals.Import;
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "CGC01_0_CaseShell"), shellMaterial);
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "CGC01_1_CoverArt"), coverMaterial);
        importer.SaveAndReimport();

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>(true);
        Require(filters.Length == 1, "Combiner requires exactly one source MeshFilter.");
        Mesh source = filters[0].sharedMesh;
        MeshRenderer renderer = filters[0].GetComponent<MeshRenderer>();
        Require(source.isReadable && source.subMeshCount == 2, "Source must be readable with two submeshes.");
        Require(renderer.sharedMaterials.SequenceEqual(new[] { shellMaterial, coverMaterial }), "Material order must be Shell=0, Cover=1.");
        Require((source.bounds.size-new Vector3(.012f,.002f,.016f)).sqrMagnitude < 1e-12f,
            $"Imported mesh dimensions differ from the legacy GameBox: {source.bounds.size:F9}.");
        Require(source.bounds.center.sqrMagnitude < 1e-12f, "Case pivot must remain centred.");
        Vector3[] vertices = source.vertices;
        Vector3[] normals = source.normals;
        Vector2[] uvs = source.uv;
        Require(normals.Length == vertices.Length && uvs.Length == vertices.Length, "Source UV0/normals are incomplete.");
        foreach (int index in source.GetTriangles(1).Distinct())
        {
            Require(Vector3.Dot(normals[index], Vector3.down) > .999f, "Cover must face local -Y.");
            Vector2 expected = new Vector2(.5f-vertices[index].x/.011f, .5f+vertices[index].z/.015f);
            Require((uvs[index]-expected).sqrMagnitude < .000001f, "Cover UV orientation differs from the existing case.");
        }

        MethodInfo build = typeof(CaseMeshCombiner).GetMethod("BuildCombinedMesh", BindingFlags.NonPublic | BindingFlags.Static);
        Require(build != null, "Existing CaseMeshCombiner entry point was not found.");
        Mesh combined = (Mesh)build.Invoke(null, new object[] { source });
        combined.name = "CozyGameCase_Combined";
        Require(combined.subMeshCount == 1 && combined.triangles.Length == 420, "Unexpected combined topology.");
        var masks = new List<Vector2>();
        combined.GetUVs(1, masks);
        Require(masks.Count == combined.vertexCount, "Combiner did not generate the UV1 surface mask.");
        Require(masks.Any(mask => mask.x == 0) && masks.Any(mask => mask.x == 1) &&
                masks.All(mask => mask.x == 0 || mask.x == 1), "Surface mask must contain only shell0/cover1.");
        int[] triangles = combined.triangles;
        for (int i=0; i<triangles.Length; i+=3)
            Require(masks[triangles[i]].x == masks[triangles[i+1]].x && masks[triangles[i]].x == masks[triangles[i+2]].x,
                "A triangle interpolates between cover and shell masks.");
        Require(combined.indexFormat == IndexFormat.UInt16, "Small case should use 16-bit indices.");
        string combinedPath = Folder + "/CozyGameCase_Combined.asset";
        Mesh existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(combinedPath);
        if (existingMesh == null) AssetDatabase.CreateAsset(combined, combinedPath);
        else
        {
            EditorUtility.CopySerialized(combined, existingMesh);
            UnityEngine.Object.DestroyImmediate(combined);
            combined = existingMesh;
            AssetDatabase.SaveAssetIfDirty(existingMesh);
        }

        Material template = AssetDatabase.LoadAssetAtPath<Material>("Assets/Source/GameCase/EXW_Game Case Combined.mat");
        Require(template != null && template.shader.isSupported, "Existing combined case material is required.");
        Material runtimeMaterial = MaterialAsset("CozyGameCase_Combined", template.shader);
        runtimeMaterial.CopyPropertiesFromMaterial(template);
        runtimeMaterial.SetTexture("_ShellTexture", tones);
        runtimeMaterial.SetColor("_ShellColor", new Color32(120,156,168,255));
        EditorUtility.SetDirty(runtimeMaterial);
        AssetDatabase.SaveAssetIfDirty(runtimeMaterial);
        SaveVisualPrefab("CozyGameCase_Source", source, new[] { shellMaterial, coverMaterial });
        SaveVisualPrefab("CozyGameCase_Visual", combined, new[] { runtimeMaterial });

        // Reload generated assets and check the serialized mask and material contract.
        Mesh savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(combinedPath);
        var savedMasks = new List<Vector2>();
        savedMesh.GetUVs(1, savedMasks);
        Require(savedMesh.subMeshCount == 1 && savedMasks.SequenceEqual(masks), "Saved combined mesh lost its mask.");
        Vector3 visible = Vector3.Scale(savedMesh.bounds.size, new Vector3(40,24,40));
        Require((visible-new Vector3(.48f,.048f,.64f)).sqrMagnitude < 1e-10f, "Gameplay dimensions changed.");
        string report = $"PASS\nSource: {source.vertexCount} imported vertices, 140 triangles, Shell0/Cover1, readable.\n" +
            $"Combined: {savedMesh.vertexCount} vertices, 140 triangles, one submesh, 16-bit indices, UV1 mask verified.\n" +
            $"Gameplay dimensions: {visible:F6}; existing scale(40,24,40), Z180 rotation, centred pivot and cover UV orientation preserved.\n" +
            "Existing CaseMeshCombiner used without changes. Gameplay scene/network prefab unchanged.\n" +
            "Source prefab previews the included original artwork; runtime combined prefab uses the existing global cover array.\n" +
            "Play Mode performance was not measured.";
        File.WriteAllText(Review + "/unity-result.txt", report);
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/CozyGameCase_Source.prefab");
        QueuePreview((GameObject)Selection.activeObject);
        Debug.Log("[CozyGameCase] " + report);
    }

    private static void QueuePreview(GameObject asset)
    {
        double deadline = EditorApplication.timeSinceStartup + 15;
        EditorApplication.CallbackFunction callback = null;
        callback = () =>
        {
            Texture2D preview = AssetPreview.GetAssetPreview(asset);
            if (preview == null && EditorApplication.timeSinceStartup < deadline) return;
            EditorApplication.update -= callback;
            if (preview == null) return;
            RenderTexture temporary = RenderTexture.GetTemporary(preview.width, preview.height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            var readable = new Texture2D(preview.width, preview.height, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(preview, temporary);
                RenderTexture.active = temporary;
                readable.ReadPixels(new Rect(0,0,preview.width,preview.height),0,0);
                readable.Apply();
                File.WriteAllBytes(Review + "/unity_asset_preview.png", readable.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
                UnityEngine.Object.DestroyImmediate(readable);
            }
        };
        EditorApplication.update += callback;
    }

    private static Texture2D LoadTexture(string name)
    {
        string path = Folder + "/" + name;
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.maxTextureSize = 512;
        importer.isReadable = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Material MaterialAsset(string name, Shader shader)
    {
        string path = Folder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void SaveVisualPrefab(string name, Mesh mesh, Material[] materials)
    {
        var root = new GameObject(name);
        try
        {
            var visual = new GameObject("GameBoxVisual");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = new Vector3(40,24,40);
            visual.transform.localRotation = Quaternion.Euler(0,0,180);
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            PrefabUtility.SaveAsPrefabAsset(root, Folder + "/" + name + ".prefab", out bool saved);
            Require(saved, "Could not save visual prefab: " + name);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[CozyGameCase] " + message);
    }
}
