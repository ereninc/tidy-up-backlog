using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EXW.Multiplayer.Editor
{
    // Explicit, isolated host checks. No production scene objects are changed.
    [InitializeOnLoad]
    internal static class GameCaseAutoShelfDebugValidation
    {
        private const string Key = "AutoShelfValidation.";
        private const string ReportPath = "ArtReview/AutoShelfDebug_2026-10-24/host-checks.txt";
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly StringBuilder Report = new StringBuilder();
        private static IEnumerator _scenario;
        private static NetworkManager _manager;
        private static double _started;
        private static bool _finishing;
        private static bool _errors;

        static GameCaseAutoShelfDebugValidation()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += HandlePlayMode;
            if (!EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetBool(Key + "Restore", false))
                EditorApplication.delayCall += Restore;
        }

        [MenuItem("Tools/Game Cases/Debug/Run Auto Shelf Host Checks")]
        private static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.sceneCount != 1 ||
                SceneManager.GetActiveScene().isDirty || string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
                throw new InvalidOperationException("Save the current single scene and leave Play Mode before isolated checks.");
            SessionState.SetString(Key + "Scene", SceneManager.GetActiveScene().path);
            SessionState.SetString(Key + "StartScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(Key + "HadPref", EditorPrefs.HasKey("StartFromFirstScene"));
            SessionState.SetBool(Key + "Pref", EditorPrefs.GetBool("StartFromFirstScene", true));
            SessionState.SetBool(Key + "Background", Application.runInBackground);
            SessionState.SetBool(Key + "Restore", true);
            SessionState.SetBool(Key + "Run", true);
            EditorPrefs.SetBool("StartFromFirstScene", false);
            EditorSceneManager.playModeStartScene = null;
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, "RUNNING\n");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("AutoShelfValidationMarker");
            var gameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameView != null) EditorWindow.GetWindow(gameView).Focus();
            EditorApplication.isPlaying = true;
        }

        private static void HandlePlayMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key + "Restore", false)) return;
            if (SessionState.GetBool(Key + "Run", false)) File.AppendAllText(ReportPath, "FAIL interrupted\n");
            SessionState.SetBool(Key + "Run", false);
            EditorApplication.delayCall += Restore;
        }

        private static void Restore()
        {
            if (!SessionState.GetBool(Key + "Restore", false)) return;
            SessionState.SetBool(Key + "Restore", false);
            if (SessionState.GetBool(Key + "HadPref", false)) EditorPrefs.SetBool("StartFromFirstScene", SessionState.GetBool(Key + "Pref", true));
            else EditorPrefs.DeleteKey("StartFromFirstScene");
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key + "StartScene", ""));
            Application.runInBackground = SessionState.GetBool(Key + "Background", false);
            EditorSceneManager.OpenScene(SessionState.GetString(Key + "Scene", ""));
            _scenario = null;
            _manager = null;
            _finishing = false;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetBool(Key + "Restore", false))
            {
                if (SessionState.GetBool(Key + "Run", false)) File.AppendAllText(ReportPath, "FAIL interrupted\n");
                SessionState.SetBool(Key + "Run", false);
                Restore();
                return;
            }
            if (!EditorApplication.isPlaying || _finishing || !SessionState.GetBool(Key + "Run", false)) return;
            try
            {
                if (_scenario == null)
                {
                    Report.Clear();
                    _errors = false;
                    _started = EditorApplication.timeSinceStartup;
                    Application.logMessageReceived += CaptureErrors;
                    _scenario = Run();
                }
                if (EditorApplication.timeSinceStartup - _started > 20) throw new TimeoutException("Host checks timed out.");
                if (!_scenario.MoveNext()) Finish(null);
            }
            catch (Exception e) { Finish(e); }
        }

        private static IEnumerator Run()
        {
            Application.runInBackground = true;
            Check(GameObject.Find("AutoShelfValidationMarker") != null && NetworkManager.Singleton == null, "Isolated empty fixture");
            var runtime = new GameObject("AutoShelfHost");
            runtime.SetActive(false);
            _manager = runtime.AddComponent<NetworkManager>();
            var transport = runtime.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", 18124);
            _manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false, ForceSamePrefabs = false };
            runtime.SetActive(true);
            Check(_manager.StartHost(), "Actual NGO/UTP host starts");
            while (!_manager.IsConnectedClient) yield return null;

            var walletObject = new GameObject("Wallet", typeof(NetworkObject), typeof(NetworkSharedWallet));
            walletObject.GetComponent<NetworkObject>().Spawn();
            var wallet = walletObject.GetComponent<NetworkSharedWallet>();
            var roots = new GameObject("TargetShelves").transform;
            var free = Slot("FirstFree", roots, 0);
            var typed = Slot("ExistingTyped", roots, 3);
            var rowEmpty = Slot("RowEmpty", roots, 6);
            var rowTyped = Slot("RowTyped", roots, 9);
            var rowObject = new GameObject("RowState");
            rowObject.SetActive(false);
            var row = rowObject.AddComponent<NetworkGameCaseShelfRow>();
            SetArray(row, "slots", new Object[] { rowEmpty, rowTyped });
            rowObject.SetActive(true);
            row.GetComponent<NetworkObject>().Spawn();
            var seed1 = Case(100);
            var seed2 = Case(100);
            Check(NetworkItemTransferService.TryPlaceWorldGameCaseServer(seed1, typed.GetComponent<NetworkItemReceiver>(), out _), "World entry uses normal receiver plan/commit");
            Check(NetworkItemTransferService.TryPlaceWorldGameCaseServer(seed2, rowTyped.GetComponent<NetworkItemReceiver>(), out _), "Seed locks row through existing state hook");
            Check(row.LockedAppId == 100 && rowEmpty.SlotState.LockedAppId == 0, "Empty row slot inherits row restriction");
            var a = new[] { Case(100), Case(100), Case(100), Case(100) };
            var registry = new GameObject("Registry").AddComponent<GameCaseInstanceRegistry>();
            double wait = EditorApplication.timeSinceStartup + .3;
            while (EditorApplication.timeSinceStartup < wait) yield return null;
            registry.Rebuild();
            var toolObject = new GameObject("AutoTool");
            toolObject.SetActive(false);
            var tool = toolObject.AddComponent<GameCaseAutoShelfDebugTool>();
            var group = toolObject.GetComponent<GameCaseShelfCompletionGroup>();
            // Dynamically spawned NOs can leave their non-network staging parent.
            SetArray(group, "shelfRoots", new Object[] { free.transform, typed.transform, rowEmpty.transform, rowTyped.transform });
            SetNumber(tool, "batchSize", 1);
            SetNumber(tool, "placementInterval", 0f);
            toolObject.SetActive(true);
            tool.GetComponent<NetworkObject>().Spawn();
            Check(group.Slots.Count == 4, "Inspector target order contains all four spawned slots");
            int completed = 0;
            group.Completed += () => { completed++; Check(wallet.Balance == 400, "Aggregate fires after the last existing wallet reward"); };

            for (int i = 0; i < 4; i++)
            {
                tool.PlaceNextBatch();
                while (tool.IsBusy) yield return null;
                if (i == 0) Check(typed.OccupiedCount == 2 && free.OccupiedCount == 0 && rowEmpty.OccupiedCount == 0, "Typed slot wins over earlier free/row-empty slots");
                if (i == 1) Check(rowTyped.OccupiedCount == 2 && rowEmpty.OccupiedCount == 0, "All typed capacity is used before row-empty capacity");
                if (i >= 2) Check(rowEmpty.OccupiedCount == i - 1 && free.OccupiedCount == 0, "Same-game row-empty wins over free row");
                Check(Field<int>(tool, "_examined") == 1, "Batch budget counts candidates");
            }
            Check(!group.IsComplete && wallet.Balance == 300, "A remaining empty slot prevents aggregate completion");
            var b1 = Case(200);
            var b2 = Case(200);
            Check(!rowEmpty.TryGetPreviewWorldPose(b1.GetComponent<NetworkGameCase>(), out _, out _), "Different game is rejected by row rules");
            registry.Rebuild();
            SetNumber(tool, "batchSize", 2);
            SetNumber(tool, "placementInterval", 1f);
            tool.PlaceNextBatch();
            var routine = Field<Coroutine>(tool, "_routine");
            tool.PlaceNextBatch();
            Check(tool.IsBusy && routine == Field<Coroutine>(tool, "_routine"), "Button spam does not start a parallel coroutine");
            while (Field<int>(tool, "_placed") < 1) yield return null;
            tool.CancelCurrentBatch();
            Check(!tool.IsBusy && free.OccupiedCount == 1 && !group.IsComplete, "Cancel preserves committed placement and clears busy");
            Check((b1.IsPlaced ? b1 : b2).GetComponent<NetworkItemMotionPresenter>().IsAnimating, "Existing item motion remains active after commit/cancel");
            wait = EditorApplication.timeSinceStartup + .3;
            while (EditorApplication.timeSinceStartup < wait) yield return null;
            Check(!b1.GetComponent<NetworkItemMotionPresenter>().IsAnimating && !b2.GetComponent<NetworkItemMotionPresenter>().IsAnimating, "Committed motion finishes after cancellation");
            SetNumber(tool, "placementInterval", 0f);
            tool.PlaceNextBatch();
            while (tool.IsBusy) yield return null;
            Check(group.IsComplete && completed == 1 && wallet.Balance == 400, "Every target slot complete: one aggregate event and normal rewards");
            group.RebuildTargetsServer();
            Check(completed == 1 && wallet.Balance == 400, "Rechecking already-complete state does not replay event/rewards");
            Check(!NetworkItemTransferService.CanPlaceWorldGameCaseServer(seed1), "Already placed/completed case is not a world candidate");
            typed.GetWorldPose(seed1.Location.SlotIndex, out var expected, out _);
            Check(seed1.transform.parent == typed.transform && Vector3.Distance(seed1.transform.position, expected) < .001f, "Authoritative parent and existing layout pose agree");
            Case(300); Case(300);
            registry.Rebuild();
            SetNumber(tool, "batchSize", 1);
            tool.PlaceNextBatch();
            while (tool.IsBusy) yield return null;
            int cursor = tool.NextCaseIndex;
            Check(Field<int>(tool, "_examined") == 1 && Field<int>(tool, "_noSlot") == 1 && Field<int>(tool, "_placed") == 0, "Unplaceable candidates consume budget and remain world items");
            tool.PlaceNextBatch();
            while (tool.IsBusy) yield return null;
            Check(tool.NextCaseIndex > cursor, "Next batch advances beyond unplaceable first case");
            while (tool.NextCaseIndex < registry.Cases.Count)
            {
                tool.PlaceNextBatch();
                while (tool.IsBusy) yield return null;
            }
            tool.PlaceNextBatch();
            while (tool.IsBusy) yield return null;
            Check(Field<int>(tool, "_examined") == 1 && Field<int>(tool, "_noSlot") == 1, "Next click after end starts a new round");
            tool.PlaceNextBatch(); tool.enabled = false;
            Check(!tool.IsBusy, "Disable cancels busy state");
            tool.enabled = true; tool.PlaceNextBatch(); tool.GetComponent<NetworkObject>().Despawn(false);
            Check(!tool.IsBusy, "Despawn cancels busy state");
            foreach (NetworkGameCaseShelfDestination.ShelfLayout layout in Enum.GetValues(typeof(NetworkGameCaseShelfDestination.ShelfLayout)))
            {
                var layoutSlot = Slot("Layout " + layout, roots, 15);
                SetNumber(layoutSlot, "layout", (int)layout);
                var first = Case(400);
                var second = Case(400);
                Check(NetworkItemTransferService.TryPlaceWorldGameCaseServer(first, layoutSlot.GetComponent<NetworkItemReceiver>(), out _) &&
                    NetworkItemTransferService.TryPlaceWorldGameCaseServer(second, layoutSlot.GetComponent<NetworkItemReceiver>(), out _), "Capacity and world transfer: " + layout);
                layoutSlot.GetWorldPose(1, out var position, out var rotation);
                Check(Vector3.Distance(second.transform.position, position) < .001f && Quaternion.Angle(second.transform.rotation, rotation) < .01f,
                    "Uses existing authored layout pose: " + layout);
            }
            var playerObject = new GameObject("Normal placement player");
            playerObject.SetActive(false);
            new GameObject("ItemCarryAnchor").transform.SetParent(playerObject.transform, false);
            var carrier = playerObject.AddComponent<NetworkItemCarrier>();
            playerObject.SetActive(true);
            playerObject.GetComponent<NetworkObject>().SpawnAsPlayerObject(0);
            var sharedSlot = Slot("Held and world share one slot", roots, 18);
            var held = Case(500);
            Check(NetworkItemTransferService.TryPickup(carrier, held, out _) && held.IsHeld,
                "Normal pickup still works with a real spawned host carrier");
            Check(!NetworkItemTransferService.CanPlaceWorldGameCaseServer(held), "Held case is excluded from debug candidates");
            Check(NetworkItemTransferService.TryReceive(carrier, sharedSlot.GetComponent<NetworkItemReceiver>(), default, out _) && !carrier.HasHeldItem,
                "Normal held placement still uses the shared commit");
            var wrong = Case(600);
            Check(!NetworkItemTransferService.TryPlaceWorldGameCaseServer(wrong, sharedSlot.GetComponent<NetworkItemReceiver>(), out _) && wrong.Location.IsWorld,
                "World entry respects a lock established by normal held placement");
            Check(NetworkItemTransferService.TryPlaceWorldGameCaseServer(Case(500), sharedSlot.GetComponent<NetworkItemReceiver>(), out _) && sharedSlot.SlotState.IsComplete,
                "World and held transactions share capacity/occupancy/completion");
            var zero = new GameObject("ZeroTargets");
            zero.SetActive(false);
            var empty = zero.AddComponent<GameCaseShelfCompletionGroup>();
            SetArray(empty, "shelfRoots", new Object[] { new GameObject("EmptyScope").transform });
            zero.SetActive(true); zero.GetComponent<NetworkObject>().Spawn();
            Check(!empty.IsComplete, "Zero valid slots is not complete");
            tool.GetComponent<NetworkObject>().Spawn();
            Check(completed == 1, "Respawning group does not replay its completed celebration");
            tool.PlaceNextBatch();
            _manager.Shutdown(true);
            while (_manager.IsListening) yield return null;
            Check(tool == null || !tool.IsBusy, "Server shutdown clears the batch");
        }

        private static NetworkGameCaseShelfDestination Slot(string name, Transform root, float x)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(root);
            go.transform.position = new Vector3(x, 0, 2);
            var slot = go.AddComponent<NetworkGameCaseShelfDestination>();
            SetNumber(slot, "capacity", 2);
            go.SetActive(true);
            go.GetComponent<NetworkObject>().Spawn();
            return slot;
        }

        private static NetworkWorldItem Case(uint appId)
        {
            var go = new GameObject("Case " + appId);
            go.SetActive(false);
            go.AddComponent<NetworkWorldItem>();
            go.transform.position = new Vector3(0, 1, -2);
            new GameObject("VisualRoot").transform.SetParent(go.transform, false);
            go.AddComponent<NetworkItemMotionPresenter>();
            go.AddComponent<NetworkCarryable>();
            var gameCase = go.AddComponent<NetworkGameCase>();
            // Binding data only: no cover downloading or renderer setup in this logic fixture.
            typeof(NetworkGameCase).GetField("<AppId>k__BackingField", Hidden).SetValue(gameCase, appId);
            go.SetActive(true);
            go.GetComponent<NetworkObject>().Spawn();
            return go.GetComponent<NetworkWorldItem>();
        }

        private static T Field<T>(object target, string field) => (T)target.GetType().GetField(field, Hidden).GetValue(target);
        private static void SetNumber(Object target, string field, object value)
        {
            var so = new SerializedObject(target); var property = so.FindProperty(field);
            if (value is int integer) property.intValue = integer; else property.floatValue = (float)value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target); var property = so.FindProperty(field); property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void Check(bool condition, string description)
        {
            Report.AppendLine((condition ? "PASS " : "FAIL ") + description);
            if (!condition) throw new InvalidOperationException(description);
        }
        private static void CaptureErrors(string message, string trace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            _errors = true; Report.AppendLine("UNITY ERROR " + message);
        }
        private static void Finish(Exception exception)
        {
            _finishing = true;
            if (exception != null) Report.AppendLine("FAIL " + exception);
            Report.AppendLine(exception == null && !_errors ? "PASS host checks" : "FAIL host checks");
            Report.AppendLine("Not run: a second client, real simultaneous network races/late join or rendered mesh appearance. All four layout modes and normal held/world interleaving use real services with empty visual transforms and injected bound AppIds; interaction range/LOS is not exercised.");
            File.WriteAllText(ReportPath, Report.ToString());
            Application.logMessageReceived -= CaptureErrors;
            SessionState.SetBool(Key + "Run", false);
            try { if (_manager != null && _manager.IsListening) _manager.Shutdown(true); }
            finally { EditorApplication.isPlaying = false; }
        }
    }
}
