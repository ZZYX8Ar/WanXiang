// ============================================================================
//  WanXiang · 美术资源导入工具
//  ---------------------------------------------------------------------------
//  菜单：WanXiang / 美术 / 导入并设置美术资源
//
//  做的事（全部幂等，可反复执行）：
//    1. 把 Assets/ArtRes 下所有 png 设成 Sprite：无 mipmap、双线性、开 alpha 透明、
//       关 sRGB 之外的压缩噪点；异兽 pivot 居中、九宫格件按短边 34% 写 spriteBorder
//    2. 把 30 只异兽立绘灌进 Assets/WanXiang/Config/SpriteCatalog.asset（key = 拼音 id）
//    3. 报告数量，便于和美术方案的账目核对
//
//  为什么边界值取短边 34%：这批九宫格件的中段是纯色可拉伸区，四角是纹样。
//  34% 是"四角纹样刚好包住、中段还留得住"的经验值，改图后可在 Sprite Editor 里微调。
// ============================================================================

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using WanXiang.Battle.Presentation;

namespace WanXiang.EditorTools
{
    public static class ArtPipelineTool
    {
        private const string ArtRoot = "Assets/ArtRes";
        private const string HeadsDir = "Assets/ArtRes/UI/Heads";
        private const string CatalogPath = "Assets/WanXiang/Config/SpriteCatalog.asset";

        /// <summary>需要九宫格切片的组件（与美术方案 Chapter 07 的 NINE_SLICE 一致）。</summary>
        private static readonly HashSet<string> NineSlice = new HashSet<string>
        {
            "btn_primary", "btn_secondary", "panel_dialog", "panel_title", "bar_hp", "bar_hp2",
            "toast", "listitem", "slot_card", "tab_bar", "divider",
            "banner_win", "banner_lose", "skill_banner_normal", "skill_banner_ultimate",
        };

        [MenuItem("WanXiang/美术/导入并设置美术资源", priority = 200)]
        public static void Run()
        {
            int tex = ConfigureImporters();
            int beasts = FillSpriteCatalog();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ArtPipeline] 导入设置完成：贴图 {tex} 张，立绘目录灌入 {beasts} 只。");
        }

        // ================================================================
        //  1. 导入设置
        // ================================================================

        private static int ConfigureImporters()
        {
            if (!AssetDatabase.IsValidFolder(ArtRoot))
            {
                Debug.LogError($"[ArtPipeline] 找不到 {ArtRoot}，先跑一遍 万相_美术资源/process_art.py。");
                return 0;
            }

            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { ArtRoot });
            int n = 0;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                if (!path.EndsWith(".png")) continue;

                bool isSliced = NineSlice.Contains(Path.GetFileNameWithoutExtension(path));

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.maxTextureSize = path.Contains("/Beasts/") ? 1024 : 2048;

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.Tight;
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                settings.spritePixelsPerUnit = 100f;
                settings.spriteExtrude = 1;
                if (isSliced)
                {
                    // 34% 短边 —— 与 process_art.py 的 suggest_border 同一公式
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (tex != null)
                    {
                        float shortSide = Mathf.Min(tex.width, tex.height);
                        float b = Mathf.Round(shortSide * 0.34f);
                        settings.spriteBorder = new Vector4(b, b, b, b);
                    }
                    settings.spriteAlignment = (int)SpriteAlignment.Center;
                }
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
                n++;
            }
            return n;
        }

        // ================================================================
        //  2. 灌立绘目录
        // ================================================================

        private static int FillSpriteCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SpriteCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError($"[ArtPipeline] 找不到立绘目录资产：{CatalogPath}");
                return 0;
            }

            var dir = Path.Combine(ArtRoot, "Beasts");
            if (!Directory.Exists(dir)) return 0;

            catalog.Entries.Clear();
            var files = Directory.GetFiles(dir, "*.png");
            System.Array.Sort(files, System.StringComparer.Ordinal);
            foreach (var abs in files)
            {
                string id = Path.GetFileNameWithoutExtension(abs);
                string assetPath = ArtRoot + "/Beasts/" + id + ".png";
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (sprite == null) continue;
                var head = AssetDatabase.LoadAssetAtPath<Sprite>(HeadsDir + "/" + id + ".png");
                catalog.Entries.Add(new SpriteCatalog.Entry { Id = id, Body = sprite, Head = head });
            }
            catalog.Rebuild();
            EditorUtility.SetDirty(catalog);
            return catalog.Entries.Count;
        }

        // ================================================================
        //  3. 自检报告
        // ================================================================

        [MenuItem("WanXiang/美术/核对美术账目", priority = 201)]
        public static void Report()
        {
            var lines = new List<string>();
            foreach (var sub in new[] { "Beasts", "UI/Parts", "BattleBg", "Screens" })
            {
                var abs = Path.Combine(ArtRoot, sub);
                int c = Directory.Exists(abs) ? Directory.GetFiles(abs, "*.png").Length : 0;
                lines.Add($"{sub,-12} {c,4} 张");
            }
            var catalog = AssetDatabase.LoadAssetAtPath<SpriteCatalog>(CatalogPath);
            lines.Add($"立绘目录     {(catalog != null ? catalog.Entries.Count : 0),4} 只");
            Debug.Log("[ArtPipeline] 美术账目：\n" + string.Join("\n", lines));
        }
    }
}
