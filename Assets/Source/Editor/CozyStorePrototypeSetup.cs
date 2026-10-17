using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using EXW.Multiplayer;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

// Temporary, request-driven scene authoring utility for the imported store.
[InitializeOnLoad]
internal static class CozyStorePrototypeSetup
{
    private const string Folder = "ArtReview/CozyStorePrototype_2026-10-16";
    private const string Request = Folder + "/request.txt";
    private const string Result = Folder + "/result.txt";
    private const string AssetFolder = "Assets/__PlaceholderAssetPacks/Shelf and Slots";
    private const string PrefabFolder = AssetFolder + "/CozyPrototype";
    private static double nextCheck;

    static CozyStorePrototypeSetup()
    {
        EditorApplication.update += CheckRequest;
        if (Directory.Exists(Folder)) File.WriteAllText(Folder + "/ready.txt", "setup-v7");
    }

    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck ||
            EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request)) return;
        string command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try
        {
            if (command == "inspect") Inspect();
            else if (command == "apply") Apply();
            else if (command == "validate") ValidateSavedScene();
            else if (command == "restore") RestoreBeforeSetup();
            else if (command == "materials") RepairMaterialBindings();
            else if (command == "case") CozyGameCaseAssetBuilder.Build();
            else if (command == "pc_inspect") StoreComputerEditorSetup.Inspect();
            else if (command == "pc_setup") StoreComputerEditorSetup.Build();
            else if (command == "pc_validate") StoreComputerEditorSetup.Validate();
            else if (command == "pc_finish") StoreComputerEditorSetup.Finish();
            else if (command == "pc_test") StoreComputerPlayModeValidation.Begin();
            else if (command == "pc_recover") StoreComputerPlayModeValidation.RecoverEditorState();
            else if (command == "pc_scene") EditorSceneManager.OpenScene("Assets/Scene/03_GameplayScene.unity");
            else throw new InvalidOperationException("Unknown command: " + command);
        }
        catch (Exception e)
        {
            File.WriteAllText(Result, "ERROR\n" + e);
            Debug.LogException(e);
        }
    }

    private static GameObject FindStore()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            GameObject found = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Select(t => t.gameObject)
                .FirstOrDefault(go => go.name == "CozyGamingStore_v03");
            if (found != null) return found;
        }
        throw new InvalidOperationException("CozyGamingStore_v03 was not found in an open scene.");
    }

    private static void Inspect()
    {
        GameObject store = FindStore();
        var report = new StringBuilder();
        report.AppendLine("INSPECTED " + store.scene.path + " dirty=" + store.scene.isDirty);
        foreach (Transform t in store.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.Contains("Shelf") || t.name.Contains("Placeholder"))
                report.AppendLine($"OBJECT {GetPath(t)} local={t.localPosition:F5} rotation={t.localEulerAngles:F3} scale={t.localScale:F5} world={t.position:F5} worldRot={t.eulerAngles:F3} active={t.gameObject.activeInHierarchy}");
            var mesh = t.GetComponent<MeshFilter>();
            if (mesh != null && (t.name.Contains("Shelf") || t.name.Contains("Visual") || t.name.Contains("Spotlight")))
                report.AppendLine($"MESH {GetPath(t)} asset={AssetDatabase.GetAssetPath(mesh.sharedMesh)} bounds={mesh.sharedMesh.bounds} readable={mesh.sharedMesh.isReadable} rendererBounds={t.GetComponent<Renderer>()?.bounds}");
        }
        foreach (var destination in UnityEngine.Object.FindObjectsByType<EXW.Multiplayer.NetworkGameCaseShelfDestination>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            report.AppendLine("EXISTING_SLOT " + GetPath(destination.transform));
        report.AppendLine("STORE_NETWORK_OBJECTS " + store.GetComponentsInChildren<Unity.Netcode.NetworkObject>(true).Length);
        report.AppendLine("STORE_PREFAB " + PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(store));
        File.WriteAllText(Result, report.ToString());
    }

    private static string GetPath(Transform t)
    {
        return t.parent == null ? t.name : GetPath(t.parent) + "/" + t.name;
    }

    [MenuItem("Tools/Game Cases/Temporary/Prepare Cozy Store Prototype")]
    private static void Apply()
    {
        GameObject store = FindStore();
        Scene scene = store.scene;
        if (scene.GetRootGameObjects().Any(go => go.name == "CozyStoreShelves"))
            throw new InvalidOperationException("CozyStoreShelves already exists; use validation instead of repeating migration.");
        GameObject slotTemplate = AssetDatabase.LoadAssetAtPath<GameObject>(AssetFolder + "/GameCaseShelfDestination.prefab");
        GameObject labelTemplate = AssetDatabase.LoadAssetAtPath<GameObject>(AssetFolder + "/ShelfRowGameInfoParent.prefab");
        if (slotTemplate == null || labelTemplate == null)
            throw new InvalidOperationException("The existing slot or row label prefab is missing.");

        Transform[] originals = store.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name.StartsWith("CST01_FBX_CST01_Shelf_") && !t.name.EndsWith("_Visual"))
            .OrderBy(t => t.name).ToArray();
        if (originals.Length != 10)
            throw new InvalidOperationException("Expected ten original shelf holders, found " + originals.Length);
        if (originals.Any(t => t.GetComponentsInChildren<NetworkObject>(true).Length != 0))
            throw new InvalidOperationException("An original shelf already has network state.");

        Directory.CreateDirectory(Folder);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the scene before migration.");
        if (!File.Exists(Folder + "/03_GameplayScene.before-shelf-setup.unity"))
            File.Copy(scene.path, Folder + "/03_GameplayScene.before-shelf-setup.unity", false);
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder(AssetFolder, "CozyPrototype");
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Prepare Cozy Store Shelves");
        Undo.RegisterFullObjectHierarchyUndo(store, "Unpack store and extract shelves");
        if (PrefabUtility.IsPartOfPrefabInstance(store))
            PrefabUtility.UnpackPrefabInstance(store, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        Transform container = originals[0].parent;
        var holders = originals.ToList();
        Transform originalDouble = originals.Single(t => t.name.Contains("Rear_Archive_Double"));
        foreach (float x in new[] { -6.2f, 6.2f })
        {
            GameObject copy = UnityEngine.Object.Instantiate(originalDouble.gameObject, container);
            copy.name = "CST01_FBX_CST01_Shelf_Extra_Double_" + (x < 0 ? "West" : "East");
            copy.transform.position = container.TransformPoint(new Vector3(-x * .01f, -.08f, 0));
            copy.transform.rotation = container.rotation * Quaternion.Euler(0, 0, -90);
            Undo.RegisterCreatedObjectUndo(copy, "Additional prototype rack");
            holders.Add(copy.transform);
        }

        var parent = new GameObject("CozyStoreShelves");
        SceneManager.MoveGameObjectToScene(parent, scene);
        Undo.RegisterCreatedObjectUndo(parent, "Shelf prefab instances");
        var prepared = new List<GameObject>();
        var summary = new StringBuilder();
        foreach (Transform holder in holders)
        {
            string name = holder.name.Replace("CST01_FBX_CST01_", "Cozy_");
            bool featured = name.Contains("Spotlight");
            bool doubleSided = name.Contains("Double");
            bool low = name.Contains("Island");
            // Imported FBX vertices are centimetres with reversed X, Z up.
            // Build a unit-scale, Y-up gameplay frame from the live model axes.
            Vector3 up = holder.TransformDirection(Vector3.forward).normalized;
            Vector3 back = holder.TransformDirection(Vector3.up).normalized;
            if (Vector3.Angle(up, Vector3.up) > .1f)
                throw new InvalidOperationException("Shelf is not upright: " + name);
            var root = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(root, "Extract shelf");
            root.transform.SetParent(parent.transform, false);
            root.transform.SetPositionAndRotation(holder.position, Quaternion.LookRotation(back, up));
            root.transform.localScale = Vector3.one;
            var originalMatrices = holder.GetComponentsInChildren<MeshFilter>(true)
                .ToDictionary(mesh => mesh, mesh => mesh.transform.localToWorldMatrix);
            Undo.SetTransformParent(holder, root.transform, "Extract shelf visual");
            holder.name = "Visual";
            foreach (MeshFilter mesh in holder.GetComponentsInChildren<MeshFilter>(true))
            {
                Matrix4x4 before = originalMatrices[mesh];
                Matrix4x4 after = mesh.transform.localToWorldMatrix;
                if (Enumerable.Range(0, 16).Any(i => Mathf.Abs(before[i] - after[i]) > .0001f))
                    throw new InvalidOperationException("Visual moved while extracting: " + name);
            }
            // Only the structural shelf mesh collides; decorative poster layers do not.
            MeshFilter structure = holder.GetComponentsInChildren<MeshFilter>(true)
                .First(mesh => mesh.name.EndsWith("_Visual"));
            var physicalCollider = structure.GetComponent<MeshCollider>();
            if (physicalCollider == null)
                physicalCollider = structure.gameObject.AddComponent<MeshCollider>();
            physicalCollider.sharedMesh = structure.sharedMesh;
            physicalCollider.convex = false;
            physicalCollider.isTrigger = false;

            Transform slots = NewChild("Slots", root.transform);
            Transform states = NewChild("Rows", root.transform);
            Transform labels = NewChild("Labels", root.transform);
            float[] decks = featured ? new[] { .56f } : low ? new[] { .13f, .94f } :
                doubleSided ? new[] { .32f, 1.18f, 2.04f, 2.90f } : new[] { .32f, 1.18f, 2.04f };
            int columns = featured ? 5 : 10;
            int faces = doubleSided ? 2 : 1;
            for (int face = 0; face < faces; face++)
            for (int level = 0; level < decks.Length; level++)
            {
                float front = featured ? -.43f : doubleSided ? -.59f : -.28f;
                float deck = decks[level];
                for (int group = 0; group < columns / 5; group++)
                {
                    string rowName = (face == 0 ? "Front" : "Back") + "_Row" + (level + 1).ToString("00") + "_Group" + (group + 1);
                    Transform rowObject = NewChild(rowName, states);
                    rowObject.gameObject.AddComponent<NetworkObject>();
                    var row = rowObject.gameObject.AddComponent<NetworkGameCaseShelfRow>();
                    var rowSlots = new NetworkGameCaseShelfDestination[5];
                    for (int lane = 0; lane < 5; lane++)
                    {
                        int column = group * 5 + lane;
                        float x = (column - (columns - 1) * .5f) * .512f;
                        GameObject slot = (GameObject)PrefabUtility.InstantiatePrefab(slotTemplate, scene);
                        Undo.RegisterCreatedObjectUndo(slot, "Configure shelf slot");
                        slot.name = rowName + "_Slot" + (lane + 1).ToString("00");
                        slot.transform.SetParent(slots, false);
                        slot.transform.localPosition = new Vector3(x, deck, face == 0 ? front : -front);
                        slot.transform.localRotation = Quaternion.Euler(0, face == 0 ? 0 : 180, 0);
                        slot.transform.localScale = Vector3.one;
                        var destination = slot.GetComponent<NetworkGameCaseShelfDestination>();
                        var receiver = slot.GetComponent<NetworkItemReceiver>();
                        var collider = slot.GetComponent<BoxCollider>();
                        collider.isTrigger = true;
                        collider.center = new Vector3(0, .35f, .185f);
                        collider.size = new Vector3(.510f, .70f, .67f);
                        Transform start = slot.transform.Find("StartPoint");
                        start.localPosition = Vector3.zero;
                        start.localRotation = Quaternion.identity;
                        start.localScale = Vector3.one;
                        var serialized = new SerializedObject(destination);
                        serialized.FindProperty("layout").enumValueIndex = (int)NetworkGameCaseShelfDestination.ShelfLayout.BackToFront;
                        serialized.FindProperty("facing").enumValueIndex = (int)NetworkGameCaseShelfDestination.ShelfFacing.Front;
                        serialized.FindProperty("capacity").intValue = 10;
                        serialized.FindProperty("startPoint").objectReferenceValue = start;
                        serialized.FindProperty("localStep").vector3Value = new Vector3(0, 0, .052f);
                        serialized.FindProperty("firstItemLocalOffset").vector3Value = new Vector3(0, .325f, .468f);
                        serialized.FindProperty("itemEulerOffset").vector3Value = new Vector3(270, 0, 0);
                        serialized.FindProperty("slotState").objectReferenceValue = slot.GetComponent<NetworkGameCaseShelfSlotState>();
                        serialized.FindProperty("previewCaseSize").vector3Value = new Vector3(.48f, .048f, .64f);
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        receiver.EditorConfigure(name + " / " + rowName, destination);
                        receiver.EditorConfigureInteractionColliders(new Collider[] { collider });
                        EditorUtility.SetDirty(receiver);
                        rowSlots[lane] = destination;
                    }
                    var rowSerialized = new SerializedObject(row);
                    SerializedProperty references = rowSerialized.FindProperty("slots");
                    references.arraySize = 5;
                    for (int lane = 0; lane < 5; lane++)
                        references.GetArrayElementAtIndex(lane).objectReferenceValue = rowSlots[lane];
                    rowSerialized.ApplyModifiedPropertiesWithoutUndo();
                    GameObject label = (GameObject)PrefabUtility.InstantiatePrefab(labelTemplate, scene);
                    Undo.RegisterCreatedObjectUndo(label, "Configure game row label");
                    label.name = rowName + "_GameInfo";
                    label.transform.SetParent(labels, false);
                    float labelX = featured ? 0 : (group == 0 ? -1.28f : 1.28f);
                    float labelZ = featured ? -.555f : doubleSided ? -.733f : -.415f;
                    label.transform.localPosition = new Vector3(labelX, featured ? .46f : deck - .018f, face == 0 ? labelZ : -labelZ);
                    label.transform.localRotation = Quaternion.Euler(0, face == 0 ? 0 : 180, 0);
                    label.transform.localScale = Vector3.one;
                    var sprite = label.GetComponent<SpriteRenderer>();
                    sprite.size = new Vector2(featured ? 2.20f : 1.80f, .08f);
                    sprite.color = new Color(.47f, .61f, .66f);
                    TMP_Text text = label.GetComponentInChildren<TMP_Text>(true);
                    text.rectTransform.sizeDelta = new Vector2(sprite.size.x - .10f, .075f);
                    text.enableWordWrapping = false;
                    text.enableAutoSizing = true;
                    text.fontSizeMin = .22f;
                    text.fontSizeMax = .72f;
                    text.fontSize = .72f;
                    text.text = "Empty";
                    var presenter = new SerializedObject(label.GetComponent<GameCaseShelfRowInfoPresenter>());
                    presenter.FindProperty("row").objectReferenceValue = row;
                    presenter.FindProperty("gameNameText").objectReferenceValue = text;
                    presenter.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            // Author assets at their ground-centred origin, then restore the scene placement.
            Vector3 position = root.transform.localPosition;
            Quaternion rotation = root.transform.localRotation;
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            string path = PrefabFolder + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, path, InteractionMode.AutomatedAction, out bool saved);
            if (!saved) throw new IOException("Could not save shelf prefab: " + path);
            root.transform.localPosition = position;
            root.transform.localRotation = rotation;
            prepared.Add(root);
            summary.AppendLine(name + " rows=" + root.GetComponentsInChildren<NetworkGameCaseShelfRow>(true).Length + " slots=" + root.GetComponentsInChildren<NetworkGameCaseShelfDestination>(true).Length + " prefab=" + path);
        }
        Undo.CollapseUndoOperations(undoGroup);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save prepared scene.");
        Selection.activeGameObject = parent;
        string validation = CozyStorePrototypeValidation.Validate(prepared.ToArray(), false);
        File.WriteAllText(Result, "APPLIED\n" + summary + validation);
        Debug.Log("[CozyStorePrototype] " + validation);
    }

    private static Transform NewChild(string name, Transform parent)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child.transform;
    }

    [MenuItem("Tools/Game Cases/Temporary/Validate Cozy Store Prototype")]
    private static void ValidateSavedScene()
    {
        Scene scene = FindStore().scene;
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save scene for reload validation.");
        scene = EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);
        if (PrefabUtility.IsPartOfPrefabInstance(FindStore()))
            throw new InvalidOperationException("The store must remain unpacked.");
        GameObject parent = scene.GetRootGameObjects().Single(go => go.name == "CozyStoreShelves");
        GameObject[] shelves = Enumerable.Range(0, parent.transform.childCount).Select(i => parent.transform.GetChild(i).gameObject).ToArray();
        File.WriteAllText(Result, "VALIDATED\n" + CozyStorePrototypeValidation.Validate(shelves, true));
        Selection.activeGameObject = parent;
        EditorApplication.update -= CheckRequest;
    }

    private static void RestoreBeforeSetup()
    {
        string path = FindStore().scene.path;
        File.Copy(Folder + "/03_GameplayScene.before-shelf-setup.unity", path, true);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        File.WriteAllText(Result, "RESTORED migration backup; ready to retry.");
    }

    [MenuItem("Tools/Game Cases/Temporary/Rebind Cozy Store Extracted Materials")]
    private static void RepairMaterialBindings()
    {
        const string modelPath = AssetFolder + "/CozyGamingStore_v03.fbx";
        const string materialFolder = AssetFolder + "/BlenderMats";
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        var extracted = AssetDatabase.FindAssets("t:Material", new[] { materialFolder })
            .Select(guid => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)))
            .ToDictionary(material => material.name, StringComparer.Ordinal);
        if (model == null || extracted.Count != 15)
            throw new InvalidOperationException("Expected the imported store and its 15 extracted materials.");
        foreach (Material material in extracted.Values)
            if (material.shader == null || material.shader.name != "Universal Render Pipeline/Lit" || !material.shader.isSupported)
                throw new InvalidOperationException("Extracted material has an unsupported shader: " + material.name);

        var byMesh = new Dictionary<Mesh, Material[]>();
        foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) continue;
            Material[] materials = renderer.sharedMaterials.Select(material =>
            {
                if (material == null || !extracted.TryGetValue(material.name, out Material external))
                    throw new InvalidOperationException("The model material remap is unresolved: " + renderer.name);
                return external;
            }).ToArray();
            if (byMesh.TryGetValue(mesh, out Material[] previous) && !previous.SequenceEqual(materials))
                throw new InvalidOperationException("Shared mesh has ambiguous material slots: " + renderer.name);
            byMesh[mesh] = materials;
        }

        Scene scene = FindStore().scene;
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save current scene before rebinding.");
        string backup = Folder + "/BeforeMaterialRebind";
        Directory.CreateDirectory(backup);
        if (!File.Exists(backup + "/03_GameplayScene.unity"))
            File.Copy(scene.path, backup + "/03_GameplayScene.unity");
        int prefabSlots = 0;
        int prefabCount = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string copy = backup + "/" + Path.GetFileName(path);
            if (!File.Exists(copy)) File.Copy(path, copy);
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                prefabSlots += RebindRenderers(contents, byMesh, modelPath, false);
                PrefabUtility.SaveAsPrefabAsset(contents, path, out bool saved);
                if (!saved) throw new IOException("Could not save material bindings: " + path);
                prefabCount++;
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
        int sceneSlots = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
            sceneSlots += RebindRenderers(root, byMesh, modelPath, true);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save scene material bindings.");

        // Reload serialized results so validation cannot rely on in-memory edits.
        scene = EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);
        int verifiedSceneSlots = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
            verifiedSceneSlots += VerifyMaterialBindings(root, byMesh, modelPath);
        int verifiedPrefabSlots = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
            verifiedPrefabSlots += VerifyMaterialBindings(
                AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)), byMesh, modelPath);
        File.WriteAllText(Result, $"MATERIALS_FIXED\n{prefabCount} prefabs; {prefabSlots} prefab slots and {sceneSlots} scene slots rebound.\n" +
            $"Reload verified: {verifiedPrefabSlots} prefab slots, {verifiedSceneSlots} scene slots reference BlenderMats assets.\n" +
            "All 15 extracted materials use supported URP/Lit. Existing material values preserved.");
        EditorApplication.update -= CheckRequest;
    }

    private static int RebindRenderers(GameObject root, Dictionary<Mesh, Material[]> byMesh, string modelPath, bool undo)
    {
        int changedSlots = 0;
        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || AssetDatabase.GetAssetPath(mesh) != modelPath) continue;
            if (!byMesh.TryGetValue(mesh, out Material[] expected))
                throw new InvalidOperationException("No imported material layout for " + renderer.name);
            if (renderer.sharedMaterials.SequenceEqual(expected)) continue;
            if (undo) Undo.RecordObject(renderer, "Rebind extracted store materials");
            renderer.sharedMaterials = expected;
            if (PrefabUtility.IsPartOfPrefabInstance(renderer))
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            EditorUtility.SetDirty(renderer);
            changedSlots += expected.Length;
        }
        return changedSlots;
    }

    private static int VerifyMaterialBindings(GameObject root, Dictionary<Mesh, Material[]> byMesh, string modelPath)
    {
        int slots = 0;
        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || AssetDatabase.GetAssetPath(mesh) != modelPath) continue;
            if (!byMesh.TryGetValue(mesh, out Material[] expected) || !renderer.sharedMaterials.SequenceEqual(expected))
                throw new InvalidOperationException("Material bindings did not persist: " + renderer.name);
            slots += expected.Length;
        }
        return slots;
    }
}
