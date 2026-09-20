// ============================================================================
//  UI 预制体生成器 · 构件工具
//  ---------------------------------------------------------------------------
//  只服务 UIPanelPrefabBuilder：节点/图片/文字/按钮/滚动区/网格的快捷构件方法，
//  以及「SerializedObject 直接把字段绑定到节点」—— 生成的预制体开箱即带引用。
//  占位配色与《美术资产生产方案 v1.2》一致，用户自行替换美术。
// ============================================================================

using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.EditorTools
{
    internal static class UIBuild
    {
        // ---- 占位配色（与美术规范同一套色板）----
        internal static readonly Color Ink   = Hex("#2A2118");
        internal static readonly Color Ink2  = Hex("#6B6157");
        internal static readonly Color Paper = Hex("#F5F0E6");
        internal static readonly Color Silk  = Hex("#E9E2D2");
        internal static readonly Color Card  = Hex("#FBF8F1");
        internal static readonly Color Gold  = Hex("#C9A063");
        internal static readonly Color Dim   = Hex("#C8BDA8");
        internal static readonly Color Night = Hex("#2C3A55");
        internal static readonly Color Red   = Hex("#C8352C");
        internal static readonly Color Wood  = Hex("#789262");

        internal static TMP_FontAsset Font { get; private set; }

        internal static void SetFont(TMP_FontAsset font) { Font = font; }

        internal static Color Hex(string s)
        {
            s = s.TrimStart('#');
            float r = int.Parse(s.Substring(0, 2), System.Globalization.NumberStyles.HexNumber) / 255f;
            float g = int.Parse(s.Substring(2, 2), System.Globalization.NumberStyles.HexNumber) / 255f;
            float b = int.Parse(s.Substring(4, 2), System.Globalization.NumberStyles.HexNumber) / 255f;
            return new Color(r, g, b, 1f);
        }

        // ------------------------------------------------------------------
        // 节点
        // ------------------------------------------------------------------

        internal static RectTransform Node(Transform parent, string name,
            Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
            return rt;
        }

        /// <summary>四向拉伸，带边距（left, top, right, bottom）。</summary>
        internal static RectTransform Stretch(Transform parent, string name,
            float l = 0, float t = 0, float r = 0, float b = 0)
        {
            return Node(parent, name, new Vector2(0, 0), new Vector2(1, 1),
                new Vector2(l, b), new Vector2(-r, -t));
        }

        /// <summary>顶栏：横向拉伸，高度固定 h，下探 h。</summary>
        internal static RectTransform Top(Transform parent, string name, float h,
            float ml = 0, float mr = 0, float mt = 0)
        {
            return Node(parent, name, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(ml, -h - mt), new Vector2(-mr, -mt));
        }

        /// <summary>底栏：横向拉伸，高度固定 h。</summary>
        internal static RectTransform Bottom(Transform parent, string name, float h,
            float ml = 0, float mr = 0, float mb = 0)
        {
            return Node(parent, name, new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(ml, mb), new Vector2(-mr, mb + h));
        }

        /// <summary>左侧栏：纵向拉伸，宽度固定 w。</summary>
        internal static RectTransform Left(Transform parent, string name, float w,
            float ml = 0, float mt = 0, float mb = 0)
        {
            return Node(parent, name, new Vector2(0, 0), new Vector2(0, 1),
                new Vector2(ml, mb), new Vector2(ml + w, -mt));
        }

        /// <summary>右侧栏：纵向拉伸，宽度固定 w。</summary>
        internal static RectTransform Right(Transform parent, string name, float w,
            float mr = 0, float mt = 0, float mb = 0)
        {
            return Node(parent, name, new Vector2(1, 0), new Vector2(1, 1),
                new Vector2(-mr - w, mb), new Vector2(-mr, -mt));
        }

        /// <summary>固定尺寸：锚点 anchor（同一值），anchoredPosition = offset。</summary>
        internal static RectTransform Fixed(Transform parent, string name,
            Vector2 anchor, Vector2 size, Vector2 offset)
        {
            var rt = Node(parent, name, anchor, anchor, Vector2.zero, Vector2.zero);
            rt.sizeDelta = size;
            rt.anchoredPosition = offset;
            return rt;
        }

        /// <summary>居中固定尺寸。</summary>
        internal static RectTransform Center(Transform parent, string name, Vector2 size)
        {
            return Fixed(parent, name, new Vector2(0.5f, 0.5f), size, Vector2.zero);
        }

        // ------------------------------------------------------------------
        // 组件
        // ------------------------------------------------------------------

        internal static Image Img(RectTransform rt, Color c, bool raycast = false)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = raycast;
            return img;
        }

        internal static TextMeshProUGUI Tmp(RectTransform rt, string text, float size, Color c,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (Font != null) tmp.font = Font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = c;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            return tmp;
        }

        /// <summary>给已有 Image 的节点加 Button（ColorTint 三态）。</summary>
        internal static Button Btn(RectTransform rt)
        {
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = rt.GetComponent<Graphic>();
            var colors = btn.colors;
            colors.pressedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
            colors.disabledColor = new Color(0.40f, 0.40f, 0.40f, 0.5f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            return btn;
        }

        /// <summary>快捷按钮：固定尺寸 + Image + TMP 标签 + Button。</summary>
        internal static RectTransform MakeBtn(Transform parent, string name, Vector2 anchor,
            Vector2 size, Vector2 offset, string label, Color c, float fontSize = 30f)
        {
            var rt = Fixed(parent, name, anchor, size, offset);
            Img(rt, c, true);
            var child = Stretch(rt, "Tmp_Label", 10, 8, 10, 8);
            Tmp(child, label, fontSize, Ink);
            Btn(rt);
            return rt;
        }

        /// <summary>居中按钮（BottomBar 常用）。</summary>
        internal static RectTransform MakeBtnX(Transform parent, string name, Vector2 anchor,
            Vector2 size, float x, float y, string label, Color c, float fontSize = 30f)
        {
            return MakeBtn(parent, name, anchor, size, new Vector2(x, y), label, c, fontSize);
        }

        /// <summary>滚动区：ScrollRect + Viewport(RectMask2D) + Content(VerticalLayoutGroup)。
        /// 返回 Content —— 列表项由代码生成；模板放 rt 外部。</summary>
        internal static RectTransform ScrollVertical(Transform parent, string name,
            Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, float spacing = 10f)
        {
            var rt = Node(parent, name, aMin, aMax, offMin, offMax);
            var viewport = Node(rt, "Viewport", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Node(viewport, "Content", new Vector2(0, 1), new Vector2(1, 1),
                Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(0.5f, 1f);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = spacing;
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = rt.gameObject.AddComponent<ScrollRect>();
            sr.content = content;
            sr.viewport = viewport;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 24;
            return content;
        }

        /// <summary>网格容器：GridLayoutGroup（图鉴印章墙 / 商品货架用）。</summary>
        internal static RectTransform GridLayout(Transform parent, string name,
            Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax,
            Vector2 cell, Vector2 spacing, int columns)
        {
            var rt = Node(parent, name, aMin, aMax, offMin, offMax);
            var grid = rt.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = cell;
            grid.spacing = spacing;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.padding = new RectOffset(12, 12, 12, 12);
            return rt;
        }

        /// <summary>横向布局容器（羁绊栏 / 三选一卡排）。</summary>
        internal static RectTransform HLayout(Transform parent, string name,
            Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, float spacing = 12f)
        {
            var rt = Node(parent, name, aMin, aMax, offMin, offMax);
            var layout = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = spacing;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            return rt;
        }

        // ------------------------------------------------------------------
        // 复合控件
        // ------------------------------------------------------------------

        /// <summary>布局组内的槽位（LayoutElement 控制尺寸）。</summary>
        internal static RectTransform Slot(Transform parent, string name, float w, float h)
        {
            var rt = Fixed(parent, name, new Vector2(0.5f, 0.5f), new Vector2(w, h), Vector2.zero);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = h;
            return rt;
        }

        /// <summary>网格滚动区（图鉴印章墙等）。返回 Content，sr 返回 ScrollRect。</summary>
        internal static RectTransform ScrollGrid(Transform parent, string name,
            Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax,
            Vector2 cell, Vector2 spacing, int columns, out ScrollRect sr)
        {
            var rt = Node(parent, name, aMin, aMax, offMin, offMax);
            var viewport = Node(rt, "Viewport", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Node(viewport, "Content", new Vector2(0, 1), new Vector2(1, 1),
                Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(0.5f, 1f);
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = cell;
            grid.spacing = spacing;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.padding = new RectOffset(12, 12, 12, 12);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr = rt.gameObject.AddComponent<ScrollRect>();
            sr.content = content;
            sr.viewport = viewport;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 24;
            return content;
        }

        /// <summary>滑条（Background + Fill Area/Fill + Handle Slide Area/Handle）。</summary>
        internal static RectTransform MakeSlider(Transform parent, string name,
            Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, string label)
        {
            var rt = Node(parent, name, aMin, aMax, offMin, offMax);
            var bg = Stretch(rt, "Tmp_Background", 0, 0, 0, 0);
            Img(bg, Dim, true);

            var fillArea = Node(rt, "Tmp_FillArea", new Vector2(0, 0.5f), new Vector2(1, 0.5f),
                new Vector2(4, -9), new Vector2(-4, 9));
            var fill = Node(fillArea, "Tmp_Fill", new Vector2(0, 0.5f), new Vector2(1, 0.5f),
                Vector2.zero, Vector2.zero);
            fill.sizeDelta = new Vector2(-8, 18);
            var fimg = Img(fill, Wood, false);
            fimg.type = Image.Type.Filled;
            fimg.fillMethod = Image.FillMethod.Horizontal;
            fimg.fillOrigin = (int)Image.OriginHorizontal.Left;

            var slide = Node(rt, "Tmp_SlideArea", new Vector2(0, 0.5f), new Vector2(1, 0.5f),
                new Vector2(0, -14), Vector2.zero);
            slide.offsetMax = new Vector2(0, 14);
            var handle = Fixed(slide, "Tmp_Handle", new Vector2(0, 0.5f), new Vector2(28, 0), Vector2.zero);
            handle.anchorMax = new Vector2(0, 1);
            handle.sizeDelta = new Vector2(28, -6);
            Img(handle, Gold, true);

            var slider = rt.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Graphic>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.value = 0.8f;

            var lbl = Stretch(rt, "Tmp_Label", 4, 4, 4, 4);
            Tmp(lbl, label, 24, Ink, TextAlignmentOptions.Left);
            return rt;
        }

        /// <summary>开关。</summary>
        internal static RectTransform MakeToggle(Transform parent, string name,
            Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, string label)
        {
            var rt = Node(parent, name, aMin, aMax, offMin, offMax);
            var bg = Fixed(rt, "Tmp_Background", new Vector2(0, 0.5f), new Vector2(44, 44), new Vector2(30, 0));
            Img(bg, Card, true);
            var mark = Fixed(bg, "Tmp_Checkmark", new Vector2(0.5f, 0.5f), new Vector2(28, 28), Vector2.zero);
            Img(mark, Wood, false);
            var lbl = Stretch(rt, "Tmp_Label", 90, 4, 4, 4);
            Tmp(lbl, label, 26, Ink, TextAlignmentOptions.Left);

            var tgl = rt.gameObject.AddComponent<Toggle>();
            tgl.targetGraphic = bg.GetComponent<Graphic>();
            tgl.graphic = mark.GetComponent<Graphic>();
            tgl.isOn = true;
            return rt;
        }

        /// <summary>单行输入框（分享码导入）。</summary>
        internal static RectTransform MakeInput(Transform parent, string name,
            Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
        {
            var rt = Node(parent, name, aMin, aMax, offMin, offMax);
            Img(rt, Card, true);

            var area = Stretch(rt, "Tmp_TextArea", 14, 6, 14, 6);
            area.gameObject.AddComponent<RectMask2D>();
            var placeholder = Node(area, "Tmp_Placeholder", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var ptmp = Tmp(placeholder, "input share code", 26, Dim, TextAlignmentOptions.Left);
            var text = Node(area, "Tmp_InputText", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var ttmp = Tmp(text, "", 26, Ink, TextAlignmentOptions.Left);

            var input = rt.gameObject.AddComponent<TMP_InputField>();
            input.textComponent = ttmp;
            input.placeholder = ptmp;
            input.targetGraphic = rt.GetComponent<Graphic>();
            return rt;
        }

        // ------------------------------------------------------------------
        // 绑定：SerializedObject 直接把节点塞进字段
        // ------------------------------------------------------------------

        internal static void Bind(Component comp, string field, Object value)
        {
            var so = new SerializedObject(comp);
            var p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogError("[PrefabBuilder] 字段不存在：" + field + "（组件 " + comp.GetType().Name + "）");
                return;
            }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void BindArr(Component comp, string field, params Object[] values)
        {
            var so = new SerializedObject(comp);
            var p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogError("[PrefabBuilder] 数组字段不存在：" + field + "（组件 " + comp.GetType().Name + "）");
                return;
            }
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>存为 Prefab 并清掉场景临时对象。统一插入一张透明底图节点 Img_Plate（最底层）。
        /// 底图由 UIPanelArtApplier 按面板名灌入 Assets/ArtRes/Screens/Panel_X.png。</summary>
        internal static GameObject SavePrefab(GameObject root, string file)
        {
            // ★ 防覆盖：prefab 已存在就跳过落盘 —— 用户会在 prefab 里手工调整
            //   （位置/字体/配色），生成器重跑一次就把手改全冲掉（用户实测暴怒点）。
            //   要全量刷新时用菜单 WanXiang/UI/强制重建全部面板（先删 Resources/UI 再跑）。
            string existing = "Assets/Resources/UI/" + file + ".prefab";
            if (System.IO.File.Exists(existing))
            {
                Debug.LogWarning("[PrefabBuilder] 跳过（已存在，保留你的手工修改）：" + existing);
                // ★ 临时对象必须销毁！否则每次跑生成器都往 Boot 场景里堆一整套面板
                //   （用户实测：Hierarchy 里堆出两套 Panel_*）。Bind 都在 SavePrefab 之前
                //   完成，这里销毁是安全的。
                Object.DestroyImmediate(root);
                return null;
            }

            var rt = (RectTransform)root.transform;
            var plate = Node(root.transform, "Img_Plate", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            plate.SetAsFirstSibling();
            var img = Img(plate, new Color(1f, 1f, 1f, 0f));
            img.raycastTarget = false;

            string path = "Assets/Resources/UI/" + file + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Debug.Log("[PrefabBuilder] OK  " + path);
            return root;
        }
    }
}
