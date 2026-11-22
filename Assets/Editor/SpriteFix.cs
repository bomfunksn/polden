// SpriteFix.cs — массовая починка настроек импорта спрайтов.
// Положить в Assets/Editor/ рядом с SpriteAudit.cs
// Меню:
//   Tools → Sprite Fix → 1. Preview  — только показывает, что будет изменено (ничего не трогает)
//   Tools → Sprite Fix → 2. Apply    — применяет после подтверждения
// Undo для настроек импорта в Unity нет — ПЕРЕД Apply сделайте коммит в git.
// Нужен Unity 2021.2 или новее.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class SpriteFix
{
    // ================== НАСТРОЙКИ ==================

    // Где искать картинки
    static string ScanRoot = "Assets/Graphics";

    // --- Безопасные правки: не меняют размер и положение объектов в сценах ---
    static bool FixPhysicsShape = true;          // Generate Physics Shape → выкл.
    static bool FixMipReadAlpha = true;          // MipMaps выкл., Read/Write выкл., Alpha Is Transparency вкл.
    static bool UnusedMultipleToSingle = true;   // Multiple → Single, ТОЛЬКО если 1 спрайт и картинка нигде не используется
    static bool RenameHeroFrames = true;         // "Прыжок-3 3.png" → "hero_jump_03.png" (ссылки сохраняются)
    static string HeroAnimFolder = "Assets/Graphics/Player/Animation";
    static string HeroPrefix = "hero";

    // --- ОСТОРОЖНО: меняют размер/положение объектов в сценах. ---
    // Включать только вместе с программистом, когда будете заново выставлять объекты в сценах.
    static bool UnifyPPU = false;                // всем спрайтам одинаковый PPU
    static float TargetPPU = 300f;
    static bool HeroPivotBottom = false;         // pivot кадров героя → низ по центру (ноги)

    // ===============================================

    class ImportPlan
    {
        public string Path;
        public List<string> What = new List<string>();
        public List<Action<TextureImporterSettings>> Acts = new List<Action<TextureImporterSettings>>();
    }

    class RenamePlan { public string Path, NewName; }

    [MenuItem("Tools/Sprite Fix/1. Preview (nothing changes)")]
    static void Preview()
    {
        var imports = new List<ImportPlan>(); var renames = new List<RenamePlan>(); var manual = new List<string>();
        try { BuildPlan(imports, renames, manual); }
        finally { EditorUtility.ClearProgressBar(); }

        var file = WriteReport("preview", imports, renames, manual);
        EditorUtility.DisplayDialog("Sprite Fix — предпросмотр",
            $"Изменить настройки импорта: {imports.Count} файлов\n" +
            $"Переименовать: {renames.Count}\n" +
            $"Проверить вручную: {manual.Count}\n\n" +
            $"Ничего не изменено. Подробности в Console и в файле:\n{System.IO.Path.GetFileName(file)}",
            "OK");
        EditorUtility.RevealInFinder(file);
    }

    [MenuItem("Tools/Sprite Fix/2. Apply")]
    static void Apply()
    {
        var imports = new List<ImportPlan>(); var renames = new List<RenamePlan>(); var manual = new List<string>();
        try { BuildPlan(imports, renames, manual); }
        finally { EditorUtility.ClearProgressBar(); }

        if (imports.Count == 0 && renames.Count == 0)
        {
            EditorUtility.DisplayDialog("Sprite Fix", "Менять нечего — всё уже в порядке.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Sprite Fix",
                $"Изменить настройки импорта у {imports.Count} файлов и переименовать {renames.Count}?\n\n" +
                "Отменить через Ctrl+Z будет нельзя. Коммит в git сделан?",
                "Применить", "Отмена"))
            return;

        // 1. Настройки импорта
        try
        {
            AssetDatabase.StartAssetEditing();
            for (int i = 0; i < imports.Count; i++)
            {
                var p = imports[i];
                EditorUtility.DisplayProgressBar("Sprite Fix", p.Path, (float)i / imports.Count);
                var ti = AssetImporter.GetAtPath(p.Path) as TextureImporter;
                if (ti == null) continue;
                var s = new TextureImporterSettings();
                ti.ReadTextureSettings(s);
                foreach (var act in p.Acts) act(s);
                ti.SetTextureSettings(s);
                EditorUtility.SetDirty(ti);
                ti.SaveAndReimport();
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
        }

        // 2. Переименования (через AssetDatabase — GUID и ссылки сохраняются)
        foreach (var r in renames)
        {
            var err = AssetDatabase.RenameAsset(r.Path, r.NewName);
            if (!string.IsNullOrEmpty(err)) manual.Add($"{r.Path}: не удалось переименовать — {err}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var file = WriteReport("applied", imports, renames, manual);
        EditorUtility.DisplayDialog("Sprite Fix", $"Готово. Отчёт: {System.IO.Path.GetFileName(file)}", "OK");
    }

    // ---------------------------------------------------------------

    static void BuildPlan(List<ImportPlan> imports, List<RenamePlan> renames, List<string> manual)
    {
        EditorUtility.DisplayProgressBar("Sprite Fix", "Ищу, где используются ассеты…", 0f);
        var used = UsedAssets();

        var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { ScanRoot }).Distinct().ToArray();
        for (int i = 0; i < guids.Length; i++)
        {
            var path = AssetDatabase.GUIDToAssetPath(guids[i]);
            EditorUtility.DisplayProgressBar("Sprite Fix", path, (float)i / guids.Length);

            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null || ti.textureType != TextureImporterType.Sprite) continue;

            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            var p = new ImportPlan { Path = path };
            bool isUsed = used.Contains(path);
            bool hero = path.StartsWith(HeroAnimFolder + "/");
            int spriteCount = AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<Sprite>().Count();
            bool multiple = ti.spriteImportMode == SpriteImportMode.Multiple;

            if (FixPhysicsShape && s.spriteGenerateFallbackPhysicsShape)
            {
                p.What.Add("Physics Shape → выкл.");
                p.Acts.Add(x => x.spriteGenerateFallbackPhysicsShape = false);
            }

            if (FixMipReadAlpha)
            {
                if (s.mipmapEnabled) { p.What.Add("MipMaps → выкл."); p.Acts.Add(x => x.mipmapEnabled = false); }
                if (s.readable) { p.What.Add("Read/Write → выкл."); p.Acts.Add(x => x.readable = false); }
                if (!s.alphaIsTransparency && ti.DoesSourceTextureHaveAlpha())
                { p.What.Add("Alpha Is Transparency → вкл."); p.Acts.Add(x => x.alphaIsTransparency = true); }
            }

            if (multiple && spriteCount <= 1)
            {
                if (UnusedMultipleToSingle && !isUsed)
                {
                    p.What.Add("Multiple → Single");
                    p.Acts.Add(x => x.spriteMode = (int)SpriteImportMode.Single);
                    multiple = false;
                }
                else if (isUsed)
                {
                    manual.Add($"{path}: Multiple с 1 спрайтом, но стоит в сцене — не трогаю (иначе слетит ссылка). Можно оставить как есть.");
                }
            }

            if (multiple && spriteCount > 1 && hero)
                manual.Add($"{path}: нарезано {spriteCount} спрайтов — проверить в Sprite Editor, похоже на лишнюю автонарезку");

            if (UnifyPPU && Math.Abs(s.spritePixelsPerUnit - TargetPPU) > 0.01f)
            {
                p.What.Add($"PPU {s.spritePixelsPerUnit} → {TargetPPU}");
                p.Acts.Add(x => x.spritePixelsPerUnit = TargetPPU);
            }

            if (HeroPivotBottom && hero)
            {
                if (multiple)
                    manual.Add($"{path}: Multiple — pivot Bottom выставить вручную в Sprite Editor");
                else if (s.spriteAlignment != (int)SpriteAlignment.BottomCenter)
                {
                    p.What.Add("Pivot → Bottom");
                    p.Acts.Add(x => x.spriteAlignment = (int)SpriteAlignment.BottomCenter);
                }
            }

            if (p.Acts.Count > 0) imports.Add(p);
        }

        if (RenameHeroFrames && AssetDatabase.IsValidFolder(HeroAnimFolder))
            PlanHeroRenames(renames, manual);
    }

    static void PlanHeroRenames(List<RenamePlan> renames, List<string> manual)
    {
        var targets = new Dictionary<string, List<string>>();
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { HeroAnimFolder }).Distinct())
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var dir = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (dir == null || dir == HeroAnimFolder) continue; // кадры должны лежать в подпапках анимаций

            var anim = Regex.Replace(System.IO.Path.GetFileName(dir).ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(anim))
            {
                manual.Add($"{path}: папка анимации названа не латиницей — переименуйте папку, потом запустите снова");
                continue;
            }

            var m = Regex.Match(name, @"-(\d+)");
            if (!m.Success)
            {
                if (!Regex.IsMatch(name, $"^{HeroPrefix}_{anim}_\\d+$"))
                    manual.Add($"{path}: не нашла номер кадра в имени — переименовать вручную");
                continue;
            }

            var newName = $"{HeroPrefix}_{anim}_{int.Parse(m.Groups[1].Value):00}";
            if (newName == name) continue;
            var newPath = $"{dir}/{newName}{System.IO.Path.GetExtension(path)}";
            if (!targets.TryGetValue(newPath, out var list)) targets[newPath] = list = new List<string>();
            list.Add(path);
        }

        foreach (var kv in targets)
        {
            if (kv.Value.Count > 1)
                manual.Add($"Конфликт: {string.Join(" и ", kv.Value)} → оба стали бы {System.IO.Path.GetFileName(kv.Key)}. Переименуйте вручную.");
            else if (File.Exists(kv.Key))
                manual.Add($"{kv.Value[0]}: файл {System.IO.Path.GetFileName(kv.Key)} уже существует — переименуйте вручную");
            else
                renames.Add(new RenamePlan { Path = kv.Value[0], NewName = System.IO.Path.GetFileNameWithoutExtension(kv.Key) });
        }
    }

    static HashSet<string> UsedAssets()
    {
        string[] userExt = { ".unity", ".prefab", ".anim", ".controller", ".overrideController", ".asset", ".mat" };
        var users = AssetDatabase.GetAllAssetPaths()
            .Where(p => p.StartsWith("Assets/") && userExt.Any(e => p.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var used = new HashSet<string>(AssetDatabase.GetDependencies(users, true));
        foreach (var p in AssetDatabase.GetAllAssetPaths().Where(p => p.Contains("/Resources/"))) used.Add(p);
        return used;
    }

    static string WriteReport(string kind, List<ImportPlan> imports, List<RenamePlan> renames, List<string> manual)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Path;Type;Change");
        foreach (var p in imports.OrderBy(p => p.Path))
            foreach (var w in p.What) sb.AppendLine($"{Q(p.Path)};{Q("import")};{Q(w)}");
        foreach (var r in renames.OrderBy(r => r.Path))
            sb.AppendLine($"{Q(r.Path)};{Q("rename")};{Q("→ " + r.NewName)}");
        foreach (var m in manual)
            sb.AppendLine($"{Q("")};{Q("manual")};{Q(m)}");

        var root = Directory.GetParent(Application.dataPath).FullName;
        var file = System.IO.Path.Combine(root, $"SpriteFix_{kind}_{DateTime.Now:yyyyMMdd_HHmm}.csv");
        File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));

        var log = new StringBuilder();
        log.AppendLine($"[SpriteFix] {kind}: настройки — {imports.Count} файлов, переименований — {renames.Count}, вручную — {manual.Count}");
        foreach (var g in imports.SelectMany(p => p.What).Select(w => Regex.Replace(w, @"PPU .*", "PPU"))
                     .GroupBy(w => w).OrderByDescending(g => g.Count()))
            log.AppendLine($"  {g.Key}: {g.Count()}");
        foreach (var r in renames) log.AppendLine($"  rename: {System.IO.Path.GetFileName(r.Path)} → {r.NewName}");
        foreach (var m in manual) log.AppendLine($"  вручную: {m}");
        log.AppendLine($"Файл: {file}");
        Debug.Log(log.ToString());
        return file;
    }

    static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
}
#endif
