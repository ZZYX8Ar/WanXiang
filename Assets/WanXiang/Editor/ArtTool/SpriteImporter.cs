// ============================================================================
//  万相 · 立绘导入器（菜单：万相/美术/① 导入立绘到目录）
//  ---------------------------------------------------------------------------
//  扫描 Assets/WanXiang/Art/ArtRes/Units/**/*.png：
//    · TextureImporter → Sprite（Single）、Pivot = 底部中心（立绘基线 y=88% 已在图内）
//    · 文件名 {id}_body.png → id
//    · 写进 SpriteCatalog 资产（Assets/WanXiang/Config/SpriteCatalog.asset，没有则创建）
//
//  之后 BattleStage2D / 融合预览 / 图鉴都从 SpriteCatalog 按 BeastDef.Id 查立绘。
// ============================================================================

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WanXiang.Battle.Presentation;

namespace WanXiang.Editor.ArtTool
{
    public static class SpriteImporter
    {
        private const string ArtRoot = "Assets/WanXiang/Art/ArtRes/Units";
        private const string CatalogPath = "Assets/WanXiang/Config/SpriteCatalog.asset";

        [MenuItem("万相/美术/① 导入立绘到目录")]
        public static void ImportAll()
        {
            var catalog = LoadOrCreateCatalog();
            catalog.Entries.Clear();

            var pngs = Directory.GetFiles(ArtRoot, "*_body.png", SearchOption.AllDirectories);
            int ok = 0;
            foreach (var path in pngs)
            {
                var assetPath = path.Replace('\\', '/');
                var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.mipmapEnabled = false;
                    importer.alphaIsTransparency = true;
                    // 立绘的"脚底基线"已在图内 y=88% 处 —— Pivot 定在底部中，战斗摆放才齐
                    importer.spritePivot = new Vector2(0.5f, 0f);
                    importer.SaveAndReimport();
                }

                string id = Path.GetFileNameWithoutExtension(assetPath);
                if (id.EndsWith("_body")) id = id.Substring(0, id.Length - "_body".Length);

                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (sprite == null)
                {
                    Debug.LogWarning($"[立绘导入] {assetPath} 没有 Sprite（导入设置未生效？）");
                    continue;
                }
                catalog.Entries.Add(new SpriteCatalog.Entry { Id = id, Body = sprite });
                ok++;
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[立绘导入] {ok} 张立绘已写入 {CatalogPath}");
        }

        [MenuItem("万相/美术/② 检查立绘覆盖情况")]
        public static void ReportCoverage()
        {
            var guids = AssetDatabase.FindAssets("t:SpriteCatalog");
            if (guids.Length == 0) { Debug.LogWarning("[立绘导入] 没有 SpriteCatalog 资产"); return; }
            var catalog = AssetDatabase.LoadAssetAtPath<SpriteCatalog>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
            var ids = new HashSet<string>();
            foreach (var e in catalog.Entries) ids.Add(e.Id);

            // ⚠ BeastDef 是普通类不是 UnityEngine.Object，FindAssets/t: 查不到 ——
            //   覆盖率直接对 SpriteCatalog 里已导入的 id 汇报（与 beasts.json 的 30 只比对）。
            int total = 0, withSprite = 0;
            var missing = new List<string>();
            var beastsJson = System.IO.Path.Combine(
                System.IO.Directory.GetParent(System.IO.Directory.GetCurrentDirectory()).Parent.FullName,
                "Docs/Design/data/beasts.json");
            var knownIds = new List<string>();
            if (System.IO.File.Exists(beastsJson))
            {
                    // beasts.json 结构：{"beasts": [...]} —— 逐行找 "id": "xxx"，
                    // 不引 JSON 库也不玩正则转义（字符串里的引号转义极易踩坑）
                    foreach (var line in System.IO.File.ReadAllLines(beastsJson))
                    {
                        var s = line.Trim();
                        if (!s.StartsWith("\"id\"")) continue;
                        int a = s.IndexOf('"', 6);
                        if (a < 0) continue;
                        int b = s.IndexOf('"', a + 1);
                        if (b > a) knownIds.Add(s.Substring(a + 1, b - a - 1));
                    }
            }
            else
            {
                knownIds.AddRange(ids);   // 没找到 json 就退化：以目录为准
            }
            total = knownIds.Count;
            foreach (var id in knownIds)
            {
                if (ids.Contains(id)) withSprite++;
                else missing.Add(id);
            }
            Debug.Log($"[立绘导入] 覆盖 {withSprite}/{total}。" +
                      (missing.Count > 0 ? "缺：" + string.Join("、", missing) : "全覆盖。"));
        }

        private static SpriteCatalog LoadOrCreateCatalog()
        {
            var existing = AssetDatabase.FindAssets("t:SpriteCatalog");
            if (existing.Length > 0)
                return AssetDatabase.LoadAssetAtPath<SpriteCatalog>(AssetDatabase.GUIDToAssetPath(existing[0]));

            if (!Directory.Exists("Assets/WanXiang/Config"))
                Directory.CreateDirectory("Assets/WanXiang/Config");
            var asset = ScriptableObject.CreateInstance<SpriteCatalog>();
            AssetDatabase.CreateAsset(asset, CatalogPath);
            return asset;
        }
    }
}
