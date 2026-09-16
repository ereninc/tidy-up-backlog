#if UNITY_EDITOR

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CaseMeshCombiner
{
    [MenuItem(
        "Tools/Optimization/Create Combined Case Mesh")]
    private static void CreateCombinedMesh()
    {
        GameObject selected =
            Selection.activeGameObject;

        if (!selected)
        {
            Debug.LogError(
                "Select a GameObject containing the ORIGINAL Game Box MeshFilter.");

            return;
        }

        MeshFilter meshFilter =
            selected.GetComponentInChildren<MeshFilter>();

        if (!meshFilter ||
            !meshFilter.sharedMesh)
        {
            Debug.LogError(
                "Selected object has no MeshFilter/sharedMesh.");

            return;
        }

        Mesh source =
            meshFilter.sharedMesh;

        if (source.subMeshCount < 2)
        {
            Debug.LogError(
                $"{source.name} has only {source.subMeshCount} submesh(es). " +
                "Select the ORIGINAL Game Box mesh with Shell=0 and Cover=1.");

            return;
        }

        Mesh result =
            BuildCombinedMesh(source);

        string sourcePath =
            AssetDatabase.GetAssetPath(source);

        string directory =
            System.IO.Path.GetDirectoryName(sourcePath);

        string path =
            AssetDatabase.GenerateUniqueAssetPath(
                $"{directory}/{source.name}_Combined.asset");

        AssetDatabase.CreateAsset(
            result,
            path);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorGUIUtility.PingObject(result);

        Debug.Log(
            $"[CaseMeshCombiner] Created: {path}\n" +
            $"Source vertices: {source.vertexCount}\n" +
            $"Combined vertices: {result.vertexCount}\n" +
            $"Submeshes: {source.subMeshCount} -> {result.subMeshCount}",
            result);
    }

    private static Mesh BuildCombinedMesh(
        Mesh source)
    {
        Vector3[] sourceVertices =
            source.vertices;

        Vector3[] sourceNormals =
            source.normals;

        Vector4[] sourceTangents =
            source.tangents;

        Vector2[] sourceUV0 =
            source.uv;

        Color[] sourceColors =
            source.colors;

        var vertices =
            new List<Vector3>();

        var normals =
            new List<Vector3>();

        var tangents =
            new List<Vector4>();

        var uv0 =
            new List<Vector2>();

        /*
         * Unity Mesh UV channel 1
         * -> shader TEXCOORD1
         *
         * x = 0 -> shell
         * x = 1 -> cover
         */
        var surfaceData =
            new List<Vector2>();

        var colors =
            new List<Color>();

        var triangles =
            new List<int>();

        /*
         * Aynı source vertex hem shell hem cover tarafından
         * kullanılıyorsa duplicate ediyoruz.
         *
         * Çünkü aynı vertex'in maskesi hem 0 hem 1 olamaz.
         */
        var vertexMap =
            new Dictionary<VertexKey, int>();

        for (int subMeshIndex = 0;
             subMeshIndex < source.subMeshCount;
             subMeshIndex++)
        {
            /*
             * SENİN Game Box yapın:
             *
             * Submesh 0 = Shell
             * Submesh 1 = Cover Art
             *
             * 2+'daki herhangi bir şey varsa shell kabul edilir.
             */
            bool isCover =
                subMeshIndex == 1;

            float coverMask =
                isCover ? 1f : 0f;

            int[] sourceTriangles =
                source.GetTriangles(
                    subMeshIndex);

            for (int i = 0;
                 i < sourceTriangles.Length;
                 i++)
            {
                int sourceVertexIndex =
                    sourceTriangles[i];

                var key =
                    new VertexKey(
                        sourceVertexIndex,
                        isCover);

                if (!vertexMap.TryGetValue(
                        key,
                        out int destinationIndex))
                {
                    destinationIndex =
                        vertices.Count;

                    vertexMap.Add(
                        key,
                        destinationIndex);

                    // Position
                    vertices.Add(
                        sourceVertices[sourceVertexIndex]);

                    // Normal
                    if (sourceNormals != null &&
                        sourceNormals.Length ==
                        sourceVertices.Length)
                    {
                        normals.Add(
                            sourceNormals[
                                sourceVertexIndex]);
                    }

                    // Tangent
                    if (sourceTangents != null &&
                        sourceTangents.Length ==
                        sourceVertices.Length)
                    {
                        tangents.Add(
                            sourceTangents[
                                sourceVertexIndex]);
                    }

                    // UV0
                    if (sourceUV0 != null &&
                        sourceUV0.Length ==
                        sourceVertices.Length)
                    {
                        uv0.Add(
                            sourceUV0[
                                sourceVertexIndex]);
                    }
                    else
                    {
                        uv0.Add(
                            Vector2.zero);
                    }

                    /*
                     * TEXCOORD1.x
                     *
                     * 0 = shell
                     * 1 = cover
                     */
                    surfaceData.Add(
                        new Vector2(
                            coverMask,
                            0f));

                    /*
                     * Eski vertex color varsa koruyoruz.
                     * Mask için artık kullanılmıyor.
                     */
                    if (sourceColors != null &&
                        sourceColors.Length ==
                        sourceVertices.Length)
                    {
                        colors.Add(
                            sourceColors[
                                sourceVertexIndex]);
                    }
                    else
                    {
                        colors.Add(
                            Color.white);
                    }
                }

                triangles.Add(
                    destinationIndex);
            }
        }

        var mesh =
            new Mesh
            {
                name =
                    $"{source.name}_Combined"
            };

        if (vertices.Count >
            ushort.MaxValue)
        {
            mesh.indexFormat =
                IndexFormat.UInt32;
        }

        mesh.SetVertices(
            vertices);

        if (normals.Count ==
            vertices.Count)
        {
            mesh.SetNormals(
                normals);
        }

        if (tangents.Count ==
            vertices.Count)
        {
            mesh.SetTangents(
                tangents);
        }

        /*
         * TEXCOORD0
         */
        mesh.SetUVs(
            0,
            uv0);

        /*
         * TEXCOORD1
         *
         * Shader:
         *
         * float2 surfaceData : TEXCOORD1;
         */
        mesh.SetUVs(
            1,
            surfaceData);

        if (colors.Count ==
            vertices.Count)
        {
            mesh.SetColors(
                colors);
        }

        mesh.subMeshCount = 1;

        mesh.SetTriangles(
            triangles,
            0,
            true);

        if (normals.Count !=
            vertices.Count)
        {
            mesh.RecalculateNormals();
        }

        mesh.RecalculateBounds();

        return mesh;
    }

    private readonly struct VertexKey
    {
        private readonly int _sourceVertexIndex;
        private readonly bool _isCover;

        public VertexKey(
            int sourceVertexIndex,
            bool isCover)
        {
            _sourceVertexIndex =
                sourceVertexIndex;

            _isCover =
                isCover;
        }

        public override bool Equals(
            object obj)
        {
            return
                obj is VertexKey other &&
                _sourceVertexIndex ==
                other._sourceVertexIndex &&
                _isCover ==
                other._isCover;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash =
                    _sourceVertexIndex;

                hash =
                    (hash * 397) ^
                    _isCover.GetHashCode();

                return hash;
            }
        }
    }
}

#endif