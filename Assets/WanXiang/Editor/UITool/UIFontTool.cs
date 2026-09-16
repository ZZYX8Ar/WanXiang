// ============================================================================
//  WanXiang · UI 中文字体工具
//  ---------------------------------------------------------------------------
//  菜单：
//    WanXiang / UI / 字体 / 选择中文字体并应用到全部面板…
//    WanXiang / UI / 字体 / 应用工程内已有的 TMP 中文字体
//
//  背景：TMP 自带的 LiberationSans SDF 没有中文字形，所有中文会渲染成 □。
//        本工具从你指定的 ttf/otf **动态**建一个 TMP 字体资产（不做静态图集，
//        中文按需生成字形，几十兆的字体也不会撑爆图集），再把它灌到
//        14 个面板的全部 TMP 文本 + TMP Settings 默认字体上。
//
//  ⚠ 为什么让你自己指定字体文件，而不是我拷一个系统字体进工程：
//    字体有授权问题。请用你打算正式使用的字体（美术规范里定的是思源黑体 /
//    霞鹜文楷）。开发期临时用系统的等线 Deng.ttf 也可以，但发布前务必替换。
// ============================================================================

using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace WanXiang.EditorTools
{
    public static class UIFontTool
    {
        private const string PrefabDir = "Assets/Resources/UI";
        private const string FontDir = "Assets/ArtRes/Fonts";

        [MenuItem("WanXiang/UI/字体/选择中文字体并应用到全部面板…", priority = 110)]
        public static void PickAndApply()
        {
            string src = EditorUtility.OpenFilePanelWithFilters(
                "选择中文字体（ttf / otf）", "C:/Windows/Fonts", new[] { "字体文件", "ttf,otf" });
            if (string.IsNullOrEmpty(src)) return;

            if (!AssetDatabase.IsValidFolder("Assets/ArtRes"))
                AssetDatabase.CreateFolder("Assets", "ArtRes");
            if (!AssetDatabase.IsValidFolder(FontDir))
                AssetDatabase.CreateFolder("Assets/ArtRes", "Fonts");

            string fileName = Path.GetFileName(src);
            string dst = FontDir + "/" + fileName;
            File.Copy(src, Path.GetFullPath(dst), true);
            AssetDatabase.Refresh();

            var fontAsset = BuildDynamicFontAsset(dst);
            if (fontAsset == null) return;
            ApplyToAll(fontAsset);
        }

        [MenuItem("WanXiang/UI/字体/应用工程内已有的 TMP 中文字体", priority = 111)]
        public static void ApplyExisting()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (fa == null) continue;
                if (fa.name.StartsWith("LiberationSans")) continue;   // 自带的无中文
                Debug.Log("[FontTool] 使用已有字体资产：" + path);
                ApplyToAll(fa);
                return;
            }
            Debug.LogWarning("[FontTool] 工程里没有可用的中文字体资产。先跑 " +
                             "「选择中文字体并应用到全部面板…」。");
        }

        /// <summary>
        /// 一键从 C:/Windows/Fonts 导入三款中文字体并**按用途分配**：
        ///   等线 Deng.ttf   → 正文/数值（默认字体）
        ///   黑体 simhei.ttf → 标题/按钮（字重更足，标题才压得住）
        ///   楷体 simkai.ttf → 典籍引文/题记（国风文档质感）
        /// ⚠ 这是**开发期**用系统字体，发布前请换成有授权的字体（思源黑体 / 霞鹜文楷）。
        /// </summary>
        [MenuItem("WanXiang/UI/字体/一键导入系统中文三字体（等线/黑体/楷体）", priority = 112)]
        public static void ImportSystemCjkPreset()
        {
            const string fontDir = "C:/Windows/Fonts";
            var plan = new[]
            {
                new { file = "Deng.ttf",   asset = "DengXian SDF" },
                new { file = "simhei.ttf", asset = "SimHei SDF" },
                new { file = "simkai.ttf", asset = "SimKai SDF" },
            };

            if (!AssetDatabase.IsValidFolder("Assets/ArtRes"))
                AssetDatabase.CreateFolder("Assets", "ArtRes");
            if (!AssetDatabase.IsValidFolder(FontDir))
                AssetDatabase.CreateFolder("Assets/ArtRes", "Fonts");

            foreach (var p in plan)
            {
                string src = Path.Combine(fontDir, p.file);
                if (!File.Exists(src))
                {
                    Debug.LogWarning("[FontTool] 系统里没有 " + src + "，跳过。");
                    continue;
                }
                string dst = FontDir + "/" + p.file;
                File.Copy(src, Path.GetFullPath(dst), true);
            }
            AssetDatabase.Refresh();

            var body = LoadOrBuild(FontDir + "/Deng.ttf");
            var title = LoadOrBuild(FontDir + "/simhei.ttf");
            var quote = LoadOrBuild(FontDir + "/simkai.ttf");

            if (body == null)
            {
                Debug.LogError("[FontTool] 正文字体没建起来，中止。");
                return;
            }

            ApplyByRole(body, title ?? body, quote ?? body);
        }

        // ================================================================

        private static TMP_FontAsset LoadOrBuild(string ttfAssetPath)
        {
            if (!File.Exists(Path.GetFullPath(ttfAssetPath)))
            {
                Debug.LogWarning("[FontTool] 字体文件不存在：" + ttfAssetPath);
                return null;
            }
            return BuildDynamicFontAsset(ttfAssetPath);
        }

        /// <summary>按节点命名约定分配字体：标题→黑体、引文→楷体、其余→正文。</summary>
        private static void ApplyByRole(TMP_FontAsset body, TMP_FontAsset title, TMP_FontAsset quote)
        {
            string[] titleNames = { "Tmp_Title", "Tmp_TitleEn", "Tmp_ActTitle", "Tmp_NodeName", "Tmp_Label",
                                    "Tmp_BannerText", "Tmp_RealmText", "Tmp_TotalPower", "Tmp_EnemyPower",
                                    "Tmp_WeatherName", "Tmp_Round", "Tmp_ResultName" };
            string[] quoteNames = { "Tmp_ClassicQuote", "Tmp_Quote", "Tmp_StoryText", "Tmp_Tagline",
                                    "Tmp_Source", "Tmp_ClassicSource" };

            int texts = 0, panels = 0, titleHits = 0, quoteHits = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!Path.GetFileName(path).StartsWith("Panel_")) continue;

                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
                    {
                        var n = t.gameObject.name;
                        if (StartsAny(n, titleNames)) { t.font = title; titleHits++; }
                        else if (StartsAny(n, quoteNames)) { t.font = quote; quoteHits++; }
                        else t.font = body;
                        texts++;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    panels++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            SetDefaultFont(body);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[FontTool] 三字体分配完成：{panels} 面板 / {texts} 文本" +
                      $"（标题用黑体 {titleHits} 处、引文用楷体 {quoteHits} 处、其余等线）。" +
                      "⚠ 开发期系统字体，发布前换成有授权的字体。");
        }

        private static bool StartsAny(string name, string[] list)
        {
            foreach (var s in list) if (name.StartsWith(s)) return true;
            return false;
        }

        private static void SetDefaultFont(TMP_FontAsset fontAsset)
        {
            var settings = TMP_Settings.instance;
            if (settings == null) return;
            var so = new SerializedObject(settings);
            var prop = so.FindProperty("m_defaultFontAsset");
            if (prop == null) return;
            prop.objectReferenceValue = fontAsset;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            Debug.Log("[FontTool] TMP Settings 默认字体 = " + fontAsset.name);
        }

        // ================================================================

        private static TMP_FontAsset BuildDynamicFontAsset(string ttfAssetPath)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(ttfAssetPath);
            if (font == null)
            {
                Debug.LogError("[FontTool] 这个文件 Unity 没当成字体导入：" + ttfAssetPath +
                               "（确认是 ttf/otf；ttc 字体集不支持）");
                return null;
            }

            string baseName = Path.GetFileNameWithoutExtension(ttfAssetPath) + " SDF";
            string assetPath = FontDir + "/" + baseName + ".asset";

            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null) return existing;

            var fa = TMP_FontAsset.CreateFontAsset(font);      // 动态模式：字形按需生成
            if (fa == null)
            {
                Debug.LogError("[FontTool] 创建 TMP 字体资产失败。");
                return null;
            }
            fa.name = baseName;
            AssetDatabase.CreateAsset(fa, assetPath);
            if (fa.atlasTextures != null && fa.atlasTextures.Length > 0 && fa.atlasTextures[0] != null)
            {
                fa.atlasTextures[0].name = baseName + " Atlas";
                AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            }
            if (fa.material != null)
            {
                fa.material.name = baseName + " Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[FontTool] 已生成 TMP 字体资产：" + assetPath + "（动态模式）");
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        }

        private static void ApplyToAll(TMP_FontAsset fontAsset)
        {
            int texts = 0, panels = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!Path.GetFileName(path).StartsWith("Panel_")) continue;

                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
                    {
                        t.font = fontAsset;
                        texts++;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    panels++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            // TMP Settings 默认字体（新建文本默认就用它）
            var settings = TMP_Settings.instance;
            if (settings != null)
            {
                var so = new SerializedObject(settings);
                var prop = so.FindProperty("m_defaultFontAsset");
                if (prop != null)
                {
                    prop.objectReferenceValue = fontAsset;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(settings);
                    Debug.Log("[FontTool] TMP Settings 默认字体已切换。");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[FontTool] 字体应用完成：{panels} 个面板 / {texts} 个文本 → {fontAsset.name}");
        }
    }
}
