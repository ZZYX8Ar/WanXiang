// ============================================================================
//  万相 · UI 图集打包
//  ---------------------------------------------------------------------------
//  菜单：WanXiang / 美术 / 打包 UI 图集（部件 + 头像）
//
//  为什么必须打图集：uGUI 的合批是"同一 Canvas 里、层级连续、同材质（同纹理）"
//  的元素合成一个 mesh。我们 45 个 UI 部件 + 30 张异兽头像全是散图，
//  每张图一次纹理切换 = 一次 DC。一屏铺开就是几十个 DC，真机上必卡。
//
//  打完图集后的 DC 结构（以主城为例）：
//    底图（Panel_Home 概念稿，全屏独占） 1
//    + 全部 UI 部件（同图集）            1~3（按层级被打断的次数）
//    + TMP 文本（字体 SDF 图集）          1~2
//    + 异兽头像（头像图集）               1
//  从"图数 × 1"变成"图集数 + 文本"，与界面复杂度基本无关。
//
//  两张图集分开的原因：
//    · Parts：45 个通用部件（按钮/卷轴/格子/滑条…），每个面板都用到
//    · Heads：30 张 256 头像，只有列表/预览用；分开后战场场景不加载它
//
//  刻意**不**打进图集的：
//    · Panels 概念稿（1280×720 整屏底图）：一屏一张、本身独占 DC，打包只浪费内存
//    · Beasts 立绘（1024）：只在战斗场景整只显示，走战场管线
//
//  层级要不要手动调？不用。打完图集后同屏元素共享一张纹理，
//  "层级穿插打断合批"的问题自然消失；层与层本来就是独立 Canvas（改一层不重建别层）。
// ============================================================================

using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace WanXiang.EditorTools
{
    public static class UIAtlasTool
    {
        private const string PartsDir = "Assets/ArtRes/UI/Parts";
        private const string HeadsDir = "Assets/ArtRes/UI/Heads";
        private const string PartsAtlas = "Assets/ArtRes/UI/UIParts.spriteatlas";
        private const string HeadsAtlas = "Assets/ArtRes/UI/UIHeads.spriteatlas";

        [MenuItem("WanXiang/美术/打包 UI 图集（部件 + 头像）", priority = 130)]
        public static void Pack()
        {
            EnsureAtlas(PartsAtlas, PartsDir, 2048);
            EnsureAtlas(HeadsAtlas, HeadsDir, 1024);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var a1 = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(PartsAtlas);
            var a2 = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(HeadsAtlas);
            Debug.Log("[Atlas] UI 图集打包完成：UIParts " +
                      (a1 != null ? a1.spriteCount.ToString() : "?") + " 张部件、UIHeads " +
                      (a2 != null ? a2.spriteCount.ToString() : "?") + " 张头像。" +
                      "新生成的部件图改完后重跑本菜单即可重新打包。");
        }

        /// <summary>建（或重建）一张图集并加入整个目录。</summary>
        private static void EnsureAtlas(string atlasPath, string sourceDir, int maxSize)
        {
            var existing = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            if (existing == null)
            {
                var atlas = new SpriteAtlas();
                var settings = atlas.GetPackingSettings();
                settings.enableRotation = false;      // 旋转会让 9-slice 边界难排查
                settings.enableTightPacking = false;  // 同上；部件留矩形更稳
                settings.padding = 4;
                atlas.SetPackingSettings(settings);

                var tex = atlas.GetTextureSettings();
                tex.generateMipMaps = false;          // UI 不缩放显示，mip 是白费内存
                atlas.SetTextureSettings(tex);

                // 目标平台：编辑器 + 主流移动端全用 ASTC（平涂 + 等宽描边的最优解）
                atlas.SetPlatformSettings(new TextureImporterPlatformSettings
                {
                    name = "Standalone",
                    overridden = true,
                    maxTextureSize = maxSize,
                    format = TextureImporterFormat.ASTC_6x6,
                    textureCompression = TextureImporterCompression.Compressed,
                });
                atlas.SetPlatformSettings(new TextureImporterPlatformSettings
                {
                    name = "Android",
                    overridden = true,
                    maxTextureSize = maxSize,
                    format = TextureImporterFormat.ASTC_6x6,
                    textureCompression = TextureImporterCompression.Compressed,
                });
                atlas.SetPlatformSettings(new TextureImporterPlatformSettings
                {
                    name = "iPhone",
                    overridden = true,
                    maxTextureSize = maxSize,
                    format = TextureImporterFormat.ASTC_6x6,
                    textureCompression = TextureImporterCompression.Compressed,
                });

                AssetDatabase.CreateAsset(atlas, atlasPath);
            }

            // 显式枚举 sprite 加入（文件夹对象作为 packable 在代码路径下不会触发打包，
            // 实测 spriteCount 恒为 0；枚举加入虽然要在新增图片时重跑本菜单，但胜在确定生效）。
            var atlas2 = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            var guids = AssetDatabase.FindAssets("t:Sprite", new[] { sourceDir });
            var sprites = new Object[guids.Length];
            for (int i = 0; i < guids.Length; i++)
                sprites[i] = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guids[i]));
            if (sprites.Length == 0)
            {
                Debug.LogError("[Atlas] 目录里没有 sprite：" + sourceDir);
                return;
            }
            atlas2.Remove(atlas2.GetPackables());           // 先清再加，保证可重跑
            atlas2.Add(sprites);
            EditorUtility.SetDirty(atlas2);
            Debug.Log("[Atlas] " + atlasPath + " ← " + sourceDir);
        }
    }
}
