// ============================================================================
//  HomeMapRing —— 主界面中央「四季节气环」地图（终末地风格）
//  · 24 节气布成圆环（立春在正上，顺时针每 15° 一个），四个 90° 扇区 = 四季
//  · 旅程进度（RunState.Act / NodeOffset）点亮对应扇区；当前节气位置挂菱形标记
//  · 鼠标悬浮：圆盘朝光标方向轻晃（±2.5° + 平移 + 放大 1.03），离开平滑回弹
//  ⛔ 本组件只填数据/动表现，不建 UI —— 所有节点由 Panel_Home.prefab 提供，
//    节点圆点用 Nde_Template 模板克隆（规范允许：模板 Instantiate）。
// ============================================================================

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanXiang.Run;

namespace WanXiang.Modules.UI
{
    public sealed class HomeMapRing : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
    {
        // ---- 绑定（HomeMapPrefabTool 生成 prefab 时用 SerializedObject 接好）----
        [SerializeField] private RectTransform _tilt;          // Map_Tilt    悬浮晃动作用于此
        [SerializeField] private Image      _disc;             // Img_MapDisc 等高线圆盘
        [SerializeField] private Image      _frame;            // Img_MapFrame 外环刻度
        [SerializeField] private Image[]    _sectors;          // 春(0) 夏(1) 秋(2) 冬(3) Radial360
        [SerializeField] private RectTransform _nodesRoot;     // Map_Nodes   24 节点容器
        [SerializeField] private Image      _nodeTemplate;     // Nde_Template 节点模板（默认隐藏）
        [SerializeField] private Image      _marker;           // Img_Marker  当前节气菱形标记
        [SerializeField] private TMP_Text   _label;            // Tmp_MapLabel 中央「第X幕·春 / 节气」

        [Header("表现参数（可在 Inspector 直接调）")]
        [SerializeField] private float _tiltDeg    = 2.5f;     // 悬浮最大倾角
        [SerializeField] private float _tiltShift  = 8f;       // 悬浮最大平移（px）
        [SerializeField] private float _tiltScale  = 1.03f;    // 悬浮放大
        [SerializeField] private float _nodeRadius = 0.44f;    // 节点半径（占半宽比例）
        [SerializeField] private float _markerRadius = 0.57f;  // 标记半径（占半宽比例）

        // ---- 常量表 ----
        private static readonly string[] Terms =
        {
            "立春", "雨水", "惊蛰", "春分", "清明", "谷雨",
            "立夏", "小满", "芒种", "夏至", "小暑", "大暑",
            "立秋", "处暑", "白露", "秋分", "寒露", "霜降",
            "立冬", "小雪", "大雪", "冬至", "小寒", "大寒",
        };
        private static readonly string[] SeasonNames = { "春", "夏", "秋", "冬" };
        private static readonly Color[]  SeasonColors =
        {
            new Color(0.45f, 0.88f, 0.55f),   // 春 · 嫩绿
            new Color(0.98f, 0.48f, 0.36f),   // 夏 · 朱红
            new Color(0.95f, 0.75f, 0.32f),   // 秋 · 金黄
            new Color(0.55f, 0.78f, 0.98f),   // 冬 · 冰蓝
        };
        private static readonly Color FinaleColor = new Color(0.95f, 0.85f, 0.50f); // 终幕 · 琥珀
        private static readonly Color DimColor    = new Color(0.55f, 0.60f, 0.66f, 0.30f);

        // 四季扇区起始屏幕角（-90=正上，顺时针）：春起于上、夏起于右、秋起于下、冬起于左
        private static readonly float[] SeasonStart = { -90f, 0f, 90f, 180f };
        // Unity Radial360 fillOrigin：0=Bottom 1=Right 2=Top 3=Left
        private static readonly int[] SeasonFillOrigin = { 2, 1, 0, 3 };

        private const int NodesPerSeason = 6;
        private const int TotalTerms     = 24;

        // ---- 运行时 ----
        private readonly List<Image> _nodes = new List<Image>();
        private bool _hover;
        private Vector2 _hoverNorm;            // 光标相对盘心归一化坐标（-1..1）
        private bool _built;
        private bool _lastMarkerOn;

        private void OnEnable()  { Refresh(); }

        // ------------------------------------------------------------------
        //  数据 → 表现。HomePanel.OnOpenAsync 每次打开都会调用。
        // ------------------------------------------------------------------
        public void Refresh()
        {
            var run = RunSave.Current;
            int  act    = run != null ? Mathf.Clamp(run.Act, 1, 5) : 1;
            int  offset = run != null ? run.NodeOffset : -1;
            // 幕内进度：NodeOffset=-1 未出发 → 0；0..13 → (N+1)/14
            float prog = offset < 0 ? 0f : Mathf.Clamp01((offset + 1) / 14f);

            EnsureNodesBuilt();
            ApplySectors(act, prog);
            ApplyNodes(act, prog);
            ApplyMarker(act, prog);
            ApplyLabel(act, prog);
        }

        // ------------------------------------------------------------------ 四季扇区
        private void ApplySectors(int act, float prog)
        {
            if (_sectors == null) return;
            bool finale = act >= 5;
            for (int s = 0; s < 4 && s < _sectors.Length; s++)
            {
                var img = _sectors[s];
                if (img == null) continue;

                img.type = Image.Type.Filled;
                img.fillMethod = Image.FillMethod.Radial360;
                img.fillClockwise = true;
                img.fillOrigin = SeasonFillOrigin[s];

                float amount;                      // 该季已点亮的比例
                if (finale || act > s + 1) amount = 1f;
                else if (act == s + 1)     amount = prog;
                else                       amount = 0f;
                img.fillAmount = amount;

                Color c = finale ? FinaleColor : SeasonColors[s];
                c.a = amount > 0.001f ? 0.62f : 0.06f;   // 未点亮的季节留一丝底光
                img.color = c;
            }
        }

        // ------------------------------------------------------------------ 24 节点
        private void EnsureNodesBuilt()
        {
            if (_built) return;
            if (_nodesRoot == null || _nodeTemplate == null) return;

            _nodeTemplate.gameObject.SetActive(false);
            _nodes.Clear();
            for (int k = 0; k < TotalTerms; k++)
            {
                var img = Instantiate(_nodeTemplate, _nodesRoot);
                img.name = "Nde_Term_" + Terms[k];
                img.gameObject.SetActive(true);
                var rt = img.rectTransform;
                rt.anchoredPosition = AngleToPoint(-90f + k * (360f / TotalTerms), NodeR());
                rt.localRotation = Quaternion.identity;
                rt.localScale = Vector3.one;
                _nodes.Add(img);
            }
            _built = true;
        }

        private void ApplyNodes(int act, float prog)
        {
            if (!_built || _nodes.Count != TotalTerms) return;
            bool finale = act >= 5;
            // 已到达的节气总数（1 基）：整幕数 × 6 + 当前幕内推进的节气数
            int reached = finale ? TotalTerms
                                 : (act - 1) * NodesPerSeason
                                   + Mathf.Clamp(Mathf.CeilToInt(prog * NodesPerSeason - 0.001f), 0, NodesPerSeason);

            for (int k = 0; k < TotalTerms; k++)
            {
                var img = _nodes[k];
                if (img == null) continue;
                if (k < reached)
                {
                    Color c = finale ? FinaleColor : SeasonColors[k / NodesPerSeason];
                    c.a = 1f;
                    img.color = c;
                }
                else
                {
                    img.color = DimColor;
                }
            }
        }

        // ------------------------------------------------------------------ 当前节气标记
        private void ApplyMarker(int act, float prog)
        {
            if (_marker == null) return;
            int s = Mathf.Clamp(act - 1, 0, 3);
            float a = SeasonStart[s] + Mathf.Clamp(prog, 0f, 1f) * 90f;
            a = Mathf.Clamp(a, SeasonStart[s] + 4f, SeasonStart[s] + 86f);
            _marker.rectTransform.anchoredPosition = AngleToPoint(a, MarkerR());
            _marker.gameObject.SetActive(true);
            _lastMarkerOn = true;
        }

        // ------------------------------------------------------------------ 中央文案
        private void ApplyLabel(int act, float prog)
        {
            if (_label == null) return;
            if (act >= 5)
            {
                _label.text = "终幕 · 长夏\n四季归一";
                _label.color = FinaleColor;
                return;
            }
            int s = Mathf.Clamp(act - 1, 0, 3);
            int termInSeason = Mathf.Clamp(Mathf.FloorToInt(prog * NodesPerSeason), 0, NodesPerSeason - 1);
            _label.text = string.Format("第{0}幕 · {1}\n{2}", act, SeasonNames[s], Terms[s * NodesPerSeason + termInSeason]);
            _label.color = SeasonColors[s];
        }

        // ------------------------------------------------------------------ 悬浮晃动
        public void OnPointerEnter(PointerEventData eventData)
        {
            _hover = true;
            UpdateHoverNorm(eventData);
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            if (_hover) UpdateHoverNorm(eventData);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hover = false;
        }

        private void UpdateHoverNorm(PointerEventData eventData)
        {
            var rt = transform as RectTransform;
            if (rt == null) return;
            Vector2 local;
            // UGUI 多为 Screen Space Overlay，camera 传 null 也正确
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rt, eventData.position, eventData.pressEventCamera, out local))
            {
                Vector2 half = rt.rect.size * 0.5f;
                if (half.x > 0.5f && half.y > 0.5f)
                {
                    _hoverNorm = new Vector2(
                        Mathf.Clamp(local.x / half.x, -1f, 1f),
                        Mathf.Clamp(local.y / half.y, -1f, 1f));
                }
            }
        }

        private void Update()
        {
            if (_tilt != null)
            {
                // 目标姿态：悬浮 → 朝光标偏；离开 → 归位
                float  tRot   = _hover ? -_hoverNorm.x * _tiltDeg : 0f;
                Vector2 tPos  = _hover ? new Vector2(_hoverNorm.x, _hoverNorm.y) * _tiltShift : Vector2.zero;
                float  tScale = _hover ? _tiltScale : 1f;

                float k = 1f - Mathf.Exp(-10f * Time.deltaTime);   // 帧率无关平滑
                Vector3 e = _tilt.localEulerAngles;
                _tilt.localEulerAngles = new Vector3(e.x, e.y, Mathf.LerpAngle(e.z, tRot, k));
                _tilt.anchoredPosition = Vector2.Lerp(_tilt.anchoredPosition, tPos, k);
                float sc = Mathf.Lerp(_tilt.localScale.x, tScale, k);
                _tilt.localScale = new Vector3(sc, sc, 1f);
            }

            // 标记呼吸脉冲
            if (_marker != null && _marker.gameObject.activeSelf)
            {
                float p = 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 3.2f);
                _marker.rectTransform.localScale = new Vector3(p, p, 1f);
            }
        }

        // ------------------------------------------------------------------ 工具
        private float HalfW()
        {
            if (_disc != null) return _disc.rectTransform.rect.width * 0.5f;
            var rt = transform as RectTransform;
            return rt != null ? rt.rect.width * 0.5f : 280f;
        }

        private float NodeR()   { return HalfW() * _nodeRadius; }
        private float MarkerR() { return HalfW() * _markerRadius; }

        /// <summary>屏幕角（-90=正上，顺时针增）→ 环上锚点（UGUI y 轴向上）。</summary>
        private static Vector2 AngleToPoint(float aDeg, float r)
        {
            float a = aDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * r;
        }
    }
}
