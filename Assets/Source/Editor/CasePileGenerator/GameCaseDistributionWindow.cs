using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class GameCaseDistributionWindow : EditorWindow
{
    private const string WindowTitle = "Game Case Distribution";

    private static readonly Color[] ZonePalette =
    {
        new Color(0.15f, 0.75f, 1f, 0.22f),
        new Color(1f, 0.55f, 0.18f, 0.22f),
        new Color(0.35f, 0.9f, 0.4f, 0.22f),
        new Color(0.85f, 0.35f, 1f, 0.22f),
        new Color(1f, 0.3f, 0.45f, 0.22f),
        new Color(0.95f, 0.85f, 0.2f, 0.22f)
    };

    private GameCaseDistributionGenerator _generator;
    private Editor _generatorEditor;
    private Vector2 _scroll;
    private Vector2 _zoneScroll;
    private bool _showGeneratorSettings = true;
    private bool _showZones = true;
    private bool _showPreviewPoints;
    private int _previewPointsPerZone = 80;

    private bool _lassoEnabled;
    private bool _isDrawing;
    private float _drawPlaneY;
    private float _sampleSpacing = 0.15f;
    private float _simplifyTolerance = 0.12f;
    private readonly List<Vector3> _lassoPoints = new List<Vector3>();

    public static bool IsDrawingLasso { get; private set; }

    [MenuItem("Tools/Rogue Duck/Game Case Distribution")]
    public static void Open()
    {
        GetWindow<GameCaseDistributionWindow>(WindowTitle);
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += DuringSceneGui;
        EditorApplication.hierarchyChanged += Repaint;
        TryFindGenerator();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= DuringSceneGui;
        EditorApplication.hierarchyChanged -= Repaint;
        DestroyImmediate(_generatorEditor);
        _generatorEditor = null;
        IsDrawingLasso = false;
    }

    private void OnGUI()
    {
        DrawHeader();
        DrawGeneratorSelection();

        if (!_generator)
        {
            EditorGUILayout.HelpBox(
                "Create or assign a GameCaseDistributionGenerator to begin.",
                MessageType.Info);
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawDrawTools();
        DrawGenerationTools();
        DrawZoneList();
        DrawGeneratorSettings();
        EditorGUILayout.EndScrollView();
    }

    private void DrawHeader()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField(WindowTitle, EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Draw freehand XZ zones in Scene View, then physics-bake cases into them.",
            EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space(4f);
    }

    private void DrawGeneratorSelection()
    {
        EditorGUI.BeginChangeCheck();
        var selected = (GameCaseDistributionGenerator)EditorGUILayout.ObjectField(
            "Generator",
            _generator,
            typeof(GameCaseDistributionGenerator),
            true);

        if (EditorGUI.EndChangeCheck())
            SetGenerator(selected);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Create Generator"))
                CreateGenerator();

            using (new EditorGUI.DisabledScope(!_generator))
            {
                if (GUILayout.Button("Select"))
                    Selection.activeGameObject = _generator.gameObject;

                if (GUILayout.Button("Top View"))
                    SetTopView();
            }
        }

        EditorGUILayout.Space(5f);
    }

    private void DrawDrawTools()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Freehand Lasso", EditorStyles.boldLabel);

        _drawPlaneY = EditorGUILayout.FloatField(
            new GUIContent("Drawing Height Y", "Set this to the floor or balcony height you want to draw on."),
            _drawPlaneY);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Use Generator Y"))
                _drawPlaneY = _generator.transform.position.y;

            if (GUILayout.Button("Use Selection Y") && Selection.activeTransform)
                _drawPlaneY = Selection.activeTransform.position.y;
        }

        _sampleSpacing = EditorGUILayout.Slider("Point Spacing", _sampleSpacing, 0.03f, 1f);
        _simplifyTolerance = EditorGUILayout.Slider("Simplify", _simplifyTolerance, 0.01f, 1f);

        Color previous = GUI.backgroundColor;
        GUI.backgroundColor = _lassoEnabled ? new Color(0.35f, 1f, 0.5f) : previous;

        if (GUILayout.Button(_lassoEnabled ? "Lasso Active — Drag In Scene View" : "Enable Lasso Tool", GUILayout.Height(28f)))
        {
            _lassoEnabled = !_lassoEnabled;
            _isDrawing = false;
            _lassoPoints.Clear();
            IsDrawingLasso = _lassoEnabled;
            SceneView.RepaintAll();
        }

        GUI.backgroundColor = previous;

        if (_lassoEnabled)
        {
            EditorGUILayout.HelpBox(
                "Hold left mouse and draw a closed area in Scene View. Release to create a zone. " +
                "Escape cancels the current stroke; press Escape again to leave Lasso mode.",
                MessageType.Info);
        }

        if (GUILayout.Button("Create Rectangle Zone"))
            CreateRectangleZone();

        _showPreviewPoints = EditorGUILayout.ToggleLeft("Preview sampled surface points", _showPreviewPoints);

        if (_showPreviewPoints)
            _previewPointsPerZone = EditorGUILayout.IntSlider("Points Per Zone", _previewPointsPerZone, 10, 300);

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(4f);
    }

    private void DrawGenerationTools()
    {
        GameCaseSpawnZone selectedZone = GetSelectedZone();

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Bake", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        using (new EditorGUI.DisabledScope(!selectedZone))
        {
            if (GUILayout.Button("Generate Selected"))
                GameCaseDistributionBaker.GenerateZone(_generator, selectedZone);

            if (GUILayout.Button("Randomize Selected"))
                GameCaseDistributionBaker.GenerateZone(_generator, selectedZone, randomizeSeed: true);

            if (GUILayout.Button("Clear Selected"))
                GameCaseDistributionBaker.ClearZone(selectedZone);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate All", GUILayout.Height(26f)))
                GameCaseDistributionBaker.GenerateAll(_generator);

            if (GUILayout.Button("Randomize All", GUILayout.Height(26f)))
                GameCaseDistributionBaker.GenerateAll(_generator, randomizeSeed: true);

            if (GUILayout.Button("Clear All", GUILayout.Height(26f)))
                GameCaseDistributionBaker.ClearAll(_generator);
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(4f);
    }

    private void DrawZoneList()
    {
        _showZones = EditorGUILayout.Foldout(_showZones, "Zones", true);

        if (!_showZones)
            return;

        GameCaseSpawnZone[] zones = _generator.GetZones();
        GameCaseSpawnZone[] included = zones
            .Where(zone => zone && zone.gameObject.activeInHierarchy && zone.IncludeInGenerateAll)
            .ToArray();
        Dictionary<GameCaseSpawnZone, int> distributed =
            GameCaseDistributionBaker.CalculateCounts(_generator, included);

        int total = distributed.Values.Sum();
        EditorGUILayout.LabelField($"Enabled: {included.Length}    Assigned cases: {total}", EditorStyles.miniBoldLabel);

        float listHeight = Mathf.Min(220f, Mathf.Max(52f, zones.Length * 25f + 6f));
        _zoneScroll = EditorGUILayout.BeginScrollView(_zoneScroll, EditorStyles.helpBox, GUILayout.Height(listHeight));

        for (int i = 0; i < zones.Length; i++)
        {
            GameCaseSpawnZone zone = zones[i];

            if (!zone)
                continue;

            using (new EditorGUILayout.HorizontalScope())
            {
                bool include = EditorGUILayout.Toggle(zone.IncludeInGenerateAll, GUILayout.Width(18f));

                if (include != zone.IncludeInGenerateAll)
                {
                    Undo.RecordObject(zone, "Toggle Game Case Zone");
                    zone.IncludeInGenerateAll = include;
                    EditorUtility.SetDirty(zone);
                }

                Color oldColor = GUI.color;

                if (!zone.IsValid)
                    GUI.color = new Color(1f, 0.45f, 0.45f);

                if (GUILayout.Button(zone.name, EditorStyles.miniButtonLeft))
                    Selection.activeGameObject = zone.gameObject;

                GUI.color = oldColor;

                int count = distributed.TryGetValue(zone, out int value) ? value : zone.CaseCount;
                GUILayout.Label(count.ToString(), EditorStyles.miniLabel, GUILayout.Width(46f));

                if (GUILayout.Button("F", EditorStyles.miniButtonRight, GUILayout.Width(24f)))
                {
                    Selection.activeGameObject = zone.gameObject;
                    SceneView.lastActiveSceneView?.FrameSelected();
                }
            }
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.Space(4f);
    }

    private void DrawGeneratorSettings()
    {
        _showGeneratorSettings = EditorGUILayout.Foldout(_showGeneratorSettings, "Generator Settings", true);

        if (!_showGeneratorSettings)
            return;

        Editor.CreateCachedEditor(_generator, null, ref _generatorEditor);
        _generatorEditor.OnInspectorGUI();
    }

    private void DuringSceneGui(SceneView sceneView)
    {
        if (!_generator)
            return;

        DrawZonesInScene();

        if (_showPreviewPoints && Event.current.type == EventType.Repaint)
            DrawPreviewPoints();

        if (!_lassoEnabled)
            return;

        Event current = Event.current;
        int controlId = GUIUtility.GetControlID("GameCaseLasso".GetHashCode(), FocusType.Passive);

        if (!current.alt)
            HandleUtility.AddDefaultControl(controlId);

        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
        {
            if (_isDrawing)
            {
                _isDrawing = false;
                _lassoPoints.Clear();
            }
            else
            {
                _lassoEnabled = false;
                IsDrawingLasso = false;
            }

            current.Use();
            Repaint();
            sceneView.Repaint();
            return;
        }

        if (current.alt)
            return;

        switch (current.type)
        {
            case EventType.MouseDown when current.button == 0:
                if (TryMouseToPlane(current.mousePosition, out Vector3 firstPoint))
                {
                    _isDrawing = true;
                    _lassoPoints.Clear();
                    _lassoPoints.Add(firstPoint);
                    GUIUtility.hotControl = controlId;
                    current.Use();
                }
                break;

            case EventType.MouseDrag when _isDrawing && current.button == 0:
                if (TryMouseToPlane(current.mousePosition, out Vector3 dragPoint) &&
                    (_lassoPoints.Count == 0 || Vector3.Distance(_lassoPoints[^1], dragPoint) >= _sampleSpacing))
                {
                    _lassoPoints.Add(dragPoint);
                    sceneView.Repaint();
                }
                current.Use();
                break;

            case EventType.MouseUp when _isDrawing && current.button == 0:
                GUIUtility.hotControl = 0;
                _isDrawing = false;
                FinishLasso();
                current.Use();
                break;
        }

        if (_lassoPoints.Count > 1)
        {
            Handles.color = Color.white;
            Handles.DrawAAPolyLine(4f, _lassoPoints.ToArray());

            if (_isDrawing)
                Handles.DrawDottedLine(_lassoPoints[^1], _lassoPoints[0], 5f);
        }
    }

    private void DrawZonesInScene()
    {
        GameCaseSpawnZone[] zones = _generator.GetZones();
        GameCaseSpawnZone[] included = zones
            .Where(zone => zone && zone.gameObject.activeInHierarchy && zone.IncludeInGenerateAll)
            .ToArray();
        Dictionary<GameCaseSpawnZone, int> distributed =
            GameCaseDistributionBaker.CalculateCounts(_generator, included);

        for (int z = 0; z < zones.Length; z++)
        {
            GameCaseSpawnZone zone = zones[z];

            if (!zone || zone.Points.Count < 3)
                continue;

            Vector3[] worldPoints = new Vector3[zone.Points.Count];

            for (int i = 0; i < worldPoints.Length; i++)
                worldPoints[i] = zone.GetWorldPoint(i) + Vector3.up * 0.015f;

            Color fill = zone.IsValid ? zone.Color : new Color(1f, 0.1f, 0.1f, 0.3f);
            Color outline = new Color(fill.r, fill.g, fill.b, 0.95f);
            DrawConcaveFill(zone, worldPoints, fill);

            var closed = new Vector3[worldPoints.Length + 1];
            Array.Copy(worldPoints, closed, worldPoints.Length);
            closed[^1] = worldPoints[0];
            Handles.color = outline;
            Handles.DrawAAPolyLine(3f, closed);

            if (zone.ShowLabel)
            {
                int displayCount = distributed.TryGetValue(zone, out int value) ? value : zone.CaseCount;
                Handles.Label(
                    zone.GetWorldCenter() + Vector3.up * 0.08f,
                    $"{zone.name}\n{displayCount} cases",
                    EditorStyles.whiteMiniLabel);
            }
        }
    }

    private static void DrawConcaveFill(GameCaseSpawnZone zone, Vector3[] worldPoints, Color color)
    {
        if (!zone.IsValid)
            return;

        List<int> triangles = GameCasePolygonUtility.Triangulate(zone.Points);
        Handles.color = color;

        for (int i = 0; i + 2 < triangles.Count; i += 3)
        {
            Handles.DrawAAConvexPolygon(
                worldPoints[triangles[i]],
                worldPoints[triangles[i + 1]],
                worldPoints[triangles[i + 2]]);
        }
    }

    private void DrawPreviewPoints()
    {
        GameCaseSpawnZone[] zones = _generator.GetZones()
            .Where(zone => zone && zone.gameObject.activeInHierarchy && zone.IncludeInGenerateAll && zone.IsValid)
            .ToArray();

        for (int z = 0; z < zones.Length; z++)
        {
            GameCaseSpawnZone zone = zones[z];
            var random = new System.Random(unchecked(_generator.Seed + zone.SeedOffset * 397));
            Handles.color = new Color(1f, 0.85f, 0.2f, 0.9f);

            for (int i = 0; i < _previewPointsPerZone; i++)
            {
                if (!zone.TrySampleSurface(random, _generator.PointSampleAttempts, out Vector3 point))
                    continue;

                float size = HandleUtility.GetHandleSize(point) * 0.025f;
                Handles.DotHandleCap(0, point + Vector3.up * 0.025f, Quaternion.identity, size, EventType.Repaint);
            }
        }
    }

    private void FinishLasso()
    {
        if (_lassoPoints.Count < 3)
        {
            _lassoPoints.Clear();
            ShowNotification(new GUIContent("Draw a larger area."));
            return;
        }

        var raw = _lassoPoints.Select(point => new Vector2(point.x, point.z)).ToList();

        if ((raw[0] - raw[^1]).sqrMagnitude < _sampleSpacing * _sampleSpacing)
            raw.RemoveAt(raw.Count - 1);

        List<Vector2> simplified = GameCasePolygonUtility.SimplifyClosed(raw, _simplifyTolerance);

        if (simplified.Count < 3 || GameCasePolygonUtility.Area(simplified) < 0.01f)
        {
            _lassoPoints.Clear();
            ShowNotification(new GUIContent("The lasso area is too small."));
            return;
        }

        if (!GameCasePolygonUtility.IsSimple(simplified))
        {
            _lassoPoints.Clear();
            ShowNotification(new GUIContent("The lasso crossed itself. Draw it again."));
            Debug.LogWarning("[GameCaseDistribution] Lasso rejected because its outline crosses itself.");
            return;
        }

        Vector2 center2D = Vector2.zero;

        for (int i = 0; i < simplified.Count; i++)
            center2D += simplified[i];

        center2D /= simplified.Count;

        Transform parent = _generator.EnsureZonesRoot();
        string zoneName = GameObjectUtility.GetUniqueNameForSibling(parent, "Spawn Zone");
        var gameObject = new GameObject(zoneName);
        Undo.RegisterCreatedObjectUndo(gameObject, "Create Game Case Spawn Zone");
        gameObject.transform.SetParent(parent, true);
        gameObject.transform.position = new Vector3(center2D.x, _drawPlaneY, center2D.y);

        GameCaseSpawnZone zone = Undo.AddComponent<GameCaseSpawnZone>(gameObject);
        zone.SetSceneColor(ZonePalette[(parent.childCount - 1) % ZonePalette.Length]);
        var worldPoints = new List<Vector3>(simplified.Count);

        for (int i = 0; i < simplified.Count; i++)
            worldPoints.Add(new Vector3(simplified[i].x, _drawPlaneY, simplified[i].y));

        zone.SetPointsFromWorld(worldPoints);
        EditorUtility.SetDirty(zone);
        EditorUtility.SetDirty(_generator);
        Selection.activeGameObject = gameObject;
        _lassoPoints.Clear();
        Repaint();
        SceneView.RepaintAll();
    }

    private void CreateRectangleZone()
    {
        Transform parent = _generator.EnsureZonesRoot();
        string zoneName = GameObjectUtility.GetUniqueNameForSibling(parent, "Spawn Zone");
        var gameObject = new GameObject(zoneName);
        Undo.RegisterCreatedObjectUndo(gameObject, "Create Game Case Spawn Zone");
        gameObject.transform.SetParent(parent, true);

        Vector3 position = Selection.activeTransform
            ? Selection.activeTransform.position
            : _generator.transform.position;

        position.y = _drawPlaneY;
        gameObject.transform.position = position;
        GameCaseSpawnZone zone = Undo.AddComponent<GameCaseSpawnZone>(gameObject);
        zone.SetSceneColor(ZonePalette[(parent.childCount - 1) % ZonePalette.Length]);
        EditorUtility.SetDirty(zone);
        Selection.activeGameObject = gameObject;
        EditorUtility.SetDirty(_generator);
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    private bool TryMouseToPlane(Vector2 mousePosition, out Vector3 point)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        var plane = new Plane(Vector3.up, new Vector3(0f, _drawPlaneY, 0f));

        if (plane.Raycast(ray, out float distance))
        {
            point = ray.GetPoint(distance);
            return true;
        }

        point = default;
        return false;
    }

    private void SetTopView()
    {
        SceneView view = SceneView.lastActiveSceneView;

        if (!view)
            return;

        Vector3 pivot = _generator.transform.position;
        GameCaseSpawnZone selectedZone = GetSelectedZone();

        if (selectedZone)
            pivot = selectedZone.GetWorldCenter();

        view.orthographic = true;
        view.rotation = Quaternion.Euler(90f, 0f, 0f);
        view.pivot = pivot;
        view.Repaint();
    }

    private void CreateGenerator()
    {
        var gameObject = new GameObject("GameCaseDistribution");
        Undo.RegisterCreatedObjectUndo(gameObject, "Create Game Case Distribution");
        GameCaseDistributionGenerator generator = Undo.AddComponent<GameCaseDistributionGenerator>(gameObject);
        generator.EnsureZonesRoot();
        generator.EnsureGeneratedRoot();
        SetGenerator(generator);
        Selection.activeGameObject = gameObject;
    }

    private void TryFindGenerator()
    {
        if (_generator)
            return;

        SetGenerator(UnityEngine.Object.FindFirstObjectByType<GameCaseDistributionGenerator>());
    }

    private void SetGenerator(GameCaseDistributionGenerator generator)
    {
        if (_generator == generator)
            return;

        DestroyImmediate(_generatorEditor);
        _generatorEditor = null;
        _generator = generator;

        if (_generator)
            _drawPlaneY = _generator.transform.position.y;

        Repaint();
        SceneView.RepaintAll();
    }

    private GameCaseSpawnZone GetSelectedZone()
    {
        if (!Selection.activeGameObject)
            return null;

        GameCaseSpawnZone zone = Selection.activeGameObject.GetComponent<GameCaseSpawnZone>();

        if (!zone || !_generator)
            return null;

        return zone.transform.IsChildOf(_generator.transform) ? zone : null;
    }
}
