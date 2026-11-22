// SceneBuilder.cs — помощник для сборки 2D-сцены из слоёв фона.
// Положить в Assets/Editor/. Нужен также Parallax.cs (в Assets/Scripts/).
// Открыть: Tools → Scene Builder
//
// Все действия со сценой отменяются через Ctrl+Z.
// Действия с картинками (кнопка "Подготовить спрайты") — нет, перед ними коммит.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SceneBuilder : EditorWindow
{
    // Планы: имя группы, сортировочный слой, коэффициент параллакса (null = без параллакса)
    struct Plane
    {
        public string Group, SortingLayer; public float? Factor; public string Hint;
        public Plane(string g, string l, float? f, string h) { Group = g; SortingLayer = l; Factor = f; Hint = h; }
    }

    static readonly Plane[] Planes =
    {
        new Plane("BG_Sky",       "Sky",        0.95f, "небо"),
        new Plane("BG_Forest",    "Forest",     0.8f,  "полоса леса"),
        new Plane("BG_FieldFar",  "FieldFar",   0.6f,  "дальнее поле"),
        new Plane("BG_Tree",      "Tree",       0.4f,  "дерево и средний план"),
        new Plane("BG_WheatMid",  "WheatMid",   0.15f, "колоски за героиней"),
        new Plane("Gameplay",     "Default",    null,  "земля, платформы, герои"),
        new Plane("FG_WheatNear", "Foreground", -0.3f, "колоски перед героиней"),
    };

    // Порядок слоёв: всё до Default — за игровым слоем, после — перед ним
    static readonly string[] LayersBehind = { "Sky", "Forest", "FieldFar", "Tree", "WheatMid" };
    static readonly string[] LayersFront = { "Foreground" };

    string rootName = "Level_Field";
    float targetPPU = 300f;
    float overlapPx = 0f;
    bool alignBottoms = true;
    float orthoSize = 3.6f;
    Vector2 scroll;

    [MenuItem("Tools/Scene Builder")]
    static void Open() => GetWindow<SceneBuilder>("Scene Builder");

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        var wrap = new GUIStyle(EditorStyles.helpBox) { wordWrap = true };

        // ---------- 1. Спрайты ----------
        EditorGUILayout.LabelField("1. Подготовить картинки фона", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Выделите картинки или папки в окне Project. Всем будет выставлен один PPU, " +
                                   "Max Size по размеру картинки (до 4096), выключены Physics Shape и MipMaps. " +
                                   "Размер объектов в сцене изменится. Без Undo — сначала коммит.", wrap);
        targetPPU = EditorGUILayout.FloatField("PPU", targetPPU);
        if (GUILayout.Button("Подготовить выделенные спрайты")) PrepareSprites();

        EditorGUILayout.Space(12);

        // ---------- 2. Структура ----------
        EditorGUILayout.LabelField("2. Структура сцены", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Создаёт корневой объект с группами-планами, сортировочные слои и параллакс на группах. " +
                                   "Повторный запуск ничего не дублирует.", wrap);
        rootName = EditorGUILayout.TextField("Корневой объект", rootName);
        if (GUILayout.Button("Создать структуру")) CreateStructure();

        EditorGUILayout.Space(12);

        // ---------- 3. Разложить по планам ----------
        EditorGUILayout.LabelField("3. Разложить объекты по планам", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Выделите объекты в Hierarchy и нажмите план. Объект переедет в группу " +
                                   "(положение на экране не изменится) и получит её сортировочный слой.", wrap);
        foreach (var p in Planes)
            if (GUILayout.Button($"→ {p.Group}   ({p.Hint})")) AssignToPlane(p);

        EditorGUILayout.Space(12);

        // ---------- 4. Инструменты ----------
        EditorGUILayout.LabelField("4. Инструменты", EditorStyles.boldLabel);
        if (GUILayout.Button("Найти объекты фона со Scale ≠ 1")) FindScaled();
        if (GUILayout.Button("Сбросить Scale в 1 у выделенных")) ResetScale();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Выстроить выделенные куски в ряд слева направо, край к краю:", wrap);
        overlapPx = EditorGUILayout.FloatField("Нахлёст, px", overlapPx);
        alignBottoms = EditorGUILayout.Toggle("Выровнять по низу", alignBottoms);
        if (GUILayout.Button("Выстроить в ряд")) AlignRow();

        EditorGUILayout.Space(12);

        // ---------- 5. Камера ----------
        EditorGUILayout.LabelField("5. Камера", EditorStyles.boldLabel);
        orthoSize = EditorGUILayout.FloatField("Orthographic Size", orthoSize);
        EditorGUILayout.LabelField($"При PPU {targetPPU} камера покажет {orthoSize * 2 * targetPPU:0} px картинки по высоте.", wrap);
        if (GUILayout.Button("Выставить Main Camera")) SetCamera();

        EditorGUILayout.EndScrollView();
    }

    // ================= 1. Спрайты =================

    void PrepareSprites()
    {
        var paths = new HashSet<string>();
        foreach (var o in Selection.objects)
        {
            var p = AssetDatabase.GetAssetPath(o);
            if (string.IsNullOrEmpty(p)) continue;
            if (AssetDatabase.IsValidFolder(p))
                foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { p })) paths.Add(AssetDatabase.GUIDToAssetPath(g));
            else paths.Add(p);
        }
        var importers = paths.Select(p => AssetImporter.GetAtPath(p) as TextureImporter)
                             .Where(t => t != null && t.textureType == TextureImporterType.Sprite).ToList();
        if (importers.Count == 0)
        {
            EditorUtility.DisplayDialog("Scene Builder", "Выделите в окне Project картинки-спрайты или папки с ними.", "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("Scene Builder",
                $"Изменить {importers.Count} спрайтов: PPU {targetPPU}, Max Size по размеру, без Physics Shape и MipMaps?\n\n" +
                "Объекты с этими спрайтами в сценах изменят размер. Ctrl+Z не отменит. Коммит сделан?", "Да", "Отмена"))
            return;

        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var ti in importers)
            {
                var s = new TextureImporterSettings();
                ti.ReadTextureSettings(s);
                s.spritePixelsPerUnit = targetPPU;
                s.spriteGenerateFallbackPhysicsShape = false;
                s.mipmapEnabled = false;
                ti.SetTextureSettings(s);

                ti.GetSourceTextureWidthAndHeight(out int w, out int h);
                var ps = ti.GetDefaultPlatformTextureSettings();
                ps.maxTextureSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(w, h)), 32, 4096);
                ti.SetPlatformTextureSettings(ps);

                EditorUtility.SetDirty(ti);
                ti.SaveAndReimport();
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        Debug.Log($"[SceneBuilder] Подготовлено спрайтов: {importers.Count}");
    }

    // ================= 2. Структура =================

    void CreateStructure()
    {
        EnsureSortingLayers();

        var root = FindRoot(create: true);
        var parallax = ParallaxType();
        foreach (var p in Planes)
        {
            var t = root.Find(p.Group);
            if (t == null)
            {
                var go = new GameObject(p.Group);
                Undo.RegisterCreatedObjectUndo(go, "Create plane");
                Undo.SetTransformParent(go.transform, root, "Create plane");
                go.transform.localPosition = Vector3.zero;
                t = go.transform;
            }
            if (p.Factor.HasValue && parallax != null)
            {
                var c = t.GetComponent(parallax);
                if (c == null) c = Undo.AddComponent(t.gameObject, parallax);
                var so = new SerializedObject(c);
                var f = so.FindProperty("factor");
                if (f != null) { f.floatValue = p.Factor.Value; so.ApplyModifiedProperties(); }
            }
        }
        Selection.activeTransform = root;
        if (parallax == null)
            Debug.LogWarning("[SceneBuilder] Не нашла скрипт Parallax — положите Parallax.cs в Assets/Scripts и нажмите «Создать структуру» ещё раз.");
        Debug.Log($"[SceneBuilder] Структура {rootName} готова.");
    }

    Transform FindRoot(bool create)
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var existing = scene.GetRootGameObjects().FirstOrDefault(g => g.name == rootName);
        if (existing != null) return existing.transform;
        if (!create) return null;
        var go = new GameObject(rootName);
        Undo.RegisterCreatedObjectUndo(go, "Create root");
        return go.transform;
    }

    static Type ParallaxType() =>
        TypeCache.GetTypesDerivedFrom<MonoBehaviour>().FirstOrDefault(t => t.Name == "Parallax");

    static void EnsureSortingLayers()
    {
        var tagManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset").FirstOrDefault();
        if (tagManager == null) return;
        var so = new SerializedObject(tagManager);
        var arr = so.FindProperty("m_SortingLayers");
        var added = new List<int>();

        foreach (var name in LayersBehind)
        {
            if (IndexOf(arr, name) >= 0) continue;
            Insert(arr, name, Mathf.Max(0, IndexOf(arr, "Default")), added);
        }
        foreach (var name in LayersFront)
        {
            if (IndexOf(arr, name) >= 0) continue;
            Insert(arr, name, arr.arraySize, added);
        }
        so.ApplyModifiedProperties();

        if (added.Count > 0)
        {
            AddLayersToLights(added);
            Debug.Log($"[SceneBuilder] Добавлены сортировочные слои: {string.Join(", ", LayersBehind.Concat(LayersFront))}");
        }
    }

    static int IndexOf(SerializedProperty arr, string name)
    {
        for (int i = 0; i < arr.arraySize; i++)
            if (arr.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == name) return i;
        return -1;
    }

    static void Insert(SerializedProperty arr, string name, int index, List<int> added)
    {
        index = Mathf.Clamp(index, 0, arr.arraySize);
        if (index == arr.arraySize) arr.arraySize++;
        else arr.InsertArrayElementAtIndex(index);

        var used = new HashSet<long>();
        for (int i = 0; i < arr.arraySize; i++)
            if (i != index) used.Add(arr.GetArrayElementAtIndex(i).FindPropertyRelative("uniqueID").longValue);
        var rnd = new System.Random();
        int id;
        do id = rnd.Next(1, int.MaxValue); while (used.Contains(id));

        var el = arr.GetArrayElementAtIndex(index);
        el.FindPropertyRelative("name").stringValue = name;
        el.FindPropertyRelative("uniqueID").longValue = id;
        var locked = el.FindPropertyRelative("locked");
        if (locked != null)
        {
            if (locked.propertyType == SerializedPropertyType.Boolean) locked.boolValue = false;
            else locked.intValue = 0;
        }
        added.Add(id);
    }

    // В URP 2D свет освещает только перечисленные сортировочные слои — добавляем новые,
    // иначе спрайты на новых слоях будут тёмными.
    static void AddLayersToLights(List<int> ids)
    {
        var lightType = TypeCache.GetTypesDerivedFrom<Behaviour>().FirstOrDefault(t => t.Name == "Light2D");
        if (lightType == null) return;
        foreach (var light in FindObjectsByType(lightType, FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var so = new SerializedObject(light);
            var arr = so.FindProperty("m_ApplyToSortingLayers");
            if (arr == null || !arr.isArray) continue;
            var existing = new HashSet<long>();
            for (int i = 0; i < arr.arraySize; i++) existing.Add(arr.GetArrayElementAtIndex(i).longValue);
            foreach (var id in ids)
            {
                if (existing.Contains(id)) continue;
                arr.arraySize++;
                arr.GetArrayElementAtIndex(arr.arraySize - 1).longValue = id;
            }
            so.ApplyModifiedProperties();
        }
    }

    // ================= 3. Планы =================

    void AssignToPlane(Plane p)
    {
        var selected = Selection.transforms;
        if (selected.Length == 0)
        {
            EditorUtility.DisplayDialog("Scene Builder", "Выделите объекты в Hierarchy.", "OK");
            return;
        }
        var root = FindRoot(create: false);
        var group = root != null ? root.Find(p.Group) : null;
        if (group == null)
        {
            EditorUtility.DisplayDialog("Scene Builder", "Сначала нажмите «Создать структуру».", "OK");
            return;
        }

        int renderers = 0;
        foreach (var t in selected)
        {
            if (t == group || t == root || group.IsChildOf(t)) continue;
            Undo.SetTransformParent(t, group, $"Move to {p.Group}");
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                Undo.RecordObject(r, "Set sorting layer");
                r.sortingLayerName = p.SortingLayer;
                renderers++;
            }
        }
        Debug.Log($"[SceneBuilder] В {p.Group}: объектов {selected.Length}, рендереров {renderers}, слой {p.SortingLayer}");
    }

    // ================= 4. Инструменты =================

    void FindScaled()
    {
        var root = FindRoot(create: false);
        IEnumerable<SpriteRenderer> srs = root != null
            ? root.GetComponentsInChildren<SpriteRenderer>(true)
            : FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        var bad = srs.Where(r =>
        {
            var s = r.transform.lossyScale;
            return Mathf.Abs(Mathf.Abs(s.x) - 1f) > 0.001f || Mathf.Abs(Mathf.Abs(s.y) - 1f) > 0.001f;
        }).Select(r => r.gameObject).ToArray();

        Selection.objects = bad;
        foreach (var g in bad) Debug.Log($"[SceneBuilder] Scale {g.transform.lossyScale}: {g.name}", g);
        EditorUtility.DisplayDialog("Scene Builder",
            bad.Length == 0 ? "Все объекты со Scale 1 — отлично." : $"Найдено {bad.Length}, они выделены. Список в Console.", "OK");
    }

    void ResetScale()
    {
        foreach (var t in Selection.transforms)
        {
            Undo.RecordObject(t, "Reset scale");
            var s = t.localScale;
            t.localScale = new Vector3(Mathf.Sign(s.x == 0 ? 1 : s.x), Mathf.Sign(s.y == 0 ? 1 : s.y), 1f); // отражение сохраняем
        }
    }

    void AlignRow()
    {
        var items = Selection.transforms
            .Select(t => (t, r: t.GetComponentInChildren<Renderer>()))
            .Where(x => x.r != null)
            .OrderBy(x => x.r.bounds.min.x)
            .ToList();
        if (items.Count < 2)
        {
            EditorUtility.DisplayDialog("Scene Builder", "Выделите минимум 2 куска со спрайтами.", "OK");
            return;
        }

        float overlapUnits = overlapPx / Mathf.Max(1f, targetPPU);
        var prev = items[0].r.bounds;
        for (int i = 1; i < items.Count; i++)
        {
            var (t, r) = items[i];
            var b = r.bounds;
            Undo.RecordObject(t, "Align row");
            var delta = new Vector3(prev.max.x - overlapUnits - b.min.x, alignBottoms ? items[0].r.bounds.min.y - b.min.y : 0f, 0f);
            t.position += delta;
            prev = new Bounds(b.center + delta, b.size);
        }
    }

    // ================= 5. Камера =================

    void SetCamera()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            EditorUtility.DisplayDialog("Scene Builder", "Не нашла камеру с тегом MainCamera.", "OK");
            return;
        }
        Undo.RecordObject(cam, "Set camera");
        cam.orthographic = true;
        cam.orthographicSize = orthoSize;
    }
}
#endif
