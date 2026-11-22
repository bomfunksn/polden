// HeroCalibrate.cs — выравнивает масштаб и точку опоры (ноги) кадров героя между анимациями.
// Временная заплатка, пока кадры не переэкспортированы на едином холсте.
// Положить в Assets/Editor/
// Меню:
//   Tools → Hero Calibrate → 1. Preview — измерить кадры и показать, что будет выставлено (ничего не меняет)
//   Tools → Hero Calibrate → 2. Apply   — выставить PPU и pivot
// ПЕРЕД Apply — коммит в git.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class HeroCalibrate
{
    // ================== НАСТРОЙКИ ==================

    static string HeroAnimFolder = "Assets/Graphics/Player/Animation";

    // Эталонная анимация: под её масштаб подгоняются остальные
    static string ReferenceFolder = "idle";
    static float ReferencePPU = 500f;

    // Порог прозрачности: пиксели прозрачнее этого не считаются частью персонажа (0..255)
    static byte AlphaThreshold = 40;

    // Ручная подстройка масштаба. Скрипт оценивает масштаб по росту персонажа,
    // но в беге она наклонена, поэтому оценка может выйти чуть меньше нужного.
    // Если после Apply бег всё ещё мельче или крупнее — впишите сюда множитель
    // и запустите Apply ещё раз. Например { "run", 0.9f } — бег станет на ~10% крупнее
    // (меньше PPU = крупнее спрайт).
    static Dictionary<string, float> PpuMultiplier = new Dictionary<string, float>
    {
        // { "run", 0.9f },
    };

    // ===============================================

    class Frame { public string Path; public int W, H, Top, Bottom; public bool Multiple; }

    class AnimInfo
    {
        public string Name;
        public List<Frame> Frames = new List<Frame>();
        public int CanvasH, CanvasW, MaxHeight, FeetY; // FeetY — от низа холста
        public float PPU, PivotY;
        public bool HasMultiple;
    }

    [MenuItem("Tools/Hero Calibrate/1. Preview (nothing changes)")]
    static void Preview()
    {
        var anims = Measure();
        if (anims == null) return;
        var text = Report(anims);
        Debug.Log(text);
        EditorUtility.DisplayDialog("Hero Calibrate — предпросмотр", text, "OK");
    }

    [MenuItem("Tools/Hero Calibrate/2. Apply")]
    static void Apply()
    {
        var anims = Measure();
        if (anims == null) return;
        var text = Report(anims);
        if (!EditorUtility.DisplayDialog("Hero Calibrate", text + "\nПрименить? Коммит в git сделан?", "Применить", "Отмена"))
            return;

        int changed = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var a in anims)
                foreach (var f in a.Frames.Where(f => !f.Multiple))
                {
                    var ti = AssetImporter.GetAtPath(f.Path) as TextureImporter;
                    if (ti == null) continue;
                    var s = new TextureImporterSettings();
                    ti.ReadTextureSettings(s);
                    s.spritePixelsPerUnit = a.PPU;
                    s.spriteAlignment = (int)SpriteAlignment.Custom;
                    s.spritePivot = new Vector2(0.5f, a.PivotY);
                    ti.SetTextureSettings(s);
                    EditorUtility.SetDirty(ti);
                    ti.SaveAndReimport();
                    changed++;
                }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
        AssetDatabase.Refresh();
        Debug.Log($"[HeroCalibrate] Изменено кадров: {changed}\n{text}");
        EditorUtility.DisplayDialog("Hero Calibrate",
            $"Готово, изменено кадров: {changed}.\n\nТеперь ноги героя — в точке объекта. " +
            "Программисту нужно один раз сдвинуть коллайдер (Offset), чтобы его низ совпал с ногами.", "OK");
    }

    // ---------------------------------------------------------------

    static List<AnimInfo> Measure()
    {
        if (!AssetDatabase.IsValidFolder(HeroAnimFolder))
        {
            EditorUtility.DisplayDialog("Hero Calibrate", $"Нет папки {HeroAnimFolder}", "OK");
            return null;
        }

        var anims = new List<AnimInfo>();
        try
        {
            foreach (var folder in AssetDatabase.GetSubFolders(HeroAnimFolder))
            {
                var a = new AnimInfo { Name = System.IO.Path.GetFileName(folder) };
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }).Distinct())
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') != folder) continue;
                    EditorUtility.DisplayProgressBar("Hero Calibrate", path, 0.5f);
                    var f = MeasureFrame(path);
                    if (f != null) a.Frames.Add(f);
                }
                if (a.Frames.Count == 0) continue;

                a.CanvasH = a.Frames.Max(f => f.H);
                a.CanvasW = a.Frames.Max(f => f.W);
                a.MaxHeight = a.Frames.Max(f => f.Top - f.Bottom);
                a.FeetY = a.Frames.Min(f => f.Bottom); // самая низкая точка — касание земли
                a.HasMultiple = a.Frames.Any(f => f.Multiple);
                a.PivotY = a.CanvasH > 0 ? (float)a.FeetY / a.CanvasH : 0f;
                anims.Add(a);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        var reference = anims.FirstOrDefault(a => string.Equals(a.Name, ReferenceFolder, StringComparison.OrdinalIgnoreCase));
        if (reference == null)
        {
            EditorUtility.DisplayDialog("Hero Calibrate", $"Не нашла эталонную папку '{ReferenceFolder}' внутри {HeroAnimFolder}", "OK");
            return null;
        }

        foreach (var a in anims)
        {
            float ppu = ReferencePPU * a.MaxHeight / Mathf.Max(1, reference.MaxHeight);
            var key = PpuMultiplier.Keys.FirstOrDefault(k => string.Equals(k, a.Name, StringComparison.OrdinalIgnoreCase));
            if (key != null) ppu *= PpuMultiplier[key];
            a.PPU = Mathf.Round(ppu);
        }
        return anims;
    }

    // Читает исходный PNG с диска в полном разрешении (не трогая настройки импорта)
    static Frame MeasureFrame(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return null;

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!tex.LoadImage(File.ReadAllBytes(path))) return null;
            var px = tex.GetPixels32();
            int w = tex.width, h = tex.height, top = -1, bottom = -1;

            // строки в GetPixels32 идут снизу вверх
            for (int y = 0; y < h && bottom < 0; y++)
                for (int x = 0; x < w; x++)
                    if (px[y * w + x].a >= AlphaThreshold) { bottom = y; break; }
            for (int y = h - 1; y >= 0 && top < 0; y--)
                for (int x = 0; x < w; x++)
                    if (px[y * w + x].a >= AlphaThreshold) { top = y; break; }
            if (top < 0) return null;

            return new Frame
            {
                Path = path, W = w, H = h, Top = top, Bottom = bottom,
                Multiple = ti.spriteImportMode == SpriteImportMode.Multiple
            };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }

    static string Report(List<AnimInfo> anims)
    {
        var sb = new StringBuilder();
        foreach (var a in anims.OrderBy(a => a.Name))
        {
            sb.AppendLine($"{a.Name}: кадров {a.Frames.Count}, холст {a.CanvasW}×{a.CanvasH}, " +
                          $"рост {a.MaxHeight} px, ноги {a.FeetY} px от низа");
            sb.AppendLine($"   → PPU {a.PPU}, pivot (0.5, {a.PivotY:0.000})");
            if (a.HasMultiple)
                sb.AppendLine($"   ⚠ {a.Frames.Count(f => f.Multiple)} кадр(ов) в режиме Multiple — их пропускаю, pivot вручную в Sprite Editor");
            if (a.Frames.Select(f => f.H).Distinct().Count() > 1)
                sb.AppendLine("   ⚠ кадры внутри анимации разной высоты — проверьте экспорт");
        }
        return sb.ToString();
    }
}
#endif
