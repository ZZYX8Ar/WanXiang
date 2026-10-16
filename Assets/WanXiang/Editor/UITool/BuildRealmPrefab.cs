// ============================================================================
//  Panel_Realm —— 秘境面板预制体构建器（2026-10-16）
//  ---------------------------------------------------------------------------
//  面板有两块（对应设计文档第 5 章的两种界面）：
//    ① 入口三选一：问号节点揭晓为"秘境"时弹 —— 显示场域的 **效果/收益/代价** 三行，
//       必须让玩家**进之前就看清**（不允许"进去了才知道有什么"）。
//    ② 秘境内小地图：格子推进，底部有「撤离」按钮。
//
//  ⚠ UI 一律做成 prefab（项目铁律）：本文件是构建器，不在运行期new GameObject 拼界面；
//   面板运行时只做 Instantiate(_nodeTemplate) / Find / 改 text / SetActive / onClick。
// ============================================================================

using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Modules.UI;

namespace WanXiang.EditorTools
{
    internal static class BuildRealmPrefab
    {
        private const string Dir = "Assets/Resources/UI";

        [MenuItem("WanXiang/UI/生成秘境面板", priority = 102)]
        internal static void Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets/Resources", "UI");

            // 中文字体（Deng SDF；换机把源 TTF 放回 Assets/ArtRes/Fonts 再跑字体菜单）
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/ArtRes/Fonts/Deng SDF.asset");
            if (font == null)
                font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            UIBuild.SetFont(font);

            BuildRealm();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[BuildRealm] Panel_Realm 生成完毕 → " + Dir);
        }

        private static void BuildRealm()
        {
            SafeDelete("Panel_Realm");

            var root = Place(Null(), "Panel_Realm", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(1080f, 720f), Vector2.zero);
            var comp = root.gameObject.AddComponent<RealmPanel>();
            UIBuild.Img(root, new Color(0.06f, 0.05f, 0.08f, 0.94f));

            // ---- 标题与状态行 ----
            var title = UIBuild.Tmp(Place(root, "Tmp_Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(1080f, 48f), new Vector2(0f, -12f)),
                "秘境", 32, UIBuild.Ink, TextAlignmentOptions.Center);
            var status = UIBuild.Tmp(Place(root, "Tmp_Status", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(1080f, 30f), new Vector2(0f, -62f)),
                "第 0 / 3 波　·　星币 0", 19, UIBuild.Ink2, TextAlignmentOptions.Center);

            // ---- 左：场域三行（场地效果 / 收益 / 代价）----
            var info = Place(root, "Root_RealmInfo", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(320f, 300f), new Vector2(24f, -100f));
            UIBuild.Img(info, UIBuild.Card, true);
            //⚠ 每行给足高度：这四行都是会换行的长句（17px 中文在 288 宽里能折两行），
            //   高度不够就会**压到下一行**上（第一版踩过，截图才看出来）。
            var infoName = UIBuild.Tmp(Place(info, "Tmp_RealmName", new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(288f, 56f), new Vector2(16f, -12f)),
                "苍泽", 24, UIBuild.Ink, TextAlignmentOptions.TopLeft);
            var infoHazard = UIBuild.Tmp(Place(info, "Tmp_Hazard", new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(288f, 76f), new Vector2(16f, -74f)),
                "场地：每回合敌方全体「湿」1 层", 17, UIBuild.Red, TextAlignmentOptions.TopLeft);
            var infoBoon = UIBuild.Tmp(Place(info, "Tmp_Boon", new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(288f, 60f), new Vector2(16f, -156f)),
                "收益：产出 ×1.5", 17, UIBuild.Ink, TextAlignmentOptions.TopLeft);
            var infoCost = UIBuild.Tmp(Place(info, "Tmp_Cost", new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(288f, 76f), new Vector2(16f, -222f)),
                "代价：全程速度 -10%", 17, UIBuild.Ink2, TextAlignmentOptions.TopLeft);

            // ---- 右：小地图（GridLayoutGroup + 节点模板）----
            //   节点数最多 25（5×5）⇒ 5 列 × 5 行；格子 92px、间距 12px，5×92+4×12 = 508 ≤ 520。
            var mapRoot = Place(root, "Root_Map", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(560f, 560f), new Vector2(-24f, -100f));
            UIBuild.Img(mapRoot, UIBuild.Card, true);
            var grid = Place(mapRoot, "Grid_Nodes", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(520f, 520f), Vector2.zero);
            var glg = grid.gameObject.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(92f, 92f);
            glg.spacing = new Vector2(12f, 12f);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 5;
            glg.childAlignment = TextAnchor.MiddleCenter;

            // 节点模板：底板 + 图标字 + 名称 + 已清标记（运行时只克隆这个）
            var nodeTpl = Place(grid, "Item_NodeTemplate", Vector2.zero, Vector2.zero,
                new Vector2(92f, 92f), Vector2.zero);
            UIBuild.Img(nodeTpl, UIBuild.Card, true);
            UIBuild.Btn(nodeTpl);       // ⛔ 格子必须可点，否则"点了没反应"
            var nodeIcon = UIBuild.Tmp(Place(nodeTpl, "Tmp_NodeIcon", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(84f, 36f), new Vector2(0f, 10f)),
                "战", 30, UIBuild.Ink, TextAlignmentOptions.Center);
            var nodeName = UIBuild.Tmp(Place(nodeTpl, "Tmp_NodeName", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(84f, 22f), new Vector2(0f, 6f)),
                "遭遇", 17, UIBuild.Ink2, TextAlignmentOptions.Center);
            var nodeMark = UIBuild.Img(Place(nodeTpl, "Img_Cleared", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(84f, 84f), Vector2.zero), UIBuild.Gold);
            nodeMark.raycastTarget = false;
            nodeMark.gameObject.SetActive(false);     // 默认隐藏，抵达后才点亮
            nodeTpl.gameObject.SetActive(false);       // 模板自身隐藏，运行时只克隆

            // ---- 底部：进入 / 暂不进入 / 换一个 ----
            var enter = MakeBtn(root, "Btn_Enter", new Vector2(0.5f, 0f),
                new Vector2(260f, 76f), new Vector2(-136f, 40f), "进入秘境", UIBuild.Silk, 26f);
            var leave = MakeBtn(root, "Btn_Leave", new Vector2(0.5f, 0f),
                new Vector2(260f, 76f), new Vector2(136f, 40f), "暂不进入", UIBuild.Card, 26f);
            var reroll = MakeBtn(root, "Btn_Reroll", new Vector2(0f, 0f),
                new Vector2(190f, 48f), new Vector2(24f, 44f), "换一个（免费 1 次）", UIBuild.Card, 18f);

            // ---- 秘境内才出现的：撤离 ----
            var quit = MakeBtn(root, "Btn_Quit", new Vector2(1f, 0f),
                new Vector2(210f, 60f), new Vector2(-24f, 42f), "撤离秘境", UIBuild.Card, 22f);
            quit.gameObject.SetActive(false);          // 进场后才显示

            UIBuild.Bind(comp, "_tmpTitle", title);
            UIBuild.Bind(comp, "_tmpStatus", status);
            UIBuild.Bind(comp, "_infoRoot", info);
            UIBuild.Bind(comp, "_tmpRealmName", infoName);
            UIBuild.Bind(comp, "_tmpHazard", infoHazard);
            UIBuild.Bind(comp, "_tmpBoon", infoBoon);
            UIBuild.Bind(comp, "_tmpCost", infoCost);
            UIBuild.Bind(comp, "_mapRoot", mapRoot);
            UIBuild.Bind(comp, "_gridContent", grid);
            UIBuild.Bind(comp, "_nodeTemplate", nodeTpl.gameObject);
            UIBuild.Bind(comp, "_tmpNodeIconTemplate", nodeIcon);
            UIBuild.Bind(comp, "_tmpNodeNameTemplate", nodeName);
            UIBuild.Bind(comp, "_imgClearedTemplate", nodeMark.GetComponent<Image>());
            UIBuild.Bind(comp, "_btnEnter", enter.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnLeave", leave.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnReroll", reroll.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnQuit", quit.GetComponent<Button>());

            UIBuild.SavePrefab(root.gameObject, "Panel_Realm");
        }

        /// <summary>
        /// 放一个子节点。⚠ 与 <c>UIBuild.Fixed</c> 的区别：**这里把 pivot 设成 anchorMin**。
        ///   UIBuild.Fixed 不动 pivot（默认 0.5），子节点会从锚点往两边各伸一半 ——
        ///   文字就跑到卡片外面去了（第一版踩过，截图才发现）。
        /// </summary>
        private static RectTransform Place(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = anchorMin;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return rt;
        }

        /// <summary>固定尺寸按钮（pivot 跟着锚点，避免文字/按钮错位）。</summary>
        private static RectTransform MakeBtn(Transform parent, string name, Vector2 anchor,
            Vector2 size, Vector2 pos, string label, Color c, float fontSize)
        {
            var rt = Place(parent, name, anchor, anchor, size, pos);
            UIBuild.Img(rt, c, true);
            var label2 = Place(rt, "Tmp_Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            UIBuild.Tmp(label2, label, fontSize, UIBuild.Ink);
            UIBuild.Btn(rt);
            return rt;
        }

        /// <summary>UIBuild 的工具都以 Transform 为 parent，这里给个假父级建根。</summary>
        private static Transform Null()
        {
            var go = new GameObject("_RealmBuilderRoot", typeof(RectTransform));
            return go.transform;
        }

        private static void SafeDelete(string prefabName)
        {
            string path = Dir + "/" + prefabName + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
            AssetDatabase.DeleteAsset(path);
        }
    }
}