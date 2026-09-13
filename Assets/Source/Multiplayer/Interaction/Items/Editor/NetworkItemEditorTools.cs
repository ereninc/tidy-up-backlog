#if UNITY_EDITOR
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace EXW.Multiplayer.Editor
{
    public static class NetworkItemEditorTools
    {
        private const string RootMenu = "Tools/EXW/Multiplayer/Items/";

        [MenuItem(RootMenu + "Configure Selected Player Carrier", priority = 10)]
        private static void ConfigureSelectedPlayerCarrier()
        {
            if (!TryGetSelectedNetworkRoot(out GameObject playerRoot))
            {
                EditorUtility.DisplayDialog(
                    "Configure Item Carrier",
                    "Select the player prefab's NetworkObject root. Open a prefab " +
                    "asset in Prefab Mode before running this command.",
                    "OK");
                return;
            }

            if (playerRoot.GetComponent<NetworkInteractionController>() == null)
            {
                EditorUtility.DisplayDialog(
                    "Configure Item Carrier",
                    "The selected NetworkObject has no NetworkInteractionController. " +
                    "Configure the network player interaction component first.",
                    "OK");
                return;
            }

            Undo.SetCurrentGroupName("Configure Network Item Carrier");
            int undoGroup = Undo.GetCurrentGroup();

            NetworkItemCarrier carrier =
                playerRoot.GetComponent<NetworkItemCarrier>();

            if (carrier == null)
            {
                carrier = Undo.AddComponent<NetworkItemCarrier>(playerRoot);
            }

            Transform anchor = FindChildByName(
                playerRoot.transform,
                "ItemCarryAnchor");

            if (anchor == null)
            {
                Camera viewCamera =
                    playerRoot.GetComponentInChildren<Camera>(true);
                Transform parent = viewCamera != null
                    ? viewCamera.transform
                    : playerRoot.transform;

                GameObject anchorObject = new GameObject("ItemCarryAnchor");
                Undo.RegisterCreatedObjectUndo(
                    anchorObject,
                    "Create Item Carry Anchor");
                Undo.SetTransformParent(
                    anchorObject.transform,
                    parent,
                    "Parent Item Carry Anchor");
                anchor = anchorObject.transform;
                anchor.localRotation = Quaternion.identity;
                anchor.localScale = Vector3.one;
                anchor.localPosition = viewCamera != null
                    ? new Vector3(0.28f, -0.22f, 0.7f)
                    : new Vector3(0.28f, 1.35f, 0.7f);
            }

            Undo.RecordObject(carrier, "Assign Item Carry Anchor");
            carrier.EditorAssignCarryAnchor(anchor);

            NetworkItemCarrierDebugHud hud =
                playerRoot.GetComponent<NetworkItemCarrierDebugHud>();

            if (hud == null)
            {
                hud = Undo.AddComponent<NetworkItemCarrierDebugHud>(playerRoot);
            }

            Undo.RecordObject(hud, "Assign Carrier HUD References");
            hud.AutoAssignReferences();

            EditorUtility.SetDirty(carrier);
            EditorUtility.SetDirty(hud);
            PrefabUtility.RecordPrefabInstancePropertyModifications(carrier);
            PrefabUtility.RecordPrefabInstancePropertyModifications(hud);
            Undo.CollapseUndoOperations(undoGroup);

            Selection.activeGameObject = playerRoot;
            Debug.Log(
                "[ItemSetup] Player carrier configured. Verify ItemCarryAnchor " +
                "position in the prefab Scene view, then save the prefab.",
                playerRoot);
        }

        [MenuItem(RootMenu + "Configure Selected Player Carrier", true)]
        private static bool CanConfigureSelectedPlayerCarrier()
        {
            return Selection.activeGameObject != null;
        }

        [MenuItem(RootMenu + "Configure Selected Carry Item", priority = 12)]
        private static void ConfigureSelectedCarryItem()
        {
            GameObject root = Selection.activeGameObject;

            if (root == null || EditorUtility.IsPersistent(root))
            {
                EditorUtility.DisplayDialog(
                    "Configure Carry Item",
                    "Select the intended item root in a scene or open its prefab " +
                    "in Prefab Mode first.",
                    "OK");
                return;
            }

            NetworkObject parentNetworkObject = root.transform.parent != null
                ? root.transform.parent.GetComponentInParent<NetworkObject>()
                : null;

            if (parentNetworkObject != null)
            {
                EditorUtility.DisplayDialog(
                    "Configure Carry Item",
                    "The selected root is already below another NetworkObject. " +
                    "Use a separate scale-one item root.",
                    "OK");
                return;
            }

            Undo.SetCurrentGroupName("Configure Network Carry Item");
            int undoGroup = Undo.GetCurrentGroup();

            if (root.GetComponent<NetworkObject>() == null)
            {
                Undo.AddComponent<NetworkObject>(root);
            }

            NetworkWorldItem item = root.GetComponent<NetworkWorldItem>();

            if (item == null)
            {
                item = Undo.AddComponent<NetworkWorldItem>(root);
            }

            NetworkItemPresentation presentation =
                root.GetComponent<NetworkItemPresentation>();

            if (presentation == null)
            {
                presentation =
                    Undo.AddComponent<NetworkItemPresentation>(root);
            }

            if (root.GetComponent<NetworkCarryable>() == null)
            {
                Undo.AddComponent<NetworkCarryable>(root);
            }

            NetworkHeldItemPoseFollower follower =
                root.GetComponent<NetworkHeldItemPoseFollower>();

            if (follower == null)
            {
                follower =
                    Undo.AddComponent<NetworkHeldItemPoseFollower>(root);
            }

            if (root.GetComponent<NetworkItemInteractable>() == null)
            {
                Undo.AddComponent<NetworkItemInteractable>(root);
            }

            Undo.RecordObject(item, "Set Item Display Name");
            item.EditorConfigure(root.name);
            Undo.RecordObject(presentation, "Assign Item Presentation");
            presentation.AutoAssignReferences();
            Undo.RecordObject(follower, "Assign Held Pose Follower");
            follower.AutoAssignReferences();

            EditorUtility.SetDirty(item);
            EditorUtility.SetDirty(presentation);
            EditorUtility.SetDirty(follower);
            PrefabUtility.RecordPrefabInstancePropertyModifications(item);
            PrefabUtility.RecordPrefabInstancePropertyModifications(presentation);
            PrefabUtility.RecordPrefabInstancePropertyModifications(follower);
            Undo.CollapseUndoOperations(undoGroup);

            if (!ApproximatelyOne(root.transform.localScale))
            {
                Debug.LogWarning(
                    "[ItemSetup] Item configured, but its NetworkObject root scale " +
                    "is not 1. Move scaling to a visual child before testing.",
                    root);
            }

            Debug.Log(
                "[ItemSetup] Carry item configured. Verify Presentation colliders " +
                "and authored grip/drop offsets, then save.",
                root);
        }

        [MenuItem(RootMenu + "Configure Selected Carry Item", true)]
        private static bool CanConfigureSelectedCarryItem()
        {
            return Selection.activeGameObject != null;
        }

        [MenuItem(RootMenu + "Create Scene Carry Test Rig", priority = 20)]
        private static void CreateSceneCarryTestRig()
        {
            Undo.SetCurrentGroupName("Create Network Item Test Rig");
            int undoGroup = Undo.GetCurrentGroup();

            GetRigBasis(
                out Vector3 origin,
                out Vector3 forward,
                out Vector3 right);

            GameObject bottle = CreateCarryItem(
                "Blue Bottle",
                PrimitiveType.Cylinder,
                origin + forward * 2.1f - right * 0.8f + Vector3.up * 0.7f,
                new Vector3(0.35f, 0.7f, 0.35f));
            CreateCarryItem(
                "Repair Tool",
                PrimitiveType.Cube,
                origin + forward * 2.1f + Vector3.up * 0.11f,
                new Vector3(0.65f, 0.22f, 0.25f));
            CreateCarryItem(
                "Trash Bag",
                PrimitiveType.Sphere,
                origin + forward * 2.1f + right * 0.8f + Vector3.up * 0.4f,
                new Vector3(0.7f, 0.8f, 0.7f));

            CreateShelf(
                origin + forward * 5f - right * 3f,
                forward);
            CreateTable(
                origin + forward * 5f,
                forward);
            CreateTrash(
                origin + forward * 5f + right * 3f,
                forward);

            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = bottle;
            EditorGUIUtility.PingObject(bottle);

            Debug.Log(
                "[ItemSetup] Carry test rig created: 3 named items, a 3-slot " +
                "shelf, free-placement table and consuming trash bin. Save the " +
                "scene before starting host/client.");
        }

        [MenuItem(RootMenu + "Validate Open Scene", priority = 30)]
        private static void ValidateOpenScene()
        {
            int errors = 0;
            int warnings = 0;

            NetworkItemCarrier[] carriers =
                Object.FindObjectsByType<NetworkItemCarrier>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            NetworkWorldItem[] items =
                Object.FindObjectsByType<NetworkWorldItem>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            NetworkItemReceiver[] receivers =
                Object.FindObjectsByType<NetworkItemReceiver>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

            for (int i = 0; i < carriers.Length; i++)
            {
                if (carriers[i].ItemCarryAnchor == null)
                {
                    errors++;
                    Debug.LogError(
                        "[ItemValidation] Carrier has no ItemCarryAnchor.",
                        carriers[i]);
                }

                if (carriers[i].GetComponent<NetworkInteractionController>() == null)
                {
                    errors++;
                    Debug.LogError(
                        "[ItemValidation] Carrier root has no " +
                        "NetworkInteractionController.",
                        carriers[i]);
                }
            }

            for (int i = 0; i < items.Length; i++)
            {
                NetworkWorldItem item = items[i];

                if (item.GetComponent<NetworkCarryable>() == null ||
                    item.GetComponent<NetworkItemInteractable>() == null ||
                    item.GetComponent<NetworkItemPresentation>() == null)
                {
                    errors++;
                    Debug.LogError(
                        "[ItemValidation] Carry item is missing Carryable, " +
                        "ItemInteractable or Presentation.",
                        item);
                }

                if (item.GetComponent<NetworkHeldItemPoseFollower>() == null)
                {
                    warnings++;
                    Debug.LogWarning(
                        "[ItemValidation] Item has no held pose follower. It will " +
                        "follow player root parenting but not camera pitch.",
                        item);
                }

                if (!ApproximatelyOne(item.transform.localScale))
                {
                    warnings++;
                    Debug.LogWarning(
                        "[ItemValidation] Keep NetworkObject root scale at 1 and " +
                        "scale a visual child instead.",
                        item);
                }
            }

            for (int i = 0; i < receivers.Length; i++)
            {
                if (receivers[i].Destination == null)
                {
                    errors++;
                    Debug.LogError(
                        "[ItemValidation] Receiver has no destination strategy.",
                        receivers[i]);
                }
            }

            if (carriers.Length == 0)
            {
                warnings++;
                Debug.LogWarning(
                    "[ItemValidation] No scene player carrier found. This is " +
                    "normal if the network player exists only as a prefab asset.");
            }

            if (errors == 0)
            {
                Debug.Log(
                    $"[ItemValidation] PASS | Items={items.Length}, " +
                    $"Receivers={receivers.Length}, Carriers={carriers.Length}, " +
                    $"Warnings={warnings}.");
            }
            else
            {
                Debug.LogError(
                    $"[ItemValidation] FAIL | Errors={errors}, " +
                    $"Warnings={warnings}.");
            }
        }

        [MenuItem(RootMenu + "Run State Self-Test", priority = 31)]
        private static void RunStateSelfTest()
        {
            int passed = 0;
            int total = 0;

            NetworkItemLocationState world =
                NetworkItemLocationState.World(1);
            Check(world.IsWorld && !world.IsHeld && !world.IsPlaced,
                "world state", ref passed, ref total);
            Check(world.HolderClientId == NetworkItemLocationState.NoClient,
                "world has no holder", ref passed, ref total);

            NetworkItemLocationState held =
                NetworkItemLocationState.Held(7, 2);
            Check(held.IsHeld && held.HolderClientId == 7,
                "held state", ref passed, ref total);
            Check(held.Revision > world.Revision,
                "revision advances", ref passed, ref total);

            NetworkItemPlacementPlan consume =
                NetworkItemPlacementPlan.Consume();
            Check(
                consume.Disposition ==
                NetworkItemDestinationDisposition.Consume,
                "consume plan",
                ref passed,
                ref total);

            if (passed == total)
            {
                Debug.Log($"[ItemSelfTest] PASS {passed}/{total} validations.");
            }
            else
            {
                Debug.LogError(
                    $"[ItemSelfTest] FAIL {passed}/{total} validations.");
            }
        }

        private static GameObject CreateCarryItem(
            string displayName,
            PrimitiveType primitiveType,
            Vector3 worldPosition,
            Vector3 visualScale)
        {
            GameObject root = CreateRoot(displayName, worldPosition);
            GameObject visual = CreateVisualChild(
                root.transform,
                displayName + " Visual",
                primitiveType,
                Vector3.zero,
                visualScale);

            Undo.AddComponent<NetworkObject>(root);
            NetworkWorldItem item = Undo.AddComponent<NetworkWorldItem>(root);
            NetworkItemPresentation presentation =
                Undo.AddComponent<NetworkItemPresentation>(root);
            Undo.AddComponent<NetworkCarryable>(root);
            Undo.AddComponent<NetworkHeldItemPoseFollower>(root);
            Undo.AddComponent<NetworkItemInteractable>(root);

            item.EditorConfigure(displayName);
            presentation.AutoAssignReferences();
            EditorUtility.SetDirty(item);
            EditorUtility.SetDirty(presentation);
            EditorUtility.SetDirty(visual);
            return root;
        }

        private static void CreateShelf(Vector3 position, Vector3 forward)
        {
            GameObject root = CreateRoot("Test Shelf", position);
            root.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            CreateVisualChild(
                root.transform,
                "Shelf Visual",
                PrimitiveType.Cube,
                new Vector3(0f, 1f, 0f),
                new Vector3(3f, 2f, 0.5f));

            Undo.AddComponent<NetworkObject>(root);
            NetworkItemSlotDestination destination =
                Undo.AddComponent<NetworkItemSlotDestination>(root);
            NetworkItemReceiver receiver =
                Undo.AddComponent<NetworkItemReceiver>(root);

            Transform[] slots = new Transform[3];

            for (int i = 0; i < slots.Length; i++)
            {
                GameObject slot = new GameObject($"Slot_{i + 1}");
                Undo.RegisterCreatedObjectUndo(slot, "Create Item Slot");
                Undo.SetTransformParent(
                    slot.transform,
                    root.transform,
                    "Parent Item Slot");
                slot.transform.localPosition =
                    new Vector3((i - 1) * 0.9f, 2f, 0f);
                slot.transform.localRotation = Quaternion.identity;
                slots[i] = slot.transform;
            }

            destination.EditorConfigure(slots);
            receiver.EditorConfigure("3-Slot Shelf", destination);
            EditorUtility.SetDirty(destination);
            EditorUtility.SetDirty(receiver);
        }

        private static void CreateTable(Vector3 position, Vector3 forward)
        {
            GameObject root = CreateRoot("Test Table", position);
            root.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            CreateVisualChild(
                root.transform,
                "Table Visual",
                PrimitiveType.Cube,
                new Vector3(0f, 0.5f, 0f),
                new Vector3(3f, 1f, 2f));

            Undo.AddComponent<NetworkObject>(root);
            NetworkItemSurfaceDestination destination =
                Undo.AddComponent<NetworkItemSurfaceDestination>(root);
            NetworkItemReceiver receiver =
                Undo.AddComponent<NetworkItemReceiver>(root);
            receiver.EditorConfigure("Placement Table", destination);
            EditorUtility.SetDirty(receiver);
        }

        private static void CreateTrash(Vector3 position, Vector3 forward)
        {
            GameObject root = CreateRoot("Test Trash Bin", position);
            root.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            CreateVisualChild(
                root.transform,
                "Trash Bin Visual",
                PrimitiveType.Cylinder,
                new Vector3(0f, 0.65f, 0f),
                new Vector3(1.2f, 0.65f, 1.2f));

            Undo.AddComponent<NetworkObject>(root);
            NetworkItemConsumeDestination destination =
                Undo.AddComponent<NetworkItemConsumeDestination>(root);
            NetworkItemReceiver receiver =
                Undo.AddComponent<NetworkItemReceiver>(root);
            receiver.EditorConfigure("Trash Bin", destination);
            EditorUtility.SetDirty(receiver);
        }

        private static GameObject CreateRoot(string objectName, Vector3 position)
        {
            GameObject root = new GameObject(objectName);
            Undo.RegisterCreatedObjectUndo(root, "Create " + objectName);
            root.transform.SetPositionAndRotation(position, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            return root;
        }

        private static GameObject CreateVisualChild(
            Transform parent,
            string objectName,
            PrimitiveType primitiveType,
            Vector3 localPosition,
            Vector3 localScale)
        {
            GameObject visual = GameObject.CreatePrimitive(primitiveType);
            Undo.RegisterCreatedObjectUndo(visual, "Create " + objectName);
            visual.name = objectName;
            Undo.SetTransformParent(
                visual.transform,
                parent,
                "Parent " + objectName);
            visual.transform.localPosition = localPosition;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = localScale;
            return visual;
        }

        private static bool TryGetSelectedNetworkRoot(out GameObject root)
        {
            root = null;
            GameObject selected = Selection.activeGameObject;

            if (selected == null || EditorUtility.IsPersistent(selected))
            {
                return false;
            }

            NetworkObject networkObject =
                selected.GetComponentInParent<NetworkObject>();

            if (networkObject == null)
            {
                return false;
            }

            root = networkObject.gameObject;
            return true;
        }

        private static Transform FindChildByName(Transform root, string childName)
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == childName)
                {
                    return children[i];
                }
            }

            return null;
        }

        private static void GetRigBasis(
            out Vector3 origin,
            out Vector3 forward,
            out Vector3 right)
        {
            NetworkInteractionController selectedController =
                Selection.activeGameObject != null
                    ? Selection.activeGameObject.GetComponentInParent<
                        NetworkInteractionController>()
                    : null;

            if (selectedController != null)
            {
                origin = selectedController.transform.position;
                forward = Vector3.ProjectOnPlane(
                    selectedController.transform.forward,
                    Vector3.up).normalized;
            }
            else
            {
                SceneView sceneView = SceneView.lastActiveSceneView;
                origin = sceneView != null ? sceneView.pivot : Vector3.zero;
                forward = Vector3.forward;
            }

            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            right = Vector3.Cross(Vector3.up, forward).normalized;
        }

        private static bool ApproximatelyOne(Vector3 scale)
        {
            return Mathf.Abs(scale.x - 1f) < 0.001f &&
                   Mathf.Abs(scale.y - 1f) < 0.001f &&
                   Mathf.Abs(scale.z - 1f) < 0.001f;
        }

        private static void Check(
            bool condition,
            string label,
            ref int passed,
            ref int total)
        {
            total++;

            if (condition)
            {
                passed++;
            }
            else
            {
                Debug.LogError($"[ItemSelfTest] Failed: {label}.");
            }
        }
    }
}
#endif
