// ============================================================================
//  WanXiang · UI 面板预制体批量生成器
//  ---------------------------------------------------------------------------
//  菜单：WanXiang / UI / 生成面板预制体
//  依据《UGUI 拼装规范 v1.0》第 7 章的层级树与绑定表，把 13 个面板一次性生成到
//  Assets/Resources/UI/ 下：节点结构、占位配色、面板脚本、SerializedObject 字段绑定
//  全部就位 —— 用户只需替换美术与微调布局，不需要再拖 Inspector 引用。
//
//  ⚠ 重复执行会整体重建同名 Prefab（手工改动会被覆盖），改结构请改本文件。
// ============================================================================

using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Battle.Presentation;
using WanXiang.Framework.UI;
using WanXiang.Fusion;
using WanXiang.Modules.UI;

namespace WanXiang.EditorTools
{
    internal static class UIPanelPrefabBuilder
    {
        private const string ContentCatalogPath = "Assets/WanXiang/Config/ContentCatalog.asset";
        private const string SpriteCatalogPath = "Assets/WanXiang/Config/SpriteCatalog.asset";
        private const string ArtParts = "Assets/ArtRes/UI/Parts/";

        /// <summary>缺资产时返回 null（面板仍能生成，只是那项引用为空）。</summary>
        private static T LoadAsset<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) Debug.LogWarning("[PrefabBuilder] 资产缺失：" + path);
            return asset;
        }

        private static GameObject NewPanelRoot(string name, bool fullscreen, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            if (fullscreen)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            else
            {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = size;
            }
            return go;
        }

        [MenuItem("WanXiang/UI/生成面板预制体", priority = 100)]
        internal static void BuildAll()
        {
            EnsureFolders();
            ImportTmpEssentials();

            BuildStart();
            BuildSave();
            BuildHome();
            BuildCampaign();
            BuildFormation();
            BuildBattle();
            BuildResult();
            BuildMarket();
            BuildTale();
            BuildOmen();
            BuildForge();
            BuildCodex();
            BuildMeta();
            BuildTrial();
            BuildSettings();
            BuildDialog();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[PrefabBuilder] " + "16 个面板预制体生成完毕 → Assets/Resources/UI/");
        }

        // ==================================================================
        // 环境准备
        // ==================================================================

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/UI"))
                AssetDatabase.CreateFolder("Assets/Resources", "UI");
        }

        /// <summary>导入 TMP Essentials（静默），并把 LiberationSans SDF 设为生成文本的默认字体。</summary>
        private static void ImportTmpEssentials()
        {
            var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(
                "Assets/TextMesh Pro/Resources/TMP Settings.asset");
            if (settings == null)
            {
                string pkg = "Packages/com.unity.textmeshpro/Package Resources/TMP Essential Resources.unitypackage";
                string abs = System.IO.Path.GetFullPath(pkg);
                if (System.IO.File.Exists(abs))
                {
                    AssetDatabase.ImportPackage(abs, false);
                    AssetDatabase.Refresh();
                }
                else
                {
                    Debug.LogWarning("[PrefabBuilder] 未找到 TMP Essential Resources.unitypackage：" + abs);
                }
            }

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            if (font == null)
                Debug.LogWarning(
                    "[PrefabBuilder] TMP 字体不可用，文本暂无字体（结构不受影响；" +
                    "可手动执行 Window > TextMeshPro > Import TMP Essential Resources）。");
            UIBuild.SetFont(font);
        }

        private static void EnsureFolder(string parent, string leaf)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + leaf))
                AssetDatabase.CreateFolder(parent, leaf);
        }

        // ==================================================================
        // 00 Panel_Start —— 开始界面（标题画面）
        // ==================================================================

        private static void BuildStart()
        {
            var root = NewPanelRoot("Panel_Start", true, Vector2.zero);
            var comp = root.AddComponent<StartPanel>();
            var rt = (RectTransform)root.transform;

            // 标题：卷轴底托 + 大标题 + 英文小字 + 题记
            var scrollBg = UIBuild.Top(rt, "Img_TitleScroll", 200, 520, 520, 110);
            UIBuild.Img(scrollBg, UIBuild.Gold);
            var tmpTitle = UIBuild.Tmp(UIBuild.Stretch(scrollBg, "Tmp_Title", 20, 60, 20, 76),
                "万相", 120, UIBuild.Ink, TextAlignmentOptions.Center);
            var tmpTitleEn = UIBuild.Tmp(UIBuild.Stretch(scrollBg, "Tmp_TitleEn", 20, 130, 20, 20),
                "M Y R I A D", 30, UIBuild.Ink2, TextAlignmentOptions.Center);
            var tmpTagline = UIBuild.Tmp(UIBuild.Top(rt, "Tmp_Tagline", 70, 520, 520, 330),
                "山海万相，皆由我生", 34, UIBuild.Ink, TextAlignmentOptions.Center);

            // 菜单：开始 / 设置 / 退出（竖排居中偏下）
            var bStart = UIBuild.MakeBtn(rt, "Btn_Start", new Vector2(0.5f, 0f),
                new Vector2(560, 140), new Vector2(0, 330), "开始游戏", UIBuild.Gold, 46f);
            var bSettings = UIBuild.MakeBtn(rt, "Btn_Settings", new Vector2(0.5f, 0f),
                new Vector2(460, 110), new Vector2(0, 200), "设 置", UIBuild.Card, 36f);
            var bQuit = UIBuild.MakeBtn(rt, "Btn_Quit", new Vector2(0.5f, 0f),
                new Vector2(460, 110), new Vector2(0, 80), "退 出", UIBuild.Card, 36f);

            var ver = UIBuild.Fixed(rt, "Tmp_Version", new Vector2(0f, 0f),
                new Vector2(420, 36), new Vector2(130, 24));
            UIBuild.Tmp(ver, "v0.1", 22, UIBuild.Ink2, TextAlignmentOptions.Left);

            UIBuild.Bind(comp, "_tmpTitle", tmpTitle);
            UIBuild.Bind(comp, "_tmpTitleEn", tmpTitleEn);
            UIBuild.Bind(comp, "_tmpTagline", tmpTagline);
            UIBuild.Bind(comp, "_tmpVersion", ver);
            UIBuild.Bind(comp, "_btnStart", bStart.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnSettings", bSettings.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnQuit", bQuit.GetComponent<Button>());
            UIBuild.SavePrefab(root, "Panel_Start");
        }

        // ==================================================================
        // 02 Panel_Save —— 存档选择（开始游戏后进来）
        //  ------------------------------------------------------------------
        //  三个槽位一行一档：空档 = 开新程（从头开始）；有档 = 继承。
        //  没有 Panel_Save 的专属概念稿 → 底图用天阙远景云台层（美术应用器兜底）。
        // ==================================================================

        private static void BuildSave()
        {
            var root = NewPanelRoot("Panel_Save", true, Vector2.zero);
            var comp = root.AddComponent<SavePanel>();
            var rt = (RectTransform)root.transform;

            var title = UIBuild.Fixed(rt, "Tmp_Title", new Vector2(0.5f, 1f),
                new Vector2(900, 110), new Vector2(0, -70));
            UIBuild.Tmp(title, "万相 · 选择旅程", 60, UIBuild.Ink, TextAlignmentOptions.Center);
            var sub = UIBuild.Fixed(rt, "Tmp_Sub", new Vector2(0.5f, 1f),
                new Vector2(900, 44), new Vector2(0, -180));
            UIBuild.Tmp(sub, "三段旅程，各自生长 —— 空档从头开始，有档接着走", 26, UIBuild.Ink2, TextAlignmentOptions.Center);

            // 三个槽位（空档/有档由运行期文案区分）
            var slots = new RectTransform[3];
            var slotDetails = new TextMeshProUGUI[3];
            var slotBtns = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                var slotRt = UIBuild.MakeBtn(rt, "Btn_Slot" + i, new Vector2(0.5f, 0.5f),
                    new Vector2(1200, 176), new Vector2(0, 760 - i * 210), "", UIBuild.Card);
                slots[i] = slotRt;
                slotBtns[i] = slotRt.GetComponent<Button>();
                var detail = UIBuild.Fixed(slotRt, "Tmp_Detail", new Vector2(0.5f, 0.5f),
                    new Vector2(1120, 120), Vector2.zero);
                slotDetails[i] = UIBuild.Tmp(detail, "存档 " + (i + 1) + " · 空档\n从这里开始一段新的旅程",
                    26, UIBuild.Ink, TextAlignmentOptions.Left);
            }

            var bNew = UIBuild.MakeBtn(rt, "Btn_New", new Vector2(0.5f, 0f),
                new Vector2(460, 130), new Vector2(0, 120), "新的旅程", UIBuild.Gold, 40f);
            var bBack = UIBuild.MakeBtn(rt, "Btn_Back", new Vector2(0f, 1f),
                new Vector2(220, 96), new Vector2(70, -60), "返回", UIBuild.Card, 28f);
            var hint = UIBuild.Fixed(rt, "Tmp_Hint", new Vector2(0.5f, 0f),
                new Vector2(1100, 40), new Vector2(0, 60));
            UIBuild.Tmp(hint, "旅程保存在本机；同档覆盖前请确认", 20, UIBuild.Ink2, TextAlignmentOptions.Center);

            UIBuild.Bind(comp, "_btnNew", bNew.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnBack", bBack.GetComponent<Button>());
            UIBuild.BindArr(comp, "_btnSlots", slotBtns);
            UIBuild.BindArr(comp, "_tmpSlots", slotDetails);
            UIBuild.Bind(comp, "_tmpHint", hint.GetComponent<TMP_Text>());
            UIBuild.SavePrefab(root, "Panel_Save");
        }

        // ==================================================================
        // 03 Panel_Home —— 主界面
        //  ------------------------------------------------------------------
        //  ⚠ 主界面的按钮位置**不是随便定的**：底图 Panel_Home.png 上已经画好了
        //    金色圆形主按钮（右下）与底排 8 个功能图标。这里的功能节点一律用
        //    「透明 Image + raycastTarget」盖在画好的热区上，图看着还是美术画的，
        //    点下去却真的响应 —— 改位置就是改下面这几个常量（单位：1920×1080，
        //    y 从屏幕底往上算；数值由底图量出来，见 memory 2026-09-16 追加段）。
        // ==================================================================

        private const float HomeCircleX = 1773f;    // 金色主按钮圆心
        private const float HomeCircleY = 173f;
        private const float HomeCircleSize = 340f;
        private const float HomeIconY = 150f;       // 底排图标中心高度
        private const float HomeIconSize = 104f;

        /// <summary>底排 8 个图标的圆心 x（从左到右）。</summary>
        private static readonly float[] HomeIconX = { 188f, 393f, 593f, 803f, 990f, 1118f, 1313f, 1515f };

        private static void BuildHome()
        {
            var root = NewPanelRoot("Panel_Home", true, Vector2.zero);
            var comp = root.AddComponent<HomePanel>();
            var rt = (RectTransform)root.transform;

            var top = UIBuild.Top(rt, "Root_TopBar", 96);
            UIBuild.Img(top, UIBuild.Silk);
            var tmpEggs = UIBuild.Tmp(UIBuild.Fixed(top, "Tmp_Eggs", new Vector2(0f, 0.5f),
                new Vector2(260, 48), new Vector2(150, 0)), "灵卵 0", 30, UIBuild.Ink, TextAlignmentOptions.Left);
            var tmpInk = UIBuild.Tmp(UIBuild.Fixed(top, "Tmp_Ink", new Vector2(0f, 0.5f),
                new Vector2(260, 48), new Vector2(440, 0)), "墨锭 0", 30, UIBuild.Ink, TextAlignmentOptions.Left);
            var badge = UIBuild.Fixed(top, "Img_RealmBadge", new Vector2(1f, 0.5f),
                new Vector2(84, 84), new Vector2(-60, 0));
            UIBuild.Img(badge, UIBuild.Gold);

            var hero = UIBuild.Stretch(rt, "Root_Hero", 380, 140, 560, 230);
            UIBuild.Img(hero, new Color(1f, 1f, 1f, 0f));
            var podium = UIBuild.Bottom(hero, "Img_HeroPodium", 110);
            UIBuild.Img(podium, UIBuild.Dim);
            var sprite = UIBuild.Center(hero, "Img_HeroSprite", new Vector2(560, 560));
            var imgHero = UIBuild.Img(sprite, UIBuild.Card);

            // ---- 底排功能图标（透明热区盖在底图画好的图标上）----
            var bCodex = Hotspot(rt, "Hot_Codex", HomeIconX[1], HomeIconY, HomeIconSize, "图鉴");
            var bMeta = Hotspot(rt, "Hot_Meta", HomeIconX[2], HomeIconY, HomeIconSize, "成长");
            var bMarket = Hotspot(rt, "Hot_Market", HomeIconX[3], HomeIconY, HomeIconSize, "灵市");
            var bSettings = Hotspot(rt, "Hot_Settings", HomeIconX[7], HomeIconY, HomeIconSize, "设置");
            // 底排其余四个：亭子→异闻 / 刀剑→试炼 / 双人→铸魂台 / 卷轴→天象
            // （映射是"按图标形态猜的"，用户改主意只改这一行）
            var bTale = Hotspot(rt, "Hot_Tale", HomeIconX[0], HomeIconY, HomeIconSize, "异闻");
            var bTrial = Hotspot(rt, "Hot_Trial", HomeIconX[4], HomeIconY, HomeIconSize, "试炼");
            var bForge = Hotspot(rt, "Hot_Forge", HomeIconX[5], HomeIconY, HomeIconSize, "铸魂");
            var bOmen = Hotspot(rt, "Hot_Omen", HomeIconX[6], HomeIconY, HomeIconSize, "天象");

            // ---- 右下金色圆：出征（主按钮）----
            var bDeploy = UIBuild.Fixed(rt, "Hot_Deploy", new Vector2(1f, 0f),
                new Vector2(HomeCircleSize, HomeCircleSize), new Vector2(HomeCircleX - 1920f, HomeCircleY));
            UIBuild.Img(bDeploy, new Color(1f, 1f, 1f, 0f), true);
            UIBuild.Btn(bDeploy);
            var deployLabel = UIBuild.Fixed(bDeploy, "Tmp_Label", new Vector2(0.5f, 0f),
                new Vector2(220, 56), new Vector2(0f, 60f));
            UIBuild.Tmp(deployLabel, "出征", 44, UIBuild.Ink, TextAlignmentOptions.Center);

            var ver = UIBuild.Fixed(rt, "Tmp_Version", new Vector2(0f, 0f),
                new Vector2(420, 36), new Vector2(130, 24));
            UIBuild.Tmp(ver, "v0.1  结构验证版", 20, UIBuild.Ink2, TextAlignmentOptions.Left);

            UIBuild.Bind(comp, "_tmpEggs", tmpEggs);
            UIBuild.Bind(comp, "_tmpInk", tmpInk);
            UIBuild.Bind(comp, "_imgHero", imgHero);

            // ⚠ 这九条以前是漏的 —— 按钮全都 Bind 不上（字段为 null），
            //   表现就是「主界面的按钮点了没反应」，而且没有任何报错（用户实测抓到）。
            UIBuild.Bind(comp, "_btnDeploy", bDeploy);
            UIBuild.Bind(comp, "_btnCodex", bCodex);
            UIBuild.Bind(comp, "_btnMeta", bMeta);
            UIBuild.Bind(comp, "_btnMarket", bMarket);
            UIBuild.Bind(comp, "_btnTale", bTale);
            UIBuild.Bind(comp, "_btnTrial", bTrial);
            UIBuild.Bind(comp, "_btnForge", bForge);
            UIBuild.Bind(comp, "_btnOmen", bOmen);
            UIBuild.Bind(comp, "_btnSettings", bSettings);
            UIBuild.Bind(comp, "_btnDeploy", bDeploy.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnCodex", bCodex.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnMeta", bMeta.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnMarket", bMarket.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnSettings", bSettings.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnTale", bTale.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnTrial", bTrial.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnForge", bForge.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnOmen", bOmen.GetComponent<Button>());
            UIBuild.Bind(comp, "_contentCatalog", LoadAsset<ContentCatalogSO>(ContentCatalogPath));
            UIBuild.Bind(comp, "_sprites", LoadAsset<SpriteCatalog>(SpriteCatalogPath));
            UIBuild.SavePrefab(root, "Panel_Home");
        }

        /// <summary>
        /// 透明热区：盖在底图画好的图标/按钮上。
        /// 图是美术画的（透明 Image 不遮画面），命中判定与标签由这里给。
        ///
        /// ⚠ 命名必须用 Hot_ 前缀，不能用 Btn_：
        ///   美术应用器（UIPanelArtApplier）对 Btn_ 前缀的规则是"挂主/次按钮图"，
        ///   热区一旦叫 Btn_xxx，重跑美术应用就会被贴上一张按钮图把底图盖住，
        ///   而且 Inspector 里看不出是热区。Hot_ = 透明的命中判定区（节点名前缀见拼装规范）。
        /// </summary>
        private static RectTransform Hotspot(Transform parent, string name, float cx, float cy,
                                             float size, string label)
        {
            var rt = UIBuild.Fixed(parent, name, new Vector2(0f, 0f),
                new Vector2(size, size), new Vector2(cx, cy));
            UIBuild.Img(rt, new Color(1f, 1f, 1f, 0f), true);
            UIBuild.Btn(rt);

            if (!string.IsNullOrEmpty(label))
            {
                var lbl = UIBuild.Fixed(rt, "Tmp_Label", new Vector2(0.5f, 0f),
                    new Vector2(120, 30), new Vector2(0f, -12f));
                UIBuild.Tmp(lbl, label, 20, UIBuild.Ink2, TextAlignmentOptions.Center);
            }
            return rt;
        }

        // ==================================================================
        // 02 Panel_Campaign —— 节点地图
        // ==================================================================

        private static void BuildCampaign()
        {
            var root = NewPanelRoot("Panel_Campaign", true, Vector2.zero);
            var comp = root.AddComponent<CampaignPanel>();
            var rt = (RectTransform)root.transform;

            var top = UIBuild.Top(rt, "Root_TopBar", 96);
            UIBuild.Img(top, UIBuild.Silk);
            var tmpAct = UIBuild.Tmp(UIBuild.Fixed(top, "Tmp_ActTitle", new Vector2(0f, 0.5f),
                new Vector2(560, 48), new Vector2(140, 0)), "Act II - Xia", 32, UIBuild.Ink, TextAlignmentOptions.Left);
            var tmpJie = UIBuild.Tmp(UIBuild.Fixed(top, "Tmp_JieCount", new Vector2(1f, 0.5f),
                new Vector2(300, 48), new Vector2(-140, 0)), "Jie 1", 30, UIBuild.Ink2, TextAlignmentOptions.Right);

            var scroll = UIBuild.ScrollVertical(rt, "Scroll_Nodes",
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(24, 210), new Vector2(950, -140));
            var srNodes = scroll.parent.parent.GetComponent<ScrollRect>();
            var nodeTpl = UIBuild.Fixed(scroll.parent.parent, "Item_Node",
                new Vector2(0.5f, 0.5f), new Vector2(860, 120), Vector2.zero);
            nodeTpl.gameObject.SetActive(false);
            UIBuild.Img(nodeTpl, UIBuild.Card, true);
            var nodeDot = UIBuild.Fixed(nodeTpl, "Img_Dot", new Vector2(0f, 0.5f),
                new Vector2(72, 72), new Vector2(60, 0));
            UIBuild.Img(nodeDot, UIBuild.Gold);
            var nodeName = UIBuild.Tmp(UIBuild.Stretch(nodeTpl, "Tmp_NodeText", 130, 20, 120, 50),
                "Node", 28, UIBuild.Ink, TextAlignmentOptions.Left);

            var info = UIBuild.Right(rt, "Root_NodeInfo", 460, 24, 140, 210);
            UIBuild.Img(info, UIBuild.Card);
            var icon = UIBuild.Fixed(info, "Img_NodeIcon", new Vector2(0.5f, 1f),
                new Vector2(140, 140), new Vector2(0, -90));
            UIBuild.Img(icon, UIBuild.Silk);
            var tmpNodeName = UIBuild.Tmp(UIBuild.Stretch(info, "Tmp_NodeName", 24, 250, 24, 190),
                "Node Name", 34, UIBuild.Ink, TextAlignmentOptions.Center);
            var tmpNodeType = UIBuild.Tmp(UIBuild.Stretch(info, "Tmp_NodeType", 24, 200, 24, 260),
                "Type", 26, UIBuild.Ink2, TextAlignmentOptions.Center);
            var tmpWeather = UIBuild.Tmp(UIBuild.Stretch(info, "Tmp_Weather", 30, 300, 30, 120),
                "Weather", 24, UIBuild.Ink2, TextAlignmentOptions.Left);

            var team = UIBuild.Bottom(rt, "Root_TeamPreview", 150, 24, 24, 560);
            UIBuild.Img(team, UIBuild.Card);
            var avatars = new Image[5];
            for (int i = 0; i < 5; i++)
            {
                var slot = UIBuild.Fixed(team, "Img_Avatar_" + i, new Vector2(0f, 0.5f),
                    new Vector2(110, 110), new Vector2(80 + i * 130, 0));
                avatars[i] = UIBuild.Img(slot, UIBuild.Silk);
            }

            var bNext = UIBuild.MakeBtn(rt, "Btn_Next", new Vector2(1f, 0f),
                new Vector2(360, 110), new Vector2(-90, 60), "下一步", UIBuild.Gold, 34f);
            var bBack = UIBuild.MakeBtn(rt, "Btn_Back", new Vector2(1f, 1f),
                new Vector2(96, 96), new Vector2(-50, -50), "返回", UIBuild.Card, 26f);

            UIBuild.Bind(comp, "_tmpActTitle", tmpAct);
            UIBuild.Bind(comp, "_tmpJie", tmpJie);
            UIBuild.Bind(comp, "_scrollNodes", srNodes);
            UIBuild.Bind(comp, "_nodeItemTemplate", nodeTpl);
            UIBuild.Bind(comp, "_rootNodeInfo", info.gameObject);
            UIBuild.Bind(comp, "_tmpNodeName", tmpNodeName);
            UIBuild.Bind(comp, "_tmpNodeType", tmpNodeType);
            UIBuild.Bind(comp, "_tmpWeather", tmpWeather);
            UIBuild.Bind(comp, "_btnNext", bNext.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnBack", bBack.GetComponent<Button>());
            UIBuild.BindArr(comp, "_imgAvatars", avatars);
            UIBuild.Bind(comp, "_contentCatalog", LoadAsset<ContentCatalogSO>(ContentCatalogPath));
            UIBuild.Bind(comp, "_sprites", LoadAsset<SpriteCatalog>(SpriteCatalogPath));
            UIBuild.SavePrefab(root, "Panel_Campaign");
        }

        // ==================================================================
        // 03 Panel_Formation —— 布阵（核心）
        // ==================================================================

        private static void BuildFormation()
        {
            var root = NewPanelRoot("Panel_Formation", true, Vector2.zero);
            var comp = root.AddComponent<FormationPanel>();
            var rt = (RectTransform)root.transform;

            var top = UIBuild.Top(rt, "Root_TopBar", 96);
            UIBuild.Img(top, UIBuild.Silk);
            var tmpTitle = UIBuild.Tmp(UIBuild.Fixed(top, "Tmp_NodeTitle", new Vector2(0f, 0.5f),
                new Vector2(560, 48), new Vector2(140, 0)), "Term 07 - XiaoShu", 32, UIBuild.Ink, TextAlignmentOptions.Left);
            var tmpWarn = UIBuild.Tmp(UIBuild.Fixed(top, "Tmp_WeatherWarn", new Vector2(1f, 0.5f),
                new Vector2(620, 48), new Vector2(-140, 0)), "Weather warn", 26, UIBuild.Red, TextAlignmentOptions.Right);

            var intel = UIBuild.Top(rt, "Root_EnemyIntel", 150, 24, 100, 520);
            UIBuild.Img(intel, UIBuild.Card);
            var enemies = new Image[5];
            for (int i = 0; i < 5; i++)
            {
                var slot = UIBuild.Fixed(intel, "Img_EnemyAvatar_" + i, new Vector2(0f, 0.5f),
                    new Vector2(110, 110), new Vector2(90 + i * 130, 0));
                enemies[i] = UIBuild.Img(slot, UIBuild.Night);
            }
            var tmpPower = UIBuild.Tmp(UIBuild.Fixed(intel, "Tmp_EnemyPower", new Vector2(1f, 0.5f),
                new Vector2(320, 56), new Vector2(-190, 0)), "敌方强度 5.35", 30, UIBuild.Ink, TextAlignmentOptions.Right);
            var imgWeather = UIBuild.Img(UIBuild.Fixed(intel, "Img_WeatherIcon", new Vector2(1f, 0.5f),
                new Vector2(96, 96), new Vector2(-70, 0)), UIBuild.Red);

            var bonds = UIBuild.Stretch(rt, "Root_BondBar", 24, 260, 520, 690);
            UIBuild.Img(bonds, UIBuild.Silk);
            var bondRow = UIBuild.HLayout(bonds, "Root_BondRow",
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(16, 10), new Vector2(-16, -10), 14f);
            var imgBonds = new Image[8];
            var tmpBonds = new TMP_Text[8];
            for (int i = 0; i < 8; i++)
            {
                var slot = UIBuild.Slot(bondRow, "Img_Bond_" + i, 120, 110);
                imgBonds[i] = UIBuild.Img(slot, i < 5 ? UIBuild.Dim : UIBuild.Wood);
                var nameRt = UIBuild.Stretch(slot, "Tmp_BondName_" + i, 4, 4, 4, 4);
                tmpBonds[i] = UIBuild.Tmp(nameRt, "B" + i, 22, UIBuild.Ink, TextAlignmentOptions.Center);
            }

            var board = UIBuild.Center(rt, "Root_Board", new Vector2(900, 700));
            board.anchoredPosition = new Vector2(-260, 60);
            var boardBg = UIBuild.Stretch(board, "Img_BoardBg");
            UIBuild.Img(boardBg, UIBuild.Silk);
            var cellsGrid = UIBuild.GridLayout(board, "Root_Cells",
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(40, 40), new Vector2(-40, -40),
                new Vector2(190, 190), new Vector2(18, 18), 3);
            var cells = new RectTransform[9];
            for (int i = 0; i < 9; i++)
            {
                var cell = UIBuild.Fixed(cellsGrid, "Cell_" + i, new Vector2(0.5f, 0.5f),
                    Vector2.zero, Vector2.zero);
                cells[i] = cell;
                var cellImg = cell.gameObject.AddComponent<Image>();
                cellImg.color = i == 4 ? new Color(0.79f, 0.63f, 0.39f, 0.55f) : new Color(1f, 1f, 1f, 0.35f);
                cellImg.raycastTarget = true;
            }
            var cellHi = UIBuild.Center(board, "Img_CellHighlight", new Vector2(210, 210));
            var cellHiImg = UIBuild.Img(cellHi, new Color(0.79f, 0.63f, 0.39f, 0.85f));
            cellHiImg.raycastTarget = false;

            var rosterZone = UIBuild.Right(rt, "Root_Roster", 460, 24, 140, 200);
            UIBuild.Img(rosterZone, UIBuild.Card);
            var rosterContent = UIBuild.ScrollVertical(rosterZone, "Scroll_Roster",
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(12, 12), new Vector2(-12, -12));
            var srRoster = rosterContent.parent.parent.GetComponent<ScrollRect>();
            var rosterTpl = UIBuild.Fixed(rosterContent.parent.parent, "Item_Roster",
                new Vector2(0.5f, 0.5f), new Vector2(410, 120), Vector2.zero);
            rosterTpl.gameObject.SetActive(false);
            UIBuild.Img(rosterTpl, UIBuild.Card, true);
            var head = UIBuild.Fixed(rosterTpl, "Img_Head", new Vector2(0f, 0.5f),
                new Vector2(96, 96), new Vector2(62, 0));
            UIBuild.Img(head, UIBuild.Silk);
            var rosterName = UIBuild.Tmp(UIBuild.Stretch(rosterTpl, "Tmp_Name", 130, 20, 100, 50),
                "Beast", 26, UIBuild.Ink, TextAlignmentOptions.Left);
            var elem = UIBuild.Fixed(rosterTpl, "Img_Element", new Vector2(1f, 0.5f),
                new Vector2(48, 48), new Vector2(-40, 0));
            UIBuild.Img(elem, UIBuild.Wood);

            var bottom = UIBuild.Bottom(rt, "Root_BottomBar", 150, 24, 24, 510);
            UIBuild.Img(bottom, UIBuild.Card);
            var tmpTotalPower = UIBuild.Tmp(UIBuild.Fixed(bottom, "Tmp_TotalPower", new Vector2(0f, 0.5f),
                new Vector2(360, 56), new Vector2(130, 0)), "总战力 0", 34, UIBuild.Ink, TextAlignmentOptions.Left);
            var bAutoFill = UIBuild.MakeBtn(bottom, "Btn_AutoFill", new Vector2(1f, 0.5f),
                new Vector2(230, 100), new Vector2(-560, 0), "托管", UIBuild.Card, 26f);
            var bClear = UIBuild.MakeBtn(bottom, "Btn_Clear", new Vector2(1f, 0.5f),
                new Vector2(200, 100), new Vector2(-310, 0), "清空", UIBuild.Card, 26f);
            var bDeploy = UIBuild.MakeBtn(bottom, "Btn_Deploy", new Vector2(1f, 0.5f),
                new Vector2(280, 110), new Vector2(-40, 0), "出征", UIBuild.Gold, 32f);

            UIBuild.Bind(comp, "_tmpNodeTitle", tmpTitle);
            UIBuild.Bind(comp, "_tmpWeatherWarn", tmpWarn);
            UIBuild.BindArr(comp, "_imgEnemies", enemies);
            UIBuild.Bind(comp, "_tmpEnemyPower", tmpPower);
            UIBuild.Bind(comp, "_imgWeather", imgWeather);
            UIBuild.BindArr(comp, "_imgBonds", imgBonds);
            UIBuild.BindArr(comp, "_tmpBonds", tmpBonds);
            UIBuild.BindArr(comp, "_cells", cells);
            UIBuild.Bind(comp, "_imgCellHi", cellHiImg);
            UIBuild.Bind(comp, "_scrollRoster", srRoster);
            UIBuild.Bind(comp, "_rosterItemTemplate", rosterTpl);
            UIBuild.Bind(comp, "_tmpPower", tmpTotalPower);
            UIBuild.Bind(comp, "_btnAutoFill", bAutoFill.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnClear", bClear.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnDeploy", bDeploy.GetComponent<Button>());
            UIBuild.Bind(comp, "_contentCatalog", LoadAsset<ContentCatalogSO>(ContentCatalogPath));
            UIBuild.Bind(comp, "_sprites", LoadAsset<SpriteCatalog>(SpriteCatalogPath));
            UIBuild.SavePrefab(root, "Panel_Formation");
        }

        // ==================================================================
        // 04 Panel_Battle —— 战斗界面
        // ==================================================================

        private static void BuildBattle()
        {
            var root = NewPanelRoot("Panel_Battle", true, Vector2.zero);
            var comp = root.AddComponent<BattlePanel>();
            var rt = (RectTransform)root.transform;

            var top = UIBuild.Top(rt, "Root_TopBar", 100);
            var tmpRound = UIBuild.Tmp(UIBuild.Fixed(top, "Tmp_Round", new Vector2(0.5f, 0.5f),
                new Vector2(300, 52), Vector2.zero), "第 1 回合", 34, UIBuild.Ink, TextAlignmentOptions.Center);
            var banner = UIBuild.Stretch(top, "Root_WeatherBanner", 480, 2, 480, 2);
            UIBuild.Img(banner, UIBuild.Night);
            var tmpWeatherName = UIBuild.Tmp(UIBuild.Stretch(banner, "Tmp_WeatherName", 20, 8, 20, 8),
                "Weather", 28, UIBuild.Paper, TextAlignmentOptions.Center);

            var stage = UIBuild.Stretch(rt, "Root_Stage", 60, 110, 60, 300);
            var units = UIBuild.Stretch(stage, "Root_Units");
            var hpRoot = UIBuild.Stretch(stage, "Root_HpBars");
            var hpTpl = UIBuild.Fixed(hpRoot, "Item_HpBar", new Vector2(0.5f, 0.5f),
                new Vector2(280, 60), Vector2.zero);
            hpTpl.gameObject.SetActive(false);
            var hpBg = UIBuild.Stretch(hpTpl, "Img_HpBg");
            UIBuild.Img(hpBg, UIBuild.Night, false);
            var hpFillRt = UIBuild.Stretch(hpTpl, "Img_HpFill", 4, 4, 4, 4);
            var hpFill = UIBuild.Img(hpFillRt, UIBuild.Wood);
            hpFill.type = Image.Type.Filled;
            hpFill.fillMethod = Image.FillMethod.Horizontal;
            hpFill.fillAmount = 0.7f;
            var hpNum = UIBuild.Tmp(UIBuild.Stretch(hpTpl, "Tmp_HpNum", 8, 4, 8, 4),
                "999", 22, UIBuild.Paper, TextAlignmentOptions.Center);

            var ult = UIBuild.Right(rt, "Root_Ultimate", 440, 24, 300, 200);
            var rageBgRt = UIBuild.Center(ult, "Img_RageRingBg", new Vector2(250, 250));
            UIBuild.Img(rageBgRt, UIBuild.Night);
            var rageRt = UIBuild.Center(ult, "Img_RageRing", new Vector2(250, 250));
            var rage = UIBuild.Img(rageRt, UIBuild.Gold);
            rage.type = Image.Type.Filled;
            rage.fillMethod = Image.FillMethod.Radial360;
            rage.fillOrigin = (int)Image.Origin360.Bottom;
            rage.fillAmount = 0.4f;
            rage.raycastTarget = false;
            var bUlt = UIBuild.MakeBtn(ult, "Btn_Ultimate", new Vector2(0.5f, 0.5f),
                new Vector2(180, 180), Vector2.zero, "绝技", UIBuild.Red, 34f);
            var tmpRage = UIBuild.Tmp(UIBuild.Fixed(ult, "Tmp_RageValue", new Vector2(0.5f, 0f),
                new Vector2(220, 44), new Vector2(0, 30)), "怒气 0", 26, UIBuild.Ink, TextAlignmentOptions.Center);

            var ctl = UIBuild.Bottom(rt, "Root_BattleCtl", 120, 24, 24, 340);
            var bSpeed = UIBuild.MakeBtn(ctl, "Btn_Speed", new Vector2(0f, 0.5f),
                new Vector2(180, 90), new Vector2(110, 0), "x1", UIBuild.Card, 28f);
            var bAuto = UIBuild.MakeBtn(ctl, "Btn_Auto", new Vector2(0f, 0.5f),
                new Vector2(180, 90), new Vector2(310, 0), "自动布阵", UIBuild.Card, 26f);
            var bLeave = UIBuild.MakeBtn(ctl, "Btn_Leave", new Vector2(0f, 0.5f),
                new Vector2(180, 90), new Vector2(510, 0), "撤退", UIBuild.Card, 28f);

            var log = UIBuild.Bottom(rt, "Root_Log", 110, 500, 24, 24);
            UIBuild.Img(log, UIBuild.Night);
            var tmpLog = UIBuild.Tmp(UIBuild.Stretch(log, "Tmp_LogLine", 16, 8, 16, 8),
                "战斗日志", 22, UIBuild.Paper, TextAlignmentOptions.BottomLeft);

            UIBuild.Bind(comp, "_tmpRound", tmpRound);
            UIBuild.Bind(comp, "_rootWeather", banner.gameObject);
            UIBuild.Bind(comp, "_tmpWeatherName", tmpWeatherName);
            UIBuild.Bind(comp, "_rootStage", stage);
            UIBuild.Bind(comp, "_rootUnits", units);
            UIBuild.Bind(comp, "_hpBarTemplate", hpTpl);
            UIBuild.Bind(comp, "_btnUltimate", bUlt.GetComponent<Button>());
            UIBuild.Bind(comp, "_imgRage", rage);
            UIBuild.Bind(comp, "_tmpRage", tmpRage);
            UIBuild.Bind(comp, "_btnSpeed", bSpeed.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnAuto", bAuto.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnLeave", bLeave.GetComponent<Button>());
            UIBuild.Bind(comp, "_tmpLog", tmpLog);
            // ---- 回合制 v2.1 P1-3：战记操作区（底部，默认隐藏，等下令时亮出）----
            var actionBar = UIBuild.Fixed(rt, "Root_Action", new Vector2(0.5f, 0f),
                new Vector2(1800, 150), new Vector2(0f, 95f));
            UIBuild.Img(actionBar, UIBuild.Night);
            var tmpActor = UIBuild.Tmp(UIBuild.Fixed(actionBar, "Tmp_Actor", new Vector2(0.5f, 1f),
                new Vector2(1700, 40), new Vector2(0f, -8f)), "轮到我方行动", 26, UIBuild.Paper,
                TextAlignmentOptions.Center);
            var skillBtns = new Button[3];
            string[] skillNames = { "普攻", "战记", "终结技" };
            for (int si = 0; si < 3; si++)
                skillBtns[si] = UIBuild.MakeBtn(actionBar, "Btn_Skill_" + si, new Vector2(0f, 0f),
                    new Vector2(190, 76), new Vector2(120f + si * 210f, 54f),
                    skillNames[si], UIBuild.Gold, 26f).GetComponent<Button>();
            var bAutoBattle = UIBuild.MakeBtn(actionBar, "Btn_AutoBattle", new Vector2(1f, 0f),
                new Vector2(200, 76), new Vector2(-120f, 54f), "自动战斗", UIBuild.Card, 24f)
                .GetComponent<Button>();
            actionBar.gameObject.SetActive(false);   // 等 AwaitingCommand 时由 BattlePanel 亮出
            UIBuild.Bind(comp, "_actionBar", actionBar);
            UIBuild.Bind(comp, "_tmpActor", tmpActor);
            UIBuild.BindArr(comp, "_skillBtns", skillBtns);
            UIBuild.Bind(comp, "_btnAutoBattle", bAutoBattle);


            // ---- 连携按钮（主兽 + 对应元素伙伴在场时可用，v2.1 P4）----
            var bCombo = UIBuild.MakeBtn(actionBar, "Btn_Combo", new Vector2(1f, 0f),
                new Vector2(200, 76), new Vector2(-240f, 54f), "连携", UIBuild.Card, 24f)
                .GetComponent<Button>();
            bCombo.interactable = false;
            UIBuild.Bind(comp, "_btnCombo", bCombo);
            // ---- 行动顺序（右上角）：标题 + 8 行（每行：头像 + 名字）----
            var orderPanel = UIBuild.Fixed(rt, "Root_OrderList", new Vector2(1f, 1f),
                new Vector2(330, 380), new Vector2(-24f, -120f));
            orderPanel.pivot = new Vector2(1f, 1f);      // 从右上角往左下排
            UIBuild.Img(orderPanel, UIBuild.Night);

            var tmpOrderTitle = UIBuild.Tmp(UIBuild.Fixed(orderPanel, "Tmp_OrderTitle",
                new Vector2(0.5f, 1f), new Vector2(310, 30), new Vector2(0f, -22f)),
                "行动顺序（按速度）", 20, UIBuild.Paper, TextAlignmentOptions.Center);

            var orderRows = new RectTransform[8];
            for (int ri = 0; ri < 8; ri++)
            {
                var row = UIBuild.Fixed(orderPanel, "OrderRow_" + ri, new Vector2(0.5f, 1f),
                    new Vector2(304, 38), new Vector2(0f, -59f - ri * 41f));
                // 行内：头像 + 名字（BattlePanel 首次运行按名字取引用，不逐帧创建）
                var head = UIBuild.Fixed(row, "Head", new Vector2(0f, 0.5f),
                    new Vector2(36, 36), new Vector2(24f, 0f));
                UIBuild.Img(head, UIBuild.Paper);
                UIBuild.Tmp(UIBuild.Stretch(row, "Tmp_Name", 50, 0, 6, 0),
                    "—", 19, UIBuild.Paper, TextAlignmentOptions.MidlineLeft);
                orderRows[ri] = row;
            }
            orderPanel.gameObject.SetActive(false);

            // ---- 战记悬浮提示（左侧）----
            var tipPanel = UIBuild.Fixed(rt, "Root_SkillTip", new Vector2(0f, 0.5f),
                new Vector2(430, 260), new Vector2(52f, 40f));
            tipPanel.pivot = new Vector2(0f, 0.5f);
            UIBuild.Img(tipPanel, UIBuild.Night);
            var tmpTip = UIBuild.Tmp(UIBuild.Stretch(tipPanel, "Tmp_Tip", 18, 14, 18, 14),
                "", 22, UIBuild.Paper, TextAlignmentOptions.TopLeft);
            tipPanel.gameObject.SetActive(false);

            UIBuild.Bind(comp, "_orderPanel", orderPanel);
            UIBuild.BindArr(comp, "_orderRows", orderRows);
            UIBuild.Bind(comp, "_tipPanel", tipPanel);
            UIBuild.Bind(comp, "_tipText", tmpTip);
            UIBuild.Bind(comp, "_contentCatalog", LoadAsset<ContentCatalogSO>(ContentCatalogPath));
            UIBuild.Bind(comp, "_sprites", LoadAsset<SpriteCatalog>(SpriteCatalogPath));
            UIBuild.SavePrefab(root, "Panel_Battle");
        }

        // ==================================================================
        // 05 Panel_Result —— 结算（居中弹窗）
        // ==================================================================

        private static void BuildResult()
        {
            var root = NewPanelRoot("Panel_Result", false, new Vector2(1400, 950));
            var comp = root.AddComponent<ResultPanel>();
            var rt = (RectTransform)root.transform;

            var banner = UIBuild.Top(rt, "Img_Banner", 140, 40, 40, 20);
            var bannerImg = UIBuild.Img(banner, UIBuild.Gold);
            var bannerTmp = UIBuild.Stretch(banner, "Tmp_BannerText", 20, 10, 20, 10);
            UIBuild.Tmp(bannerTmp, "胜", 44, UIBuild.Ink, TextAlignmentOptions.Center);

            var detail = UIBuild.Left(rt, "Root_Detail", 660, 40, 180, 320);
            UIBuild.Img(detail, UIBuild.Card);
            var lines = new TMP_Text[4];
            for (int i = 0; i < 4; i++)
            {
                var l = UIBuild.Tmp(UIBuild.Fixed(detail, "Tmp_Line_" + i, new Vector2(0f, 1f),
                    new Vector2(580, 56), new Vector2(300, -50 - i * 76)), "reward " + i, 26, UIBuild.Ink,
                    TextAlignmentOptions.Left);
                lines[i] = l;
            }

            var egg = UIBuild.Center(rt, "Img_RewardEgg", new Vector2(320, 320));
            egg.anchoredPosition = new Vector2(300, 60);
            var eggImg = UIBuild.Img(egg, UIBuild.Silk);

            var drafts = UIBuild.HLayout(rt, "Root_Drafts",
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(60, 140), new Vector2(-60, 340), 24f);
            var draftBtns = new Button[3];
            var draftNames = new TMP_Text[3];
            var draftDescs = new TMP_Text[3];
            for (int i = 0; i < 3; i++)
            {
                var card = UIBuild.Slot(drafts, "Draft_" + i, 400, 380);
                UIBuild.Img(card, UIBuild.Card, true);
                var icon = UIBuild.Fixed(card, "Img_DraftIcon", new Vector2(0.5f, 1f),
                    new Vector2(140, 140), new Vector2(0, -90));
                UIBuild.Img(icon, UIBuild.Silk);
                var nm = UIBuild.Tmp(UIBuild.Stretch(card, "Tmp_DraftName_" + i, 20, 170, 20, 240),
                    "Draft " + i, 30, UIBuild.Ink, TextAlignmentOptions.Center);
                var ds = UIBuild.Tmp(UIBuild.Stretch(card, "Tmp_DraftDesc_" + i, 24, 250, 24, 40),
                    "desc", 22, UIBuild.Ink2, TextAlignmentOptions.Center);
                draftBtns[i] = card.gameObject.AddComponent<Button>();
                draftBtns[i].targetGraphic = card.GetComponent<Graphic>();
                draftNames[i] = nm;
                draftDescs[i] = ds;
            }

            var bSkip = UIBuild.MakeBtn(rt, "Btn_Skip", new Vector2(0f, 0f),
                new Vector2(360, 110), new Vector2(320, 50), "跳过（+1 灵卵）", UIBuild.Card, 26f);
            var bConfirm = UIBuild.MakeBtn(rt, "Btn_Confirm", new Vector2(1f, 0f),
                new Vector2(460, 110), new Vector2(-320, 50), "确认", UIBuild.Gold, 32f);

            UIBuild.Bind(comp, "_imgBanner", bannerImg);
            UIBuild.BindArr(comp, "_tmpLines", lines);
            UIBuild.Bind(comp, "_imgEgg", eggImg);
            UIBuild.BindArr(comp, "_draftBtns", draftBtns);
            UIBuild.BindArr(comp, "_tmpDraftNames", draftNames);
            UIBuild.BindArr(comp, "_tmpDraftDescs", draftDescs);
            UIBuild.Bind(comp, "_btnSkip", bSkip.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnConfirm", bConfirm.GetComponent<Button>());
            UIBuild.Bind(comp, "_spriteWin", LoadAsset<Sprite>(ArtParts + "banner_win.png"));
            UIBuild.Bind(comp, "_spriteLose", LoadAsset<Sprite>(ArtParts + "banner_lose.png"));
            UIBuild.SavePrefab(root, "Panel_Result");
        }

        // ==================================================================
        // 06 Panel_Market —— 灵市
        // ==================================================================

        private static void BuildMarket()
        {
            var root = NewPanelRoot("Panel_Market", true, Vector2.zero);
            var comp = root.AddComponent<MarketPanel>();
            var rt = (RectTransform)root.transform;

            var top = UIBuild.Top(rt, "Root_TopBar", 96);
            UIBuild.Img(top, UIBuild.Silk);
            var tmpEggs = UIBuild.Tmp(UIBuild.Fixed(top, "Tmp_Eggs", new Vector2(0f, 0.5f),
                new Vector2(320, 48), new Vector2(150, 0)), "Eggs 0", 30, UIBuild.Ink, TextAlignmentOptions.Left);
            var tmpCost = UIBuild.Tmp(UIBuild.Fixed(top, "Tmp_RefreshCost", new Vector2(1f, 0.5f),
                new Vector2(240, 48), new Vector2(-440, 0)), "刷新 3", 26, UIBuild.Ink2, TextAlignmentOptions.Right);
            var bRefresh = UIBuild.MakeBtn(top, "Btn_Refresh", new Vector2(1f, 0.5f),
                new Vector2(260, 72), new Vector2(-150, 0), "刷新 1", UIBuild.Card, 26f);
            var bBack = UIBuild.MakeBtn(top, "Btn_Back", new Vector2(0f, 0.5f),
                new Vector2(260, 72), new Vector2(-150, 0), "返回地图", UIBuild.Card, 26f);

            var goods = UIBuild.GridLayout(rt, "Root_Goods",
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(24, -300), new Vector2(-24, 400),
                new Vector2(580, 330), new Vector2(24, 24), 3);
            var goodsBtns = new Button[6];
            var goodsHeads = new Image[6];
            var goodsPrices = new TMP_Text[6];
            var goodsSold = new Image[6];
            for (int i = 0; i < 6; i++)
            {
                var card = UIBuild.Fixed(goods, "Goods_" + i, new Vector2(0.5f, 0.5f),
                    Vector2.zero, Vector2.zero);
                UIBuild.Img(card, UIBuild.Card, true);
                var head = UIBuild.Fixed(card, "Img_GoodsHead_" + i, new Vector2(0f, 0.5f),
                    new Vector2(150, 150), new Vector2(110, 0));
                goodsHeads[i] = UIBuild.Img(head, UIBuild.Silk);
                var nm = UIBuild.Tmp(UIBuild.Stretch(card, "Tmp_GoodsName_" + i, 180, 30, 30, 60),
                    "Goods " + i, 28, UIBuild.Ink, TextAlignmentOptions.Left);
                var price = UIBuild.Tmp(UIBuild.Stretch(card, "Tmp_GoodsPrice_" + i, 180, 20, 30, 90),
                    "price", 26, UIBuild.Ink2, TextAlignmentOptions.Left);
                goodsPrices[i] = price;
                var sold = UIBuild.Stretch(card, "Img_GoodsSold_" + i, 0, 0, 0, 0);
                goodsSold[i] = UIBuild.Img(sold, new Color(0.78f, 0.21f, 0.17f, 0.65f));
                goodsSold[i].gameObject.SetActive(false);
                goodsBtns[i] = card.gameObject.AddComponent<Button>();
                goodsBtns[i].targetGraphic = card.GetComponent<Graphic>();
            }

            var tmpHint = UIBuild.Tmp(UIBuild.Bottom(rt, "Tmp_Hint", 70, 40, 40, 130),
                "点击商品即可入手", 26, UIBuild.Ink2, TextAlignmentOptions.Center);

            UIBuild.Bind(comp, "_tmpEggs", tmpEggs);
            UIBuild.Bind(comp, "_btnRefresh", bRefresh.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnBack", bBack.GetComponent<Button>());
            UIBuild.Bind(comp, "_contentCatalog", LoadAsset<ContentCatalogSO>(ContentCatalogPath));
            UIBuild.Bind(comp, "_sprites", LoadAsset<SpriteCatalog>(SpriteCatalogPath));
            UIBuild.Bind(comp, "_tmpRefreshCost", tmpCost);
            UIBuild.BindArr(comp, "_goodsBtns", goodsBtns);
            UIBuild.BindArr(comp, "_imgGoodsHead", goodsHeads);
            UIBuild.BindArr(comp, "_tmpGoodsPrice", goodsPrices);
            UIBuild.BindArr(comp, "_imgGoodsSold", goodsSold);
            UIBuild.Bind(comp, "_tmpHint", tmpHint);
            UIBuild.SavePrefab(root, "Panel_Market");
        }

        // ==================================================================
        // 07 Panel_Tale —— 异闻
        // ==================================================================

        private static void BuildTale()
        {
            var root = NewPanelRoot("Panel_Tale", false, new Vector2(1240, 940));
            var comp = root.AddComponent<TalePanel>();
            var rt = (RectTransform)root.transform;
            UIBuild.Img(rt, UIBuild.Card, false);

            var titleScroll = UIBuild.Top(rt, "Img_TitleScroll", 120, 60, 60, 20);
            UIBuild.Img(titleScroll, UIBuild.Gold);
            var tmpTitle = UIBuild.Tmp(UIBuild.Stretch(titleScroll, "Tmp_Title", 20, 10, 20, 10),
                "异闻", 36, UIBuild.Ink, TextAlignmentOptions.Center);
            var tmpQuote = UIBuild.Tmp(UIBuild.Stretch(rt, "Tmp_ClassicQuote", 70, 150, 70, 620),
                "classic quote", 24, UIBuild.Ink2, TextAlignmentOptions.TopLeft);
            var tmpStory = UIBuild.Tmp(UIBuild.Stretch(rt, "Tmp_StoryText", 70, 330, 70, 420),
                "story text", 28, UIBuild.Ink, TextAlignmentOptions.TopLeft);

            var opts = new Button[3];
            var optTexts = new TMP_Text[3];
            var optCosts = new TMP_Text[3];
            for (int i = 0; i < 3; i++)
            {
                var opt = UIBuild.MakeBtn(rt, "Opt_" + i, new Vector2(0.5f, 0f),
                    new Vector2(1080, 120), new Vector2(0, 230 - i * 140), "", UIBuild.Silk, 0f);
                var otmp = UIBuild.Tmp(UIBuild.Stretch(opt, "Tmp_OptionText_" + i, 30, 12, 30, 52),
                    "option " + i, 28, UIBuild.Ink, TextAlignmentOptions.Left);
                var ocost = UIBuild.Tmp(UIBuild.Stretch(opt, "Tmp_Cost_" + i, 30, 12, 30, 14),
                    "cost", 22, UIBuild.Red, TextAlignmentOptions.Left);
                opts[i] = opt.GetComponent<Button>();
                optTexts[i] = otmp;
                optCosts[i] = ocost;
            }

            var bDecline = UIBuild.MakeBtn(rt, "Btn_Decline", new Vector2(0.5f, 0f),
                new Vector2(420, 96), new Vector2(0, 50), "拒绝（+1 灵卵）", UIBuild.Card, 26f);

            UIBuild.Bind(comp, "_tmpTitle", tmpTitle);
            UIBuild.Bind(comp, "_tmpQuote", tmpQuote);
            UIBuild.Bind(comp, "_tmpStory", tmpStory);
            UIBuild.BindArr(comp, "_optBtns", opts);
            UIBuild.BindArr(comp, "_tmpOpts", optTexts);
            UIBuild.BindArr(comp, "_tmpCosts", optCosts);
            UIBuild.Bind(comp, "_btnDecline", bDecline.GetComponent<Button>());
            UIBuild.SavePrefab(root, "Panel_Tale");
        }

        // ==================================================================
        // 08 Panel_Omen —— 天象
        // ==================================================================

        private static void BuildOmen()
        {
            var root = NewPanelRoot("Panel_Omen", false, new Vector2(1280, 740));
            var comp = root.AddComponent<OmenPanel>();
            var rt = (RectTransform)root.transform;
            UIBuild.Img(rt, UIBuild.Card, false);

            var tmpTitle = UIBuild.Tmp(UIBuild.Top(rt, "Tmp_Title", 100, 60, 60, 24),
                "天象", 36, UIBuild.Ink, TextAlignmentOptions.Center);

            var gain = UIBuild.Stretch(rt, "Root_Gain", 60, 140, 650, 200);
            var gainImg = UIBuild.Img(gain, UIBuild.Silk);
            gainImg.color = new Color(0.79f, 0.63f, 0.39f, 0.35f);
            var tmpGain = UIBuild.Tmp(UIBuild.Stretch(gain, "Tmp_GainText", 24, 24, 24, 24),
                "gain", 30, UIBuild.Ink, TextAlignmentOptions.Center);

            var cost = UIBuild.Stretch(rt, "Root_Cost", 650, 140, 60, 200);
            UIBuild.Img(cost, new Color(0.78f, 0.21f, 0.17f, 0.22f));
            var tmpCost = UIBuild.Tmp(UIBuild.Stretch(cost, "Tmp_CostText", 24, 24, 24, 24),
                "cost", 30, UIBuild.Ink, TextAlignmentOptions.Center);

            var bAccept = UIBuild.MakeBtn(rt, "Btn_Accept", new Vector2(0.5f, 0f),
                new Vector2(400, 110), new Vector2(-240, 50), "接受", UIBuild.Gold, 30f);
            var bDecline = UIBuild.MakeBtn(rt, "Btn_Decline", new Vector2(0.5f, 0f),
                new Vector2(400, 110), new Vector2(240, 50), "拒绝", UIBuild.Card, 30f);

            UIBuild.Bind(comp, "_tmpTitle", tmpTitle);
            UIBuild.Bind(comp, "_tmpGain", tmpGain);
            UIBuild.Bind(comp, "_tmpCost", tmpCost);
            UIBuild.Bind(comp, "_btnAccept", bAccept.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnDecline", bDecline.GetComponent<Button>());
            UIBuild.SavePrefab(root, "Panel_Omen");
        }

        // ==================================================================
        // 09 Panel_Forge —— 铸魂台
        // ==================================================================

        private static void BuildForge()
        {
            var root = NewPanelRoot("Panel_Forge", true, Vector2.zero);
            var comp = root.AddComponent<ForgePanel>();
            var rt = (RectTransform)root.transform;

            var top = UIBuild.Top(rt, "Root_TopBar", 96);
            UIBuild.Img(top, UIBuild.Silk);
            var tmpTitle = UIBuild.Tmp(UIBuild.Fixed(top, "Tmp_Title", new Vector2(0f, 0.5f),
                new Vector2(520, 48), new Vector2(140, 0)), "铸魂台", 32, UIBuild.Ink, TextAlignmentOptions.Left);
            var tmpEggs = UIBuild.Tmp(UIBuild.Fixed(top, "Tmp_Eggs", new Vector2(1f, 0.5f),
                new Vector2(320, 48), new Vector2(-150, 0)), "灵卵 0", 30, UIBuild.Ink, TextAlignmentOptions.Right);

            var host = UIBuild.Left(rt, "Root_Host", 560, 24, 140, 240);
            UIBuild.Img(host, UIBuild.Card);
            var hostPrev = UIBuild.Top(host, "Img_HostPreview", 420, 20, 20, 20);
            var imgHost = UIBuild.Img(hostPrev, UIBuild.Silk);
            var scrollHosts = UIBuild.ScrollVertical(host, "Scroll_Hosts",
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(20, 20), new Vector2(-20, -20));

            var vortex = UIBuild.Center(rt, "Root_Vortex", new Vector2(500, 500));
            vortex.anchoredPosition = new Vector2(0, 40);
            var vortexImgRt = UIBuild.Stretch(vortex, "Img_Vortex");
            var imgVortex = UIBuild.Img(vortexImgRt, UIBuild.Night);

            var soul = UIBuild.Right(rt, "Root_Soul", 560, 24, 140, 240);
            UIBuild.Img(soul, UIBuild.Card);
            var soulPrev = UIBuild.Top(soul, "Root_SoulTop", 420, 20, 20, 20);
            var imgSoul = UIBuild.Img(UIBuild.Fixed(soulPrev, "Img_SoulPreview", new Vector2(0.5f, 1f),
                new Vector2(220, 220), new Vector2(0, -130)), UIBuild.Silk);
            var coverRow = UIBuild.HLayout(soulPrev, "Root_Coverage",
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(20, 8), new Vector2(-20, -120), 12f);
            var covers = new Image[5];
            for (int i = 0; i < 5; i++)
            {
                var slot = UIBuild.Slot(coverRow, "Img_Cover_" + i, 84, 84);
                covers[i] = UIBuild.Img(slot, UIBuild.Dim);
            }
            var scrollSouls = UIBuild.ScrollVertical(soul, "Scroll_Souls",
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(20, 20), new Vector2(-20, -20));

            var swatchBar = UIBuild.Bottom(rt, "Root_SwatchBar", 130, 24, 24, 250);
            UIBuild.Img(swatchBar, UIBuild.Card);
            var swRow = UIBuild.HLayout(swatchBar, "Root_SwatchRow",
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(20, 12), new Vector2(-20, -12), 16f);
            var swatches = new Image[5];
            for (int i = 0; i < 5; i++)
            {
                var slot = UIBuild.Slot(swRow, "Img_Sw_" + i, 150, 90);
                swatches[i] = UIBuild.Img(slot, UIBuild.Wood);
            }

            var tmpResult = UIBuild.Tmp(UIBuild.Bottom(rt, "Tmp_ResultName", 90, 24, 700, 160),
                "宿主 · 灵魂", 34, UIBuild.Ink, TextAlignmentOptions.Center);
            var bFuse = UIBuild.MakeBtn(rt, "Btn_Fuse", new Vector2(1f, 0f),
                new Vector2(420, 110), new Vector2(-120, 60), "熔炼", UIBuild.Gold, 34f);
            // 返回节点地图（熔炼完或不想熔了，得有路回去）
            var bBack = UIBuild.MakeBtn(rt, "Btn_Back", new Vector2(0f, 0f),
                new Vector2(300, 96), new Vector2(120, 60), "返回地图", UIBuild.Card, 28f);

            UIBuild.Bind(comp, "_tmpTitle", tmpTitle);
            UIBuild.Bind(comp, "_tmpEggs", tmpEggs);
            // ⚠ scrollHosts 是 RectTransform，字段是 ScrollRect —— 直接 Bind 类型不匹配，
            //   SerializedObject 赋值静默失败 ⇒ 运行时字段为 null（用户实测：列表全空）。
            UIBuild.Bind(comp, "_scrollHosts", scrollHosts.GetComponentInParent<ScrollRect>());   // ⚠ ScrollVertical 返回的是 Content！
            UIBuild.Bind(comp, "_imgHost", imgHost);
            UIBuild.Bind(comp, "_imgVortex", imgVortex);
            UIBuild.Bind(comp, "_scrollSouls", scrollSouls.GetComponentInParent<ScrollRect>());
            UIBuild.Bind(comp, "_imgSoul", imgSoul);
            UIBuild.BindArr(comp, "_imgCover", covers);
            UIBuild.BindArr(comp, "_imgSwatch", swatches);
            UIBuild.Bind(comp, "_tmpResult", tmpResult);
            UIBuild.Bind(comp, "_btnFuse", bFuse.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnBack", bBack.GetComponent<Button>());
            UIBuild.Bind(comp, "_contentCatalog", LoadAsset<ContentCatalogSO>(ContentCatalogPath));
            UIBuild.Bind(comp, "_sprites", LoadAsset<SpriteCatalog>(SpriteCatalogPath));
            UIBuild.SavePrefab(root, "Panel_Forge");
        }

        // ==================================================================
        // 10 Panel_Codex —— 图鉴
        // ==================================================================

        private static void BuildCodex()
        {
            var root = NewPanelRoot("Panel_Codex", true, Vector2.zero);
            var comp = root.AddComponent<CodexPanel>();
            var rt = (RectTransform)root.transform;

            var top = UIBuild.Top(rt, "Root_TopBar", 100);
            UIBuild.Img(top, UIBuild.Silk);
            var tabs = UIBuild.HLayout(top, "Root_Tabs",
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(24, 14), new Vector2(-360, -14), 10f);
            var tabBtns = new Button[6];
            for (int i = 0; i < 6; i++)
            {
                var t = UIBuild.Slot(tabs, "Tab_" + i, 150, 70);
                UIBuild.Img(t, UIBuild.Card, true);
                var lbl = UIBuild.Stretch(t, "Tmp_Label", 6, 6, 6, 6);
                UIBuild.Tmp(lbl, "T" + i, 24, UIBuild.Ink, TextAlignmentOptions.Center);
                tabBtns[i] = t.gameObject.AddComponent<Button>();
                tabBtns[i].targetGraphic = t.GetComponent<Graphic>();
            }
            var tmpRate = UIBuild.Tmp(UIBuild.Fixed(top, "Tmp_CollectRate", new Vector2(1f, 0.5f),
                new Vector2(320, 48), new Vector2(-40, 0)), "收集 0 / 30", 30, UIBuild.Ink, TextAlignmentOptions.Right);

            ScrollRect srGrid;
            var gridContent = UIBuild.ScrollGrid(rt, "Scroll_Grid",
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(24, 40), new Vector2(-24, -140),
                new Vector2(200, 240), new Vector2(20, 20), 8, out srGrid);
            var sealTpl = UIBuild.Fixed(gridContent.parent.parent, "Item_Seal",
                new Vector2(0.5f, 0.5f), new Vector2(200, 240), Vector2.zero);
            sealTpl.gameObject.SetActive(false);
            UIBuild.Img(sealTpl, UIBuild.Card, true);
            var sealImg = UIBuild.Fixed(sealTpl, "Img_Seal", new Vector2(0.5f, 1f),
                new Vector2(160, 160), new Vector2(0, -90));
            UIBuild.Img(sealImg, UIBuild.Dim);
            var sealName = UIBuild.Tmp(UIBuild.Stretch(sealTpl, "Tmp_SealName", 10, 180, 10, 20),
                "Seal", 24, UIBuild.Ink, TextAlignmentOptions.Center);

            var detail = UIBuild.Right(rt, "Root_Detail", 780, 24, 140, 40);
            UIBuild.Img(detail, UIBuild.Card);
            var bigRt = UIBuild.Fixed(detail, "Img_BeastBig", new Vector2(0f, 0.5f),
                new Vector2(460, 560), new Vector2(260, 0));
            var imgBig = UIBuild.Img(bigRt, UIBuild.Silk);
            var tmpName = UIBuild.Tmp(UIBuild.Stretch(detail, "Tmp_Name", 500, 60, 60, 320),
                "Name", 40, UIBuild.Ink, TextAlignmentOptions.Left);
            var tmpSource = UIBuild.Tmp(UIBuild.Stretch(detail, "Tmp_ClassicSource", 500, 130, 60, 260),
                "source", 22, UIBuild.Ink2, TextAlignmentOptions.Left);
            var tmpQuote = UIBuild.Tmp(UIBuild.Stretch(detail, "Tmp_ClassicQuote", 500, 240, 60, 130),
                "quote", 26, UIBuild.Ink, TextAlignmentOptions.TopLeft);
            var tmpTrait = UIBuild.Tmp(UIBuild.Stretch(detail, "Tmp_Trait", 500, 120, 60, 60),
                "trait", 24, UIBuild.Ink2, TextAlignmentOptions.TopLeft);
            var tmpSchool = UIBuild.Tmp(UIBuild.Stretch(detail, "Tmp_School", 500, 60, 60, 20),
                "school", 22, UIBuild.Wood, TextAlignmentOptions.Left);
            var bClose = UIBuild.MakeBtn(detail, "Btn_CloseDetail", new Vector2(1f, 1f),
                new Vector2(96, 96), new Vector2(-50, -50), "返回", UIBuild.Card, 26f);

            UIBuild.BindArr(comp, "_tabBtns", tabBtns);
            UIBuild.Bind(comp, "_tmpRate", tmpRate);
            UIBuild.Bind(comp, "_scrollGrid", srGrid);
            UIBuild.Bind(comp, "_sealTemplate", sealTpl);
            UIBuild.Bind(comp, "_rootDetail", detail.gameObject);
            UIBuild.Bind(comp, "_imgBig", imgBig);
            UIBuild.Bind(comp, "_tmpName", tmpName);
            UIBuild.Bind(comp, "_tmpSource", tmpSource);
            UIBuild.Bind(comp, "_tmpQuote", tmpQuote);
            UIBuild.Bind(comp, "_tmpTrait", tmpTrait);
            UIBuild.Bind(comp, "_tmpSchool", tmpSchool);
            UIBuild.Bind(comp, "_btnCloseDetail", bClose.GetComponent<Button>());
            UIBuild.SavePrefab(root, "Panel_Codex");
        }

        // ==================================================================
        // 11 Panel_Meta —— 局外成长
        // ==================================================================

        private static void BuildMeta()
        {
            var root = NewPanelRoot("Panel_Meta", true, Vector2.zero);
            var comp = root.AddComponent<MetaPanel>();
            var rt = (RectTransform)root.transform;

            var top = UIBuild.Top(rt, "Root_TopBar", 110);
            UIBuild.Img(top, UIBuild.Silk);
            var badge = UIBuild.Fixed(top, "Img_RealmBadge", new Vector2(0f, 0.5f),
                new Vector2(84, 84), new Vector2(90, 0));
            UIBuild.Img(badge, UIBuild.Gold);
            var tmpRealm = UIBuild.Tmp(UIBuild.Fixed(top, "Tmp_RealmText", new Vector2(0f, 0.5f),
                new Vector2(560, 56), new Vector2(400, 0)), "第一境 · 第一劫", 32, UIBuild.Ink,
                TextAlignmentOptions.Left);

            var tracks = UIBuild.Stretch(rt, "Root_Tracks", 24, 140, 24, 360);
            var vLayout = tracks.gameObject.AddComponent<VerticalLayoutGroup>();
            vLayout.childAlignment = TextAnchor.UpperCenter;
            vLayout.spacing = 16;
            vLayout.padding = new RectOffset(12, 12, 12, 12);
            vLayout.childControlHeight = true;
            vLayout.childControlWidth = true;
            vLayout.childForceExpandHeight = false;
            vLayout.childForceExpandWidth = false;
            var trackNames = new TMP_Text[5];
            var trackNodes = new RectTransform[5];
            for (int i = 0; i < 5; i++)
            {
                var track = UIBuild.Slot(tracks, "Track_" + i, 1820, 150);
                UIBuild.Img(track, UIBuild.Card);
                var line = UIBuild.Stretch(track, "Img_TrackLine", 60, 60, 60, 60);
                UIBuild.Img(line, UIBuild.Dim);
                var nm = UIBuild.Tmp(UIBuild.Stretch(track, "Tmp_TrackName_" + i, 24, 20, 24, 96),
                    "Track " + i, 28, UIBuild.Ink, TextAlignmentOptions.Left);
                trackNames[i] = nm;
                trackNodes[i] = track;
            }

            var rewards = UIBuild.Bottom(rt, "Root_Rewards", 250, 24, 24, 80);
            UIBuild.Img(rewards, UIBuild.Card);
            var rewardTpl = UIBuild.Fixed(rewards, "Item_Reward", new Vector2(0.5f, 0.5f),
                new Vector2(260, 190), Vector2.zero);
            rewardTpl.gameObject.SetActive(false);
            UIBuild.Img(rewardTpl, UIBuild.Silk, true);
            var rewardName = UIBuild.Tmp(UIBuild.Stretch(rewardTpl, "Tmp_RewardName", 12, 12, 12, 70),
                "Reward", 24, UIBuild.Ink, TextAlignmentOptions.Center);
            var bReward = rewardTpl.gameObject.AddComponent<Button>();
            bReward.targetGraphic = rewardTpl.GetComponent<Graphic>();

            var tmpCap = UIBuild.Tmp(UIBuild.Stretch(rt, "Tmp_MetaCap", 24, 1080 - 60 - 260, 24, 300),
                "数值类增益封顶 +8%", 24, UIBuild.Ink2, TextAlignmentOptions.Left);

            UIBuild.Bind(comp, "_tmpRealm", tmpRealm);
            UIBuild.BindArr(comp, "_tmpTrackNames", trackNames);
            UIBuild.BindArr(comp, "_trackNodes", trackNodes);
            UIBuild.Bind(comp, "_rewardItemTemplate", rewardTpl);
            UIBuild.Bind(comp, "_tmpCap", tmpCap);
            UIBuild.SavePrefab(root, "Panel_Meta");
        }

        // ==================================================================
        // 12 Panel_Trial —— 天阙抉择
        // ==================================================================

        private static void BuildTrial()
        {
            var root = NewPanelRoot("Panel_Trial", true, Vector2.zero);
            var comp = root.AddComponent<TrialPanel>();
            var rt = (RectTransform)root.transform;

            var bg = UIBuild.Stretch(rt, "Img_FullBg");
            UIBuild.Img(bg, UIBuild.Night);

            var choices = UIBuild.HLayout(rt, "Root_Choices",
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(180, 400), new Vector2(-180, 180), 40f);
            var choiceColors = new[] { UIBuild.Gold, UIBuild.Card, UIBuild.Dim };
            var choiceBtns = new Button[3];
            var choiceTitles = new TMP_Text[3];
            var choiceDescs = new TMP_Text[3];
            for (int i = 0; i < 3; i++)
            {
                var card = UIBuild.Slot(choices, "Choice_" + i, 480, 500);
                UIBuild.Img(card, choiceColors[i], true);
                var ttl = UIBuild.Tmp(UIBuild.Stretch(card, "Tmp_ChoiceTitle_" + i, 24, 30, 24, 400),
                    "Choice " + i, 38, UIBuild.Ink, TextAlignmentOptions.Center);
                var ds = UIBuild.Tmp(UIBuild.Stretch(card, "Tmp_ChoiceDesc_" + i, 30, 130, 30, 40),
                    "desc", 26, UIBuild.Ink2, TextAlignmentOptions.TopLeft);
                choiceBtns[i] = card.gameObject.AddComponent<Button>();
                choiceBtns[i].targetGraphic = card.GetComponent<Graphic>();
                choiceTitles[i] = ttl;
                choiceDescs[i] = ds;
            }

            var tmpStatus = UIBuild.Tmp(UIBuild.Bottom(rt, "Tmp_Status", 0, 0, 40),
                "第一境 · 第一劫 · 灵卵 12", 28, UIBuild.Paper, TextAlignmentOptions.Center);

            UIBuild.BindArr(comp, "_choiceBtns", choiceBtns);
            UIBuild.BindArr(comp, "_tmpChoiceTitles", choiceTitles);
            UIBuild.BindArr(comp, "_tmpChoiceDescs", choiceDescs);
            UIBuild.Bind(comp, "_tmpStatus", tmpStatus);
            UIBuild.Bind(comp, "_contentCatalog", LoadAsset<ContentCatalogSO>(ContentCatalogPath));
            UIBuild.SavePrefab(root, "Panel_Trial");
        }

        // ==================================================================
        // 13 Panel_Settings —— 设置
        // ==================================================================

        /// <summary>
        /// 通用弹窗面板（v1.2 新增）。
        /// ⚠ 它刻意**只建一个空根节点**：弹窗的标题/正文/按钮由 DialogPanel.BuildUi()
        ///   在运行时用代码搭（见 DialogPanel 的注释）—— 弹窗是所有功能的公共依赖，
        ///   先保证任何地方都能立刻弹一个能用的框；等美术给了对话框稿子，
        ///   把 BuildUi() 换成读完这张 prefab 即可，调用方一行不用改。
        /// </summary>
        private static void BuildDialog()
        {
            var root = NewPanelRoot("Panel_Dialog", true, Vector2.zero);
            root.AddComponent<WanXiang.Modules.UI.DialogPanel>();
            UIBuild.SavePrefab(root, "Panel_Dialog");   // ⚠ 漏了这句，prefab 不会落盘（踩过）
        }

        private static void BuildSettings()
        {
            var root = NewPanelRoot("Panel_Settings", true, Vector2.zero);
            var comp = root.AddComponent<SettingsPanel>();
            var rt = (RectTransform)root.transform;

            var tmpTitle = UIBuild.Tmp(UIBuild.Top(rt, "Tmp_Title", 110, 40, 40, 24),
                "设置", 38, UIBuild.Ink, TextAlignmentOptions.Center);

            var audio = UIBuild.Stretch(rt, "Root_Audio", 40, 130, 40, 700);
            UIBuild.Img(audio, UIBuild.Card);
            var sldBgm = UIBuild.MakeSlider(audio, "Sld_Bgm",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30, -130), new Vector2(-30, -50), "音乐");
            var sldSfx = UIBuild.MakeSlider(audio, "Sld_Sfx",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30, -240), new Vector2(-30, -160), "音效");

            var video = UIBuild.Stretch(rt, "Root_Video", 40, 400, 40, 640);
            UIBuild.Img(video, UIBuild.Card);
            var tglFs = UIBuild.MakeToggle(video, "Tgl_Fullscreen",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30, -100), new Vector2(-30, -30), "全屏");
            var tglVs = UIBuild.MakeToggle(video, "Tgl_Vsync",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30, -220), new Vector2(-30, -150), "垂直同步");

            var keys = UIBuild.Stretch(rt, "Root_Keybinds", 40, 660, 40, 240);
            UIBuild.Img(keys, UIBuild.Card);
            var scrollKeys = UIBuild.ScrollVertical(keys, "Scroll_Keys",
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(14, 14), new Vector2(-14, -14));
            var keyTpl = UIBuild.Fixed(scrollKeys.parent.parent, "Item_Key",
                new Vector2(0.5f, 0.5f), new Vector2(1740, 90), Vector2.zero);
            keyTpl.gameObject.SetActive(false);
            UIBuild.Img(keyTpl, UIBuild.Silk, true);
            var keyName = UIBuild.Tmp(UIBuild.Stretch(keyTpl, "Tmp_KeyName", 30, 12, 400, 12),
                "Key", 26, UIBuild.Ink, TextAlignmentOptions.Left);
            var bRebind = UIBuild.MakeBtn(keyTpl, "Btn_Rebind", new Vector2(1f, 0.5f),
                new Vector2(240, 70), new Vector2(-40, 0), "改键", UIBuild.Gold, 24f);

            var share = UIBuild.Stretch(rt, "Root_Share", 40, 860, 40, 60);
            UIBuild.Img(share, UIBuild.Card);
            var input = UIBuild.MakeInput(share, "Tmp_InputShare",
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(30, -60), new Vector2(-720, 60));
            var bImport = UIBuild.MakeBtn(share, "Btn_Import", new Vector2(1f, 0.5f),
                new Vector2(300, 90), new Vector2(-340, 0), "导入", UIBuild.Gold, 26f);
            var bExport = UIBuild.MakeBtn(share, "Btn_Export", new Vector2(1f, 0.5f),
                new Vector2(300, 90), new Vector2(-30, 0), "导出", UIBuild.Card, 26f);

            var bClose = UIBuild.MakeBtn(rt, "Btn_Close", new Vector2(1f, 1f),
                new Vector2(96, 96), new Vector2(-50, -50), "返回", UIBuild.Card, 26f);

            UIBuild.Bind(comp, "_sldBgm", sldBgm.GetComponent<Slider>());
            UIBuild.Bind(comp, "_sldSfx", sldSfx.GetComponent<Slider>());
            UIBuild.Bind(comp, "_tglFullscreen", tglFs.GetComponent<Toggle>());
            UIBuild.Bind(comp, "_tglVsync", tglVs.GetComponent<Toggle>());
            UIBuild.Bind(comp, "_scrollKeys", scrollKeys);
            UIBuild.Bind(comp, "_keyItemTemplate", keyTpl);
            UIBuild.Bind(comp, "_inputShare", input.GetComponent<TMP_InputField>());
            UIBuild.Bind(comp, "_btnImport", bImport.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnExport", bExport.GetComponent<Button>());
            UIBuild.Bind(comp, "_btnClose", bClose.GetComponent<Button>());
            // 「返回开始界面」：回到最初的开始界面（回菜单，不弃档）
            var bBackToStart = UIBuild.MakeBtn(rt, "Btn_BackToStart", new Vector2(0.5f, 0f),
                new Vector2(520, 96), new Vector2(0f, 120f), "返回开始界面", UIBuild.Card, 28f);
            UIBuild.Bind(comp, "_btnBackToStart", bBackToStart);
            UIBuild.SavePrefab(root, "Panel_Settings");
        }
    }
}
