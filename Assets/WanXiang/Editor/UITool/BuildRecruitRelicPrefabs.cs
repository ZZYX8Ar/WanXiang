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
        private static void BuildRecruit()
        {
            SafeDelete("Panel_Recruit");

            var root = NewPanel("Panel_Recruit", 900f, 640f);
            var comp = root.AddComponent<RecruitPanel>();
            var rt = (RectTransform)root.transform;

            var title = UIBuild.Tmp(UIBuild.Top(rt, "Tmp_Title", 56, 30, 30, 14),
                "招募异兽", 34, UIBuild.Ink, TextAlignmentOptions.Center);
            var hint = UIBuild.Tmp(UIBuild.Top(rt, "Tmp_Hint", 40, 30, 30, 66),
                "每属性随机 2 选；免费刷新 1 次，之后刷新消耗灵卵。", 20, UIBuild.Ink2, TextAlignmentOptions.Center);

            // 候选卡滚动区（Content 带 VerticalLayoutGroup；Viewport 已内置受击层）
            var content = UIBuild.ScrollVertical(rt, "Scroll_Candidates",
                Vector2.zero, Vector2.one, new Vector2(30f, 100f), new Vector2(-30f, -120f), 10f);

            // 卡模板（失活，运行时 Instantiate(_cardTemplate, _scrollContent) 克隆）
            var cardTpl = UIBuild.Fixed(content, "Item_CardTemplate", new Vector2(0.5f, 1f),
                new Vector2(800f, 96f), Vector2.zero);
            var le = cardTpl.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 780f; le.preferredHeight = 96f;
            UIBuild.Img(cardTpl, UIBuild.Card, true);
            UIBuild.Tmp(UIBuild.Stretch(cardTpl, "Tmp_CardName", 18, 8, 360, 8),
                "异兽名", 24, UIBuild.Ink, TextAlignmentOptions.Left);
            UIBuild.Tmp(UIBuild.Fixed(cardTpl, "Tmp_CardElem", new Vector2(0f, 0.5f),
                new Vector2(140f, 36f), new Vector2(400f, 0f)),
                "属性", 20, UIBuild.Ink2, TextAlignmentOptions.Left);
            UIBuild.MakeBtn(cardTpl, "Btn_CardPick", new Vector2(1f, 0.5f),
                new Vector2(120f, 52f), new Vector2(-70f, 0f), "选择", UIBuild.Gold, 22f);
            cardTpl.gameObject.SetActive(false);

            // 底部信息 + 按钮
            var sel = UIBuild.Tmp(UIBuild.Fixed(rt, "Tmp_Selected", new Vector2(0f, 0f),
                new Vector2(240f, 36f), new Vector2(140f, 32f)),
                "已选 0 / 3", 22, UIBuild.Ink, TextAlignmentOptions.Left);
            var egg = UIBuild.Tmp(UIBuild.Fixed(rt, "Tmp_EggCost", new Vector2(0f, 0f),
                new Vector2(260f, 36f), new Vector2(390f, 32f)),
                "刷新：免费", 22, UIBuild.Ink2, TextAlignmentOptions.Left);

            var bRefresh = UIBuild.MakeBtn(rt, "Btn_Refresh", new Vector2(1f, 0f),
                new Vector2(170f, 52f), new Vector2(-200f, 32f), "刷新", UIBuild.Card, 24f);
            var bConfirm = UIBuild.MakeBtn(rt, "Btn_Confirm", new Vector2(1f, 0f),
                new Vector2(170f, 52f), new Vector2(-20f, 32f), "确定", UIBuild.Gold, 24f);

            UIBuild.Bind(comp, "_tmpTitle", title);
            UIBuild.Bind(comp, "_tmpHint", hint);
            UIBuild.Bind(comp, "_tmpSelected", sel);
            UIBuild.Bind(comp, "_tmpEggCost", egg);
            UIBuild.Bind(comp, "_scrollContent", content);
            UIBuild.Bind(comp, "_cardTemplate", cardTpl.gameObject);
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
