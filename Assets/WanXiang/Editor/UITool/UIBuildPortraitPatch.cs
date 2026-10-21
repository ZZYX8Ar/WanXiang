// ============================================================================
//  万相 · UI 补丁：主界面动态立绘（2026-10-17）
//  ---------------------------------------------------------------------------
//  ⛔ 增量补丁（项目铁律）：LoadPrefabContents → 只补缺失节点 → 重绑字段 → 存回。
//    改动前先把 prefab 备份到工程外 _backups/。
//
//  A. Panel_Home：
//     · Btn_Portrait（右上「看板」入口）
//     · Portrait_Root（立绘模式整组：黑底 / Raw_Stage / UI_Layer(名字+左右切换+
//       隐藏界面+回到地图) / Img_TapCatch）+ HomePortraitStage + VideoPlayer 组件
//     · 绑定 HomePanel._btnPortrait / _portraitRoot / _portraitStage / _btnPortraitBack
//  B. Panel_Archive：
//     · 卡片模板加 Btn_LiveClaim（领取动态立绘行），卡高 268→304，grid cellSize 同步
// ============================================================================

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Video;

namespace WanXiang.EditorTools
{
    public static class UIBuildPortraitPatch
    {
        private const string HomePath = "Assets/Resources/UI/Panel_Home.prefab";
        private const string ArchivePath = "Assets/Resources/UI/Panel_Archive.prefab";
        private const string BoardPath = "Assets/Resources/UI/Panel_PortraitBoard.prefab";
        private const string BackupDir = @"D:\Unity_Project\MYRIAD\_backups";
        private const string BackupTag = "bak_20261017_portrait";

        private static readonly Color Ink = new Color(0.16f, 0.13f, 0.09f, 1f);
        private static readonly Color Ink2 = new Color(0.42f, 0.38f, 0.34f, 1f);
        private static readonly Color Paper = new Color(0.914f, 0.886f, 0.824f, 1f);
        private static readonly Color DimGlass = new Color(0f, 0f, 0f, 0.45f);
        private static readonly Color Chalk = new Color(0.97f, 0.95f, 0.90f, 1f);

        [MenuItem("WanXiang/UI/补丁：主界面动态立绘", priority = 121)]
        internal static void Patch()
        {
            PatchHome();
            PatchArchive();
            PatchBoard();
            AssetDatabase.Refresh();
        }

        // ================================================================ A. Panel_Home
        private static void PatchHome()
        {
            if (!System.IO.File.Exists(HomePath)) { Debug.LogError("[PortraitPatch] 找不到 " + HomePath); return; }
            Backup(HomePath);

            var root = PrefabUtility.LoadPrefabContents(HomePath);
            if (root == null) { Debug.LogError("[PortraitPatch] 载入 prefab 失败"); return; }
            try
            {
                bool created = EnsurePortraitRoot(root);
                EnsureToggleButton(root);
                EnsureBoardManagerButton(root);
                EnsureFlashNode(root);
                BindHome(root);

                PrefabUtility.SaveAsPrefabAsset(root, HomePath);
                Debug.Log("[PortraitPatch] Panel_Home OK（新建=" + created + "）");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>触屏互动白闪层：Raw_Stage 之上、UI_Layer 之下；点击立绘时 α 0→0.16→0。</summary>
        private static void EnsureFlashNode(GameObject root)
        {
            var prt = FindDeep(root.transform, "Portrait_Root");
            if (prt == null) { Debug.LogError("[PortraitPatch] 找不到 Portrait_Root，跳过 Img_Flash"); return; }
            if (FindDeep(prt, "Img_Flash") != null) return;   // 幂等
            var raw = FindDeep(prt, "Raw_Stage");

            var go = new GameObject("Img_Flash", typeof(RectTransform));
            go.transform.SetParent(prt, false);
            var frt = (RectTransform)go.transform;
            Stretch(frt, true);
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);
            img.raycastTarget = false;
            go.SetActive(false);
            if (raw != null) go.transform.SetSiblingIndex(raw.GetSiblingIndex() + 1);  // 紧贴视频层之上
            Debug.Log("[PortraitPatch] + Img_Flash（点击白闪层）");
        }

        /// <summary>2026-10-17 二轮：看板/立绘库入口挪到底排按钮下方（用户指定位置）。</summary>
        private static void EnsureToggleButton(GameObject root)
        {
            var t = FindDeep(root.transform, "Btn_Portrait");
            if (t == null)
            {
                var btn = MakePaperButton(root, "Btn_Portrait", new Vector2(0f, 0f), new Vector2(1408f, 62f), "看板");
                ((RectTransform)btn.transform).sizeDelta = new Vector2(132f, 52f);
                Debug.Log("[PortraitPatch] + Btn_Portrait（底排下方）");
                return;
            }
            // 已存在 → 挪到底排（幂等重定位，老位置右上角作废）
            var rt = (RectTransform)t;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(132f, 52f);
            rt.anchoredPosition = new Vector2(1408f, 62f);
            var lbl = FindDeep(t, "Tmp_Label");
            if (lbl != null)
            {
                var tmp = lbl.GetComponent<TMP_Text>();
                if (tmp != null) tmp.fontSize = 26;
            }
        }

        private static void EnsureBoardManagerButton(GameObject root)
        {
            if (FindDeep(root.transform, "Btn_BoardManager") != null) return;
            var btn = MakePaperButton(root, "Btn_BoardManager", new Vector2(0f, 0f), new Vector2(1552f, 62f), "立绘库");
            ((RectTransform)btn.transform).sizeDelta = new Vector2(132f, 52f);
            var lbl = FindDeep(btn.transform, "Tmp_Label");
            if (lbl != null)
            {
                var tmp = lbl.GetComponent<TMP_Text>();
                if (tmp != null) tmp.fontSize = 26;
            }
            Debug.Log("[PortraitPatch] + Btn_BoardManager（底排下方「立绘库」）");
        }

        private static bool EnsurePortraitRoot(GameObject root)
        {
            var exist = FindDeep(root.transform, "Portrait_Root");
            if (exist != null) return false;   // 幂等：已有就只重绑

            var panelRt = root.GetComponent<RectTransform>();

            var go = new GameObject("Portrait_Root", typeof(RectTransform));
            go.transform.SetParent(root.transform, false);
            var prt = (RectTransform)go.transform;
            Stretch(prt, panelRt != null);
            go.SetActive(false);                       // 默认隐藏（地图模式）

            // 黑底（视频铺不满时补边 + 挡住下层点击）
            var bg = Child(go, "Img_Bg", stretch: true);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = Color.black;
            bgImg.raycastTarget = true;

            // 视频画面（居中按比例适配）
            var stage = Child(go, "Raw_Stage", stretch: false);
            var srt = (RectTransform)stage.transform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0.5f, 0.5f);
            srt.sizeDelta = Vector2.zero;
            var raw = stage.AddComponent<RawImage>();
            raw.raycastTarget = false;
            var fitter = stage.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 1344f / 768f;         // 初值；prepareCompleted 后按视频实际比例更新

            // UI 层（隐藏界面时整体淡出）
            var uiLayer = Child(go, "UI_Layer", stretch: true);
            uiLayer.AddComponent<CanvasGroup>();

            var nameGo = Child(uiLayer, "Tmp_Name", stretch: false);
            var nrt = (RectTransform)nameGo.transform;
            nrt.anchorMin = nrt.anchorMax = nrt.pivot = new Vector2(0f, 0f);
            nrt.sizeDelta = new Vector2(1000f, 64f);
            nrt.anchoredPosition = new Vector2(44f, 96f);
            var tmp = nameGo.AddComponent<TextMeshProUGUI>();
            tmp.text = "";
            tmp.fontSize = 40;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.color = Chalk;
            tmp.raycastTarget = false;
            if (UIBuild.Font != null) tmp.font = UIBuild.Font;

            var prev = MakeGlassButton(uiLayer, "Btn_Prev", new Vector2(0f, 0.5f), new Vector2(40f, 0f), 92f, "<", 48);
            var next = MakeGlassButton(uiLayer, "Btn_Next", new Vector2(1f, 0.5f), new Vector2(-40f, 0f), 92f, ">", 48);
            var hide = MakeGlassButton(uiLayer, "Btn_HideUI", new Vector2(1f, 1f), new Vector2(-60f, -44f), 0f, "隐藏界面", 26);
            var hideRt = (RectTransform)hide.transform;
            hideRt.sizeDelta = new Vector2(170f, 54f);
            var back = MakePaperButton(uiLayer, "Btn_Back", new Vector2(0f, 1f), new Vector2(60f, -44f), "回到地图");

            // UI 隐藏期的点击接管层
            var tap = Child(go, "Img_TapCatch", stretch: true);
            var tapImg = tap.AddComponent<Image>();
            tapImg.color = new Color(0f, 0f, 0f, 0f);
            tapImg.raycastTarget = true;
            tap.AddComponent<Button>();
            tap.SetActive(false);

            // 组件：播放层 + VideoPlayer
            var stageComp = go.AddComponent<WanXiang.Modules.UI.HomePortraitStage>();
            var vp = go.AddComponent<VideoPlayer>();
            vp.playOnAwake = false;
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.audioOutputMode = VideoAudioOutputMode.None;
            vp.isLooping = true;

            BindStage(stageComp, go, raw, tmp, uiLayer.GetComponent<CanvasGroup>(),
                      tap, prev.GetComponent<Button>(), next.GetComponent<Button>(), hide.GetComponent<Button>());
            Debug.Log("[PortraitPatch] + Portrait_Root（Raw_Stage / UI_Layer / TapCatch / Stage / VideoPlayer）");
            return true;
        }

        private static void BindHome(GameObject root)
        {
            var panel = root.GetComponent<WanXiang.Modules.UI.HomePanel>();
            if (panel == null) { Debug.LogError("[PortraitPatch] Panel_Home 根上没有 HomePanel"); return; }
            var so = new SerializedObject(panel);
            Bind(so, "_btnPortrait", FindDeep(root.transform, "Btn_Portrait")?.GetComponent<Button>());
            Bind(so, "_portraitRoot", FindDeep(root.transform, "Portrait_Root")?.gameObject);
            Bind(so, "_portraitStage", FindDeep(root.transform, "Portrait_Root")?.GetComponent<WanXiang.Modules.UI.HomePortraitStage>());
            Bind(so, "_btnPortraitBack", FindDeep(root.transform, "Btn_Back")?.GetComponent<Button>());
            Bind(so, "_btnBoardManager", FindDeep(root.transform, "Btn_BoardManager")?.GetComponent<Button>());
            var stage = FindDeep(root.transform, "Portrait_Root")?.GetComponent<WanXiang.Modules.UI.HomePortraitStage>();
            if (stage != null)
            {
                var sso = new SerializedObject(stage);   // ⚠ _imgFlash 在 HomePortraitStage 上，不在 HomePanel
                Bind(sso, "_imgFlash", FindDeep(FindDeep(root.transform, "Portrait_Root"), "Img_Flash")?.GetComponent<Image>());
            }
        }

        private static void BindStage(WanXiang.Modules.UI.HomePortraitStage stage, GameObject root,
            RawImage raw, TMP_Text name, CanvasGroup uiLayer, GameObject tap,
            Button prev, Button next, Button hide)
        {
            var so = new SerializedObject(stage);
            Bind(so, "_stage", raw);
            Bind(so, "_tmpName", name);
            Bind(so, "_uiLayer", uiLayer);
            Bind(so, "_tapCatch", tap);
            Bind(so, "_btnPrev", prev);
            Bind(so, "_btnNext", next);
            Bind(so, "_btnHide", hide);
        }

        // ================================================================ B. Panel_Archive
        private static void PatchArchive()
        {
            if (!System.IO.File.Exists(ArchivePath)) { Debug.LogError("[PortraitPatch] 找不到 " + ArchivePath); return; }
            Backup(ArchivePath);

            var root = PrefabUtility.LoadPrefabContents(ArchivePath);
            if (root == null) { Debug.LogError("[PortraitPatch] 载入 prefab 失败"); return; }
            try
            {
                var card = FindDeep(root.transform, "Item_BeastCard") as RectTransform;
                if (card == null) { Debug.LogError("[PortraitPatch] Panel_Archive 缺 Item_BeastCard"); return; }

                bool created = false;
                if (FindDeep(card, "Btn_LiveClaim") == null)
                {
                    var btnGo = new GameObject("Btn_LiveClaim", typeof(RectTransform));
                    btnGo.transform.SetParent(card, false);
                    var rt = (RectTransform)btnGo.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                    rt.pivot = new Vector2(0f, 1f);
                    rt.anchoredPosition = new Vector2(152f, -264f);
                    rt.sizeDelta = new Vector2(204f, 32f);
                    var img = btnGo.AddComponent<Image>();
                    img.color = new Color(0.90f, 0.89f, 0.87f, 1f);
                    var btn = btnGo.AddComponent<Button>();
                    btn.targetGraphic = img;

                    var lbl = Child(btnGo, "Tmp_Label", stretch: true);
                    var t = lbl.AddComponent<TextMeshProUGUI>();
                    t.text = "领取动态立绘";
                    t.fontSize = 17;
                    t.alignment = TextAlignmentOptions.Center;
                    t.color = Ink2;
                    t.raycastTarget = false;
                    if (UIBuild.Font != null) t.font = UIBuild.Font;
                    created = true;
                    Debug.Log("[PortraitPatch] + Item_BeastCard/Btn_LiveClaim");
                }

                // 卡高 268 → 304（模板 + grid cellSize 同步，两者必须一致）
                var cardRt = (RectTransform)card;
                cardRt.sizeDelta = new Vector2(368f, 304f);
                var grid = card.parent.GetComponent<GridLayoutGroup>();
                if (grid != null) grid.cellSize = new Vector2(368f, 304f);

                PrefabUtility.SaveAsPrefabAsset(root, ArchivePath);
                Debug.Log("[PortraitPatch] Panel_Archive OK（新建 Btn_LiveClaim=" + created + "，卡高 304）");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ================================================================ C. Panel_PortraitBoard（看板库，新建式：存在即跳过）
        private static void PatchBoard()
        {
            if (System.IO.File.Exists(BoardPath)) { Debug.Log("[PortraitPatch] Panel_PortraitBoard 已存在，跳过"); return; }

            var root = new GameObject("Panel_PortraitBoard", typeof(RectTransform));
            var prt = (RectTransform)root.transform;
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
            var panel = root.AddComponent<WanXiang.Modules.UI.PortraitBoardPanel>();

            var bg = Child(root, "Img_Bg", stretch: true);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.92f);
            bgImg.raycastTarget = true;

            var card = Child(root, "Card", stretch: false);
            var crt = (RectTransform)card.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(1560f, 880f);
            var cardImg = card.AddComponent<Image>();
            cardImg.color = Paper;
            cardImg.raycastTarget = true;

            var title = Child(card, "Tmp_Title", stretch: false);
            var trt = (RectTransform)title.transform;
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 1f);
            trt.sizeDelta = new Vector2(800f, 64f);
            trt.anchoredPosition = new Vector2(0f, -46f);
            var tt = title.AddComponent<TextMeshProUGUI>();
            tt.text = "看板 · 动态立绘";
            tt.fontSize = 40;
            tt.alignment = TextAlignmentOptions.Center;
            tt.color = Ink;
            tt.raycastTarget = false;
            if (UIBuild.Font != null) tt.font = UIBuild.Font;

            var hint = Child(card, "Tmp_Hint", stretch: false);
            var hrt = (RectTransform)hint.transform;
            hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0.5f, 1f);
            hrt.sizeDelta = new Vector2(1440f, 40f);
            hrt.anchoredPosition = new Vector2(0f, -96f);
            var ht = hint.AddComponent<TextMeshProUGUI>();
            ht.text = "点击立绘加入轮换";
            ht.fontSize = 20;
            ht.alignment = TextAlignmentOptions.Center;
            ht.color = Ink2;
            ht.raycastTarget = false;
            if (UIBuild.Font != null) ht.font = UIBuild.Font;

            // ---- 滚动区（Viewport + Mask + Content 网格）----
            var scroll = Child(card, "Scroll_Portraits", stretch: false);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0.5f, 1f);
            srt.sizeDelta = new Vector2(1480f, 560f);
            srt.anchoredPosition = new Vector2(0f, -150f);
            var viewport = Child(scroll, "Viewport", stretch: true);
            var vrt = (RectTransform)viewport.transform;
            viewport.AddComponent<Image>().raycastTarget = true;
            viewport.AddComponent<RectMask2D>();
            var content = Child(viewport, "Content", stretch: false);
            var crt2 = (RectTransform)content.transform;
            crt2.anchorMin = new Vector2(0f, 1f);
            crt2.anchorMax = new Vector2(1f, 1f);
            crt2.pivot = new Vector2(0.5f, 1f);
            crt2.offsetMin = new Vector2(0f, crt2.offsetMin.y);
            crt2.offsetMax = new Vector2(0f, crt2.offsetMax.y);
            var grid = content.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(350f, 300f);
            grid.spacing = new Vector2(20f, 20f);
            grid.padding = new RectOffset(16, 16, 16, 16);
            grid.childAlignment = TextAnchor.UpperCenter;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = scroll.AddComponent<ScrollRect>();
            sr.content = crt2;
            sr.viewport = vrt;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 24f;

            // ---- 卡片模板（默认隐藏）----
            var tpl = Child(content, "Item_PortraitCard", stretch: false);
            var trt2 = (RectTransform)tpl.transform;
            trt2.anchorMin = new Vector2(0f, 1f);
            trt2.anchorMax = new Vector2(0f, 1f);
            trt2.pivot = new Vector2(0f, 1f);
            var cardImg2 = tpl.AddComponent<Image>();
            cardImg2.color = Color.white;
            var tplBtn = tpl.AddComponent<Button>();
            tplBtn.targetGraphic = cardImg2;

            var head = Child(tpl, "Img_Head", stretch: false);
            var hrt2 = (RectTransform)head.transform;
            hrt2.anchorMin = hrt2.anchorMax = hrt2.pivot = new Vector2(0.5f, 1f);
            hrt2.sizeDelta = new Vector2(310f, 200f);
            hrt2.anchoredPosition = new Vector2(0f, -18f);
            var himg = head.AddComponent<Image>();
            himg.raycastTarget = false;

            var nm = Child(tpl, "Tmp_Name", stretch: false);
            var nrt = (RectTransform)nm.transform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0f, 0f);
            nrt.pivot = new Vector2(0.5f, 0f);
            nrt.sizeDelta = new Vector2(330f, 84f);
            nrt.anchoredPosition = new Vector2(175f, 8f);
            var nt = nm.AddComponent<TextMeshProUGUI>();
            nt.text = "异兽名";
            nt.fontSize = 22;
            nt.alignment = TextAlignmentOptions.Center;
            nt.color = Ink;
            nt.raycastTarget = false;
            if (UIBuild.Font != null) nt.font = UIBuild.Font;

            var order = Child(tpl, "Tmp_Order", stretch: false);
            var ort = (RectTransform)order.transform;
            ort.anchorMin = ort.anchorMax = ort.pivot = new Vector2(0f, 1f);
            ort.sizeDelta = new Vector2(120f, 40f);
            ort.anchoredPosition = new Vector2(10f, -10f);
            var ot = order.AddComponent<TextMeshProUGUI>();
            ot.text = "";
            ot.fontSize = 24;
            ot.alignment = TextAlignmentOptions.Left;
            ot.color = Ink;
            ot.raycastTarget = false;
            if (UIBuild.Font != null) ot.font = UIBuild.Font;

            tpl.gameObject.SetActive(false);   // 模板默认隐藏

            // ---- 底部设置行 ----
            var auto = MakePaperButton(card, "Btn_AutoRotate", new Vector2(0f, 0f), new Vector2(120f, 60f), "自动轮换：开");
            ((RectTransform)auto.transform).sizeDelta = new Vector2(360f, 60f);
            var itv = MakePaperButton(card, "Btn_Interval", new Vector2(0f, 0f), new Vector2(600f, 60f), "轮换间隔：180 秒（约一首歌）");
            ((RectTransform)itv.transform).sizeDelta = new Vector2(460f, 60f);
            var close = MakePaperButton(card, "Btn_Close", new Vector2(1f, 0f), new Vector2(-120f, 60f), "关闭");
            ((RectTransform)close.transform).sizeDelta = new Vector2(240f, 60f);

            // ---- 绑定 ----
            var so = new SerializedObject(panel);
            Bind(so, "_tmpHint", ht);
            Bind(so, "_scrollContent", crt2);
            Bind(so, "_cardTemplate", trt2);
            Bind(so, "_btnAutoRotate", auto.GetComponent<Button>());
            Bind(so, "_btnInterval", itv.GetComponent<Button>());
            Bind(so, "_btnClose", close.GetComponent<Button>());

            PrefabUtility.SaveAsPrefabAsset(root, BoardPath);
            Object.DestroyImmediate(root);
            Debug.Log("[PortraitPatch] + Panel_PortraitBoard.prefab（看板库）");
        }

        // ================================================================ 小工具
        private static void Backup(string prefabPath)
        {
            try
            {
                if (!System.IO.Directory.Exists(BackupDir)) System.IO.Directory.CreateDirectory(BackupDir);
                var dst = System.IO.Path.Combine(BackupDir,
                    System.IO.Path.GetFileNameWithoutExtension(prefabPath) + ".prefab." + BackupTag);
                if (!System.IO.File.Exists(dst)) System.IO.File.Copy(prefabPath, dst, false);
            }
            catch (System.Exception e) { Debug.LogWarning("[PortraitPatch] 备份失败：" + e.Message); }
        }

        private static void Bind(SerializedObject so, string field, Object value)
        {
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError("[PortraitPatch] 找不到字段 " + field); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[PortraitPatch] " + field + " ← " + (value != null ? value.name : "null ✗"));
        }

        private static GameObject Child(GameObject parent, string name, bool stretch = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var rt = (RectTransform)go.transform;
            if (stretch)
            {
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            }
            return go;
        }

        private static void Stretch(RectTransform rt, bool dummy)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        /// <summary>半透明黑底按钮（视频上的悬浮控件）。</summary>
        private static GameObject MakeGlassButton(GameObject parent, string name, Vector2 anchor,
            Vector2 pos, float size, string label, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.sizeDelta = size > 0f ? new Vector2(size, size) : new Vector2(170f, 54f);
            rt.anchoredPosition = pos;
            var img = go.AddComponent<Image>();
            img.color = DimGlass;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var lbl = Child(go, "Tmp_Label", stretch: true);
            var t = lbl.AddComponent<TextMeshProUGUI>();
            t.text = label;
            t.fontSize = fontSize;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Chalk;
            t.raycastTarget = false;
            if (UIBuild.Font != null) t.font = UIBuild.Font;
            return go;
        }

        /// <summary>纸面按钮（实底浅色 + 墨字）。</summary>
        private static GameObject MakePaperButton(GameObject parent, string name, Vector2 anchor,
            Vector2 pos, string label)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.sizeDelta = new Vector2(170f, 54f);
            rt.anchoredPosition = pos;
            var img = go.AddComponent<Image>();
            img.color = Paper;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var lbl = Child(go, "Tmp_Label", stretch: true);
            var t = lbl.AddComponent<TextMeshProUGUI>();
            t.text = label;
            t.fontSize = 26;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Ink;
            t.raycastTarget = false;
            if (UIBuild.Font != null) t.font = UIBuild.Font;
            return go;
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
