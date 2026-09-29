// ============================================================================
//  万相 · 生成 招募 / 遗物 两个面板预制体（独立脚本，不碰其它 17 个面板）
//  ---------------------------------------------------------------------------
//  菜单：WanXiang / UI / 生成招募与遗物面板
//  只生成 Panel_Recruit / Panel_Relic 两个 prefab 到 Assets/Resources/UI/。
//
//  ⚠ 这两个 prefab 是本生成器的产出（占位美术，用户后续自行替换），
//    与其它面板不同：本脚本**允许覆盖**（先 AssetDatabase.DeleteAsset 再生成），
//    因为它们是新面板、用户尚未手工微调。用户改过之后请改用「增量补丁」。
//
//  绑定走 UIBuild.Bind（SerializedObject），与 UIPanelPrefabBuilder 完全同构：
//    节点名 → 面板 [SerializeField] 字段，开箱即带引用，不用运行时 Find。
// ============================================================================

using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Modules.UI;

namespace WanXiang.EditorTools
{
    internal static class BuildRecruitRelicPrefabs
    {
        private const string Dir = "Assets/Resources/UI";

        [MenuItem("WanXiang/UI/生成招募与遗物面板", priority = 101)]
        internal static void Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets/Resources", "UI");

            // 中文字体（Deng SDF 已入库；换机把源 TTF 放回 Assets/ArtRes/Fonts 再跑字体菜单）
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/ArtRes/Fonts/Deng SDF.asset");
            if (font == null)
                font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            UIBuild.SetFont(font);

            BuildRelic();
            BuildRecruit();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[BuildRecruitRelic] Panel_Recruit / Panel_Relic 生成完毕 → " + Dir);
        }

        // ---------------------------------------------------------------- 遗物
        private static void BuildRelic()
        {
            SafeDelete("Panel_Relic");

            var root = NewPanel("Panel_Relic", 720f, 520f);
            var comp = root.AddComponent<RelicPanel>();
            var rt = (RectTransform)root.transform;

            var title = UIBuild.Tmp(UIBuild.Top(rt, "Tmp_Title", 56, 30, 30, 16),
                "遗物 · 三选一", 34, UIBuild.Ink, TextAlignmentOptions.Center);

            var draftBtns = new Button[3];
            var draftNames = new TMP_Text[3];
            var draftDescs = new TMP_Text[3];
            for (int i = 0; i < 3; i++)
            {
                float y = 150f - i * 150f;
                var card = UIBuild.Fixed(rt, "Draft_" + i, new Vector2(0.5f, 0.5f),
                    new Vector2(620f, 120f), new Vector2(0f, y));
                UIBuild.Img(card, UIBuild.Card, true);
                draftBtns[i] = UIBuild.Btn(card);

                draftNames[i] = UIBuild.Tmp(UIBuild.Stretch(card, "Tmp_DraftName_" + i, 18, 12, 18, 60),
                    "遗物名", 26, UIBuild.Ink, TextAlignmentOptions.Left);
                draftDescs[i] = UIBuild.Tmp(UIBuild.Stretch(card, "Tmp_DraftDesc_" + i, 18, 52, 18, 12),
                    "描述", 20, UIBuild.Ink2, TextAlignmentOptions.TopLeft);
            }

            var bConfirm = UIBuild.MakeBtn(rt, "Btn_Confirm", new Vector2(1f, 0f),
                new Vector2(170f, 52f), new Vector2(-200f, 32f), "确定", UIBuild.Gold, 24f);
            var bSkip = UIBuild.MakeBtn(rt, "Btn_Skip", new Vector2(1f, 0f),
                new Vector2(170f, 52f), new Vector2(-20f, 32f), "跳过", UIBuild.Card, 24f);

            UIBuild.Bind(comp, "_tmpTitle", title);
            UIBuild.BindArr(comp, "_draftBtns", draftBtns);
            UIBuild.BindArr(comp, "_tmpDraftNames", draftNames);
            UIBuild.BindArr(comp, "_tmpDraftDescs", draftDescs);
            UIBuild.Bind(comp, "_btnConfirm", bConfirm.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnSkip", bSkip.GetComponent<Button>());

            UIBuild.SavePrefab(root, "Panel_Relic");
        }

        // ---------------------------------------------------------------- 招募
        //  布局参考【灵市】：左侧候选格子（五行各 2 ⇒ 最多 10 格，2 列自动排），
        //  点格子看右侧详情，双击格子（或点详情里的【选择】）入队。
        private static void BuildRecruit()
        {
            SafeDelete("Panel_Recruit");

            var root = NewPanel("Panel_Recruit", 900f, 640f);
            var comp = root.AddComponent<RecruitPanel>();
            var rt = (RectTransform)root.transform;

            var title = UIBuild.Tmp(UIBuild.Top(rt, "Tmp_Title", 52, 30, 30, 14),
                "招募 · 挑选至多 3 只异兽", 32, UIBuild.Ink, TextAlignmentOptions.Center);
            var hint = UIBuild.Tmp(UIBuild.Top(rt, "Tmp_Hint", 40, 30, 30, 62),
                "本幕可选至多 3 只（可少选）· 已选 0 / 3　—— 点格子看详情，双击入队",
                20, UIBuild.Ink2, TextAlignmentOptions.Center);

            // 左：候选格子滚动区（Content 带 GridLayoutGroup，2 列；Viewport 已内置受击层）
            var content = UIBuild.ScrollGrid(rt, "Scroll_Candidates",
                Vector2.zero, Vector2.one, new Vector2(30f, 104f), new Vector2(-460f, -116f),
                new Vector2(190f, 104f), new Vector2(12f, 12f), 2, out var _sr);

            // 格子模板（失活，运行时 Instantiate(_cardTemplate, _gridContent) 克隆）
            var cardTpl = UIBuild.Fixed(content, "Item_CardTemplate", new Vector2(0f, 1f),
                new Vector2(190f, 104f), Vector2.zero);
            UIBuild.Img(cardTpl, UIBuild.Card, true);
            var cardHead = UIBuild.Img(UIBuild.Fixed(cardTpl, "Img_CardHead", new Vector2(0f, 0.5f),
                new Vector2(76f, 76f), new Vector2(12f, 0f)), new Color(0.86f, 0.82f, 0.74f, 1f));
            cardHead.preserveAspect = true;
            UIBuild.Tmp(UIBuild.Fixed(cardTpl, "Tmp_CardName", new Vector2(0f, 0.5f),
                new Vector2(96f, 40f), new Vector2(98f, 18f)), "异兽名", 24, UIBuild.Ink, TextAlignmentOptions.Left);
            UIBuild.Tmp(UIBuild.Fixed(cardTpl, "Tmp_CardElem", new Vector2(0f, 0.5f),
                new Vector2(96f, 32f), new Vector2(98f, -22f)), "属性", 20, UIBuild.Ink2, TextAlignmentOptions.Left);
            var cardSel = UIBuild.Img(UIBuild.Fixed(cardTpl, "Img_CardSel", new Vector2(1f, 0f),
                new Vector2(30f, 30f), new Vector2(-10f, 10f)), UIBuild.Gold);
            cardSel.raycastTarget = false;
            cardTpl.gameObject.SetActive(false);

            // 右：详情区（初始隐藏；点格子时由代码 SetActive(true) 并填数据）
            var detail = UIBuild.Fixed(rt, "Root_Detail", new Vector2(1f, 1f),
                new Vector2(400f, 430f), new Vector2(-30f, -116f));
            detail.pivot = new Vector2(1f, 1f);
            UIBuild.Img(detail, UIBuild.Card, true);
            var dBig = UIBuild.Img(UIBuild.Fixed(detail, "Img_DetailBig", new Vector2(0f, 1f),
                new Vector2(128f, 128f), new Vector2(20f, -20f)), new Color(0.86f, 0.82f, 0.74f, 1f));
            dBig.preserveAspect = true;
            var dName = UIBuild.Tmp(UIBuild.Fixed(detail, "Tmp_DetailName", new Vector2(0f, 1f),
                new Vector2(230f, 44f), new Vector2(158f, -24f)), "异兽名", 28, UIBuild.Ink, TextAlignmentOptions.Left);
            var dClass = UIBuild.Tmp(UIBuild.Fixed(detail, "Tmp_DetailClass", new Vector2(0f, 1f),
                new Vector2(230f, 32f), new Vector2(158f, -70f)), "五行 · 定位 · 品阶", 20, UIBuild.Ink2, TextAlignmentOptions.Left);
            var dStats = UIBuild.Tmp(UIBuild.Fixed(detail, "Tmp_DetailStats", new Vector2(0f, 1f),
                new Vector2(230f, 74f), new Vector2(158f, -104f)), "生命 / 攻击…", 18, UIBuild.Ink2, TextAlignmentOptions.TopLeft);
            dStats.enableWordWrapping = true;
            var dSkills = UIBuild.Tmp(UIBuild.Fixed(detail, "Tmp_DetailSkills", new Vector2(0f, 1f),
                new Vector2(360f, 190f), new Vector2(20f, -166f)), "技能…", 18, UIBuild.Ink, TextAlignmentOptions.TopLeft);
            dSkills.enableWordWrapping = true;
            var pickRt = UIBuild.Fixed(detail, "Btn_Pick", new Vector2(0.5f, 0f),
                new Vector2(200f, 56f), new Vector2(0f, 18f));
            UIBuild.Img(pickRt, UIBuild.Gold, true);
            UIBuild.Tmp(UIBuild.Stretch(pickRt, "Tmp_Label", 8, 6, 8, 6), "选择", 24, UIBuild.Ink, TextAlignmentOptions.Center);
            var pickBtn = UIBuild.Btn(pickRt);
            detail.gameObject.SetActive(false);

            // 底：刷新 / 确定
            var egg = UIBuild.Tmp(UIBuild.Fixed(rt, "Tmp_EggCost", new Vector2(0f, 0f),
                new Vector2(280f, 36f), new Vector2(140f, 34f)), "刷新：免费", 22, UIBuild.Ink2, TextAlignmentOptions.Left);
            var bRefresh = UIBuild.MakeBtn(rt, "Btn_Refresh", new Vector2(1f, 0f),
                new Vector2(170f, 52f), new Vector2(-200f, 34f), "刷新", UIBuild.Card, 24f);
            var bConfirm = UIBuild.MakeBtn(rt, "Btn_Confirm", new Vector2(1f, 0f),
                new Vector2(170f, 52f), new Vector2(-20f, 34f), "确定", UIBuild.Gold, 24f);

            UIBuild.Bind(comp, "_tmpTitle", title);
            UIBuild.Bind(comp, "_tmpHint", hint);
            UIBuild.Bind(comp, "_gridContent", content);
            UIBuild.Bind(comp, "_cardTemplate", cardTpl.gameObject);
            UIBuild.Bind(comp, "_detailRoot", detail.gameObject);
            UIBuild.Bind(comp, "_detailBig", dBig);
            UIBuild.Bind(comp, "_detailName", dName);
            UIBuild.Bind(comp, "_detailClass", dClass);
            UIBuild.Bind(comp, "_detailStats", dStats);
            UIBuild.Bind(comp, "_detailSkills", dSkills);
            UIBuild.Bind(comp, "_btnPick", pickBtn);
            UIBuild.Bind(comp, "_tmpEggCost", egg);
            UIBuild.Bind(comp, "_btnRefresh", bRefresh.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnConfirm", bConfirm.GetComponent<Button>());

            UIBuild.SavePrefab(root, "Panel_Recruit");
        }

        // ---------------------------------------------------------------- 工具
        /// <summary>删除本生成器上一次产出的占位 prefab（这两个面板允许覆盖）。</summary>
        private static void SafeDelete(string file)
        {
            string path = Dir + "/" + file + ".prefab";
            if (System.IO.File.Exists(path))
                AssetDatabase.DeleteAsset(path);
        }

        private static GameObject NewPanel(string name, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(w, h);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.10f, 0.10f, 0.12f, 0.98f);
            return go;
        }
    }
}
