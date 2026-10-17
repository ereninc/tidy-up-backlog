using System;
using System.IO;
using System.Linq;
using System.Text;
using EXW.Multiplayer;
using EXW.UI.Navigation;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Isolated, one-time authoring utility. It never runs without a menu/request command.
internal static class StoreComputerEditorSetup
{
    private const string Folder = "Assets/GameAssets/Prefabs/Computer";
    private const string ReviewFolder = "ArtReview/StoreComputer_2026-10-17";
    private const string Result = "ArtReview/CozyStorePrototype_2026-10-16/result.txt";
    private const string ScreenName = "CST01_FBX_CST01_Till_Screen";
    private const string MonitorName = "CST01_FBX_CST01_Till_Monitor";
    private static readonly Color Ink = Hex("294D52");
    private static readonly Color Teal = Hex("568C91");
    private static readonly Color Cream = Hex("F3EBD9");
    private static readonly Color Pale = Hex("E1EBE5");
    private static readonly Color Mustard = Hex("C9A858");
    private static Sprite rounded;

    internal static void Inspect()
    {
        Directory.CreateDirectory(ReviewFolder);
        var report = new StringBuilder();
        foreach (var scene in Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt))
            report.AppendLine($"SCENE {scene.path} loaded={scene.isLoaded} dirty={scene.isDirty}");
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!t.name.Contains("Till_") && !t.name.Contains("Checkout_Counter")) continue;
            var renderer = t.GetComponent<Renderer>();
            report.AppendLine($"OBJECT {Path(t)} pos={t.position:F5} rot={t.eulerAngles:F3} scale={t.lossyScale:F5} active={t.gameObject.activeInHierarchy}");
            report.AppendLine($"AXES right={t.right:F5} up={t.up:F5} forward={t.forward:F5}");
            if (renderer != null)
            {
                report.AppendLine($"BOUNDS {renderer.bounds} materials={string.Join(",", renderer.sharedMaterials.Select(m => m ? m.name + ":" + m.shader.name : "NULL"))}");
                var filter = t.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    report.AppendLine($"MESH {AssetDatabase.GetAssetPath(filter.sharedMesh)} localBounds={filter.sharedMesh.bounds} readable={filter.sharedMesh.isReadable}");
                    if (filter.sharedMesh.isReadable)
                        foreach (Vector3 vertex in filter.sharedMesh.vertices.Take(12))
                            report.AppendLine($"VERTEX {vertex:F6} world={t.TransformPoint(vertex):F5}");
                }
            }
            foreach (Collider collider in t.GetComponents<Collider>())
                report.AppendLine($"COLLIDER {collider.GetType().Name} trigger={collider.isTrigger} bounds={collider.bounds}");
            report.AppendLine($"NETWORK_ANCESTOR {t.GetComponentInParent<NetworkObject>()}");
        }
        foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var module = es.GetComponent<InputSystemUIInputModule>();
            report.AppendLine($"EVENTSYSTEM {Path(es.transform)} active={es.gameObject.activeInHierarchy} module={module} actions={module?.actionsAsset}");
        }
        foreach (var focus in Object.FindObjectsByType<UIFocusCoordinator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            report.AppendLine($"FOCUS {Path(focus.transform)}");
        foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            report.AppendLine($"CANVAS {Path(canvas.transform)} mode={canvas.renderMode} enabled={canvas.enabled} group={canvas.GetComponent<CanvasGroup>()}");
        var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameAssets/Prefabs/Network/Player.prefab");
        if (player != null)
            foreach (var camera in player.GetComponentsInChildren<Camera>(true))
                report.AppendLine($"PLAYER_CAMERA {Path(camera.transform)} FOV={camera.fieldOfView} near={camera.nearClipPlane} PP={camera.GetComponent<UniversalAdditionalCameraData>()?.renderPostProcessing}");
        Write(report.ToString());
    }

    [MenuItem("Tools/Computer/Build Counter PC")]
    internal static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Leave Play Mode before authoring the computer.");
        var original = FindSceneTransform(ScreenName);
        var originalRenderer = original.GetComponent<Renderer>();
        var monitor = FindSceneTransform(MonitorName).GetComponent<Renderer>();
        Scene scene = original.gameObject.scene;
        if (Object.FindObjectsByType<NetworkStoreComputer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length != 0)
            throw new InvalidOperationException("A computer already exists. Validate or edit its prefab instead of repeating setup.");
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the gameplay scene before PC setup.");
        Directory.CreateDirectory(ReviewFolder);
        string backup = ReviewFolder + "/03_GameplayScene.before-computer.unity";
        if (!File.Exists(backup)) File.Copy(scene.path, backup);
        EnsureFolder(Folder);
        CreateRoundedSprite();
        var profile = AssetDatabase.LoadAssetAtPath<UINavigationPanelProfileSO>(Folder + "/ComputerFocus.asset");
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<UINavigationPanelProfileSO>();
            AssetDatabase.CreateAsset(profile, Folder + "/ComputerFocus.asset");
        }
        Set(profile, "focusOnEnable", false);
        Set(profile, "waitForReadySignal", true);
        Set(profile, "rememberLastSelection", false);
        Set(profile, "blocksGameplay", true);
        Set(profile, "backMode", (int)UIPanelBackMode.InvokeEvent);

        var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/ComputerScreen.mat");
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("The URP Unlit shader could not be found.");
            material = new Material(shader) { name = "ComputerScreen" };
            AssetDatabase.CreateAsset(material, Folder + "/ComputerScreen.mat");
        }
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Cull", 2f);
        EditorUtility.SetDirty(material);

        var root = new GameObject("CounterComputer");
        root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, scene);
        root.AddComponent<NetworkObject>();
        var computer = root.AddComponent<NetworkStoreComputer>();
        var desktop = root.AddComponent<StoreComputerDesktopPresenter>();
        var local = root.AddComponent<ComputerLocalPresenter>();
        Set(computer, "interactionDisplayName", "Computer");
        Set(computer, "interactionPrompt", "Use Computer");
        Set(computer, "serverCooldown", 0f);
        Set(computer, "maximumInteractionDistance", 3.25f);

        // The imported monitor is vertical. Its local +Y points out of the glass.
        Vector3 outward = original.up.normalized;
        if (Mathf.Abs(Vector3.Dot(outward, Vector3.up)) > 0.01f)
            throw new InvalidOperationException("The monitor is no longer vertical; author ScreenViewAnchor manually.");
        Vector3 right = Vector3.Cross(Vector3.up, -outward).normalized;
        float width = ProjectedSize(originalRenderer.bounds, right) - 0.004f;
        float availableHeight = originalRenderer.bounds.size.y - 0.004f;
        float height = Mathf.Min(availableHeight, width * 9f / 16f);
        width = height * 16f / 9f;
        float faceOffset = ProjectedSize(originalRenderer.bounds, outward) * 0.5f + 0.002f;
        root.transform.SetPositionAndRotation(originalRenderer.bounds.center + outward * faceOffset,
            Quaternion.LookRotation(-outward, Vector3.up));
        var collider = root.AddComponent<BoxCollider>();
        collider.size = new Vector3(ProjectedSize(monitor.bounds, right) + 0.02f,
            monitor.bounds.size.y + 0.02f, ProjectedSize(monitor.bounds, outward) + 0.02f);
        collider.center = root.transform.InverseTransformPoint(monitor.bounds.center);

        var display = new GameObject("WorldScreen", typeof(MeshFilter), typeof(MeshRenderer));
        display.transform.SetParent(root.transform, false);
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/ComputerScreen.asset");
        if (mesh == null)
        {
            mesh = new Mesh { name = "ComputerScreen" };
            AssetDatabase.CreateAsset(mesh, Folder + "/ComputerScreen.asset");
        }
        mesh.Clear();
        mesh.vertices = new[] { new Vector3(-width / 2, -height / 2, 0), new Vector3(width / 2, -height / 2, 0),
            new Vector3(width / 2, height / 2, 0), new Vector3(-width / 2, height / 2, 0) };
        mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        display.GetComponent<MeshFilter>().sharedMesh = mesh;
        display.GetComponent<MeshRenderer>().sharedMaterial = material;
        display.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        display.GetComponent<MeshRenderer>().receiveShadows = false;
        var anchor = new GameObject("ScreenViewAnchor").transform;
        anchor.SetParent(root.transform, false);
        anchor.localPosition = new Vector3(0, 0, -0.44f);

        var cameraObject = new GameObject("DesktopRenderCamera", typeof(Camera), typeof(UniversalAdditionalCameraData));
        cameraObject.transform.SetParent(root.transform, false);
        cameraObject.transform.SetPositionAndRotation(new Vector3(0, -1000, 0), Quaternion.identity);
        var camera = cameraObject.GetComponent<Camera>();
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Cream;
        camera.cullingMask = 1 << LayerMask.NameToLayer("UI");
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 2f;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.orthographic = true;
        camera.orthographicSize = 3.6f;
        var cameraData = cameraObject.GetComponent<UniversalAdditionalCameraData>();
        cameraData.renderPostProcessing = false;
        cameraData.renderShadows = false;
        cameraData.requiresColorOption = CameraOverrideOption.Off;
        cameraData.requiresDepthOption = CameraOverrideOption.Off;

        var canvasObject = new GameObject("DesktopUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(ComputerDesktopRaycaster), typeof(UIPanelFocus));
        canvasObject.transform.SetParent(root.transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;
        var raycaster = canvasObject.GetComponent<ComputerDesktopRaycaster>();
        raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
        var focus = canvasObject.GetComponent<UIPanelFocus>();
        Set(focus, "profile", profile);
        Set(raycaster, "presenter", desktop);

        var background = Panel("DesktopBackground", canvas.transform, 0, 0, 1280, 720, Cream, false);
        Panel("TopBar", background, 0, 318, 1280, 84, Ink, false);
        Text("Brand", background, "little byte", -445, 319, 300, 50, 31, Cream, FontStyles.Bold);
        Text("TopStatus", background, "STORE DESKTOP  /  01", 366, 319, 420, 40, 18, Pale, FontStyles.Bold, TextAlignmentOptions.Right);
        Panel("Accent", background, -587, 247, 10, 36, Mustard, true);
        var desktopPanel = Rect("Desktop", background, 0, 27, 1280, 536).gameObject;
        Text("Welcome", desktopPanel.transform, "A little room to grow.", -200, 175, 780, 70, 47, Ink, FontStyles.Bold);
        Text("Description", desktopPanel.transform, "Your shop, one good idea at a time.", -200, 112, 780, 45, 24, Teal);
        var upgrades = AppCard("Upgrades", desktopPanel.transform, -292, -55, "+", "Upgrades", "Make space for what's next.", Teal);
        var info = AppCard("StoreInfo", desktopPanel.transform, 292, -55, "i", "Store Info", "A small look at your shop.", Ink);
        var upgradesPanel = CreateApp("Upgrades", background, "Upgrades", "+", "Upgrades coming soon", "New possibilities are on the way.", out Button upgradesClose);
        var storeInfoPanel = CreateApp("StoreInfo", background, "Store Info", "i", "Your next chapter starts here", "Store details are coming soon.", out Button infoClose);
        Panel("FooterRule", background, 0, -250, 1168, 2, Pale, false);
        Text("FooterHint", background, "BACK  /  Close app or leave desktop", -245, -301, 690, 40, 18, Teal);
        var leave = Button("LeaveComputer", background, 450, -301, 270, 58, Ink, Cream, "Leave Computer", 23);
        Set(focus, "defaultSelection", upgrades);
        UnityEventTools.AddPersistentListener(GetEvent(focus, "onBackRequested"), desktop.HandleBack);

        var fullscreen = new GameObject("ComputerFullscreen", typeof(RectTransform), typeof(Canvas));
        fullscreen.transform.SetParent(root.transform, false);
        var fullscreenCanvas = fullscreen.GetComponent<Canvas>();
        fullscreenCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        fullscreenCanvas.sortingOrder = 30000;
        var matte = Panel("Matte", fullscreen.transform, 0, 0, 0, 0, Color.black, false);
        Stretch(matte);
        var matteGroup = matte.gameObject.AddComponent<CanvasGroup>();
        matteGroup.alpha = 0;
        matteGroup.blocksRaycasts = false;
        matteGroup.interactable = false;
        var imageObject = new GameObject("DesktopImage", typeof(RectTransform), typeof(RawImage));
        imageObject.transform.SetParent(fullscreen.transform, false);
        var image = imageObject.GetComponent<RawImage>();
        image.raycastTarget = false;
        image.color = Color.white;
        fullscreen.SetActive(false);

        Set(desktop, "computer", computer);
        Set(desktop, "screenRenderer", display.GetComponent<Renderer>());
        Set(desktop, "uiCamera", camera);
        Set(desktop, "desktopCanvas", canvas);
        Set(desktop, "desktopRaycaster", raycaster);
        Set(desktop, "panelFocus", focus);
        Set(desktop, "desktopPanel", desktopPanel);
        Set(desktop, "upgradesPanel", upgradesPanel);
        Set(desktop, "storeInfoPanel", storeInfoPanel);
        Set(desktop, "upgradesButton", upgrades);
        Set(desktop, "storeInfoButton", info);
        Set(desktop, "upgradesCloseButton", upgradesClose);
        Set(desktop, "storeInfoCloseButton", infoClose);
        Set(desktop, "leaveButton", leave);
        Set(desktop, "fullscreenRoot", fullscreen);
        Set(desktop, "fullscreenImage", image);
        Set(desktop, "fullscreenMatte", matteGroup);
        Set(local, "computer", computer);
        Set(local, "desktop", desktop);
        Set(local, "screenViewAnchor", anchor);
        SetUILayer(canvasObject.transform);
        SetUILayer(fullscreen.transform);
        upgradesPanel.SetActive(false);
        storeInfoPanel.SetActive(false);
        root.SetActive(true);

        string prefabPath = Folder + "/CounterComputer.prefab";
        PrefabUtility.SaveAsPrefabAssetAndConnect(root, prefabPath, InteractionMode.AutomatedAction);
        var coordinator = Object.FindFirstObjectByType<UIFocusCoordinator>();
        Set(desktop, "focusCoordinator", coordinator);
        Set(focus, "coordinator", coordinator);
        WireHud(local);
        PrefabUtility.RecordPrefabInstancePropertyModifications(desktop);
        PrefabUtility.RecordPrefabInstancePropertyModifications(focus);
        PrefabUtility.RecordPrefabInstancePropertyModifications(local);
        var oldText = FindSceneTransform("CST01_FBX_CST01_Till_Screen_Text").GetComponent<Renderer>();
        Undo.RecordObject(oldText, "Hide static monitor text beneath live computer display");
        oldText.enabled = false;
        EditorUtility.SetDirty(oldText);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = root;
        Validate();
    }

    [MenuItem("Tools/Computer/Validate Counter PC")]
    internal static void Validate()
    {
        var report = new StringBuilder();
        var pcs = Object.FindObjectsByType<NetworkStoreComputer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Check(pcs.Length == 1, "One scene computer", report);
        var pc = pcs.Single();
        Check(pc.GetComponent<NetworkObject>() != null && pc.GetComponentsInParent<NetworkObject>(true).Length == 1,
            "Standalone in-scene NetworkObject, no nested network parent", report);
        Check(pc.GetComponent<Collider>() != null, "Interaction collider", report);
        Check(new SerializedObject(pc).FindProperty("serverCooldown").floatValue == 0, "Zero cooldown preserves exact busy rejection", report);
        var desktop = pc.GetComponent<StoreComputerDesktopPresenter>();
        Check(desktop != null && desktop.ValidateSetup(), "Desktop required references", report);
        Check(pc.GetComponent<ComputerLocalPresenter>() != null, "Local camera/input presenter", report);
        var hud = new SerializedObject(pc.GetComponent<ComputerLocalPresenter>()).FindProperty("hudGroups");
        for (int i = 0; i < hud.arraySize; i++)
            Check(hud.GetArrayElementAtIndex(i).objectReferenceValue != null, "Gameplay HUD group " + i, report);
        Check(pc.GetComponentsInChildren<Canvas>(true).Length == 2, "One desktop source canvas plus one fullscreen image canvas", report);
        var raycasters = pc.GetComponentsInChildren<GraphicRaycaster>(true);
        Check(raycasters.Length == 1 && raycasters[0] is ComputerDesktopRaycaster, "One mapped UI raycaster", report);
        Check(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length == 1, "One active existing EventSystem", report);
        var so = new SerializedObject(desktop);
        var camera = (Camera)so.FindProperty("uiCamera").objectReferenceValue;
        Check(!camera.enabled && !camera.allowHDR && camera.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing == false,
            "UI camera renders on demand without HDR/postprocessing", report);
        Check(so.FindProperty("focusCoordinator").objectReferenceValue != null, "Existing scene focus coordinator assigned", report);
        var focus = (UIPanelFocus)so.FindProperty("panelFocus").objectReferenceValue;
        Check(!focus.Profile.FocusOnEnable && focus.Profile.BackMode == UIPanelBackMode.InvokeEvent &&
            !focus.Profile.RememberLastSelection, "PC Back and default selection profile", report);
        Check(!FindSceneTransform("CST01_FBX_CST01_Till_Screen_Text").GetComponent<Renderer>().enabled,
            "Static screen text hidden, original art preserved", report);
        foreach (var size in new[] { new Vector2(1920, 1080), new Vector2(1920, 1200), new Vector2(1280, 800) })
        {
            Rect fit = StoreComputerDesktopPresenter.CalculateFitRect(new Rect(Vector2.zero, size), 16f / 9f);
            Check(Mathf.Abs(fit.width / fit.height - 16f / 9f) < 0.001f &&
                fit.width <= size.x + .01f && fit.height <= size.y + .01f, $"Aspect fit {size}", report);
        }
        report.AppendLine("PLAY MODE NOT RUN: two peers, disconnect/late join, real mouse/gamepad, transitions and cursor restoration.");
        Write(report.ToString());
    }

    private static void Check(bool passed, string name, StringBuilder report)
    {
        report.AppendLine((passed ? "PASS " : "FAIL ") + name);
        if (!passed) throw new InvalidOperationException(report.ToString());
    }

    [MenuItem("Tools/Computer/Validate Sobel UI Exclusion")]
    private static void ValidateSobel()
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(
            "Assets/Source/Shaders/HLSL/Sobel/EXW_SceneSobelOutline.shader");
        var material = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Source/Shaders/HLSL/Sobel/EXW_Fullscreen_Scene Sobel Outline.mat");
        if (shader == null || material == null) throw new InvalidOperationException("The scene Sobel shader/material is missing.");
        var report = new StringBuilder();
        var compile = typeof(ShaderUtil).GetMethod("CompilePass", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            null, new[] { typeof(Material), typeof(int), typeof(bool) }, null);
        if (compile != null)
        {
            compile.Invoke(null, new object[] { material, 0, true });
            report.AppendLine("PASS Sobel material pass 0 synchronous compilation requested.");
        }
        bool valid = true;
        foreach (var message in ShaderUtil.GetShaderMessages(shader))
        {
            report.AppendLine(message.severity + " " + message.message + " line=" + message.line);
            valid &= message.severity.ToString() != "Error";
        }
        report.AppendLine(valid ? "PASS No Sobel shader compiler errors." : "FAIL Sobel shader compiler errors.");
        report.AppendLine("Visual PC enter/exit, fade and co-op camera checks require Game View verification.");
        Directory.CreateDirectory(ReviewFolder);
        File.WriteAllText(ReviewFolder + "/sobel-validation.txt", report.ToString());
        if (!valid) throw new InvalidOperationException(report.ToString());
        Debug.Log("[Sobel UI] " + report);
    }

    private static Transform FindSceneTransform(string name)
    {
        return Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Single(t => t.name == name);
    }

    private static float ProjectedSize(Bounds bounds, Vector3 axis) =>
        Vector3.Dot(bounds.size, new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z)));

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    private static void CreateRoundedSprite()
    {
        string path = Folder + "/ComputerRounded.png";
        if (!File.Exists(path))
        {
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            var colors = new Color[32 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    var point = new Vector2(x + 0.5f, y + 0.5f);
                    var closest = new Vector2(Mathf.Clamp(point.x, 12, 20), Mathf.Clamp(point.y, 12, 20));
                    colors[y * 32 + x] = new Color(1, 1, 1, Mathf.Clamp01(12.5f - Vector2.Distance(point, closest)));
                }
            texture.SetPixels(colors);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = new Vector4(12, 12, 12, 12);
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        rounded = AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    private static RectTransform Panel(string name, Transform parent, float x, float y, float width, float height, Color color, bool soft)
    {
        var rect = Rect(name, parent, x, y, width, height);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        if (soft) { image.sprite = rounded; image.type = Image.Type.Sliced; }
        return rect;
    }

    private static TMP_Text Text(string name, Transform parent, string value, float x, float y, float width, float height,
        float size, Color color, FontStyles style = FontStyles.Normal, TextAlignmentOptions alignment = TextAlignmentOptions.Left)
    {
        var rect = Rect(name, parent, x, y, width, height);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.fontStyle = style;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    private static Button Button(string name, Transform parent, float x, float y, float width, float height,
        Color color, Color labelColor, string label, float size)
    {
        var rect = Panel(name, parent, x, y, width, height, color, true);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = rect.GetComponent<Image>();
        button.targetGraphic.raycastTarget = true;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Hex("D2E7E4");
        colors.selectedColor = Hex("B7DAD5");
        colors.pressedColor = Hex("9ABFBB");
        colors.fadeDuration = 0;
        button.colors = colors;
        Text("Label", rect, label, 0, 0, width - 30, height, size, labelColor, FontStyles.Bold, TextAlignmentOptions.Center);
        return button;
    }

    private static Button AppCard(string name, Transform parent, float x, float y, string icon, string title, string subtitle, Color color)
    {
        var button = Button(name, parent, x, y, 552, 220, color, Cream, "", 1);
        Text("Icon", button.transform, icon, -207, 42, 52, 65, 54, Cream, FontStyles.Bold, TextAlignmentOptions.Center);
        Text("Title", button.transform, title, 44, 43, 365, 50, 33, Cream, FontStyles.Bold);
        Text("Subtitle", button.transform, subtitle, 0, -52, 466, 45, 21, Cream);
        return button;
    }

    private static GameObject CreateApp(string name, Transform parent, string title, string icon, string message, string detail, out Button close)
    {
        var panel = Panel(name + "App", parent, 0, 23, 1168, 516, Pale, true);
        Text("AppTitle", panel, title, -250, 205, 590, 60, 31, Ink, FontStyles.Bold);
        close = Button("CloseApp", panel, 494, 204, 112, 48, Cream, Ink, "Close", 21);
        Panel("Rule", panel, 0, 156, 1090, 2, Hex("C9DBD5"), false);
        Panel("IconTile", panel, 0, 53, 88, 88, Cream, true);
        Text("Icon", panel, icon, 0, 55, 82, 82, 53, Teal, FontStyles.Bold, TextAlignmentOptions.Center);
        Text("Message", panel, message, 0, -45, 1070, 64, 37, Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        Text("Detail", panel, detail, 0, -108, 1070, 50, 24, Teal, FontStyles.Normal, TextAlignmentOptions.Center);
        return panel.gameObject;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void SetUILayer(Transform root)
    {
        root.gameObject.layer = LayerMask.NameToLayer("UI");
        foreach (Transform child in root) SetUILayer(child);
    }

    private static UnityEngine.Events.UnityEvent GetEvent(Object target, string name)
    {
        var field = target.GetType().GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (UnityEngine.Events.UnityEvent)field.GetValue(target);
    }

    private static void Set(Object target, string name, object value)
    {
        var so = new SerializedObject(target);
        var property = so.FindProperty(name) ?? throw new InvalidOperationException(target.GetType().Name + "." + name + " not found.");
        if (value is Object reference) property.objectReferenceValue = reference;
        else if (value == null) property.objectReferenceValue = null;
        else if (value is bool flag) property.boolValue = flag;
        else if (value is float number) property.floatValue = number;
        else if (value is int integer) property.intValue = integer;
        else if (value is string text) property.stringValue = text;
        else throw new ArgumentException("Unsupported serialized value for " + name);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void WireHud(ComputerLocalPresenter local)
    {
        var targets = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(t => t.name == "StackInfoPanel").ToArray();
        var groups = targets.Select(t =>
        {
            var group = t.GetComponent<CanvasGroup>();
            return group != null ? group : Undo.AddComponent<CanvasGroup>(t.gameObject);
        }).ToArray();
        foreach (var group in groups) EditorUtility.SetDirty(group);
        var so = new SerializedObject(local);
        var property = so.FindProperty("hudGroups");
        property.arraySize = groups.Length;
        for (int i = 0; i < groups.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = groups[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(local);
        PrefabUtility.RecordPrefabInstancePropertyModifications(local);
    }

    // Restore driven values dirtied by Unity's unrelated existing layout rebuilds.
    internal static void Finish()
    {
        var local = Object.FindFirstObjectByType<ComputerLocalPresenter>();
        WireHud(local);
        string path = ReviewFolder + "/original-scene-layout.json";
        if (File.Exists(path))
        {
            var layout = JsonUtility.FromJson<LayoutSnapshot>(File.ReadAllText(path));
            var transforms = Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var item in layout.rects)
            {
                var rect = transforms.Single(t => GlobalObjectId.GetGlobalObjectIdSlow(t).targetObjectId.ToString() == item.id);
                var so = new SerializedObject(rect);
                so.FindProperty(item.path).vector2Value = item.value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            var roots = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(t => PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)).ToArray();
            foreach (var root in roots)
            {
                var mods = PrefabUtility.GetPropertyModifications(root.gameObject);
                if (mods == null) continue;
                bool changed = false;
                foreach (var mod in mods)
                {
                    if (!mod.target || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mod.target, out string guid, out long id)) continue;
                    var saved = layout.modifications.FirstOrDefault(v => v.guid == guid && v.id == id.ToString() && v.path == mod.propertyPath);
                    if (saved == null) continue;
                    mod.value = saved.value;
                    changed = true;
                }
                if (changed) PrefabUtility.SetPropertyModifications(root.gameObject, mods);
            }
        }
        EditorSceneManager.MarkSceneDirty(local.gameObject.scene);
        EditorSceneManager.SaveScene(local.gameObject.scene);
        AssetDatabase.SaveAssets();
        Validate();
    }

    [Serializable] private class LayoutSnapshot { public LayoutRect[] rects; public LayoutModification[] modifications; }
    [Serializable] private class LayoutRect { public string id; public string path; public Vector2 value; }
    [Serializable] private class LayoutModification { public string id; public string guid; public string path; public string value; }

    private static void Write(string report)
    {
        Directory.CreateDirectory(ReviewFolder);
        File.WriteAllText(ReviewFolder + "/inspection.txt", report);
        File.WriteAllText(Result, report);
        Debug.Log("[Computer Setup] " + report);
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        return color;
    }

    private static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
}
