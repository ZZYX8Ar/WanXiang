// ============================================================================
//  万相 · UI 补丁：灵市（Panel_Market）的详情卡与「买魂」按钮
//  ---------------------------------------------------------------------------
//  背景：这两个控件原来是**运行时从零建**的（不在 prefab 里），导致
//        用户在 Editor 里看不到、也改不了。按项目约定，UI 应当落在 prefab。
//
//  本脚本的做法（**增量补丁，不是重建**）：
//    ① 载入现有 Panel_Market.prefab（保留用户的一切手工调整）
//    ② 若缺 Market_DetailCard / Btn_BuySoul 就补上（已存在则跳过 ⇒ 可反复跑）
//    ③ 存回同一个 prefab
//
//  之后 MarketPanel 里对应的字段改由 prefab 注入，代码只负责填数据。
// ============================================================================

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace WanXiang.EditorTools
{
    public static class UIBuildMarketPatch
    {
        private const string PrefabPath = "Assets/Resources/UI/Panel_Market.prefab";

        [MenuItem("WanXiang/UI/补丁：灵市详情卡与买魂按钮", priority = 103)]
        internal static void Patch()
        {
            if (!System.IO.File.Exists(PrefabPath))
            {
                Debug.LogError("[MarketPatch] 找不到 " + PrefabPath + "，请先跑 WanXiang/UI/生成面板预制体");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null)
            {
                Debug.LogError("[MarketPatch] 载入 prefab 失败");
                return;
            }
            try
            {
                bool changed = false;
                changed |= EnsureBuySoulButton(root);
                changed |= EnsureDetailCard(root);

                // 即使节点已存在，也重跑一次绑定（幂等：把节点赋给 MarketPanel 的字段）
                BindFields(root);

                if (!changed)
                {
                    Debug.Log("[MarketPatch] 节点已存在，仅重做字段绑定");
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.Refresh();
                Debug.Log("[MarketPatch] OK 已补丁 " + PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>「买魂 · 5 灵卵」按钮（挂在刷新按钮下方；找不到刷新按钮就放右下角）。</summary>
        private static bool EnsureBuySoulButton(GameObject root)
        {
            if (FindDeep(root.transform, "Btn_BuySoul") != null) return false;

            var refresh = FindDeep(root.transform, "Btn_Refresh");
            Transform parent = refresh != null ? refresh.parent : root.transform;

            var go = NewNode(parent, "Btn_BuySoul");
            var rt = (RectTransform)go.transform;
            if (refresh != null)
            {
                var rr = (RectTransform)refresh;
                rt.anchorMin = rr.anchorMin; rt.anchorMax = rr.anchorMax; rt.pivot = rr.pivot;
                rt.sizeDelta = rr.sizeDelta;
                rt.anchoredPosition = rr.anchoredPosition + new Vector2(0f, -92f);
            }
            else
            {
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
                rt.pivot = new Vector2(1f, 0f);
                rt.sizeDelta = new Vector2(240f, 64f);
                rt.anchoredPosition = new Vector2(-40f, 40f);
            }
            go.AddComponent<Image>().color = UIBuild.Hex("#CCBD8F");
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = go.GetComponent<Image>();

            var label = NewText(go.transform, "Tmp_Label", "买魂 · 5 灵卵", 24,
                                TextAlignmentOptions.Center, UIBuild.Ink);
            Fill(label);

            Debug.Log("[MarketPatch] + Btn_BuySoul");
            return true;
        }

        /// <summary>商品详情卡（默认隐藏；点货架时由代码 SetActive(true) 并填数据）。</summary>
        private static bool EnsureDetailCard(GameObject root)
        {
            if (FindDeep(root.transform, "Market_DetailCard") != null) return false;

            // 遮罩（点击关闭）
            var mask = NewNode(root.transform, "Market_DetailMask");
            var mrt = (RectTransform)mask.transform;
            mrt.anchorMin = Vector2.zero; mrt.anchorMax = Vector2.one;
            mrt.offsetMin = Vector2.zero; mrt.offsetMax = Vector2.zero;
            var mimg = mask.AddComponent<Image>();
            mimg.color = new Color(0f, 0f, 0f, 0.45f);
            var mbtn = mask.AddComponent<Button>();
            mbtn.targetGraphic = mimg;

            // 卡片
            var card = NewNode(mask.transform, "Market_DetailCard");
            var crt = (RectTransform)card.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(760f, 560f);
            crt.anchoredPosition = Vector2.zero;
            card.AddComponent<Image>().color = UIBuild.Card;
            var cbtn = card.AddComponent<Button>();
            cbtn.targetGraphic = card.GetComponent<Image>();

            // 立绘
            var big = NewNode(card.transform, "Img_Big");
            var brt = (RectTransform)big.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0f, 0.5f);
            brt.pivot = new Vector2(0f, 0.5f);
            brt.sizeDelta = new Vector2(300f, 300f);
            brt.anchoredPosition = new Vector2(30f, 60f);
            var bimg = big.AddComponent<Image>();
            bimg.preserveAspect = true;
            bimg.raycastTarget = false;

            // 文本
            var name = NewText(card.transform, "Tmp_Name", "异兽名", 40, TextAlignmentOptions.Left, UIBuild.Ink);
            PlaceTopLeft(name, 350f, -60f, 420f, 56f);
            var info = NewText(card.transform, "Tmp_Info", "五行 · 定位 · 品阶", 26, TextAlignmentOptions.Left, UIBuild.Ink);
            PlaceTopLeft(info, 350f, -128f, 420f, 48f);
            var src = NewText(card.transform, "Tmp_Source", "", 22, TextAlignmentOptions.TopLeft, UIBuild.Ink2);
            PlaceTopLeft(src, 330f, -206f, 430f, 190f);
            src.enableWordWrapping = true;
            var price = NewText(card.transform, "Tmp_Price", "价格：0 灵卵", 30, TextAlignmentOptions.Left, UIBuild.Ink);
            PlaceBottomLeft(price, 350f, 128f, 420f, 48f);

            // 按钮
            var buy = NewButton(card.transform, "Btn_Buy", "购买", new Vector2(0f, 0f), new Vector2(200f, 72f),
                                new Vector2(400f, 64f), UIBuild.Gold);
            var close = NewButton(card.transform, "Btn_Close", "关闭", new Vector2(0f, 0f), new Vector2(160f, 72f),
                                  new Vector2(180f, 64f), UIBuild.Silk);

            mask.SetActive(false);      // 默认隐藏
            Debug.Log("[MarketPatch] + Market_DetailMask / Market_DetailCard（含 7 个文本与 2 个按钮）");
            return true;
        }

        /// <summary>
        /// 把 prefab 里的节点赋给 MarketPanel 的 [SerializeField] 字段。
        /// ⚠ 只加节点不绑定 = 运行时字段仍然是 null，详情卡不会显示（必须做这一步）。
        /// </summary>
        private static void BindFields(GameObject root)
        {
            var panel = root.GetComponent<WanXiang.Modules.UI.MarketPanel>();
            if (panel == null)
            {
                Debug.LogError("[MarketPatch] Panel_Market 上没有 MarketPanel 组件，无法绑定");
                return;
            }

            var so = new SerializedObject(panel);
            SetRef(so, "_detailRoot", FindDeep(root.transform, "Market_DetailMask"));
            SetRef(so, "_detailBig", FindDeep(root.transform, "Img_Big"));
            SetRef(so, "_detailName", FindDeep(root.transform, "Tmp_Name"));
            SetRef(so, "_detailInfo", FindDeep(root.transform, "Tmp_Info"));
            SetRef(so, "_detailSource", FindDeep(root.transform, "Tmp_Source"));
            SetRef(so, "_detailPrice", FindDeep(root.transform, "Tmp_Price"));
            SetRef(so, "_detailBuy", FindDeep(root.transform, "Btn_Buy"));
            SetRef(so, "_detailClose", FindDeep(root.transform, "Btn_Close"));
            SetRef(so, "_btnBuySoul", FindDeep(root.transform, "Btn_BuySoul"));
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[MarketPatch] 字段绑定完成（9 个）");
        }

        private static void SetRef(SerializedObject so, string field, Transform t)
        {
            var p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogWarning("[MarketPatch] MarketPanel 上找不到字段：" + field);
                return;
            }
            p.objectReferenceValue = t != null ? t.gameObject : null;
            if (t == null) Debug.LogWarning("[MarketPatch] 节点缺失，字段置空：" + field);
        }

        // ---- 小工具 ----

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

        private static GameObject NewNode(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static TextMeshProUGUI NewText(Transform parent, string name, string text, int size,
                                               TextAlignmentOptions align, Color color)
        {
            var go = NewNode(parent, name);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.color = color;
            tmp.raycastTarget = false;
            if (UIBuild.Font != null) tmp.font = UIBuild.Font;
            return tmp;
        }

        private static void Fill(TextMeshProUGUI t)
        {
            var rt = (RectTransform)t.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private static void PlaceTopLeft(TextMeshProUGUI t, float x, float y, float w, float h)
        {
            var rt = (RectTransform)t.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
        }

        private static void PlaceBottomLeft(TextMeshProUGUI t, float x, float y, float w, float h)
        {
            var rt = (RectTransform)t.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
        }

        private static GameObject NewButton(Transform parent, string name, string label,
                                            Vector2 anchor, Vector2 size, Vector2 pos, Color color)
        {
            var go = NewNode(parent, name);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            go.AddComponent<Image>().color = color;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = go.GetComponent<Image>();
            var t = NewText(go.transform, "Tmp_Label", label, 28, TextAlignmentOptions.Center, UIBuild.Ink);
            Fill(t);
            return go;
        }
    }
}
#endif
