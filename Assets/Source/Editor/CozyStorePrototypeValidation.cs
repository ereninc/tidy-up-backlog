using System;
using System.Collections.Generic;
using System.Reflection;
using EXW.Multiplayer;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

internal static class CozyStorePrototypeValidation
{
    private const string PrefabFolder =
        "Assets/__PlaceholderAssetPacks/Shelf and Slots/CozyPrototype/";

    public static string Validate(GameObject[] shelves, bool checkHashes)
    {
        Require(shelves != null && shelves.Length == 12, "Expected 12 shelf instances.");
        var allSlots = new HashSet<NetworkGameCaseShelfDestination>();
        var groupedSlots = new HashSet<NetworkGameCaseShelfDestination>();
        int rowCount = 0;
        int poseCount = 0;
        float maximumSupportError = 0f;
        MethodInfo buildPose = typeof(NetworkGameCaseShelfDestination).GetMethod(
            "BuildWorldPose", BindingFlags.Instance | BindingFlags.NonPublic);
        Require(buildPose != null, "Shelf destination BuildWorldPose API was not found.");
        var poseArguments = new object[] { 0, Vector3.zero, Quaternion.identity };
        Physics.SyncTransforms();

        foreach (GameObject shelf in shelves)
        {
            Require(shelf != null, "A shelf reference is missing.");
            Require((shelf.transform.localScale - Vector3.one).sqrMagnitude < 0.000001f &&
                    (shelf.transform.lossyScale - Vector3.one).sqrMagnitude < 0.000001f,
                shelf.name + " must have unit local and world scale.");
            Require(shelf.GetComponent<NetworkObject>() == null,
                shelf.name + " must have a non-networked prefab root.");
            string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(shelf);
            Require(PrefabUtility.GetNearestPrefabInstanceRoot(shelf) == shelf &&
                    assetPath.StartsWith(PrefabFolder, StringComparison.Ordinal) &&
                    AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) != null,
                shelf.name + " is not linked to a CozyPrototype shelf prefab.");

            var slots = shelf.GetComponentsInChildren<NetworkGameCaseShelfDestination>(true);
            var rows = shelf.GetComponentsInChildren<NetworkGameCaseShelfRow>(true);
            var labels = shelf.GetComponentsInChildren<GameCaseShelfRowInfoPresenter>(true);
            var shelfSlots = new HashSet<NetworkGameCaseShelfDestination>(slots);
            var shelfRows = new HashSet<NetworkGameCaseShelfRow>(rows);
            var labeledRows = new HashSet<NetworkGameCaseShelfRow>();
            var supportMeshes = new List<MeshCollider>();

            foreach (MeshCollider collider in shelf.GetComponentsInChildren<MeshCollider>(true))
            {
                if (collider.enabled && collider.gameObject.activeInHierarchy &&
                    !collider.isTrigger && collider.sharedMesh != null &&
                    collider.GetComponentInParent<NetworkGameCaseShelfDestination>() == null &&
                    collider.GetComponentInParent<NetworkGameCaseShelfRow>() == null)
                    supportMeshes.Add(collider);
            }
            Require(supportMeshes.Count > 0, shelf.name + " has no visual mesh support colliders.");

            foreach (NetworkGameCaseShelfDestination slot in slots)
            {
                Require(allSlots.Add(slot), slot.name + " was encountered more than once.");
                ValidateNetworkRoot(slot.gameObject);
                var receiver = slot.GetComponent<NetworkItemReceiver>();
                Require(receiver != null && receiver.Destination == slot &&
                        slot.SlotState == slot.GetComponent<NetworkGameCaseShelfSlotState>(),
                    shelf.name + "/" + slot.name + " has incorrect receiver/state references.");
                Require(slot.SlotState != null && slot.Capacity == 10,
                    shelf.name + "/" + slot.name + " must have state and capacity 10.");
                var selection = receiver.EditorInteractionColliders;
                Require(selection != null && selection.Length == 1 &&
                        selection[0] is BoxCollider && selection[0].isTrigger &&
                        selection[0].enabled &&
                        selection[0].GetComponentInParent<NetworkItemReceiver>() == receiver,
                    shelf.name + "/" + slot.name + " needs one explicit trigger selection collider.");

                for (int index = 0; index < slot.Capacity; index++)
                {
                    poseArguments[0] = index;
                    buildPose.Invoke(slot, poseArguments);
                    var position = (Vector3)poseArguments[1];
                    var rotation = (Quaternion)poseArguments[2];
                    Require(Vector3.Dot(rotation * Vector3.forward, Vector3.up) > 0.999f &&
                            Vector3.Dot(rotation * Vector3.up, -slot.StartPoint.forward) > 0.999f,
                        shelf.name + "/" + slot.name + " has an incorrect upright case pose.");

                    for (int x = -1; x <= 1; x += 2)
                    for (int thickness = -1; thickness <= 1; thickness += 2)
                    {
                        Vector3 visualCorner = position + rotation *
                            new Vector3(x * 0.24f, thickness * 0.024f, -0.32f);
                        float expectedDeckY = visualCorner.y - 0.005f;
                        var ray = new Ray(
                            new Vector3(visualCorner.x, expectedDeckY + 0.03f, visualCorner.z),
                            Vector3.down);
                        bool supported = false;
                        float closestError = float.PositiveInfinity;
                        foreach (MeshCollider collider in supportMeshes)
                        {
                            if (!collider.Raycast(ray, out RaycastHit hit, 0.06f) ||
                                Vector3.Dot(hit.normal, Vector3.up) < 0.8f)
                                continue;
                            float error = Mathf.Abs(hit.point.y - expectedDeckY);
                            closestError = Mathf.Min(closestError, error);
                            if (error <= 0.003f) supported = true;
                        }
                        Require(supported,
                            $"{shelf.name}/{slot.name} case {index} corner ({x},{thickness}) " +
                            $"has no deck support at {visualCorner:F4}; closest error={closestError:F5} m.");
                        maximumSupportError = Mathf.Max(maximumSupportError, closestError);
                    }
                    poseCount++;
                }
            }

            foreach (NetworkGameCaseShelfRow row in rows)
            {
                ValidateNetworkRoot(row.gameObject);
                var references = new SerializedObject(row).FindProperty("slots");
                Require(references != null && references.arraySize == 5,
                    shelf.name + "/" + row.name + " must reference exactly five slots.");
                for (int index = 0; index < references.arraySize; index++)
                {
                    var slot = references.GetArrayElementAtIndex(index).objectReferenceValue
                        as NetworkGameCaseShelfDestination;
                    Require(slot != null && shelfSlots.Contains(slot) && groupedSlots.Add(slot),
                        shelf.name + "/" + row.name + " has missing, foreign, or duplicate slot membership.");
                }
                rowCount++;
            }

            foreach (GameCaseShelfRowInfoPresenter label in labels)
            {
                var serialized = new SerializedObject(label);
                var row = serialized.FindProperty("row").objectReferenceValue
                    as NetworkGameCaseShelfRow;
                var text = serialized.FindProperty("gameNameText").objectReferenceValue as TMP_Text;
                Require(row != null && shelfRows.Contains(row) && labeledRows.Add(row) &&
                        text != null && text.transform.IsChildOf(label.transform),
                    shelf.name + "/" + label.name + " has incorrect or duplicate row label wiring.");
            }
            Require(labeledRows.Count == rows.Length,
                shelf.name + " must have one wired label for every row group.");
        }

        Require(allSlots.Count == 445 && rowCount == 89 && poseCount == 4450 &&
                groupedSlots.SetEquals(allSlots),
            $"Unexpected totals: {allSlots.Count} slots, {rowCount} groups, {poseCount} case poses.");

        int hashCount = 0;
        if (checkHashes)
        {
            var hashes = new Dictionary<uint, NetworkObject>();
            foreach (NetworkObject networkObject in UnityEngine.Object.FindObjectsByType<NetworkObject>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var hashProperty = new SerializedObject(networkObject).FindProperty("GlobalObjectIdHash");
                Require(hashProperty != null, networkObject.name + " has no serialized NGO hash.");
                uint hash = (uint)hashProperty.longValue;
                Require(hash != 0, networkObject.name + " has a zero NGO GlobalObjectIdHash.");
                Require(!hashes.TryGetValue(hash, out NetworkObject existing),
                    $"Duplicate NGO hash {hash}: {existing?.name} and {networkObject.name}.");
                hashes.Add(hash, networkObject);
            }
            hashCount = hashes.Count;
        }

        return $"PASS: {shelves.Length} linked shelf prefabs, {allSlots.Count} slots, " +
               $"{rowCount} labeled groups, {poseCount} upright case poses, " +
               $"{poseCount * 4} supported bottom corners " +
               $"(max deck error {maximumSupportError:F6} m); " +
               (checkHashes ? $"{hashCount} unique nonzero scene NGO hashes." : "NGO hash check deferred.");
    }

    private static void ValidateNetworkRoot(GameObject target)
    {
        var networkObject = target.GetComponent<NetworkObject>();
        Require(networkObject != null, target.name + " has no NetworkObject.");
        Require(target.transform.parent == null ||
                target.transform.parent.GetComponentInParent<NetworkObject>() == null,
            target.name + " is nested under another NetworkObject.");
        NetworkObject[] descendants = target.GetComponentsInChildren<NetworkObject>(true);
        Require(descendants.Length == 1 && descendants[0] == networkObject,
            target.name + " contains a nested NetworkObject.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[CozyStorePrototype] " + message);
    }
}
