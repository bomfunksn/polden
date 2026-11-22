// SpriteAudit.cs — аудит картинок в проекте. Только ЧИТАЕТ, ничего не меняет.
// Положить в любую папку Editor внутри Assets, например Assets/Editor/SpriteAudit.cs
// Меню: Tools → Sprite Audit → Whole project / Selected folders
// Результат: CSV в корне проекта (рядом с папкой Assets), откроется в проводнике.
// Нужен Unity 2021.2 или новее.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

public static class SpriteAudit
{
    [MenuItem("Tools/Sprite Audit/Whole project")]
    static void AuditAll() => Run(new[] { "Assets" });

    [MenuItem("Tools/Sprite Audit/Selected folders")]
    static void AuditSelected()
    {
        var folders = Selection.GetFiltered<DefaultAsset>(SelectionMode.Assets)
            .Select(AssetDatabase.GetAssetPath)
            .Where(AssetDatabase.IsValidFolder)
            .ToArray();
        if (folders.Length == 0)
        {
            EditorUtility.DisplayDialog("Sprite Audit", "Выделите одну или несколько папок в окне Project.", "OK");
            return;
        }
        Run(folders);
    }

    class Row
    {
        public string Path, Folder, Name, Ext, Importer, TextureType, SpriteMode, MeshType, Filter, Wrap,
            Compression, Format, Used, Atlases, Pivot;
        public int SrcW, SrcH, ImpW, ImpH, MaxSize, SpriteCount;
        public float PPU;
        public bool PhysicsShape, MipMaps, AlphaIsTransp, HasAlpha, ReadWrite, Crunch, PcOverride, IsSprite;
        public long FileKB;
        public List<string> Issues = new List<string>();
    }

    static readonly Regex BadName = new Regex(@"[^A-Za-z0-9_\-\.]");

    static void Run(string[] folders)
    {
        try
        {
            EditorUtility.DisplayProgressBar("Sprite Audit", "Ищу, где используются ассеты…", 0f);

            // 1. Что на что ссылается: сцены, префабы, анимации, контроллеры, материалы, .asset (тайлы, SO)
            string[] userExt = { ".unity", ".prefab", ".anim", ".controller", ".overrideController", ".asset", ".mat" };
            var users = AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/") && userExt.Any(e => p.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            var used = new HashSet<string>(AssetDatabase.GetDependencies(users, true));

            // 2. Какие атласы что упаковывают
            var atlasEntries = new List<(string atlas, string path, bool folder)>();
            foreach (var g in AssetDatabase.FindAssets("t:SpriteAtlas"))
            {
                try
                {
                    var ap = AssetDatabase.GUIDToAssetPath(g);
                    var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(ap);
                    if (atlas == null) continue;
                    foreach (var o in atlas.GetPackables())
                    {
                        var p = AssetDatabase.GetAssetPath(o);
                        if (!string.IsNullOrEmpty(p)) atlasEntries.Add((atlas.name, p, AssetDatabase.IsValidFolder(p)));
                    }
                }
                catch (Exception e) { Debug.LogWarning($"[SpriteAudit] Не прочитала атлас: {e.Message}"); }
            }
            bool anyAtlas = atlasEntries.Count > 0;

            // 3. Сами картинки
            var guids = AssetDatabase.FindAssets("t:Texture2D", folders).Distinct().ToArray();
            var rows = new List<Row>();
            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!path.StartsWith("Assets/") || AssetDatabase.IsValidFolder(path)) continue;
                if (i % 20 == 0)
                    EditorUtility.DisplayProgressBar("Sprite Audit", path, (float)i / guids.Length);

                var r = new Row
                {
                    Path = path,
                    Folder = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/'),
                    Name = System.IO.Path.GetFileNameWithoutExtension(path),
                    Ext = System.IO.Path.GetExtension(path).ToLowerInvariant(),
                };
                try { r.FileKB = new FileInfo(path).Length / 1024; } catch { }

                var importer = AssetImporter.GetAtPath(path);
                r.Importer = importer != null ? importer.GetType().Name : "?";
                var ti = importer as TextureImporter;

                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex != null) { r.ImpW = tex.width; r.ImpH = tex.height; }

                if (ti != null)
                {
                    ti.GetSourceTextureWidthAndHeight(out r.SrcW, out r.SrcH);
                    var s = new TextureImporterSettings();
                    ti.ReadTextureSettings(s);

                    r.TextureType = ti.textureType.ToString();
                    r.IsSprite = ti.textureType == TextureImporterType.Sprite;
                    r.SpriteMode = ti.spriteImportMode.ToString();
                    r.PPU = ti.spritePixelsPerUnit;
                    r.MeshType = s.spriteMeshType.ToString();
                    r.PhysicsShape = s.spriteGenerateFallbackPhysicsShape;
                    r.Pivot = ti.spriteImportMode == SpriteImportMode.Single
                        ? (s.spriteAlignment == (int)SpriteAlignment.Custom
                            ? $"Custom({s.spritePivot.x:0.##},{s.spritePivot.y:0.##})"
                            : ((SpriteAlignment)s.spriteAlignment).ToString())
                        : "";
                    r.Filter = ti.filterMode.ToString();
                    r.Wrap = ti.wrapMode.ToString();
                    r.MipMaps = ti.mipmapEnabled;
                    r.AlphaIsTransp = ti.alphaIsTransparency;
                    r.HasAlpha = ti.DoesSourceTextureHaveAlpha();
                    r.ReadWrite = ti.isReadable;

                    var def = ti.GetDefaultPlatformTextureSettings();
                    r.MaxSize = def.maxTextureSize;
                    r.Compression = def.textureCompression.ToString();
                    r.Format = def.format.ToString();
                    r.Crunch = def.crunchedCompression;
                    r.PcOverride = ti.GetPlatformTextureSettings("Standalone").overridden;
                }
                else
                {
                    r.SrcW = r.ImpW; r.SrcH = r.ImpH;
                    r.TextureType = "(не TextureImporter)";
                }

                r.SpriteCount = AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<Sprite>().Count();

                bool inResources = path.Contains("/Resources/");
                r.Used = used.Contains(path) ? "yes" : inResources ? "Resources" : "no";

                r.Atlases = string.Join(", ", atlasEntries
                    .Where(a => a.folder ? path.StartsWith(a.path + "/") : a.path == path)
                    .Select(a => a.atlas).Distinct());

                rows.Add(r);
                if (i % 200 == 0) EditorUtility.UnloadUnusedAssetsImmediate();
            }

            // 4. Основной PPU = самый частый среди спрайтов
            float mainPPU = rows.Where(r => r.IsSprite)
                .GroupBy(r => r.PPU).OrderByDescending(g => g.Count())
                .Select(g => g.Key).DefaultIfEmpty(100f).First();

            // 5. Проблемы
            foreach (var r in rows)
            {
                if (r.SrcW % 4 != 0 || r.SrcH % 4 != 0) r.Issues.Add("размер не кратен 4");
                if (r.MaxSize > 0 && Math.Max(r.SrcW, r.SrcH) > r.MaxSize)
                    r.Issues.Add($"уменьшено MaxSize ({Math.Max(r.SrcW, r.SrcH)}→{r.MaxSize})");
                if (Math.Max(r.SrcW, r.SrcH) > 4096) r.Issues.Add("больше 4096 px");
                if (BadName.IsMatch(r.Name)) r.Issues.Add("имя: кириллица/пробелы/спецсимволы");
                if (r.Used == "no") r.Issues.Add("нигде не используется?");
                if (r.TextureType != null && r.TextureType != "Sprite" && r.TextureType != "(не TextureImporter)")
                    r.Issues.Add("не Sprite");
                if (r.IsSprite)
                {
                    if (Math.Abs(r.PPU - mainPPU) > 0.01f) r.Issues.Add($"PPU {r.PPU} ≠ основной {mainPPU}");
                    if (r.PhysicsShape) r.Issues.Add("Physics Shape вкл.");
                    if (r.MipMaps) r.Issues.Add("MipMaps вкл.");
                    if (r.HasAlpha && !r.AlphaIsTransp) r.Issues.Add("Alpha Is Transparency выкл.");
                    if (r.ReadWrite) r.Issues.Add("Read/Write вкл.");
                    if (r.SpriteMode == "Multiple" && r.SpriteCount <= 1) r.Issues.Add("Multiple, но 1 спрайт");
                    if (anyAtlas && string.IsNullOrEmpty(r.Atlases)) r.Issues.Add("не в атласе");
                }
            }

            // 6. CSV (разделитель ;, UTF-8 с BOM — нормально открывается в Excel и Google Sheets)
            string[] header =
            {
                "Path", "Folder", "Name", "Ext", "FileKB", "SrcW", "SrcH", "ImportedW", "ImportedH",
                "Importer", "TextureType", "SpriteMode", "SpriteCount", "PPU", "Pivot", "MeshType", "PhysicsShape",
                "Filter", "Wrap", "MipMaps", "HasAlpha", "AlphaIsTransparency", "ReadWrite",
                "MaxSize", "Compression", "Format", "Crunch", "PCOverride", "Used", "Atlases", "IssueCount", "Issues"
            };
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(";", header));
            foreach (var r in rows.OrderBy(r => r.Path))
            {
                object[] v =
                {
                    r.Path, r.Folder, r.Name, r.Ext, r.FileKB, r.SrcW, r.SrcH, r.ImpW, r.ImpH,
                    r.Importer, r.TextureType, r.SpriteMode, r.SpriteCount, r.PPU, r.Pivot, r.MeshType, r.PhysicsShape,
                    r.Filter, r.Wrap, r.MipMaps, r.HasAlpha, r.AlphaIsTransp, r.ReadWrite,
                    r.MaxSize, r.Compression, r.Format, r.Crunch, r.PcOverride, r.Used, r.Atlases,
                    r.Issues.Count, string.Join(" | ", r.Issues)
                };
                sb.AppendLine(string.Join(";", v.Select(Csv)));
            }

            var root = Directory.GetParent(Application.dataPath).FullName;
            var file = System.IO.Path.Combine(root, $"SpriteAudit_{DateTime.Now:yyyyMMdd_HHmm}.csv");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));

            // 7. Сводка в Console
            var summary = new StringBuilder();
            summary.AppendLine($"[SpriteAudit] Картинок: {rows.Count}. Основной PPU: {mainPPU}. Атласов найдено: {atlasEntries.Select(a => a.atlas).Distinct().Count()}.");
            foreach (var g in rows.SelectMany(r => r.Issues.Select(x => Regex.Replace(x, @"\(.*\)|PPU [\d\.,]+", "PPU")))
                         .GroupBy(x => x).OrderByDescending(g => g.Count()))
                summary.AppendLine($"  {g.Key}: {g.Count()}");
            summary.AppendLine($"Файл: {file}");
            Debug.Log(summary.ToString());

            EditorUtility.RevealInFinder(file);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    static string Csv(object o)
    {
        var s = o switch
        {
            null => "",
            bool b => b ? "1" : "0",
            float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => o.ToString()
        };
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
#endif
