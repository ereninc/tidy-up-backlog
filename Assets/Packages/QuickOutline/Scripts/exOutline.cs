//
//  exOutline.cs
//  Based on QuickOutline by Chris Nolet
//

using System;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class exOutline : MonoBehaviour
{
    private static readonly HashSet<Mesh> RegisteredMeshes = new HashSet<Mesh>();

    public enum Mode
    {
        OutlineAll,
        OutlineVisible,
        OutlineHidden,
        OutlineAndSilhouette,
        SilhouetteOnly
    }

    public Mode OutlineMode
    {
        get => outlineMode;
        set
        {
            outlineMode = value;
            needsUpdate = true;
        }
    }

    public Color OutlineColor
    {
        get => outlineColor;
        set
        {
            outlineColor = value;
            needsUpdate = true;
        }
    }

    public float OutlineWidth
    {
        get => outlineWidth;
        set
        {
            outlineWidth = value;
            needsUpdate = true;
        }
    }

    [Serializable]
    private class ListVector3
    {
        public List<Vector3> data;
    }

    [Header("Outline")]
    [SerializeField] private Mode outlineMode = Mode.OutlineAll;
    [SerializeField] private Color outlineColor = Color.white;
    [SerializeField, Range(0f, 10f)] private float outlineWidth = 2f;
    [SerializeField] private float hoverWidth = 2f;

    [Header("Occlusion")]
    [SerializeField, Tooltip("Ignores scene depth and draws the complete outline over every world object.")]
    private bool alwaysRenderOnTop = true;

    [SerializeField, Range(3000, 4999), Tooltip("Mask uses this queue; fill uses the following queue.")]
    private int overlayRenderQueue = 4999;

    [Header("Lifetime")]
    [SerializeField, Tooltip("Normally false for hover/interaction outlines.")]
    private bool visibleOnEnable;

    [Header("Optional")]
    [SerializeField, Tooltip(
        "Precompute enabled: Per-vertex calculations are performed in the editor and serialized with the object. "
        + "Precompute disabled: Per-vertex calculations are performed at runtime in Awake(). This may cause a pause for large meshes.")]
    private bool precomputeOutline;

    [SerializeField, HideInInspector] private List<Mesh> bakeKeys = new List<Mesh>();
    [SerializeField, HideInInspector] private List<ListVector3> bakeValues = new List<ListVector3>();

    private Renderer[] renderers;
    private Material outlineMaskMaterial;
    private Material outlineFillMaterial;

    private bool initialized;
    private bool materialsAttached;
    private bool outlineRequested;
    private bool needsUpdate;

    private void Awake()
    {
        Initialize();
        outlineRequested = visibleOnEnable;
    }

    private void OnEnable()
    {
        Initialize();

        if (!outlineRequested)
        {
            return;
        }

        AttachOutlineMaterials();
        UpdateMaterialProperties();
    }

    private void OnValidate()
    {
        overlayRenderQueue = Mathf.Clamp(overlayRenderQueue, 3000, 4999);
        needsUpdate = true;

        if ((!precomputeOutline && bakeKeys.Count != 0) || bakeKeys.Count != bakeValues.Count)
        {
            bakeKeys.Clear();
            bakeValues.Clear();
        }

        if (precomputeOutline && bakeKeys.Count == 0)
        {
            Bake();
        }
    }

    private void Update()
    {
        if (!needsUpdate || !materialsAttached)
        {
            return;
        }

        UpdateMaterialProperties();
    }

    private void OnDisable()
    {
        DetachOutlineMaterials();
    }

    private void OnDestroy()
    {
        DetachOutlineMaterials();

        if (outlineMaskMaterial)
        {
            Destroy(outlineMaskMaterial);
        }

        if (outlineFillMaterial)
        {
            Destroy(outlineFillMaterial);
        }
    }

    [Button]
    public void OnSelected()
    {
        Initialize();

        outlineColor = Color.white;
        outlineWidth = hoverWidth;
        outlineRequested = true;

        AttachOutlineMaterials();
        UpdateMaterialProperties();
    }

    [Button]
    public void OnHide()
    {
        outlineRequested = false;
        outlineWidth = 0f;
        needsUpdate = false;

        // Width=0 is not enough. The mask pass would keep writing stencil and
        // could cut the selected object's outline when many objects overlap.
        DetachOutlineMaterials();
    }

    private void Initialize()
    {
        if (initialized)
        {
            return;
        }

        renderers = GetComponentsInChildren<Renderer>(true);

        var maskTemplate = Resources.Load<Material>("Materials/OutlineMask");
        var fillTemplate = Resources.Load<Material>("Materials/OutlineFill");

        if (!maskTemplate || !fillTemplate)
        {
            Debug.LogError(
                "exOutline could not load Resources/Materials/OutlineMask and/or OutlineFill.",
                this);
            return;
        }

        outlineMaskMaterial = Instantiate(maskTemplate);
        outlineFillMaterial = Instantiate(fillTemplate);

        outlineMaskMaterial.name = "OutlineMask (Instance)";
        outlineFillMaterial.name = "OutlineFill (Instance)";

        LoadSmoothNormals();

        initialized = true;
        needsUpdate = true;
    }

    private void AttachOutlineMaterials()
    {
        if (!initialized || materialsAttached)
        {
            return;
        }

        foreach (var renderer in renderers)
        {
            if (!renderer)
            {
                continue;
            }

            var materials = renderer.sharedMaterials.ToList();

            if (!materials.Contains(outlineMaskMaterial))
            {
                materials.Add(outlineMaskMaterial);
            }

            if (!materials.Contains(outlineFillMaterial))
            {
                materials.Add(outlineFillMaterial);
            }

            // sharedMaterials avoids cloning every original material on selection.
            renderer.sharedMaterials = materials.ToArray();
        }

        materialsAttached = true;
    }

    private void DetachOutlineMaterials()
    {
        if (!materialsAttached || renderers == null)
        {
            return;
        }

        foreach (var renderer in renderers)
        {
            if (!renderer)
            {
                continue;
            }

            var materials = renderer.sharedMaterials.ToList();
            materials.RemoveAll(material =>
                material == outlineMaskMaterial || material == outlineFillMaterial);

            renderer.sharedMaterials = materials.ToArray();
        }

        materialsAttached = false;
    }

    private void Bake()
    {
        var bakedMeshes = new HashSet<Mesh>();

        foreach (var meshFilter in GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = meshFilter.sharedMesh;

            if (!mesh || !bakedMeshes.Add(mesh))
            {
                continue;
            }

            bakeKeys.Add(mesh);
            bakeValues.Add(new ListVector3 { data = SmoothNormals(mesh) });
        }
    }

    private void LoadSmoothNormals()
    {
        foreach (var meshFilter in GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = meshFilter.sharedMesh;

            if (!mesh || !RegisteredMeshes.Add(mesh))
            {
                continue;
            }

            var index = bakeKeys.IndexOf(mesh);
            var smoothNormals = index >= 0
                ? bakeValues[index].data
                : SmoothNormals(mesh);

            mesh.SetUVs(3, smoothNormals);

            var renderer = meshFilter.GetComponent<Renderer>();
            if (renderer)
            {
                CombineSubmeshes(mesh, renderer.sharedMaterials);
            }
        }

        foreach (var skinnedMeshRenderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = skinnedMeshRenderer.sharedMesh;

            if (!mesh || !RegisteredMeshes.Add(mesh))
            {
                continue;
            }

            mesh.uv4 = new Vector2[mesh.vertexCount];
            CombineSubmeshes(mesh, skinnedMeshRenderer.sharedMaterials);
        }
    }

    private static List<Vector3> SmoothNormals(Mesh mesh)
    {
        var groups = mesh.vertices
            .Select((vertex, index) => new KeyValuePair<Vector3, int>(vertex, index))
            .GroupBy(pair => pair.Key);

        var smoothNormals = new List<Vector3>(mesh.normals);

        foreach (var group in groups)
        {
            if (group.Count() == 1)
            {
                continue;
            }

            var smoothNormal = Vector3.zero;

            foreach (var pair in group)
            {
                smoothNormal += smoothNormals[pair.Value];
            }

            smoothNormal.Normalize();

            foreach (var pair in group)
            {
                smoothNormals[pair.Value] = smoothNormal;
            }
        }

        return smoothNormals;
    }

    private static void CombineSubmeshes(Mesh mesh, Material[] materials)
    {
        if (mesh.subMeshCount == 1 || mesh.subMeshCount > materials.Length)
        {
            return;
        }

        mesh.subMeshCount++;
        mesh.SetTriangles(mesh.triangles, mesh.subMeshCount - 1);
    }

    private void UpdateMaterialProperties()
    {
        if (!initialized)
        {
            return;
        }

        needsUpdate = false;

        outlineMaskMaterial.renderQueue = overlayRenderQueue;
        outlineFillMaterial.renderQueue = Mathf.Min(overlayRenderQueue + 1, 5000);
        outlineFillMaterial.SetColor("_OutlineColor", outlineColor);

        if (alwaysRenderOnTop)
        {
            // Mask first, expanded fill second. Both ignore the scene depth buffer.
            outlineMaskMaterial.SetFloat("_ZTest", (float)CompareFunction.Always);
            outlineFillMaterial.SetFloat("_ZTest", (float)CompareFunction.Always);
            outlineFillMaterial.SetFloat("_OutlineWidth", outlineWidth);
            return;
        }

        switch (outlineMode)
        {
            case Mode.OutlineAll:
                outlineMaskMaterial.SetFloat("_ZTest", (float)CompareFunction.Always);
                outlineFillMaterial.SetFloat("_ZTest", (float)CompareFunction.Always);
                outlineFillMaterial.SetFloat("_OutlineWidth", outlineWidth);
                break;

            case Mode.OutlineVisible:
                outlineMaskMaterial.SetFloat("_ZTest", (float)CompareFunction.Always);
                outlineFillMaterial.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
                outlineFillMaterial.SetFloat("_OutlineWidth", outlineWidth);
                break;

            case Mode.OutlineHidden:
                outlineMaskMaterial.SetFloat("_ZTest", (float)CompareFunction.Always);
                outlineFillMaterial.SetFloat("_ZTest", (float)CompareFunction.Greater);
                outlineFillMaterial.SetFloat("_OutlineWidth", outlineWidth);
                break;

            case Mode.OutlineAndSilhouette:
                outlineMaskMaterial.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
                outlineFillMaterial.SetFloat("_ZTest", (float)CompareFunction.Always);
                outlineFillMaterial.SetFloat("_OutlineWidth", outlineWidth);
                break;

            case Mode.SilhouetteOnly:
                outlineMaskMaterial.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
                outlineFillMaterial.SetFloat("_ZTest", (float)CompareFunction.Greater);
                outlineFillMaterial.SetFloat("_OutlineWidth", 0f);
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
