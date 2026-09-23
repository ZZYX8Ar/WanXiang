// ============================================================================
//  万相 · 局外养成面板（Panel_Meta）重新生成器
//  ---------------------------------------------------------------------------
//  用户反馈：原来的布局坍塌（标题被背景压住、五条轨道错位、祭坛行被遮）。
//  新设计（仿图鉴 Panel_Codex，但详情改为【常驻右侧】）：
//
//    ┌─ 异兽培养 ─────────────────────────────────────────────┐
//    │ 墨铊 22 · 累计 1 局 · 最远第 5 幕                       │
//    │ [全部][木][火][土][金][水]                              │
//    │ ┌── 列表（左）──┐ ┌──── 详情（右，常驻）────┐          │
//    │ │ ● 句芒 Lv0    │ │ 立绘 / 名 / 五行·定位·品阶 │        │
//    │ │ ● 祝融 Lv1    │ │ 属性（含升级预览）          │        │
//    │ │ …            │ │ 技能 ×3 / 特性             │        │
//    │ └──────────────┘ │ [升级·N 墨铊][进化·条件]  │        │
//    │                  └────────────────────────────┘        │
//    └────────────────────────────────────────────────────────┘
//
//  ⚠ 覆盖式保存（SaveAsPrefabAsset）保留 GUID，不会断引用。
//     原文件已备份到 MYRIAD/_backup_prefab/Panel_Meta_*.prefab。
// ============================================================================

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace WanXiang.EditorTools
{
    public static class UIBuildMetaPanel
    {
        private const string PrefabPath = "Assets/Resources/UI/Panel_Meta.prefab";

        // 画布基准（设计分辨率）
        private const float W = 1920f, H = 1080f;

        [MenuItem("WanXiang/UI/重建局外养成面板（异兽培养）", priority = 105)]
        internal static void Build()
        {
            var root = new GameObject("Panel_Meta", typeof(RectTransform));
            var rt = (RectTransform)root.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            // ---------- 顶栏 ----------
            var top = UIBuild.Node(root.transform, "Root_TopBar",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -96f), new Vector2(-30f, -16f));
            UIBuild.Img(top, UIBuild.Silk, true);

            UIBuild.Tmp(UIBuild.Node(top, "Tmp_Title", Vector2.zero, new Vector2(0.4f, 1f),
                new Vector2(24f, 0f), Vector2.zero), "异兽培养", 40, UIBuild.Ink,
                TextAlignmentOptions.MidlineLeft);

            UIBuild.Tmp(UIBuild.Node(top, "Tmp_Status", new Vector2(0.4f, 0f), Vector2.one,
                Vector2.zero, new Vector2(-24f, 0f)), "墨铊 0 · 累计 0 局 · 最远第 0 幕", 26, UIBuild.Ink2,
                TextAlignmentOptions.MidlineRight);

            // ---------- 五行页签（全部 + 木火土金水）----------
            var tabBar = UIBuild.Node(root.transform, "Root_Tabs",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -176f), new Vector2(-30f, -106f));
            string[] tabNames = { "全部", "木", "火", "土", "金", "水" };
            for (int i = 0; i < tabNames.Length; i++)
            {
                var tab = UIBuild.Node(tabBar, "Tab_" + i,
                    new Vector2(0f, 0f), new Vector2(0f, 1f),
                    new Vector2(i * 150f, 0f), new Vector2(i * 150f + 138f, 0f));
                var img = UIBuild.Img(tab, i == 0 ? UIBuild.Gold : UIBuild.Silk, true);
                var btn = tab.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                var label = UIBuild.Node(tab, "Tmp_Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                UIBuild.Tmp(label, tabNames[i], 28, UIBuild.Ink);
            }

            // ---------- 左侧列表 ----------
            var listBg = UIBuild.Node(root.transform, "Root_List",
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(30f, 30f), new Vector2(880f, -196f));
            UIBuild.Img(listBg, UIBuild.Card, true);

            var scrollGo = UIBuild.Node(listBg, "Scroll_Grid",
                Vector2.zero, Vector2.one, new Vector2(10f, 10f), new Vector2(-10f, -10f));
            var scroll = scrollGo.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = UIBuild.Node(scrollGo, "Viewport", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero);
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = viewport;

            var content = UIBuild.Node(viewport, "Content",
                new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, 0f);
            scroll.content = content;
            // ⚠ 关掉自动布局：高度由代码按条目数手动设（否则 ContentSizeFitter 覆盖 sizeDelta）
            var csf = content.gameObject.GetComponent<ContentSizeFitter>();
            if (csf != null) csf.enabled = false;
            var vlg = content.gameObject.GetComponent<VerticalLayoutGroup>();
            if (vlg != null) vlg.enabled = false;

            // 条目模板（默认隐藏，代码复制用）
            var item = UIBuild.Node(content, "Item_Beast", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(8f, -8f), new Vector2(-8f, -8f));
            item.pivot = new Vector2(0.5f, 1f);
            item.sizeDelta = new Vector2(-16f, 96f);
            UIBuild.Img(item, UIBuild.Silk, true);
            UIBuild.Tmp(UIBuild.Node(item, "Tmp_ItemName", new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(24f, 0f), new Vector2(-180f, 0f)), "句芒", 30, UIBuild.Ink,
                TextAlignmentOptions.MidlineLeft);
            UIBuild.Tmp(UIBuild.Node(item, "Tmp_ItemLevel", new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-170f, 0f), new Vector2(-20f, 0f)), "Lv.0", 26, UIBuild.Ink2,
                TextAlignmentOptions.MidlineRight);
            item.gameObject.SetActive(false);

            // ---------- 右侧详情（★ 常驻，不隐藏）----------
            var detail = UIBuild.Node(root.transform, "Root_Detail",
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(900f, 30f), new Vector2(-30f, -196f));
            UIBuild.Img(detail, UIBuild.Card, true);

            // 立绘
            var big = UIBuild.Node(detail, "Img_BeastBig",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -308f), new Vector2(328f, -8f));
            var bigImg = UIBuild.Img(big, Color.white, false);
            bigImg.preserveAspect = true;

            // 名字 / 分类
            UIBuild.Tmp(UIBuild.Node(detail, "Tmp_Name", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(348f, -76f), new Vector2(-24f, -16f)), "句芒", 42, UIBuild.Ink,
                TextAlignmentOptions.MidlineLeft);
            UIBuild.Tmp(UIBuild.Node(detail, "Tmp_Class", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(348f, -132f), new Vector2(-24f, -80f)), "木 · 辅助 · 传说", 28, UIBuild.Ink2,
                TextAlignmentOptions.MidlineLeft);

            // 属性（含升级预览）
            UIBuild.Tmp(UIBuild.Node(detail, "Tmp_Stats", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(348f, -300f), new Vector2(-24f, -140f)), "生命 0　攻击 0", 26, UIBuild.Ink,
                TextAlignmentOptions.TopLeft);

            // 技能 ×3
            UIBuild.Tmp(UIBuild.Node(detail, "Tmp_SkillTitle", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(28f, -352f), new Vector2(-24f, -312f)), "技能", 28, UIBuild.Gold,
                TextAlignmentOptions.MidlineLeft);
            for (int i = 0; i < 3; i++)
            {
                UIBuild.Tmp(UIBuild.Node(detail, "Tmp_Skill" + (i + 1), new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(48f, -408f - i * 52f), new Vector2(-24f, -356f - i * 52f)),
                    "—", 24, UIBuild.Ink, TextAlignmentOptions.MidlineLeft);
            }

            // 特性
            UIBuild.Tmp(UIBuild.Node(detail, "Tmp_Trait", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(28f, -588f), new Vector2(-24f, -552f)), "特性：—", 24, UIBuild.Ink2,
                TextAlignmentOptions.TopLeft);

            // 底排按钮
            UIBuild.Tmp(UIBuild.Node(detail, "Tmp_EvolveCond", new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(28f, 96f), new Vector2(-24f, 148f)), "进化条件：—", 22, UIBuild.Ink2,
                TextAlignmentOptions.MidlineLeft);

            var upBtn = UIBuild.Node(detail, "Btn_Upgrade", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(28f, 20f), new Vector2(388f, 88f));
            var upImg = UIBuild.Img(upBtn, UIBuild.Gold, true);
            var upB = upBtn.gameObject.AddComponent<Button>(); upB.targetGraphic = upImg;
            UIBuild.Tmp(UIBuild.Node(upBtn, "Tmp_Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                "升级 · 3 墨铊", 28, UIBuild.Ink);

            var evBtn = UIBuild.Node(detail, "Btn_Evolve", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(408f, 20f), new Vector2(828f, 88f));
            var evImg = UIBuild.Img(evBtn, UIBuild.Silk, true);
            var evB = evBtn.gameObject.AddComponent<Button>(); evB.targetGraphic = evImg;
            UIBuild.Tmp(UIBuild.Node(evBtn, "Tmp_Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                "进化", 28, UIBuild.Ink);

            // 返回
            var back = UIBuild.Node(root.transform, "Btn_Back", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(30f, 30f), new Vector2(230f, 90f));
            var backImg = UIBuild.Img(back, UIBuild.Silk, true);
            var backB = back.gameObject.AddComponent<Button>(); backB.targetGraphic = backImg;
            UIBuild.Tmp(UIBuild.Node(back, "Tmp_Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                "返回", 28, UIBuild.Ink);

            // 覆盖保存（保 GUID）
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing == null) { Debug.LogError("[MetaBuild] 原 prefab 不存在，无法覆盖（请先确认路径）"); Object.DestroyImmediate(root); return; }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.Refresh();
            Debug.Log("[MetaBuild] OK 已重建 " + PrefabPath + "（原文件已备份到 _backup_prefab）");
        }

        /// <summary>顺带把字段绑上（避免"节点有了但字段还是 null"）。</summary>
        [MenuItem("WanXiang/UI/绑定局外养成字段", priority = 106)]
        internal static void Bind()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null) { Debug.LogError("[MetaBuild] 载入失败"); return; }
            try
            {
                var panel = root.GetComponent<WanXiang.Modules.UI.MetaPanel>();
                if (panel == null) { Debug.LogError("[MetaBind] 根上没有 MetaPanel 组件"); return; }
                var so = new SerializedObject(panel);

                SetRef(so, root, "_tmpStatus", "Tmp_Status");
                SetRef(so, root, "_tmpTitle", "Tmp_Title");
                SetRef(so, root, "_scrollList", "Scroll_Grid");
                SetRef(so, root, "_listContent", "Content");
                SetRef(so, root, "_itemTemplate", "Item_Beast");
                SetRef(so, root, "_rootDetail", "Root_Detail");
                SetRef(so, root, "_imgBig", "Img_BeastBig");
                SetRef(so, root, "_tmpName", "Tmp_Name");
                SetRef(so, root, "_tmpClass", "Tmp_Class");
                SetRef(so, root, "_tmpStats", "Tmp_Stats");
                SetRef(so, root, "_tmpSkill1", "Tmp_Skill1");
                SetRef(so, root, "_tmpSkill2", "Tmp_Skill2");
                SetRef(so, root, "_tmpSkill3", "Tmp_Skill3");
                SetRef(so, root, "_tmpTrait", "Tmp_Trait");
                SetRef(so, root, "_tmpEvolveCond", "Tmp_EvolveCond");
                SetRef(so, root, "_btnUpgrade", "Btn_Upgrade");
                SetRef(so, root, "_btnEvolve", "Btn_Evolve");
                SetRef(so, root, "_btnBack", "Btn_Back");

                var tabs = so.FindProperty("_tabBtns");
                if (tabs != null)
                {
                    tabs.arraySize = 6;
                    for (int i = 0; i < 6; i++)
                    {
                        var t = FindDeep(root.transform, "Tab_" + i);
                        tabs.GetArrayElementAtIndex(i).objectReferenceValue = t != null ? t.gameObject : null;
                    }
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.Refresh();
                Debug.Log("[MetaBind] 字段绑定完成");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void SetRef(SerializedObject so, GameObject root, string field, string nodeName)
        {
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning("[MetaBind] 找不到字段：" + field); return; }
            var t = FindDeep(root.transform, nodeName);
            p.objectReferenceValue = t != null ? t.gameObject : null;
            if (t == null) Debug.LogWarning("[MetaBind] 节点缺失：" + nodeName + " ← " + field);
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
#endif
