#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using EXW.Multiplayer;
using EXW.UI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Explicit, temporary authoring validation. Never runs merely because the project opens.
[InitializeOnLoad]
internal static class StoreComputerPlayModeValidation
{
    private const string Key = "StoreComputerPlayModeValidation.";
    private const string ReportPath = "ArtReview/StoreComputer_2026-10-17/playmode-validation.txt";
    private const string PrefabPath = "Assets/GameAssets/Prefabs/Computer/CounterComputer.prefab";
    private const string MarkerName = "PCValidation_EmptySceneMarker";
    private const string StartFromFirstSceneKey = "StartFromFirstScene";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly StringBuilder Report = new StringBuilder();
    private static IEnumerator scenario;
    private static NetworkManager manager;
    private static GameObject fixtureUi;
    private static InputActionReference[] fixtureActions;
    private static bool finishing;
    private static bool capturedError;
    private static int checks;
    private static double startedAt;

    static StoreComputerPlayModeValidation()
    {
        EditorApplication.playModeStateChanged += HandlePlayMode;
        EditorApplication.update += Tick;
        if (!EditorApplication.isPlayingOrWillChangePlaymode &&
            SessionState.GetBool(Key + "Restore", false))
            EditorApplication.delayCall += RestoreEditorScene;
    }

    internal static void RecoverEditorState()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before recovering editor state.");
        string previous = SessionState.GetString(Key + "PreviousScene", string.Empty);
        if (string.IsNullOrEmpty(previous) || AssetDatabase.LoadAssetAtPath<SceneAsset>(previous) == null)
            throw new InvalidOperationException("No valid saved validation scene exists in SessionState.");
        SessionState.SetBool(Key + "Running", false);
        SessionState.SetBool(Key + "Restore", true);
        RestoreEditorScene();
        Debug.Log("[Computer Validation] Recovered editor state and scene: " + previous);
    }

    [MenuItem("Tools/Computer/Run Isolated Host Validation")]
    internal static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            SessionState.GetBool(Key + "Running", false))
            throw new InvalidOperationException("Stop the current Play Mode/validation first.");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            throw new InvalidOperationException("Build CounterComputer.prefab before validation.");
        Type gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor") ??
                            typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gameViewType == null)
            throw new InvalidOperationException("The Unity GameView window type is unavailable.");

        Scene previous = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(previous.path))
            throw new InvalidOperationException("Save the current scene before validation.");
        if (previous.isDirty && !EditorSceneManager.SaveScene(previous))
            throw new InvalidOperationException("The current scene could not be saved.");
        SessionState.SetString(Key + "PreviousScene", previous.path);
        SessionState.SetString(Key + "PreviousStartScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Key + "HadStartFromFirstScene", EditorPrefs.HasKey(StartFromFirstSceneKey));
        SessionState.SetBool(Key + "StartFromFirstScene", EditorPrefs.GetBool(StartFromFirstSceneKey, true));
        SessionState.SetBool(Key + "Restore", true);
        SessionState.SetBool(Key + "RunInBackground", Application.runInBackground);
        SessionState.SetBool(Key + "Running", true);
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, "RUNNING: isolated Unity host validation.\n");
        EditorPrefs.SetBool(StartFromFirstSceneKey, false);
        EditorSceneManager.playModeStartScene = null;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject(MarkerName);
        EditorWindow.GetWindow(gameViewType).Focus();
        EditorApplication.isPlaying = true;
    }

    private static void HandlePlayMode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode ||
            !SessionState.GetBool(Key + "Restore", false)) return;
        Application.logMessageReceived -= CaptureLog;
        if (SessionState.GetBool(Key + "Running", false))
            File.AppendAllText(ReportPath, "FAIL: validation was interrupted before completion.\n");
        SessionState.SetBool(Key + "Running", false);
        // Keep SceneEditor disabled through every EnteredEditMode subscriber.
        EditorApplication.delayCall += RestoreEditorScene;
        scenario = null;
        manager = null;
        finishing = false;
    }

    private static void RestoreEditorScene()
    {
        if (!SessionState.GetBool(Key + "Restore", false)) return;
        SessionState.SetBool(Key + "Restore", false);
        Application.runInBackground = SessionState.GetBool(Key + "RunInBackground", false);
        if (SessionState.GetBool(Key + "HadStartFromFirstScene", false))
            EditorPrefs.SetBool(StartFromFirstSceneKey, SessionState.GetBool(Key + "StartFromFirstScene", true));
        else
            EditorPrefs.DeleteKey(StartFromFirstSceneKey);
        string startScene = SessionState.GetString(Key + "PreviousStartScene", string.Empty);
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(startScene)
            ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(startScene);
        string previous = SessionState.GetString(Key + "PreviousScene", string.Empty);
        if (!string.IsNullOrEmpty(previous))
            EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || finishing ||
            !SessionState.GetBool(Key + "Running", false)) return;
        try
        {
            if (scenario == null)
            {
                Report.Clear();
                checks = 0;
                capturedError = false;
                startedAt = EditorApplication.timeSinceStartup;
                Application.logMessageReceived += CaptureLog;
                Report.AppendLine("Unity " + Application.unityVersion + " — isolated actual NGO host/UnityTransport fixture");
                Report.AppendLine("LIMITS: Single host only; no remote-client race/forged sender/disconnect or transport late-join test. Internal target transaction bypasses controller aim/range/LOS.");
                Report.AppendLine("LIMITS: Virtual device events are tested; physical hardware, pixel readback, visual brightness and held-case stack are not reviewed.");
                scenario = Run();
            }
            if (EditorApplication.timeSinceStartup - startedAt > 30d)
                throw new TimeoutException("30 second validation watchdog expired.");
            if (!scenario.MoveNext()) Finish(!capturedError, null);
        }
        catch (Exception exception)
        {
            Finish(false, exception is TargetInvocationException invocation
                ? invocation.InnerException ?? exception : exception);
        }
    }

    private static IEnumerator Run()
    {
        Application.runInBackground = true;
        DescribeScene();
        Scene fixtureScene = SceneManager.GetActiveScene();
        GameObject marker = GameObject.Find(MarkerName);
        Check(string.IsNullOrEmpty(fixtureScene.path) && SceneManager.sceneCount == 1 &&
              marker != null && marker.scene == fixtureScene,
            "Unique isolated unsaved fixture scene is active before runtime cleanup");
        RemoveAutoBootRuntime();
        Check(MultiplayerFlowController.Instance == null && NetworkManager.Singleton == null,
            "Fixture contains no production session/Steam/network runtime");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var runtime = new GameObject("PCValidation_NetworkRuntime");
        runtime.SetActive(false);
        manager = runtime.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig();
        UnityTransport transport = runtime.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", 17879, "127.0.0.1");
        manager.NetworkConfig.NetworkTransport = transport;
        manager.NetworkConfig.EnableSceneManagement = false;
        manager.NetworkConfig.ConnectionApproval = false;
        manager.NetworkConfig.PlayerPrefab = null;
        manager.NetworkConfig.ForceSamePrefabs = false;
        runtime.SetActive(true);
        manager.AddNetworkPrefab(prefab);
        Check(manager.StartHost(), "Actual NGO/UTP host starts");
        while (!manager.IsConnectedClient) yield return null;
        Check(manager.LocalClientId == 0 && manager.IsServer, "Actual host client ID zero is connected");

        InputSystemUIInputModule module = CreateUi();
        InputActionReferenceSnapshot originalActions = new InputActionReferenceSnapshot(module);
        var player = new GameObject("PCValidation_Player", typeof(NetworkObject),
            typeof(NetworkPlayerInputReader), typeof(NetworkPlayerPresenceController),
            typeof(NetworkInteractionController));
        var cameraObject = new GameObject("ValidationCamera", typeof(Camera),
            typeof(AudioListener), typeof(UniversalAdditionalCameraData));
        cameraObject.transform.SetParent(player.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 1.68f, -1.5f);
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.nearClipPlane = 0.13f;
        camera.fieldOfView = 63f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        cameraObject.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
        var controller = player.GetComponent<NetworkInteractionController>();
        controller.AutoAssignReferences();
        NetworkObject playerObject = player.GetComponent<NetworkObject>();
        playerObject.SpawnAsPlayerObject(0, false);
        var presence = player.GetComponent<NetworkPlayerPresenceController>();
        presence.ActivateGameplayServer(Vector3.zero, Quaternion.identity);
        var input = player.GetComponent<NetworkPlayerInputReader>();
        input.SetGameplayEnabled(true);
        Check(input == NetworkPlayerInputReader.Local && !GameplayInputGate.IsBlocked,
            "Real local input reader and gameplay presence are ready");

        var hud = new GameObject("PCValidation_HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup))
            .GetComponent<CanvasGroup>();
        hud.alpha = 0.73f;
        hud.interactable = true;
        hud.blocksRaycasts = true;
        var staging = new GameObject("PCValidation_Staging");
        staging.SetActive(false);
        GameObject pcObject = Object.Instantiate(prefab, staging.transform);
        pcObject.SetActive(false);
        pcObject.transform.SetParent(null, false);
        pcObject.transform.SetPositionAndRotation(new Vector3(0f, 1.7f, 1f), Quaternion.identity);
        var pc = pcObject.GetComponent<NetworkStoreComputer>();
        var local = pcObject.GetComponent<ComputerLocalPresenter>();
        var desktop = pcObject.GetComponent<StoreComputerDesktopPresenter>();
        var serialized = new SerializedObject(local);
        var hudGroups = serialized.FindProperty("hudGroups");
        hudGroups.arraySize = 1;
        hudGroups.GetArrayElementAtIndex(0).objectReferenceValue = hud;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        pcObject.SetActive(true);
        pc.NetworkObject.Spawn();
        Object.Destroy(staging);
        Check(desktop.ValidateSetup() && local.ValidateSetup(), "Generated PC prefab validates in isolated scene");
        Check(!pc.IsInUse && local.State == ComputerLocalUseState.Idle,
            "PC begins idle with no camera transition");
        Vector3 originalPosition = camera.transform.localPosition;
        Quaternion originalRotation = camera.transform.localRotation;
        float originalFov = camera.fieldOfView;
        float originalNear = camera.nearClipPlane;
        CursorLockMode originalLock = Cursor.lockState;
        bool originalCursor = Cursor.visible;
        float timeScale = Time.timeScale;

        Check(TryUse(pc, controller, out _, out _), "Normal server transaction commits host-zero reservation");
        uint firstToken = pc.SessionToken;
        Check(pc.IsLocalUser && pc.UserClientId == 0 && firstToken != 0,
            "Snapshot distinguishes occupied host-zero from empty");
        Check(GameplayInputGate.IsBlocked && !input.CanReadGameplayInput,
            "Accepted local grant blocks gameplay during transition");
        while (local.State != ComputerLocalUseState.Using) yield return null;
        Check(pc.NetworkObject.OwnerClientId == 0, "PC remains server-owned");
        Check(desktop.DesktopTexture != null && desktop.DesktopTexture.IsCreated(),
            "Real RenderTexture is created without readback");
        Check(hud.alpha == 0f && !hud.interactable && !hud.blocksRaycasts,
            "Local HUD is temporarily hidden");
        Transform anchor = pcObject.transform.Find("ScreenViewAnchor");
        Check(anchor != null && Vector3.Distance(camera.transform.position, anchor.position) < 0.01f,
            "Local camera reaches authored screen anchor");
        Check(input.ActionsAsset.FindActionMap("UI", true).enabled && desktop.CanRouteInput,
            "UI map and existing EventSystem remain usable");
        IEnumerator inputScenario = StoreComputerUiInputValidation.Run(pc, desktop, Check);
        try
        {
            while (inputScenario.MoveNext()) yield return null;
        }
        finally
        {
            (inputScenario as IDisposable)?.Dispose();
        }
        Check(!TryUse(pc, controller, out InteractionRejectReason rejection, out string message) &&
              rejection == InteractionRejectReason.Unavailable &&
              message == NetworkStoreComputer.UnavailableMessage && pc.SessionToken == firstToken,
            "Occupied second transaction is rejected without replacing session");
        Check(pc.RequestApp(StoreComputerApp.Upgrades, firstToken), "Authorized app request uses actual generated ServerRpc");
        while (pc.ActiveApp != StoreComputerApp.Upgrades) yield return null;
        CheckPointer(desktop);
        desktop.HandleBack();
        while (pc.ActiveApp != StoreComputerApp.Desktop) yield return null;
        Check(pc.IsInUse, "Back from app returns to desktop and retains reservation");
        pc.RequestApp(StoreComputerApp.Upgrades, firstToken);
        while (pc.ActiveApp != StoreComputerApp.Upgrades) yield return null;
        local.RequestLeaveComputer();
        local.RequestLeaveComputer();
        while (local.State != ComputerLocalUseState.Idle || pc.IsInUse) yield return null;
        Check(pc.ActiveApp == StoreComputerApp.Upgrades, "Leave/spam retains last app in world-screen state");
        Check(Vector3.Distance(camera.transform.localPosition, originalPosition) < 0.001f &&
              Quaternion.Angle(camera.transform.localRotation, originalRotation) < 0.01f &&
              Mathf.Abs(camera.fieldOfView - originalFov) < 0.001f &&
              Mathf.Abs(camera.nearClipPlane - originalNear) < 0.001f,
            "Camera transform, FOV and clipping restore");
        Check(!GameplayInputGate.IsBlocked && input.CanReadGameplayInput &&
              hud.alpha == 0.73f && hud.interactable && hud.blocksRaycasts,
            "Gameplay input and original HUD properties restore");
        Check(Cursor.lockState == originalLock && Cursor.visible == originalCursor,
            "Original cursor state restores");
        Check(originalActions.Matches(module), "Original EventSystem input action references restore");

        Check(TryUse(pc, controller, out _, out _), "Next use receives a fresh reservation");
        while (local.State != ComputerLocalUseState.Using) yield return null;
        uint secondToken = pc.SessionToken;
        Check(secondToken != firstToken, "Session token advances between uses");
        typeof(NetworkStoreComputer).GetMethod("ReleaseServerRpc", Hidden)
            .Invoke(pc, new object[] { firstToken, default(ServerRpcParams) });
        double settle = EditorApplication.timeSinceStartup + 0.15d;
        while (EditorApplication.timeSinceStartup < settle) yield return null;
        Check(pc.IsInUse && pc.SessionToken == secondToken,
            "Actual host RPC with stale token cannot release new session");
        typeof(NetworkStoreComputer).GetMethod("SetAppServerRpc", Hidden)
            .Invoke(pc, new object[] { (StoreComputerApp)255, secondToken, default(ServerRpcParams) });
        yield return null;
        Check(pc.ActiveApp == StoreComputerApp.Upgrades, "Server rejects invalid app enum through actual RPC wrapper");
        presence.DeactivateGameplayServer();
        yield return null;
        Check(!pc.IsInUse && local.State == ComputerLocalUseState.Idle,
            "Presence loss clears reservation and camera transition");
        Check(GameplayInputGate.IsBlocked, "PC cleanup preserves the independent inactive-presence input lock");
        presence.ActivateGameplayServer(Vector3.zero, Quaternion.identity);
        Check(!GameplayInputGate.IsBlocked, "Presence reactivation releases only its own lock");
        Check(TryUse(pc, controller, out _, out _), "Computer is reusable after forced cleanup");
        while (local.State != ComputerLocalUseState.Using) yield return null;
        playerObject.Despawn(false);
        yield return null;
        Check(!pc.IsInUse && local.State == ComputerLocalUseState.Idle,
            "Actual player despawn(false) clears PC and local camera state");
        Check(!GameplayInputGate.IsBlocked && ComputerLocalPresenter.Active == null,
            "Despawn leaves no PC or focus input token");
        Check(pc.ActiveApp == StoreComputerApp.Upgrades && Mathf.Approximately(Time.timeScale, timeScale),
            "Last app persists and global Time.timeScale is untouched");
        pc.NetworkObject.Despawn(false);
        manager.Shutdown(true);
    }

    private static void DescribeScene()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            Report.AppendLine("DEBUG SCENE " + scene.name + " path=" + scene.path);
            foreach (GameObject root in scene.GetRootGameObjects())
                DescribeRoot(root);
        }
        if (NetworkManager.Singleton != null) DescribeRoot(NetworkManager.Singleton.transform.root.gameObject);
        if (MultiplayerFlowController.Instance != null) DescribeRoot(MultiplayerFlowController.Instance.transform.root.gameObject);
        if (SteamBootstrap.Instance != null) DescribeRoot(SteamBootstrap.Instance.transform.root.gameObject);
    }

    private static void DescribeRoot(GameObject root)
    {
        Report.Append("DEBUG ROOT " + root.name + " scene=" + root.scene.name + " components=");
        foreach (Component component in root.GetComponents<Component>())
            Report.Append((component != null ? component.GetType().FullName : "MISSING") + ";");
        Report.AppendLine();
    }

    private static void RemoveAutoBootRuntime()
    {
        NetworkManager existing = NetworkManager.Singleton;
        MultiplayerFlowController flow = MultiplayerFlowController.Instance;
        if (existing != null || flow != null)
        {
            GameObject root = existing != null ? existing.transform.root.gameObject : flow.transform.root.gameObject;
            Check(root.name == "NetworkRuntime" &&
                  (existing == null || existing.gameObject == root) &&
                  (flow == null || flow.gameObject == root),
                "Auto-created production runtime has the exact known NetworkRuntime root identity");
            var shutdown = root.GetComponent<MultiplayerShutdownCoordinator>();
            Check(shutdown != null, "Known runtime provides its normal synchronous shutdown coordinator");
            shutdown.ShutdownImmediately("Isolated PC validation replaces auto-boot runtime");
            Object.DestroyImmediate(root);
            Report.AppendLine("DEBUG Removed only the auto-created NetworkRuntime in the isolated fixture.");
        }
        if (SteamBootstrap.Instance != null)
        {
            SteamBootstrap steam = SteamBootstrap.Instance;
            GameObject root = steam.transform.root.gameObject;
            Check(root.name == "SteamBootstrap" && steam.gameObject == root,
                "Remaining Steam bootstrap has the exact known root identity");
            steam.ShutdownSteamForApplicationExit();
            Object.DestroyImmediate(root);
        }
    }

    private static InputSystemUIInputModule CreateUi()
    {
        var root = new GameObject("PCValidation_ExistingEventSystem");
        fixtureUi = root;
        root.SetActive(false);
        var events = root.AddComponent<EventSystem>();
        var module = root.AddComponent<InputSystemUIInputModule>();
        var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
            "Assets/Source/Multiplayer/Input/PlayerInputActions.inputactions");
        Check(actions != null, "Existing PlayerInputActions asset supplies explicit EventSystem actions");
        module.actionsAsset = actions;
        string[] names = { "Point", "Click", "Navigate", "Submit", "Cancel", "ScrollWheel" };
        fixtureActions = new InputActionReference[names.Length];
        for (int i = 0; i < names.Length; i++)
            fixtureActions[i] = InputActionReference.Create(actions.FindAction("UI/" + names[i], true));
        module.point = fixtureActions[0];
        module.leftClick = fixtureActions[1];
        module.move = fixtureActions[2];
        module.submit = fixtureActions[3];
        module.cancel = fixtureActions[4];
        module.scrollWheel = fixtureActions[5];
        var tracker = root.AddComponent<UIInputModeTracker>();
        var coordinator = root.AddComponent<UIFocusCoordinator>();
        var settings = AssetDatabase.LoadAssetAtPath<UINavigationSettingsSO>(
            "Assets/UINavigation/UINavigationSettings.asset");
        Check(settings != null, "Existing Assets/UINavigation/UINavigationSettings.asset loads");
        SetReference(coordinator, "eventSystem", events);
        SetReference(coordinator, "uiInputModule", module);
        SetReference(coordinator, "inputModeTracker", tracker);
        SetReference(coordinator, "settings", settings);
        var bridge = root.AddComponent<COOPBASEGameplayInputBridge>();
        SetReference(bridge, "coordinator", coordinator);
        root.SetActive(true);
        return module;
    }

    private static void SetReference(Object target, string field, Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(field).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static bool TryUse(NetworkStoreComputer pc, NetworkInteractionController player,
        out InteractionRejectReason rejection, out string message)
    {
        var context = new NetworkInteractionContext(0, player, pc,
            player.transform.position + Vector3.up * 1.68f, Vector3.forward,
            pc.transform.position, Vector3.back, Time.realtimeSinceStartupAsDouble);
        object[] arguments = { context, null, null };
        bool result = (bool)typeof(NetworkInteractable).GetMethod("TryExecuteServer", Hidden)
            .Invoke(pc, arguments);
        rejection = (InteractionRejectReason)arguments[1];
        message = (string)arguments[2];
        return result;
    }

    private static void CheckPointer(StoreComputerDesktopPresenter desktop)
    {
        RawImage image = desktop.FullscreenImage;
        var corners = new Vector3[4];
        image.rectTransform.GetWorldCorners(corners);
        Camera camera = image.canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null : image.canvas.worldCamera;
        Vector2 center = RectTransformUtility.WorldToScreenPoint(camera, (corners[0] + corners[2]) * 0.5f);
        Check(ComputerDesktopRaycaster.TryMapPointer(image, center, desktop.TextureResolution, out Vector2 mapped) &&
              Vector2.Distance(mapped, (Vector2)desktop.TextureResolution * 0.5f) < 1f,
            "Current GameView fullscreen/canvas pointer center maps to texture center");
        foreach (Vector2 size in new[] { new Vector2(1920, 1080), new Vector2(1920, 1200) })
        {
            Rect fit = StoreComputerDesktopPresenter.CalculateFitRect(new Rect(Vector2.zero, size), 16f / 9f);
            Check(Mathf.Abs(fit.width / fit.height - 16f / 9f) < 0.0001f &&
                  fit.width <= size.x && fit.height <= size.y && Vector2.Distance(fit.center, size * 0.5f) < 0.001f,
                "Aspect-fit math preserves 16:9 image within " + size);
        }
    }

    private static void Check(bool condition, string description)
    {
        checks++;
        Report.AppendLine((condition ? "PASS " : "FAIL ") + description);
        if (!condition) throw new InvalidOperationException(description);
    }

    private static void CaptureLog(string message, string trace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        capturedError = true;
        Report.AppendLine("UNITY ERROR " + message + "\n" + trace);
    }

    private static void Finish(bool success, Exception exception)
    {
        finishing = true;
        try
        {
            (scenario as IDisposable)?.Dispose();
        }
        catch (Exception cleanupException)
        {
            success = false;
            Report.AppendLine("FAIL validation iterator cleanup: " + cleanupException);
        }
        scenario = null;
        if (exception != null) Report.AppendLine("FAIL " + exception);
        Report.AppendLine((success ? "PASS" : "FAIL") + " — " + checks + " checks; Unity Play Mode host fixture.");
        Report.AppendLine("NOT RUN: real second peer and competing clients; remote transport disconnect and late join; physical mouse/gamepad hardware; interaction-controller origin/aim/range/line-of-sight checks. Virtual Input System/UI events are reported in the checks above.");
        Application.logMessageReceived -= CaptureLog;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, Report.ToString());
        }
        finally
        {
            SessionState.SetBool(Key + "Running", false);
            try
            {
                if (manager != null && manager.IsListening) manager.Shutdown(true);
            }
            finally
            {
                try
                {
                    if (fixtureUi != null) Object.DestroyImmediate(fixtureUi);
                    if (fixtureActions != null)
                        foreach (InputActionReference action in fixtureActions)
                            if (action != null) Object.DestroyImmediate(action);
                    fixtureUi = null;
                    fixtureActions = null;
                }
                finally
                {
                    EditorApplication.isPlaying = false;
                }
            }
        }
    }

    private readonly struct InputActionReferenceSnapshot
    {
        private readonly Object point, click, move, submit, cancel, scroll;
        public InputActionReferenceSnapshot(InputSystemUIInputModule module)
        {
            point = module.point; click = module.leftClick; move = module.move;
            submit = module.submit; cancel = module.cancel; scroll = module.scrollWheel;
        }
        public bool Matches(InputSystemUIInputModule module)
        {
            return module.point == point && module.leftClick == click && module.move == move &&
                   module.submit == submit && module.cancel == cancel && module.scrollWheel == scroll;
        }
    }
}
#endif
