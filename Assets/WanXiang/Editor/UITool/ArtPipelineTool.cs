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
using WanXiang.Campaign;

namespace WanXiang.EditorTools
{
    public static class ArtPipelineTool
    {
        private const string ArtRoot = "Assets/ArtRes";
        private const string HeadsDir = "Assets/ArtRes/UI/Heads";
        private const string CatalogPath = "Assets/WanXiang/Config/SpriteCatalog.asset";
        private const string RelicArtDir = "Assets/ArtRes/Relics";
        private const string RelicCatalogPath = "Assets/Resources/RelicSpriteCatalog.asset";

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
            int relics = FillRelicSpriteCatalog();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ArtPipeline] 导入设置完成：贴图 {tex} 张，立绘目录灌入 {beasts} 只，遗物图 {relics} 张。");
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
                importer.maxTextureSize = path.Contains("/Beasts/") ? 1024 : path.Contains("/Relics/") ? 512 : 2048;

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
            // ⚠ 必须递归：立绘按五行分在 Beasts/Wood|Fire|Earth|Metal|Water 子目录里，
            //   非递归扫描会一张都扫不到 ， SpriteCatalog 被清空 ， 战斗里全是色块。
            var files = Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories);
            System.Array.Sort(files, System.StringComparer.Ordinal);
            foreach (var abs in files)
            {
                string id = Path.GetFileNameWithoutExtension(abs);
                // 用 FindFiles 返回的路径直接定位资产。
                // ⚠ 两个坑：① 传相对目录时 GetFiles 返回的也是相对路径（"Assets/..."），
                //   所以不能按 "/Assets/" 找（带前置斜杠匹配不到，会静默回退到写死路径而全部 miss）；
                //   ② 反斜杠要统一成正斜杠，AssetDatabase 只认正斜杠。
                string rel = abs.Replace('\\', '/');
                int assetsIdx = rel.IndexOf("Assets/", System.StringComparison.Ordinal);
                string assetPath = assetsIdx >= 0 ? rel.Substring(assetsIdx) : ArtRoot + "/Beasts/" + id + ".png";
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
        //  2b. 灌遗物图目录
        // ================================================================

        /// <summary>
        /// 扫描 Assets/ArtRes/Relics/*.png，把中文图名解析成 RelicDef.Id 灌入 RelicSpriteCatalog。
        /// 解析优先级：精确名 → 兽纹(r_mark_) → 克星(r_foe_*) → 元素符(r_elem_*) → 角色契印(r_role_*) → 别名表。
        /// 解析不到的图（疑似用户正在做的新遗物）只告警、不报错，且不会进目录。
        /// </summary>
        private static int FillRelicSpriteCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<RelicSpriteCatalog>(RelicCatalogPath);
            if (catalog == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                    AssetDatabase.CreateFolder("Assets", "Resources");
                catalog = ScriptableObject.CreateInstance<RelicSpriteCatalog>();
                AssetDatabase.CreateAsset(catalog, RelicCatalogPath);
            }

            if (!AssetDatabase.IsValidFolder(RelicArtDir))
            {
                Debug.LogWarning($"[ArtPipeline] 找不到 {RelicArtDir}，跳过遗物图灌入。");
                return 0;
            }

            // 别名表：图名无法用规则推导时显式指定（多为"同一概念不同叫法"，标注为推测，可在 Inspector 调）。
            var alias = new Dictionary<string, string>
            {
                { "乱心结", "r_rule_enemy_cautious" },
                { "延时沙漏", "r_rule_turns_plus" },
                { "无相环", "r_rule_no_rage" },
                { "澄明镜", "r_rule_no_hit" },
                { "真刃", "r_mech_true" },
                { "群击印", "r_mech_all_hits" },
                { "贯绝箭", "r_mech_ult_pierce" },
                { "赌骰", "r_trade_1" },
                { "速决沙漏", "r_rule_turns_minus" },
                { "双刃签", "r_mech_extra_hit" },
            };

            var all = RelicCatalog.All;
            var byId = new Dictionary<string, RelicDef>();
            foreach (var d in all) byId[d.Id] = d;

            var imageToIds = new Dictionary<string, List<string>>();
            var unmapped = new List<string>();

            var files = Directory.GetFiles(RelicArtDir, "*.png", SearchOption.TopDirectoryOnly);
            System.Array.Sort(files, System.StringComparer.Ordinal);
            foreach (var abs in files)
            {
                string fn = Path.GetFileNameWithoutExtension(abs); // 中文图名
                var ids = new List<string>();

                // 规则 1：精确名匹配
                foreach (var d in all) if (d.Name == fn) ids.Add(d.Id);

                // 规则 2：兽纹 → r_mark_<id>（图名"句芒兽纹" → "句芒·xxx"）
                if (fn.EndsWith("兽纹"))
                {
                    string key = fn.Substring(0, fn.Length - 2);
                    foreach (var d in all)
                        if (d.Id.StartsWith("r_mark_"))
                        {
                            int dot = d.Name.IndexOf('·');
                            if (dot == key.Length && d.Name.StartsWith(key)) ids.Add(d.Id);
                        }
                }
                // 规则 3：克星 → r_foe_<elem>_*（图名"厚土克星" → "厚土克星石/玉/晶/髓"）
                else if (fn.EndsWith("克星"))
                {
                    foreach (var d in all)
                        if (d.Id.StartsWith("r_foe_") && d.Name.StartsWith(fn)) ids.Add(d.Id);
                }
                // 规则 4：元素符 → r_elem_<elem>_*（图名"青木符" → "青木灵符·初..极"）
                else if (fn.EndsWith("符"))
                {
                    string glyph = fn.Substring(0, fn.Length - 1);
                    foreach (var d in all)
                        if (d.Id.StartsWith("r_elem_") && d.Name.StartsWith(glyph + "灵符")) ids.Add(d.Id);
                }
                // 规则 5：角色契印 → r_role_<role>_*（图名"御者契印" → "御者契印/宝印/神印/天印"）
                else if (fn.EndsWith("契印"))
                {
                    string roleKey = fn.Substring(0, fn.Length - 2);
                    foreach (var d in all)
                        if (d.Id.StartsWith("r_role_") && d.Name.StartsWith(roleKey)) ids.Add(d.Id);
                }

                // 规则 6：别名兜底
                if (ids.Count == 0 && alias.TryGetValue(fn, out var aid) && byId.ContainsKey(aid))
                    ids.Add(aid);

                if (ids.Count == 0) { unmapped.Add(fn); continue; }
                imageToIds[fn] = ids;
            }

            // 灌入：id → sprite（同 id 只取第一张命中的图）
            var idToSprite = new Dictionary<string, Sprite>();
            foreach (var kv in imageToIds)
            {
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>(RelicArtDir + "/" + kv.Key + ".png");
                if (sp == null) continue;
                foreach (var id in kv.Value)
                    if (!idToSprite.ContainsKey(id)) idToSprite[id] = sp;
            }

            catalog.Entries.Clear();
            foreach (var kv in idToSprite)
                catalog.Entries.Add(new RelicSpriteCatalog.Entry { Id = kv.Key, Sprite = kv.Value });
            catalog.Rebuild();
            RelicSpriteCatalog.InvalidateCache();
            EditorUtility.SetDirty(catalog);

            if (unmapped.Count > 0)
                Debug.LogWarning("[ArtPipeline] 遗物图有 " + unmapped.Count +
                    " 张在 RelicCatalog 里找不到对应条目（疑似用户正在制作的新遗物，未进目录）：\n" + string.Join("、", unmapped));

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
                int c = Directory.Exists(abs)
                    ? Directory.GetFiles(abs, "*.png", SearchOption.AllDirectories).Length : 0;
                lines.Add($"{sub,-12} {c,4} 张");
            }
            var catalog = AssetDatabase.LoadAssetAtPath<SpriteCatalog>(CatalogPath);
            lines.Add($"立绘目录     {(catalog != null ? catalog.Entries.Count : 0),4} 只");
            Debug.Log("[ArtPipeline] 美术账目：\n" + string.Join("\n", lines));
        }
    }
}
