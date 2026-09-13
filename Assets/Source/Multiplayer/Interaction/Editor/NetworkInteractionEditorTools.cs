#if UNITY_EDITOR
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace EXW.Multiplayer.Editor
{
    public static class NetworkInteractionEditorTools
    {
        private const string RootMenu =
            "Tools/EXW/Multiplayer/Interaction/";

        [MenuItem(RootMenu + "Configure Selected Player", priority = 10)]
        private static void ConfigureSelectedPlayer()
        {
            if (!TryGetSelectedPlayerRoot(out GameObject playerRoot))
            {
                EditorUtility.DisplayDialog(
                    "Configure Interaction Player",
                    "Select the NetworkObject root of the player prefab. If this " +
                    "is a prefab asset, open it in Prefab Mode first.",
                    "OK");
                return;
            }

            Undo.SetCurrentGroupName("Configure Network Interaction Player");
            int undoGroup = Undo.GetCurrentGroup();

            NetworkInteractionController controller =
                playerRoot.GetComponent<NetworkInteractionController>();

            if (controller == null)
            {
                controller =
                    Undo.AddComponent<NetworkInteractionController>(playerRoot);
            }

            Undo.RecordObject(controller, "Assign Interaction References");
            controller.AutoAssignReferences();

            NetworkInteractionDebugHud hud =
                playerRoot.GetComponent<NetworkInteractionDebugHud>();

            if (hud == null)
            {
                hud = Undo.AddComponent<NetworkInteractionDebugHud>(playerRoot);
            }

            Undo.RecordObject(hud, "Assign Interaction HUD References");
            hud.AutoAssignReferences();

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(hud);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
            PrefabUtility.RecordPrefabInstancePropertyModifications(hud);
            Undo.CollapseUndoOperations(undoGroup);

            Selection.activeGameObject = playerRoot;

            Debug.Log(
                "[InteractionSetup] Player configured. Verify View Camera, " +
                "Interaction Origin and Server Validation Origin in the inspector.",
                playerRoot);
        }

        [MenuItem(RootMenu + "Configure Selected Player", true)]
        private static bool CanConfigureSelectedPlayer()
        {
            return Selection.activeGameObject != null;
        }

        [MenuItem(RootMenu + "Create Scene Test Toggle", priority = 20)]
        private static void CreateSceneTestToggle()
        {
            GameObject testObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(
                testObject,
                "Create Network Interaction Test Toggle");

            testObject.name = "NetworkInteractionTestToggle";
            testObject.transform.position = GetTestObjectPosition();
            testObject.transform.localScale = Vector3.one;

            Undo.AddComponent<NetworkObject>(testObject);
            Undo.AddComponent<NetworkToggleInteractable>(testObject);

            Selection.activeGameObject = testObject;
            EditorGUIUtility.PingObject(testObject);

            Debug.Log(
                "[InteractionSetup] Scene test toggle created. Save the scene " +
                "before starting host/client.",
                testObject);
        }

        [MenuItem(RootMenu + "Run Validation Self-Test", priority = 30)]
        private static void RunValidationSelfTest()
        {
            int passed = 0;
            int total = 0;

            Check(
                NetworkInteractionValidation.IsFinite(Vector3.one),
                "finite vector accepted",
                ref passed,
                ref total);
            Check(
                !NetworkInteractionValidation.IsFinite(
                    new Vector3(float.NaN, 0f, 0f)),
                "NaN vector rejected",
                ref passed,
                ref total);
            Check(
                NetworkInteractionValidation.IsOriginWithinTolerance(
                    Vector3.zero,
                    new Vector3(1f, 0f, 0f),
                    1f),
                "origin boundary accepted",
                ref passed,
                ref total);
            Check(
                !NetworkInteractionValidation.IsOriginWithinTolerance(
                    Vector3.zero,
                    new Vector3(1.01f, 0f, 0f),
                    1f),
                "origin overflow rejected",
                ref passed,
                ref total);
            Check(
                NetworkInteractionValidation.IsAimWithinTolerance(
                    Vector3.forward,
                    Vector3.forward,
                    10f),
                "forward aim accepted",
                ref passed,
                ref total);
            Check(
                !NetworkInteractionValidation.IsAimWithinTolerance(
                    Vector3.forward,
                    Vector3.back,
                    100f),
                "backward aim rejected",
                ref passed,
                ref total);
            Check(
                NetworkInteractionValidation.IsWithinDistance(
                    Vector3.zero,
                    Vector3.forward * 3.2f,
                    3f,
                    0.2f),
                "distance tolerance accepted",
                ref passed,
                ref total);

            if (passed == total)
            {
                Debug.Log(
                    $"[InteractionSelfTest] PASS {passed}/{total} validations.");
            }
            else
            {
                Debug.LogError(
                    $"[InteractionSelfTest] FAIL {passed}/{total} validations.");
            }
        }

        private static bool TryGetSelectedPlayerRoot(out GameObject playerRoot)
        {
            playerRoot = null;
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

            playerRoot = networkObject.gameObject;
            return true;
        }

        private static Vector3 GetTestObjectPosition()
        {
            NetworkInteractionController selectedController =
                Selection.activeGameObject != null
                    ? Selection.activeGameObject.GetComponentInParent<
                        NetworkInteractionController>()
                    : null;

            if (selectedController != null)
            {
                Vector3 playerPosition =
                    selectedController.transform.position;
                return playerPosition +
                       selectedController.transform.forward * 2.25f +
                       Vector3.up * 0.5f;
            }

            SceneView sceneView = SceneView.lastActiveSceneView;

            if (sceneView != null)
            {
                return sceneView.pivot;
            }

            return new Vector3(0f, 0.5f, 2.25f);
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
                return;
            }

            Debug.LogError($"[InteractionSelfTest] Failed: {label}.");
        }
    }
}
#endif
