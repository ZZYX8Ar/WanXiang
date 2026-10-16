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
//
//  ★★ 第二版（2026-10-16 截图后重做）：第一版把这面板做成 1080×720 的**居中弹窗**
//   （FullScreen=false），结果三处都崩：
//     ① 底下露出主城的标题与按钮，看着像"两张界面叠在一起"；
//     ② 小地图按 layers*(NodeH+GapY)+40 摆进 560 高的卡里 ⇒ 节点溢出到卡外；
//     ③ 没有连线层 —— 主线是"路线图"，秘境只剩一堆孤立方块，观感完全不同。
//   现在：全屏面板 + 左侧场域卡 + 右侧自适应小地图 + 与主线同款的虚线/实线连线。
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

        // ---- 全屏布局常量（1920×1080 参考分辨率；CanvasScaler = ScaleWithScreenSize 1920×1080）----
        private const float RefW = 1920f, RefH = 1080f;
        private const float Margin = 60f;          // 外边距
        private const float TopBarH = 108f;        // 标题 + 状态行占用的高度
        private const float BottomBarH = 132f;     // 底部按钮条
        private const float InfoW = 420f;          // 左侧场域卡宽
        private const float GapMid = 40f;          // 左右两栏之间的缝

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

            // ---- 根：铺满整屏（FullScreen=true 时框架也会 StretchRect，两边一致最省心）----
            var root = Place(Null(), "Panel_Realm", new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero);
            //⚠ Stretch 布局下 pivot 无意义，但 anchoredPosition/sizeDelta 必须为 0。
            var comp = root.gameObject.AddComponent<RealmPanel>();
            UIBuild.Img(root, new Color(0.06f, 0.05f, 0.08f, 0.94f));

            // ---- 标题与状态行（顶部通栏）----
            var title = UIBuild.Tmp(Place(root, "Tmp_Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(1200f, 56f), new Vector2(0f, -Margin * 0.42f)),
                "秘境", 34, UIBuild.Ink, TextAlignmentOptions.Center);
            var status = UIBuild.Tmp(Place(root, "Tmp_Status", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(1200f, 32f), new Vector2(0f, -Margin * 0.42f - 62f)),
                "第 0 / 3 波　·　星币 0", 20, UIBuild.Ink2, TextAlignmentOptions.Center);

            // ---- 左：场域卡（效果 / 收益 / 代价）----
            float bodyTop = -(TopBarH);
            float bodyH = RefH - TopBarH - BottomBarH;
            var info = Place(root, "Root_RealmInfo", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(InfoW, bodyH), new Vector2(Margin, bodyTop));
            UIBuild.Img(info, UIBuild.Card, true);

            //⚠ 每行给足高度：这四行都是会换行的长句（18px 中文在 InfoW-64 宽里能折两三行），
            //   高度不够就会**压到下一行**上（第一版踩过，截图才看出来）。
            float padX = 24f, innerW = InfoW - padX * 2f;
            var infoName = UIBuild.Tmp(Place(info, "Tmp_RealmName", new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(innerW, 76f), new Vector2(padX, -24f)),
                "苍泽", 30, UIBuild.Ink, TextAlignmentOptions.TopLeft);
            var infoHazard = UIBuild.Tmp(Place(info, "Tmp_Hazard", new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(innerW, 132f), new Vector2(padX, -108f)),
                "场地：每回合敌方全体「湿」1 层", 18, UIBuild.Red, TextAlignmentOptions.TopLeft);
            var infoBoon = UIBuild.Tmp(Place(info, "Tmp_Boon", new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(innerW, 108f), new Vector2(padX, -248f)),
                "收益：产出 ×1.5", 18, UIBuild.Ink, TextAlignmentOptions.TopLeft);
            var infoCost = UIBuild.Tmp(Place(info, "Tmp_Cost", new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(innerW, 132f), new Vector2(padX, -364f)),
                "代价：全程速度 -10%", 18, UIBuild.Ink2, TextAlignmentOptions.TopLeft);

            // ---- 右：小地图卡（自适应；内部连线层 + 节点层）----
            float mapX = Margin + InfoW + GapMid;
            float mapW = RefW - mapX - Margin;
            var mapRoot = Place(root, "Root_Map", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(mapW, bodyH), new Vector2(mapX, bodyTop));
            UIBuild.Img(mapRoot, UIBuild.Card, true);

            // 连线层：**必须先建**（后建的节点会盖在它上面）
            var lineLayer = Place(mapRoot, "LineLayer", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);

            var grid = Place(mapRoot, "Grid_Nodes", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(mapW - 48f, bodyH - 48f), Vector2.zero);

            // 节点模板：底板 + 图标字 + 名称 + 已清标记（运行时只克隆这个）
            //⚠ 尺寸 = 面板里的**设计基准**（256×160）。运行时按地图实际层数/列数**自适应缩放**
            //   （见 RealmPanel.FitNodeMetrics），所以这里给大一点没关系。
            var nodeTpl = Place(grid, "Item_NodeTemplate", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(256f, 160f), Vector2.zero);
            UIBuild.Img(nodeTpl, UIBuild.Card, true);
            UIBuild.Btn(nodeTpl);       // ⛔ 格子必须可点，否则"点了没反应"
            var nodeIcon = UIBuild.Tmp(Place(nodeTpl, "Tmp_NodeIcon", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(240f, 74f), new Vector2(0f, -34f)),
                "战", 52, UIBuild.Ink, TextAlignmentOptions.Center);
            var nodeName = UIBuild.Tmp(Place(nodeTpl, "Tmp_NodeName", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(240f, 40f), new Vector2(0f, 30f)),
                "遭遇", 28, UIBuild.Ink2, TextAlignmentOptions.Center);
            var nodeMark = UIBuild.Img(Place(nodeTpl, "Img_Cleared", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(256f, 160f), Vector2.zero), UIBuild.Gold);
            nodeMark.raycastTarget = false;
            nodeMark.gameObject.SetActive(false);     // 默认隐藏，抵达后才点亮
            nodeTpl.gameObject.SetActive(false);       // 模板自身隐藏，运行时只克隆

            // ---- 底部：进入 / 暂不进入 / 换一个 / 撤离 ----
            //⛔ 全屏布局下按钮一律按**底边**摆（anchor (0.5,0)/(0,0)/(1,0)），
            //   别再用上一版的"居中弹窗底部"，那是 720 高画布的坐标，放到 1080 上会浮在半空。
            float btnY = 44f;
            var enter = MakeBtn(root, "Btn_Enter", new Vector2(0.5f, 0f),
                new Vector2(300f, 84f), new Vector2(-164f, btnY), "进入秘境", UIBuild.Silk, 28f);
            var leave = MakeBtn(root, "Btn_Leave", new Vector2(0.5f, 0f),
                new Vector2(300f, 84f), new Vector2(164f, btnY), "暂不进入", UIBuild.Card, 28f);
            var reroll = MakeBtn(root, "Btn_Reroll", new Vector2(0f, 0f),
                new Vector2(230f, 56f), new Vector2(Margin, btnY + 14f), "换一个（免费 1 次）", UIBuild.Card, 20f);
            var quit = MakeBtn(root, "Btn_Quit", new Vector2(0.5f, 0f),
                new Vector2(280f, 72f), new Vector2(0f, btnY + 6f), "撤离秘境", UIBuild.Card, 24f);
            quit.gameObject.SetActive(false);          // 进场后才显示

            UIBuild.Bind(comp, "_tmpTitle", title);
            UIBuild.Bind(comp, "_tmpStatus", status);
            UIBuild.Bind(comp, "_infoRoot", info);
            UIBuild.Bind(comp, "_tmpRealmName", infoName);
            UIBuild.Bind(comp, "_tmpHazard", infoHazard);
            UIBuild.Bind(comp, "_tmpBoon", infoBoon);
            UIBuild.Bind(comp, "_tmpCost", infoCost);
            UIBuild.Bind(comp, "_mapRoot", mapRoot);
            UIBuild.Bind(comp, "_lineLayer", lineLayer);
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
            if (anchorMin == Vector2.zero && anchorMax == Vector2.one)
            {
                // Stretch：sizeDelta/anchoredPosition 都要 0，否则会多出一圈边距。
                rt.sizeDelta = Vector2.zero;
                rt.anchoredPosition = Vector2.zero;
            }
            else
            {
                rt.sizeDelta = size;
                rt.anchoredPosition = pos;
            }
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