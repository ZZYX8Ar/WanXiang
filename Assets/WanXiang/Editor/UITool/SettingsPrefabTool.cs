// ============================================================================
//  万相 · 设置分页 prefab 补节点工具（2026-11-14）
//  ---------------------------------------------------------------------------
//  ⛔ 只补缺，不重建：先备份到工程外 _backups/设置分页_日期/，再 LoadPrefabContents，
//     只新增 Tab 栏 / 战斗页 / 存档页 / 战斗内设置按钮，最后反射绑定字段并存回。
//  用法：Unity 菜单「万相/UI/补设置分页节点」→「万相/UI/补战斗设置按钮」
//  结构见 文档/设置界面结构.md（先图后码）。
// ============================================================================
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.EditorTools
{
    public static class SettingsPrefabTool
    {
        private const string SettingsPath = "Assets/Resources/UI/Panel_Settings.prefab";
        private const string BattlePath = "Assets/Resources/UI/Panel_Battle.prefab";
        private const string Kit = "Assets/Resources/UI/Kit/";

        [MenuItem("万相/UI/补设置分页节点")]
        public static void BuildSettingsTabs()
        {
            if (!File.Exists(SettingsPath)) { Debug.LogError("[SettingsTool] 找不到 " + SettingsPath); return; }
            Backup(SettingsPath);

            var root = PrefabUtility.LoadPrefabContents(SettingsPath);
            var comp = root.GetComponent("SettingsPanel") as MonoBehaviour;
            if (comp == null) { Debug.LogError("[SettingsTool] prefab 根没有 SettingsPanel"); PrefabUtility.UnloadPrefabContents(root); return; }

            var audio = root.transform.Find("Root_Audio");
            if (audio == null) { Debug.LogError("[SettingsTool] 缺 Root_Audio（模板）"); PrefabUtility.UnloadPrefabContents(root); return; }

            TMP_FontAsset font = null;
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true)) { if (t.font != null) { font = t.font; break; } }
            var toggleTemplate = (root.transform.Find("Root_Video/Tgl_Fullscreen")
                                  ?? root.transform.Find("Tgl_Fullscreen")) as RectTransform;
            if (toggleTemplate == null)
                Debug.LogWarning("[SettingsTool] 找不到 Tgl_Fullscreen 模板：新行会建无美术的裸 Toggle");
            var btnSprite = AssetDatabase.LoadAssetAtPath<Sprite>(Kit + "btn_secondary.png");
            var btnSpriteAlt = AssetDatabase.LoadAssetAtPath<Sprite>(Kit + "btn_primary.png");

            // ---- 顶部 Tab 栏（5 个按钮）----
            var tabBar = EnsureNode(root.transform, "Root_Tabs", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(-48f, 52f));
            string[] tabNames = { "音频", "战斗", "显示", "操作", "存档" };
            var tabBtns = new Button[tabNames.Length];
            var tabKeys = new[] { "Tab_Audio", "Tab_Battle", "Tab_Video", "Tab_Keybind", "Tab_Save" };
            for (int i = 0; i < tabNames.Length; i++)
            {
                var existing = tabBar.Find(tabKeys[i]);
                var brt = existing as RectTransform;
                if (brt == null)
                {
                    var go = new GameObject(tabKeys[i], typeof(RectTransform), typeof(Image), typeof(Button));
                    brt = (RectTransform)go.transform; brt.SetParent(tabBar, false);
                    var im = go.GetComponent<Image>(); im.sprite = btnSprite != null ? btnSprite : btnSpriteAlt;
                    im.type = Image.Type.Sliced; im.color = Color.white;
                    var tx = MakeText(brt, "Tmp_Label", tabNames[i], font, 20, new Color(0.20f, 0.16f, 0.12f, 1f));
                    tx.raycastTarget = false;
                }
                brt.anchorMin = brt.anchorMax = new Vector2(0f, 1f);
                brt.pivot = new Vector2(0f, 1f);
                brt.sizeDelta = new Vector2(120f, 44f);
                brt.anchoredPosition = new Vector2(i * 128f, 0f);
                tabBtns[i] = brt.GetComponent<Button>();
            }

            // ---- 战斗页 / 存档页（克隆 Root_Audio 的矩形，清空孩子后重建行）----
            // 通栏风格：内容页统一 高 560（原 Root_Video 只有 40 高，会把 Toggle 挤成竖排）
            SetPageRect(root.transform, "Root_Video", new Vector2(0f, -180f), new Vector2(-80f, 620f));
            SetPageRect(root.transform, "Root_Keybinds", new Vector2(0f, -180f), new Vector2(-80f, 620f));
            var battlePage = EnsurePage(root.transform, "Root_Battle", (RectTransform)audio);
            SetPageRect(root.transform, "Root_Battle", new Vector2(0f, -180f), new Vector2(-80f, 620f));
            RebuildBattlePage(battlePage, font, toggleTemplate, btnSpriteAlt);
            var savePage = EnsurePage(root.transform, "Root_Save", (RectTransform)audio);
            SetPageRect(root.transform, "Root_Save", new Vector2(0f, -180f), new Vector2(-80f, 620f));
            RebuildSavePage(savePage, font, btnSpriteAlt);

            // ---- 音频页加静音行 ----
            Toggle muteTgl = FindToggle(audio, "Tgl_Mute");
            if (muteTgl == null)
                muteTgl = MakeToggleRow(audio, "Row_Mute", "静音", font, toggleTemplate, out _, "Tgl_Mute", 2);

            // ---- 页面提示文本 ----
            var hint = root.transform.Find("Tmp_PageHint") as RectTransform;
            if (hint == null)
            {
                var go = new GameObject("Tmp_PageHint", typeof(RectTransform));
                hint = (RectTransform)go.transform; hint.SetParent(root.transform, false);
                hint.anchorMin = new Vector2(0f, 0f); hint.anchorMax = new Vector2(1f, 0f);
                hint.pivot = new Vector2(0.5f, 0f); hint.sizeDelta = new Vector2(-64f, 30f);
                hint.anchoredPosition = new Vector2(0f, 24f);
                var tx = hint.gameObject.AddComponent<TextMeshProUGUI>();
                tx.font = font; tx.fontSize = 18; tx.color = new Color(0.35f, 0.30f, 0.24f, 1f);
                tx.alignment = TextAlignmentOptions.Center; tx.raycastTarget = false;
            }

            // ---- 反射绑定 ----
            Bind(comp, "_tabBtns", tabBtns);
            Bind(comp, "_tabPages", new GameObject[] { audio.gameObject, battlePage.gameObject, root.transform.Find("Root_Video")?.gameObject, root.transform.Find("Root_Keybinds")?.gameObject, savePage.gameObject });
            Bind(comp, "_tmpPageHint", hint.GetComponent<TMP_Text>());
            Bind(comp, "_tglMute", muteTgl);
            Bind(comp, "_tglAutoBattle", FindToggle(battlePage, "Tgl_AutoBattle"));
            Bind(comp, "_tglCutIn", FindToggle(battlePage, "Tgl_CutIn"));
            Bind(comp, "_tglDamageNum", FindToggle(battlePage, "Tgl_DamageNum"));
            Bind(comp, "_tglShake", FindToggle(battlePage, "Tgl_Shake"));
            Bind(comp, "_tglTooltip", FindToggle(battlePage, "Tgl_Tooltip"));
            Bind(comp, "_speedBtns", new Button[] {
                FindButton(battlePage, "Btn_Speed1"), FindButton(battlePage, "Btn_Speed15"), FindButton(battlePage, "Btn_Speed2") });
            Bind(comp, "_fpsBtns", new Button[] {
                FindButton(root.transform.Find("Root_Video"), "Btn_Fps30"),
                FindButton(root.transform.Find("Root_Video"), "Btn_Fps60"),
                FindButton(root.transform.Find("Root_Video"), "Btn_Fps120"),
                FindButton(root.transform.Find("Root_Video"), "Btn_FpsUnlimited") });
            Bind(comp, "_btnClearSave", FindButton(savePage, "Btn_ClearSave"));

            PrefabUtility.SaveAsPrefabAsset(root, SettingsPath);
            PrefabUtility.UnloadPrefabContents(root);
            AssetDatabase.Refresh();
            Debug.Log("[SettingsTool] 设置分页节点已补（备份在 _backups/设置分页_*）");
        }

        [MenuItem("万相/UI/补战斗设置按钮")]
        public static void BuildBattleSettingsButton()
        {
            if (!File.Exists(BattlePath)) { Debug.LogError("[SettingsTool] 找不到 " + BattlePath); return; }
            Backup(BattlePath);
            var root = PrefabUtility.LoadPrefabContents(BattlePath);
            var comp = root.GetComponent("BattlePanel") as MonoBehaviour;
            if (comp == null) { Debug.LogError("[SettingsTool] BattlePanel 缺失"); PrefabUtility.UnloadPrefabContents(root); return; }

            var existing = root.transform.Find("Btn_Settings");
            RectTransform rt = existing as RectTransform;
            if (rt == null)
            {
                var go = new GameObject("Btn_Settings", typeof(RectTransform), typeof(Image), typeof(Button));
                rt = (RectTransform)go.transform; rt.SetParent(root.transform, false);
                var im = go.GetComponent<Image>();
                im.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Kit + "round_btn.png");
                if (im.sprite == null) im.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Kit + "btn_secondary.png");
                im.type = Image.Type.Sliced; im.color = Color.white;
                TMP_FontAsset font = null;
                foreach (var t in root.GetComponentsInChildren<TMP_Text>(true)) { if (t.font != null) { font = t.font; break; } }
                var tx = MakeText(rt, "Tmp_Label", "设", font, 24, new Color(0.20f, 0.16f, 0.12f, 1f));
                tx.raycastTarget = false;
            }
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(56f, 56f);
            rt.anchoredPosition = new Vector2(-24f, -24f);
            rt.SetAsLastSibling();

            Bind(comp, "_btnSettings", rt.GetComponent<Button>());
            PrefabUtility.SaveAsPrefabAsset(root, BattlePath);
            PrefabUtility.UnloadPrefabContents(root);
            AssetDatabase.Refresh();
            Debug.Log("[SettingsTool] 战斗内设置按钮已补（右上角，ESC 同效）");
        }

        [MenuItem("万相/UI/统一设置页行式布局")]
        public static void LayoutAllPages()
        {
            if (!File.Exists(SettingsPath)) { Debug.LogError("[SettingsTool] 找不到 prefab"); return; }
            Backup(SettingsPath);
            var root = PrefabUtility.LoadPrefabContents(SettingsPath);
            var audio = root.transform.Find("Root_Audio") as RectTransform;
            var video = root.transform.Find("Root_Video") as RectTransform;
            if (audio == null || video == null) { Debug.LogError("[SettingsTool] 缺 Root_Audio/Video"); PrefabUtility.UnloadPrefabContents(root); return; }

            TMP_FontAsset font = null;
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true)) { if (t.font != null) { font = t.font; break; } }

            // 页容器给足高度（行式 3 行 ≈ 300）
            SetPageRect(root.transform, "Root_Audio", new Vector2(0f, -180f), new Vector2(-80f, 620f));
            SetPageRect(root.transform, "Root_Video", new Vector2(0f, -180f), new Vector2(-80f, 620f));

            LayoutSliderRow(audio, "Sld_Bgm", "音乐", 0, font);
            LayoutSliderRow(audio, "Sld_Sfx", "音效", 1, font);
            LayoutRowFor(audio, "Row_Mute", 2);
            LayoutToggleRow(video, "Tgl_Fullscreen", "全屏", 0, font);
            LayoutToggleRow(video, "Tgl_Vsync", "垂直同步", 1, font);

            PrefabUtility.SaveAsPrefabAsset(root, SettingsPath);
            PrefabUtility.UnloadPrefabContents(root);
            AssetDatabase.Refresh();
            Debug.Log("[SettingsTool] 三个旧页已统一为行式布局");
        }

        private static RectTransform RowFor(RectTransform page, string rowName, int index)
        {
            var row = page.Find(rowName) as RectTransform;
            if (row == null)
            {
                var go = new GameObject(rowName, typeof(RectTransform));
                row = (RectTransform)go.transform; row.SetParent(page, false);
            }
            row.anchorMin = new Vector2(0f, 1f); row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 0.5f);
            row.sizeDelta = new Vector2(-80f, 66f);
            row.anchoredPosition = new Vector2(0f, -35f - index * 74f);
            return row;
        }

        private static void LayoutRowFor(RectTransform page, string rowName, int index) => RowFor(page, rowName, index);

        private static void LayoutSliderRow(RectTransform page, string sliderName, string label, int index, TMP_FontAsset font)
        {
            var slider = page.Find(sliderName) as RectTransform;
            if (slider == null) return;
            var row = RowFor(page, "Row_" + sliderName.Replace("Sld_", ""), index);
            slider.SetParent(row, false);
            slider.anchorMin = slider.anchorMax = new Vector2(0f, 0.5f);
            slider.pivot = new Vector2(0.5f, 0.5f);
            slider.sizeDelta = new Vector2(620f, 42f);
            slider.anchoredPosition = new Vector2(520f, 0f);
            // 原 Slider 自带 label 会和新行 label 重叠 → 隐藏
            foreach (var t0 in slider.GetComponentsInChildren<TMP_Text>(true)) t0.gameObject.SetActive(false);
            var lab = MakeText(row, "Tmp_Label", label, font, 24, new Color(0.22f, 0.18f, 0.14f, 1f));
            lab.alignment = TextAlignmentOptions.Left;
            lab.rectTransform.anchorMin = Vector2.zero; lab.rectTransform.anchorMax = Vector2.one;
            lab.rectTransform.offsetMin = new Vector2(16f, 0f); lab.rectTransform.offsetMax = new Vector2(-700f, 0f);
            lab.rectTransform.anchoredPosition = Vector2.zero;
        }

        private static void LayoutToggleRow(RectTransform page, string toggleName, string label, int index, TMP_FontAsset font)
        {
            var tgl = page.Find(toggleName) as RectTransform;
            if (tgl == null) return;
            var row = RowFor(page, "Row_" + toggleName.Replace("Tgl_", ""), index);
            tgl.SetParent(row, false);
            tgl.anchorMin = tgl.anchorMax = new Vector2(1f, 0.5f);
            tgl.pivot = new Vector2(1f, 0.5f);
            tgl.sizeDelta = new Vector2(56f, 56f);
            tgl.anchoredPosition = new Vector2(-24f, 0f);
            // 自带 label 可能藏在子层级（Tmp_Background/Tmp_Label）→ 全隐藏，避免和新行 label 重叠
            foreach (var t0 in tgl.GetComponentsInChildren<TMP_Text>(true)) t0.gameObject.SetActive(false);
            var lab = MakeText(row, "Tmp_Label", label, font, 24, new Color(0.22f, 0.18f, 0.14f, 1f));
            lab.alignment = TextAlignmentOptions.Left;
            lab.rectTransform.anchorMin = Vector2.zero; lab.rectTransform.anchorMax = Vector2.one;
            lab.rectTransform.offsetMin = new Vector2(16f, 0f); lab.rectTransform.offsetMax = new Vector2(-120f, 0f);
            lab.rectTransform.anchoredPosition = Vector2.zero;
        }

        /// <summary>方案 B：通栏风格统一排版（按组件类型规整，不逐行硬编码）。</summary>
        [MenuItem("万相/UI/设置·通栏排版")]
        public static void ApplyComfyLayout()
        {
            if (!File.Exists(SettingsPath)) { Debug.LogError("[SettingsTool] 找不到 prefab"); return; }
            Backup(SettingsPath);
            var root = PrefabUtility.LoadPrefabContents(SettingsPath);
            var rootRt = root.transform as RectTransform;

            // ① 不透明底（解决 Home 按钮透出）
            var bg = root.transform.Find("Img_PanelBg") as RectTransform;
            if (bg == null)
            {
                var go = new GameObject("Img_PanelBg", typeof(RectTransform), typeof(Image));
                bg = (RectTransform)go.transform; bg.SetParent(root.transform, false);
            }
            // ★ 每次强制设属性（已存在时也要重设，否则旧 sprite 的透明会让 Home 透出来）
            {
                var im = bg.GetComponent<Image>();
                if (im == null) im = bg.gameObject.AddComponent<Image>();
                im.sprite = null;
                im.type = Image.Type.Simple;
                im.color = new Color(0.965f, 0.945f, 0.895f, 1f);
            }
            bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one;
            bg.offsetMin = Vector2.zero; bg.offsetMax = Vector2.zero;
            bg.SetAsFirstSibling();

            // ② 页容器：顶部锚定，Tab 之下
            foreach (var pg in new[] { "Root_Audio", "Root_Battle", "Root_Video", "Root_Keybinds", "Root_Save" })
                SetPageRect(root.transform, pg, new Vector2(0f, -180f), new Vector2(-80f, 620f));

            // ③ 行顺序（显式指定，避免静音跑第一行）
            OrderRows(root.transform, "Root_Audio", new[] { "Row_Bgm", "Row_Sfx", "Row_Mute" });
            OrderRows(root.transform, "Root_Battle", new[] { "Row_AutoBattle", "Row_CutIn", "Row_DamageNum", "Row_Shake", "Row_Tooltip", "Row_Speed" });
            OrderRows(root.transform, "Root_Video", new[] { "Row_Fullscreen", "Row_Vsync" });

            // ④ 页内所有行：Label 固定左 260 / Slider 通栏留位 / Toggle 右对齐
            foreach (var pg in new[] { "Root_Audio", "Root_Battle", "Root_Video", "Root_Save" })
            {
                var page = root.transform.Find(pg) as RectTransform;
                if (page == null) continue;
                int i = 0;
                foreach (Transform row in page)
                {
                    if (!(row is RectTransform rowRt)) continue;
                    // 行容器（有 Label/Toggle/Slider 的才当行）
                    bool isRow = row.name.StartsWith("Row_") || row.GetComponentInChildren<Slider>(true) != null
                                 || row.GetComponentInChildren<Toggle>(true) != null;
                    if (!isRow) continue;
                    rowRt.anchorMin = new Vector2(0f, 1f); rowRt.anchorMax = new Vector2(1f, 1f);
                    rowRt.pivot = new Vector2(0.5f, 0.5f);
                    rowRt.sizeDelta = new Vector2(-80f, 66f);
                    rowRt.anchoredPosition = new Vector2(0f, -35f - i * 74f);
                    i++;

                    // Label：左固定 260，绝不压滑条
                    foreach (var tx in rowRt.GetComponentsInChildren<TMP_Text>(true))
                    {
                        var lrt = tx.rectTransform;
                        lrt.anchorMin = new Vector2(0f, 0f); lrt.anchorMax = new Vector2(0f, 1f);
                        lrt.pivot = new Vector2(0f, 0.5f);
                        lrt.sizeDelta = new Vector2(260f, 0f);
                        lrt.anchoredPosition = new Vector2(24f, 0f);
                        tx.alignment = TextAlignmentOptions.Left;
                        tx.enableWordWrapping = false;
                    }
                    // Slider：恢复通栏（左右留位），不缩窄
                    foreach (var sl in rowRt.GetComponentsInChildren<Slider>(true))
                    {
                        var srt = sl.transform as RectTransform;
                        srt.anchorMin = new Vector2(0f, 0.5f); srt.anchorMax = new Vector2(1f, 0.5f);
                        srt.pivot = new Vector2(0.5f, 0.5f);
                        srt.offsetMin = new Vector2(300f, -21f);
                        srt.offsetMax = new Vector2(-40f, 21f);
                        srt.localScale = Vector3.one;
                    }
                    // Toggle：右对齐 + 隐藏其内部模板文字（行 label 是兄弟节点，安全）
                    foreach (var tg in rowRt.GetComponentsInChildren<Toggle>(true))
                    {
                        foreach (var t0 in tg.GetComponentsInChildren<TMP_Text>(true)) t0.gameObject.SetActive(false);
                        var trt = tg.transform as RectTransform;
                        trt.anchorMin = trt.anchorMax = new Vector2(1f, 0.5f);
                        trt.pivot = new Vector2(1f, 0.5f);
                        trt.sizeDelta = new Vector2(56f, 56f);
                        trt.anchoredPosition = new Vector2(-24f, 0f);
                    }
                }
            }

            // ④ 页说明：面板底部居中（原来压到 Home 的退出按钮上）
            var hint = root.transform.Find("Tmp_PageHint") as RectTransform;
            if (hint != null)
            {
                hint.anchorMin = new Vector2(0f, 0f); hint.anchorMax = new Vector2(1f, 0f);
                hint.pivot = new Vector2(0.5f, 0f);
                hint.sizeDelta = new Vector2(-80f, 34f);
                hint.anchoredPosition = new Vector2(0f, 16f);
            }

            PrefabUtility.SaveAsPrefabAsset(root, SettingsPath);
            PrefabUtility.UnloadPrefabContents(root);
            AssetDatabase.Refresh();
            Debug.Log("[SettingsTool] 通栏排版完成（背景底 + Label 左 260 + Slider 通栏 + Toggle 右）");
        }

        private static void OrderRows(Transform root, string pageName, string[] order)
        {
            var page = root.Find(pageName);
            if (page == null) return;
            for (int i = 0; i < order.Length; i++)
            {
                var row = page.Find(order[i]);
                if (row != null) row.SetSiblingIndex(i);
            }
        }

        // ---------------- 辅助 ----------------
        private static void Backup(string path)
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "..", "_backups",
                "设置分页_" + System.DateTime.Now.ToString("yyyyMMdd_HHmm"));
            Directory.CreateDirectory(dir);
            string dst = Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + "_" +
                                      System.DateTime.Now.ToString("HHmmss") + ".prefab");
            File.Copy(path, dst, true);
            Debug.Log("[SettingsTool] 备份 → " + dst);
        }

        private static RectTransform EnsureNode(Transform parent, string name, Vector2 aMin, Vector2 aMax,
                                                Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var t = parent.Find(name) as RectTransform;
            if (t == null)
            {
                var go = new GameObject(name, typeof(RectTransform));
                t = (RectTransform)go.transform; t.SetParent(parent, false);
            }
            t.anchorMin = aMin; t.anchorMax = aMax; t.pivot = pivot;
            t.anchoredPosition = pos; t.sizeDelta = size;
            return t;
        }

        /// <summary>页容器：顶部锚定（标题+Tab 之下开始），pos.y 表示页顶相对容器顶部的偏移。</summary>
        private static void SetPageRect(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var t = parent.Find(name) as RectTransform;
            if (t == null) return;
            t.anchorMin = new Vector2(0f, 1f); t.anchorMax = new Vector2(1f, 1f);
            t.pivot = new Vector2(0.5f, 1f);
            t.anchoredPosition = new Vector2(0f, pos.y);   // pos.y 为负: 页顶下移
            t.sizeDelta = new Vector2(size.x, size.y);     // size.y 为正: 页高
        }

        private static RectTransform EnsurePage(Transform parent, string name, RectTransform template)
        {
            var t = parent.Find(name) as RectTransform;
            if (t != null) return t;
            var go = new GameObject(name, typeof(RectTransform));
            t = (RectTransform)go.transform; t.SetParent(parent, false);
            t.anchorMin = template.anchorMin; t.anchorMax = template.anchorMax;
            t.pivot = template.pivot; t.anchoredPosition = template.anchoredPosition;
            t.sizeDelta = template.sizeDelta;
            t.gameObject.SetActive(false);
            return t;
        }

        private static void RebuildBattlePage(RectTransform page, TMP_FontAsset font,
                                              RectTransform toggleTemplate, Sprite btnSprite)
        {
            if (FindToggle(page, "Tgl_AutoBattle") != null) return;   // 已补过（按 Toggle 判断，行存在不算）
            MakeToggleRow(page, "Row_AutoBattle", "每局开始自动战斗", font, toggleTemplate, out _, "Tgl_AutoBattle", 0);
            MakeToggleRow(page, "Row_CutIn", "技能特写演出", font, toggleTemplate, out _, "Tgl_CutIn", 1);
            MakeToggleRow(page, "Row_DamageNum", "伤害飘字", font, toggleTemplate, out _, "Tgl_DamageNum", 2);
            MakeToggleRow(page, "Row_Shake", "屏幕震动", font, toggleTemplate, out _, "Tgl_Shake", 3);
            MakeToggleRow(page, "Row_Tooltip", "战斗内提示气泡", font, toggleTemplate, out _, "Tgl_Tooltip", 4);
            // 速度三连
            var row = EnsureNode(page, "Row_Speed", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -35f - 5 * 70f), new Vector2(-60f, 66f));
            var lab = MakeText(row, "Tmp_Label", "战斗速度", font, 22, new Color(0.22f, 0.18f, 0.14f, 1f));
            lab.alignment = TextAlignmentOptions.Left;
            lab.rectTransform.anchorMin = new Vector2(0f, 0f); lab.rectTransform.anchorMax = new Vector2(0f, 1f);
            lab.rectTransform.pivot = new Vector2(0f, 0.5f); lab.rectTransform.sizeDelta = new Vector2(200f, 0f);
            lab.rectTransform.anchoredPosition = new Vector2(0f, 0f);
            string[] names = { "1x", "1.5x", "2x" };
            string[] keys = { "Btn_Speed1", "Btn_Speed15", "Btn_Speed2" };
            for (int i = 0; i < names.Length; i++)
            {
                var b = MakeButton(row, keys[i], names[i], font, btnSprite);
                var brt2 = b.GetComponent<RectTransform>();
                brt2.anchorMin = brt2.anchorMax = new Vector2(1f, 0.5f);
                brt2.pivot = new Vector2(1f, 0.5f);
                brt2.sizeDelta = new Vector2(120f, 50f);
                brt2.anchoredPosition = new Vector2(-24f - (names.Length - 1 - i) * 132f, 0f);
            }
        }

        private static void RebuildSavePage(RectTransform page, TMP_FontAsset font, Sprite btnSprite)
        {
            if (FindButton(page, "Btn_ClearSave") != null) return;
            var b = MakeButton(page, "Btn_ClearSave", "清空当前存档", font, btnSprite);
            var rt = b.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(320f, 56f);
            rt.anchoredPosition = new Vector2(40f, -50f);
        }

        private static Toggle MakeToggleRow(Transform parent, string rowName, string label, TMP_FontAsset font,
                                            RectTransform template, out GameObject row, string toggleName = null,
                                            int rowIndex = -1)
        {
            row = null;
            int index = rowIndex >= 0 ? rowIndex : parent.childCount;
            // 通栏行：全宽 + 70 高，与旧 Tgl_Fullscreen 同风格（用户 11-14 实测：560 宽定点与旧布局打架）
            var rowRt = EnsureNode(parent, rowName, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -35f - index * 70f), new Vector2(-60f, 66f));
            row = rowRt.gameObject;
            var lab = MakeText(rowRt, "Tmp_Label", label, font, 24, new Color(0.22f, 0.18f, 0.14f, 1f));
            lab.alignment = TextAlignmentOptions.Left;
            lab.rectTransform.anchorMin = Vector2.zero; lab.rectTransform.anchorMax = Vector2.one;
            lab.rectTransform.offsetMin = new Vector2(16f, 0f); lab.rectTransform.offsetMax = new Vector2(-120f, 0f);
            lab.rectTransform.anchoredPosition = Vector2.zero;

            Toggle tgl;
            string tName = string.IsNullOrEmpty(toggleName) ? "Tgl_" + rowName.Replace("Row_", "") : toggleName;
            var exist = rowRt.Find(tName) as RectTransform;
            if (exist != null) tgl = exist.GetComponent<Toggle>();
            else if (template != null)
            {
                var clone = Object.Instantiate(template, rowRt);
                clone.name = tName;
                clone.anchorMin = clone.anchorMax = new Vector2(1f, 0.5f);
                clone.pivot = new Vector2(1f, 0.5f);
                clone.sizeDelta = new Vector2(44f, 44f);
                clone.anchoredPosition = new Vector2(-8f, 0f);
                clone.gameObject.SetActive(true);
                // ★ 模板 Tgl_Fullscreen 自带一个 "全屏" label，克隆体会把它带过来（战斗页每行都会显示"全屏"）
                foreach (var t0 in clone.GetComponentsInChildren<TMP_Text>(true)) t0.gameObject.SetActive(false);
                tgl = clone.GetComponent<Toggle>();
                var crt = (RectTransform)clone;
                crt.anchorMin = crt.anchorMax = new Vector2(1f, 0.5f);
                crt.pivot = new Vector2(1f, 0.5f);
                crt.sizeDelta = new Vector2(56f, 56f);
                crt.anchoredPosition = new Vector2(-24f, 0f);
            }
            else
            {
                var go = new GameObject(tName, typeof(RectTransform), typeof(Toggle));
                var grt = (RectTransform)go.transform;
                grt.SetParent(rowRt, false);              // ★ 必须挂到行上（上一版漏了 → 行里没 Toggle）
                grt.anchorMin = grt.anchorMax = new Vector2(1f, 0.5f);
                grt.pivot = new Vector2(1f, 0.5f);
                grt.sizeDelta = new Vector2(44f, 44f);
                grt.anchoredPosition = new Vector2(-8f, 0f);
                tgl = go.GetComponent<Toggle>();
            }
            return tgl;
        }

        private static TextMeshProUGUI MakeText(RectTransform parent, string name, string text,
                                                TMP_FontAsset font, int size, Color color)
        {
            var exist = parent.Find(name) as RectTransform;
            if (exist != null) { var t0 = exist.GetComponent<TextMeshProUGUI>(); if (t0 != null) { t0.text = text; return t0; } }
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(8f, 4f); rt.offsetMax = new Vector2(-8f, -4f);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text; t.font = font; t.fontSize = size; t.color = color;
            t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
            return t;
        }

        private static Button MakeButton(RectTransform parent, string name, string label,
                                         TMP_FontAsset font, Sprite sprite)
        {
            var exist = parent.Find(name) as RectTransform;
            if (exist != null) return exist.GetComponent<Button>();
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
            var im = go.GetComponent<Image>();
            im.sprite = sprite; im.type = Image.Type.Sliced; im.color = Color.white;
            MakeText(rt, "Tmp_Label", label, font, 20, new Color(0.20f, 0.16f, 0.12f, 1f));
            return go.GetComponent<Button>();
        }

        private static Toggle FindToggle(Transform page, string name)
        {
            if (page == null) return null;
            var t = page.Find(name); if (t != null) return t.GetComponent<Toggle>();
            foreach (var c in page.GetComponentsInChildren<Toggle>(true)) if (c.name == name) return c;
            return null;
        }

        private static Button FindButton(Transform page, string name)
        {
            if (page == null) return null;
            var t = page.Find(name); if (t != null) return t.GetComponent<Button>();
            foreach (var c in page.GetComponentsInChildren<Button>(true)) if (c.name == name) return c;
            return null;
        }

        private static void Bind(Object comp, string field, object value)
        {
            var so = new SerializedObject(comp);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning("[SettingsTool] 字段不存在：" + field); return; }
            if (value is UnityEngine.Object o) p.objectReferenceValue = o;
            else if (value is UnityEngine.Object[] arr)
            {
                p.arraySize = arr.Length;
                for (int i = 0; i < arr.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = arr[i];
            }
            else if (value is Button[] btns)
            {
                p.arraySize = btns.Length;
                for (int i = 0; i < btns.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = btns[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
