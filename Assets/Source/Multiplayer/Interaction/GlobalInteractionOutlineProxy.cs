using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class GlobalInteractionOutlineProxy : MonoBehaviour
{
    public static GlobalInteractionOutlineProxy Instance { get; private set; }

    [Header("Renderers")]
    [SerializeField] private MeshFilter maskMeshFilter;
    [SerializeField] private MeshRenderer maskRenderer;

    [SerializeField] private MeshFilter fillMeshFilter;
    [SerializeField] private MeshRenderer fillRenderer;

    [Header("Materials")]
    [SerializeField] private Material maskTemplate;
    [SerializeField] private Material fillTemplate;

    [Header("Defaults")]
    [SerializeField] private Color defaultColor = Color.white;
    [SerializeField, Range(0f, 10f)] private float defaultWidth = 2f;

    [Header("Render")]
    [SerializeField, Range(3000, 4999)]
    private int overlayRenderQueue = 4999;

    private readonly Dictionary<Mesh, Mesh> outlineMeshCache = new();

    private Renderer currentTarget;

    private Material maskMaterial;
    private Material fillMaterial;

    private Mesh currentSourceMesh;

    public Renderer CurrentTarget => currentTarget;
    public bool IsVisible => currentTarget != null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        InitializeMaterials();
        InitializeRenderers();

        SetRendererState(false);
    }

    private void LateUpdate()
    {
        if (currentTarget == null)
        {
            return;
        }

        if (!currentTarget.enabled ||
            !currentTarget.gameObject.activeInHierarchy)
        {
            HideImmediate();
            return;
        }

        SyncTransform();
    }

    public void Show(
        Renderer target,
        Color? color = null,
        float? width = null)
    {
        if (!target)
        {
            return;
        }

        if (!TryGetSourceMesh(target, out var sourceMesh))
        {
            return;
        }

        currentTarget = target;

        if (currentSourceMesh != sourceMesh)
        {
            currentSourceMesh = sourceMesh;

            Mesh outlineMesh = GetOrCreateOutlineMesh(sourceMesh);

            maskMeshFilter.sharedMesh = outlineMesh;
            fillMeshFilter.sharedMesh = outlineMesh;
        }

        int targetLayer = target.gameObject.layer;

        maskRenderer.gameObject.layer = targetLayer;
        fillRenderer.gameObject.layer = targetLayer;

        ApplyProperties(
            color ?? defaultColor,
            width ?? defaultWidth);

        SyncTransform();

        SetRendererState(true);
    }

    public void Hide(Renderer target)
    {
        // Eski target'ın geç gelen exit event'i,
        // yeni target'ın outline'ını kapatmasın.
        if (currentTarget != target)
        {
            return;
        }

        HideImmediate();
    }

    public void Hide()
    {
        HideImmediate();
    }

    private void HideImmediate()
    {
        currentTarget = null;
        SetRendererState(false);
    }

    private void SyncTransform()
    {
        if (!currentTarget)
        {
            return;
        }

        Transform targetTransform = currentTarget.transform;

        transform.SetPositionAndRotation(
            targetTransform.position,
            targetTransform.rotation);

        transform.localScale = targetTransform.lossyScale;
    }

    private void InitializeMaterials()
    {
        if (!maskTemplate)
        {
            maskTemplate =
                Resources.Load<Material>("Materials/OutlineMask");
        }

        if (!fillTemplate)
        {
            fillTemplate =
                Resources.Load<Material>("Materials/OutlineFill");
        }

        if (!maskTemplate || !fillTemplate)
        {
            Debug.LogError(
                "GlobalInteractionOutlineProxy: Outline materials could not be found.",
                this);

            enabled = false;
            return;
        }

        // Artık object başına değil, oyun boyunca sadece 2 instance.
        maskMaterial = Instantiate(maskTemplate);
        fillMaterial = Instantiate(fillTemplate);

        maskMaterial.name = "Global Outline Mask";
        fillMaterial.name = "Global Outline Fill";

        maskMaterial.renderQueue = overlayRenderQueue;
        fillMaterial.renderQueue =
            Mathf.Min(overlayRenderQueue + 1, 5000);

        maskMaterial.SetFloat(
            "_ZTest",
            (float)CompareFunction.Always);

        fillMaterial.SetFloat(
            "_ZTest",
            (float)CompareFunction.Always);
    }

    private void InitializeRenderers()
    {
        ConfigureRenderer(maskRenderer, maskMaterial);
        ConfigureRenderer(fillRenderer, fillMaterial);
    }

    private static void ConfigureRenderer(
        MeshRenderer renderer,
        Material material)
    {
        if (!renderer)
        {
            return;
        }

        renderer.sharedMaterial = material;

        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage =
            ReflectionProbeUsage.Off;

        renderer.motionVectorGenerationMode =
            MotionVectorGenerationMode.ForceNoMotion;

        renderer.allowOcclusionWhenDynamic = false;
    }

    private void ApplyProperties(
        Color color,
        float width)
    {
        fillMaterial.SetColor(
            "_OutlineColor",
            color);

        fillMaterial.SetFloat(
            "_OutlineWidth",
            width);
    }

    private void SetRendererState(bool visible)
    {
        if (maskRenderer)
        {
            maskRenderer.enabled = visible;
        }

        if (fillRenderer)
        {
            fillRenderer.enabled = visible;
        }
    }

    private static bool TryGetSourceMesh(
        Renderer target,
        out Mesh mesh)
    {
        mesh = null;

        if (target is not MeshRenderer)
        {
            Debug.LogWarning(
                $"Global outline currently supports MeshRenderer only: {target.name}",
                target);

            return false;
        }

        MeshFilter meshFilter =
            target.GetComponent<MeshFilter>();

        if (!meshFilter || !meshFilter.sharedMesh)
        {
            Debug.LogWarning(
                $"No MeshFilter/sharedMesh found for {target.name}.",
                target);

            return false;
        }

        mesh = meshFilter.sharedMesh;

        return true;
    }

    private Mesh GetOrCreateOutlineMesh(
        Mesh sourceMesh)
    {
        if (outlineMeshCache.TryGetValue(
                sourceMesh,
                out Mesh cached))
        {
            return cached;
        }

        /*
         * sourceMesh:
         *
         * submesh 0 = shell
         * submesh 1 = cover
         *
         * outlineMesh:
         *
         * submesh 0 = ALL triangles
         *
         * Böylece Mask renderer = 1 draw
         * Fill renderer = 1 draw
         */

        int[] allTriangles = sourceMesh.triangles;

        Mesh outlineMesh = Instantiate(sourceMesh);

        outlineMesh.name =
            $"{sourceMesh.name}_GlobalOutline";

        outlineMesh.subMeshCount = 1;

        outlineMesh.SetTriangles(
            allTriangles,
            0,
            true);

        List<Vector3> smoothNormals =
            SmoothNormals(sourceMesh);

        // QuickOutline'ın kullandığı UV4/TEXCOORD3.
        outlineMesh.SetUVs(
            3,
            smoothNormals);

        outlineMeshCache.Add(
            sourceMesh,
            outlineMesh);

        return outlineMesh;
    }

    private static List<Vector3> SmoothNormals(
        Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;

        var smoothNormals =
            new List<Vector3>(normals);

        var groups =
            new Dictionary<Vector3, List<int>>();

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 vertex = vertices[i];

            if (!groups.TryGetValue(
                    vertex,
                    out List<int> indices))
            {
                indices = new List<int>();
                groups.Add(vertex, indices);
            }

            indices.Add(i);
        }

        foreach (List<int> indices in groups.Values)
        {
            if (indices.Count == 1)
            {
                continue;
            }

            Vector3 smoothNormal = Vector3.zero;

            for (int i = 0; i < indices.Count; i++)
            {
                smoothNormal +=
                    smoothNormals[indices[i]];
            }

            smoothNormal.Normalize();

            for (int i = 0; i < indices.Count; i++)
            {
                smoothNormals[indices[i]] =
                    smoothNormal;
            }
        }

        return smoothNormals;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (maskMaterial)
        {
            Destroy(maskMaterial);
        }

        if (fillMaterial)
        {
            Destroy(fillMaterial);
        }

        foreach (Mesh mesh in outlineMeshCache.Values)
        {
            if (mesh)
            {
                Destroy(mesh);
            }
        }

        outlineMeshCache.Clear();
    }
}