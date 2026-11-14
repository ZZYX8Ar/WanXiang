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
            var toggleTemplate = root.transform.Find("Tgl_Fullscreen") as RectTransform;
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
            var battlePage = EnsurePage(root.transform, "Root_Battle", (RectTransform)audio);
            RebuildBattlePage(battlePage, font, toggleTemplate, btnSpriteAlt);
            var savePage = EnsurePage(root.transform, "Root_Save", (RectTransform)audio);
            RebuildSavePage(savePage, font, btnSpriteAlt);

            // ---- 音频页加静音行 ----
            Toggle muteTgl = null;
            var muteRow = audio.Find("Row_Mute");
            if (muteRow == null) muteTgl = MakeToggleRow(audio, "Row_Mute", "静音", font, toggleTemplate, out _);
            else muteTgl = muteRow.Find("Tgl_Mute")?.GetComponent<Toggle>();

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
            if (page.Find("Tgl_AutoBattle") != null) return;   // 已补过
            MakeToggleRow(page, "Row_AutoBattle", "每局开始自动战斗", font, toggleTemplate, out _, "Tgl_AutoBattle");
            MakeToggleRow(page, "Row_CutIn", "技能特写演出", font, toggleTemplate, out _, "Tgl_CutIn");
            MakeToggleRow(page, "Row_DamageNum", "伤害飘字", font, toggleTemplate, out _, "Tgl_DamageNum");
            MakeToggleRow(page, "Row_Shake", "屏幕震动", font, toggleTemplate, out _, "Tgl_Shake");
            MakeToggleRow(page, "Row_Tooltip", "战斗内提示气泡", font, toggleTemplate, out _, "Tgl_Tooltip");
            // 速度三连
            var row = EnsureNode(page, "Row_Speed", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(16f, -6f - 5 * 68f), new Vector2(560f, 56f));
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
                b.GetComponent<RectTransform>().anchorMin = b.GetComponent<RectTransform>().anchorMax = new Vector2(0f, 0.5f);
                b.GetComponent<RectTransform>().pivot = new Vector2(0f, 0.5f);
                b.GetComponent<RectTransform>().sizeDelta = new Vector2(96f, 44f);
                b.GetComponent<RectTransform>().anchoredPosition = new Vector2(210f + i * 108f, 0f);
            }
        }

        private static void RebuildSavePage(RectTransform page, TMP_FontAsset font, Sprite btnSprite)
        {
            if (page.Find("Btn_ClearSave") != null) return;
            var b = MakeButton(page, "Btn_ClearSave", "清空当前存档", font, btnSprite);
            var rt = b.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(280f, 52f);
            rt.anchoredPosition = new Vector2(16f, -16f);
        }

        private static Toggle MakeToggleRow(Transform parent, string rowName, string label, TMP_FontAsset font,
                                            RectTransform template, out GameObject row, string toggleName = null)
        {
            row = null;
            int index = parent.childCount;
            var rowRt = EnsureNode(parent, rowName, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(16f, -6f - index * 68f), new Vector2(560f, 56f));
            row = rowRt.gameObject;
            var lab = MakeText(rowRt, "Tmp_Label", label, font, 22, new Color(0.22f, 0.18f, 0.14f, 1f));
            lab.alignment = TextAlignmentOptions.Left;
            lab.rectTransform.anchorMin = new Vector2(0f, 0f); lab.rectTransform.anchorMax = new Vector2(0f, 1f);
            lab.rectTransform.pivot = new Vector2(0f, 0.5f); lab.rectTransform.sizeDelta = new Vector2(300f, 0f);
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
                tgl = clone.GetComponent<Toggle>();
            }
            else
            {
                var go = new GameObject(tName, typeof(RectTransform), typeof(Toggle));
                tgl = go.AddComponent<Toggle>();
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
