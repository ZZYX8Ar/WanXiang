// ============================================================================
//  HomeMapJourney —— 主界面「四季旅程」横条地图（旅行者风）
//  ---------------------------------------------------------------------------
//  · 一条横向长卷：左→右 = 春→夏→秋→冬，24 节气沿蛇形小路排布（**已烘焙进
//    prefab**，本组件只做染色/进度 —— 遵守"UI 一律 prefab"约定，运行时零建节点）
//  · 进度 P = ((Act-1) + 幕内进度) / 4（Act5 终幕 = 全图点亮）
//  · 未走过的区域被迷雾盖住：迷雾 Image 锚点从 P 到右缘，左缘 150px 渐隐，
//    走到哪里雾就退到哪里
//  · 旅行者（小旗）站在当前位置，随小路起伏；中央文案「第X幕 · 春 / 节气」
//  · 鼠标悬浮：整条地图朝光标方向轻移 + 微倾（视差），离开平滑回弹
//  ⛔ 节点位置公式必须与 _tools/_gen_homemap2.py 一致：
//    xFrac(k) = (k+1)/25 ；y = sin(k*0.9)*24（小路烘焙在地形图上，同公式）
// ============================================================================

using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanXiang.Run;

namespace WanXiang.Modules.UI
{
    public sealed class HomeMapJourney : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
    {
        // ---- 绑定（UIBuildHomeMap 生成 prefab 时接好）----
        [SerializeField] private RectTransform _par;       // Map_Par      悬浮视差作用层
        [SerializeField] private Image      _terrain;      // Img_Terrain  地形长卷（raycast 入口）
        [SerializeField] private Image      _fog;          // Img_Fog      迷雾（右锚拉伸）
        [SerializeField] private RectTransform _nodesRoot; // Map_Nodes    24 个烘焙节点
        [SerializeField] private Image      _marker;       // Img_Flag     旅行者小旗
        [SerializeField] private Image      _far;          // Img_Far      远山层（大气透视，视差最慢）
        [SerializeField] private Image      _near;         // Img_Near     近景层（视差最快 ， 伪3D）
        [SerializeField] private Image      _finale;       // Img_Finale   终幕「归墟之门」（仅 act≥5 显示）
        [SerializeField] private TMP_Text   _label;        // Tmp_MapLabel 中央文案

        [Header("表现参数（可在 Inspector 直接调）")]
        [SerializeField] private float _waveAmp   = 24f;   // 小路蛇形振幅（px，与美术一致）
        [SerializeField] private float _waveFreq  = 0.9f;  // 蛇形频率（与美术一致）
        [SerializeField] private float _fogSoftPx = 150f;  // 迷雾左缘渐隐宽度（px）
        [SerializeField] private float _tiltDeg   = 1.5f;  // 悬浮最大倾角
        [SerializeField] private float _shiftPx   = 8f;    // 悬浮最大平移（px）
        [Header("伪3D 分层视差")]
        [SerializeField] private float _farParallax  = 0.35f; // 远山随悬浮移动的倍率（慢）
        [SerializeField] private float _nearParallax = 1.70f; // 近景随悬浮移动的倍率（快）

        [Header("斜俯视（大世界）")]
        // ★ 2026-10-13 用户：把"横条长卷"改成斜俯视大世界地图。
        //   旅程从左下（近）走到右上（远）：节点沿新曲线排布 + 按进度缩小（透视），
        //   背景地面用 WanXiang/UI/Perspective 做梯形透视 + 空气透视雾。
        [SerializeField] private float _keystone   = 0.28f;  // 背景地面横向收缩（0=关；近宽远窄）
        [SerializeField] private float _nearScale  = 1.22f;  // 起点（近）节点缩放
        [SerializeField] private float _farScale   = 0.62f;  // 终点（远）节点缩放
        [SerializeField] private float _pathMargin = 100f;   // 路径左右留边（px）
        [SerializeField] private float _depthNear  = -170f;  // 起点 y（近·低）
        [SerializeField] private float _depthFar   = 60f;    // 终点 y（远·高）
        [SerializeField] private float _haze       = 0.45f;  // 空气透视雾强度
        [SerializeField] private float _edgeFadeY  = 0.18f;  // 上下边缘渐隐比例（消掉"硬边矩形"）
        [SerializeField] private float _edgeFadeX  = 0.10f;  // 左右边缘渐隐比例
        [SerializeField] private Sprite[] _seasonArt;        // 春/夏/秋/冬 四张水彩底图（按幕切，空则退回默认）
        [SerializeField] private float _nodeSize   = 104f;   // 节气节点基准尺寸（px，越大越醒目）
        [SerializeField] private float _mapScale   = 1.16f;  // 整张地图放大（铺满屏幕）
        private Material _perspMat;
        private Vector3 _mapScale0 = Vector3.one;
        private bool _scaleCaptured;
        // 当前节点"呼吸光晕"（prefab 节点 Img_NowGlow，按名字取，不做运行时构建）
        [SerializeField] private float _glowSize = 300f;      // 光晕直径（px）
        [SerializeField] private float _travelerScale = 1.30f; // 旅行者放大（更醒目）
        private RectTransform _nowGlow;
        private Image _nowGlowImg;
        private float _nowGlowScale = 1f;

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
            new Color(0.38f, 0.72f, 0.42f),   // 春 · 草绿
            new Color(0.92f, 0.46f, 0.34f),   // 夏 · 朱红
            new Color(0.90f, 0.68f, 0.28f),   // 秋 · 金黄
            new Color(0.48f, 0.70f, 0.95f),   // 冬 · 冰蓝
        };
        private static readonly Color FinaleColor = new Color(0.95f, 0.84f, 0.50f); // 终幕 · 琥珀
        // 四季氛围色调：给地图底图整体上色（轻，别盖掉水彩本身）
        private static readonly Color[] SeasonTint =
        {
            new Color(0.94f, 1.00f, 0.94f),   // 春 · 淡芽绿
            new Color(1.00f, 0.98f, 0.91f),   // 夏 · 暖阳
            new Color(1.00f, 0.93f, 0.83f),   // 秋 · 金
            new Color(0.91f, 0.96f, 1.00f),   // 冬 · 冷蓝
        };
        private static readonly Color DimColor    = new Color(0.70f, 0.73f, 0.76f, 0.72f);

        private const int TermsPerSeason = 6;
        private const int TotalTerms     = 24;
        private const float NodeXDenom   = 25f;   // xFrac=(k+1)/25（首尾留边）

        // ---- 运行时 ----
        private bool _hover;
        private Vector2 _hoverNorm;
        private Vector2 _hoverSmooth;   // 平滑后的悬浮向量（驱动分层视差）
        private Vector2 _farBase, _nearBase; // 远/近层基准位（prefab 里带 y 偏移）

        private void OnEnable()
        {
            if (!_scaleCaptured) { _mapScale0 = transform.localScale; _scaleCaptured = true; }
            if (_far != null) _farBase = _far.rectTransform.anchoredPosition;
            if (_near != null) _nearBase = _near.rectTransform.anchoredPosition;
            SetupPerspectiveMaterial();
            ResolveNowGlow();
            ApplyMapScale();
            Refresh();
        }

        /// <summary>把整张地图放大到铺满屏幕（prefab 原始缩放 × _mapScale；节点会跟着一起放大）。</summary>
        public void ApplyMapScale()
        {
            transform.localScale = _mapScale0 * Mathf.Max(0.1f, _mapScale);
        }

        // ------------------------------------------------------------------ 斜俯视：背景地面透视
        /// <summary>给背景层（远山 / 地面 / 近景 / 小路）套上透视 shader 材质（近宽远窄 + 空气透视雾）。</summary>
        private void SetupPerspectiveMaterial()
        {
            var sh = Resources.Load<Shader>("Shaders/UIPerspective");
            if (sh == null) sh = Shader.Find("WanXiang/UI/Perspective");
            if (sh == null) return;   // 取不到就退回普通渲染（不崩）

            _perspMat = new Material(sh);
            _perspMat.hideFlags = HideFlags.HideAndDontSave;
            _perspMat.SetFloat("_Keystone", _keystone);
            _perspMat.SetFloat("_Haze", _haze);
            _perspMat.SetColor("_HazeColor", new Color(0.93f, 0.91f, 0.87f, 1f));
            _perspMat.SetFloat("_HazeStart", 0.30f);
            _perspMat.SetFloat("_EdgeFadeY", _edgeFadeY);
            _perspMat.SetFloat("_EdgeFadeX", _edgeFadeX);

            if (_far != null) _far.material = _perspMat;
            if (_terrain != null) _terrain.material = _perspMat;
            if (_near != null) _near.material = _perspMat;
            // 小路与地面同材质：保证"路贴在地面上"一起透视
            if (_terrain != null && _terrain.transform.parent != null)
            {
                var path = _terrain.transform.parent.Find("Img_Path");
                var pimg = path != null ? path.GetComponent<Image>() : null;
                if (pimg != null) pimg.material = _perspMat;
            }
        }

        /// <summary>取"当前节点光晕"节点（prefab 里 Img_NowGlow，与 Map_Nodes 同级、排在节点之上）。</summary>
        private void ResolveNowGlow()
        {
            if (_nowGlow != null || _terrain == null || _terrain.transform.parent == null) return;
            var g = _terrain.transform.parent.Find("Img_NowGlow");
            _nowGlow = g as RectTransform;
            _nowGlowImg = g != null ? g.GetComponent<Image>() : null;
            if (_nowGlow != null) _nowGlow.sizeDelta = new Vector2(_glowSize, _glowSize);
        }

        /// <summary>旅程曲线上的点（未透视）。<paramref name="kk"/> = 连续节气相位 0..23。
        /// ⚠ 必须与 _tools/_gen_journey_path_perspective.py 的常量一致（margin / depthNear / depthFar / amp / freq）。</summary>
        private Vector2 PathPoint(float kk)
        {
            float w = StripW();
            float t = Mathf.Clamp01(kk / (TotalTerms - 1));
            float x = -w * 0.5f + _pathMargin + t * (w - 2f * _pathMargin);
            float y = Mathf.Lerp(_depthNear, _depthFar, t) + Mathf.Sin(kk * _waveFreq) * _waveAmp;
            return new Vector2(x, y);
        }

        /// <summary>叠加梯形透视后的落点（与 UIPerspective.shader 同一公式：屏幕 x = 原始 x * s，s = 1-keystone*v）。</summary>
        private Vector2 PathPointWarped(float kk)
        {
            Vector2 p = PathPoint(kk);
            float h = _terrain != null ? _terrain.rectTransform.rect.height : 560f;
            float v = Mathf.Clamp01((p.y + h * 0.5f) / h);
            float s = 1f - _keystone * v;
            return new Vector2(p.x * s, p.y);
        }

        /// <summary>四个季节标签（春/夏/秋/冬）跟到对应季首的曲线点上（浮在路线上方）。</summary>
        private void LayoutSeasonLabels()
        {
            if (_terrain == null || _terrain.transform.parent == null) return;
            var par = _terrain.transform.parent;      // Map_Par
            for (int s = 0; s < SeasonNames.Length; s++)
            {
                var rt = par.Find("Tmp_Season_" + s) as RectTransform;
                if (rt == null) continue;
                Vector2 p = PathPointWarped(s * TermsPerSeason);
                rt.anchoredPosition = new Vector2(p.x, p.y + 46f);
            }
        }

        // ------------------------------------------------------------------
        //  数据 → 表现。HomePanel.OnOpenAsync 每次打开都会调用。
        // ------------------------------------------------------------------
        public void Refresh()
        {
            var run = RunSave.Current;
            int act    = run != null ? Mathf.Clamp(run.Act, 1, 5) : 1;
            int offset = run != null ? run.NodeOffset : -1;
            // 幕内进度：NodeOffset=-1 未出发 → 0；0..13 → (N+1)/14
            float prog = offset < 0 ? 0f : Mathf.Clamp01((offset + 1) / 14f);
            // 全程进度：4 季（幕）铺满整条；终幕 Act5 = 全亮
            float p = act >= 5 ? 1f : ((act - 1) + prog) / 4f;

            ApplySeasonTint(act);
            ApplyNodes(p, act >= 5);
            LayoutSeasonLabels();
            ApplyFogAndMarker(p, act, prog);
            ApplyFinaleGate(act >= 5);
            ApplyLabel(act, prog);
        }

        // ------------------------------------------------------------------ 终幕之门（第五幕表现）
        //  第五幕=长夏·厚土归墟：四季节点全亮琥珀 + 小路右端亮出「归墟之门」，
        //  旅行者走到门前 —— 终局目的地可视化，别的东西不用改。
        private void ApplyFinaleGate(bool finale)
        {
            if (_finale == null) return;
            _finale.gameObject.SetActive(finale);

            // ⚠ 2026-10-11 地图加长（1320→1600）：归墟之门原本是 prefab 里写死的
            //   anchoredPosition.x = 628（按旧宽度调好的），加长后会脱离右端 ， 改成按
            //   实际宽度换算贴到右端内侧，保证任何宽度下都跟着走。
            if (finale)
            {
                // 斜俯视：归墟之门放到旅程终点（右上·远）之外一点，跟着新曲线走
                Vector2 end = PathPointWarped(TotalTerms - 1);
                var p = _finale.rectTransform.anchoredPosition;
                p.x = end.x + 70f;
                p.y = end.y - 6f;
                _finale.rectTransform.anchoredPosition = p;
            }
        }

        // ------------------------------------------------------------------ 四季色调
        /// <summary>给地图底图整体上季色（春淡绿/夏暖/秋金/冬冷蓝），一幕一色，氛围随进度走。</summary>
        private void ApplySeasonTint(int act)
        {
            int s = Mathf.Clamp(act - 1, 0, SeasonTint.Length - 1);
            // 按幕换底图（春/夏/秋/冬各一张水彩）
            if (_far != null && _seasonArt != null && _seasonArt.Length > s && _seasonArt[s] != null)
                _far.sprite = _seasonArt[s];
            var c = SeasonTint[s];
            if (_far != null) _far.color = c;
            if (_terrain != null) _terrain.color = c;
            if (_near != null) _near.color = new Color(c.r, c.g, c.b, Mathf.Max(0.6f, _near.color.a));
            // 当前节点光晕 = 该幕季节色
            if (_nowGlowImg != null)
            {
                var gc = act >= 5 ? FinaleColor : SeasonColors[s];
                _nowGlowImg.color = new Color(gc.r, gc.g, gc.b, _nowGlowImg.color.a);
            }
        }

        // ------------------------------------------------------------------ 节点染色
        private void ApplyNodes(float p, bool finale)
        {
            if (_nodesRoot == null) return;
            int n = _nodesRoot.childCount;
            for (int k = 0; k < n; k++)
            {
                var child = _nodesRoot.GetChild(k) as RectTransform;
                if (child == null) continue;
                var img = child.GetComponent<Image>();
                if (img == null) continue;

                // ---- 斜俯视：沿"左下(近) → 右上(远)"的曲线排布，并按进度缩小（近大远小）----
                float tk = n > 1 ? (float)k / (n - 1) : 0f;
                child.anchoredPosition = PathPointWarped(tk * (TotalTerms - 1));
                child.sizeDelta = new Vector2(_nodeSize, _nodeSize);          // 放大节点，让 journey 更醒目
                float sc = Mathf.Lerp(_nearScale, _farScale, tk);
                child.localScale = new Vector3(sc, sc, 1f);

                float xFrac = (k + 1) / NodeXDenom;
                if (finale || xFrac <= p + 0.0001f)
                {
                    var c = finale ? FinaleColor : SeasonColors[k / TermsPerSeason];
                    c.a = 1f;
                    img.color = c;
                }
                else
                {
                    img.color = DimColor;
                }
            }
        }

        // ------------------------------------------------------------------ 迷雾退散 + 旅行者
        private void ApplyFogAndMarker(float p, int act, float prog)
        {
            float w = StripW();

            // 旅行者：连续节气相位 t∈[0,23]（P=0 → 立春；P=1 → 大寒）
            float t = Mathf.Clamp(p, 0f, 1f) * (TotalTerms - 1);
            Vector2 mp = PathPointWarped(t);
            float ms = Mathf.Lerp(_nearScale, _farScale, Mathf.Clamp01(t / (TotalTerms - 1)));
            if (_marker != null)
            {
                _marker.gameObject.SetActive(true);
                _marker.rectTransform.anchoredPosition = mp + new Vector2(0f, 4f); // 底枢轴：脚踩小路
                float tsc = ms * _travelerScale;                                  // 近大远小 + 放大更醒目
                _marker.rectTransform.localScale = new Vector3(tsc, tsc, 1f);
            }
            // 当前节点呼吸光晕：跟着旅行者
            if (_nowGlow != null)
            {
                _nowGlow.anchoredPosition = mp;
                _nowGlowScale = ms;
            }

            // 迷雾：从当前位置盖到最右；走完消失。左缘自带 150px 渐隐
            if (_fog != null)
            {
                if (p >= 0.995f)
                {
                    _fog.gameObject.SetActive(false);
                }
                else
                {
                    _fog.gameObject.SetActive(true);
                    var rt = _fog.rectTransform;
                    rt.anchorMin = new Vector2(Mathf.Clamp01(p), 0f);
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = new Vector2(-_fogSoftPx, 0f);   // 左缘外扩 = 渐隐带
                    rt.offsetMax = Vector2.zero;
                }
            }
        }

        // ------------------------------------------------------------------ 中央文案
        private void ApplyLabel(int act, float prog)
        {
            if (_label == null) return;
            if (act >= 5)
            {
                _label.text = "终幕 · 长夏";
                _label.color = FinaleColor;
                return;
            }
            int s = Mathf.Clamp(act - 1, 0, 3);
            int termInSeason = Mathf.Clamp(Mathf.FloorToInt(prog * TermsPerSeason), 0, TermsPerSeason - 1);
            _label.text = string.Format("第{0}幕 · {1}\n{2}", act, SeasonNames[s], Terms[s * TermsPerSeason + termInSeason]);
            _label.color = SeasonColors[s];
        }

        // ------------------------------------------------------------------ 悬浮视差
        public void OnPointerEnter(PointerEventData e) { _hover = true;  UpdateHoverNorm(e); }
        public void OnPointerMove(PointerEventData e)  { if (_hover) UpdateHoverNorm(e); }
        public void OnPointerExit(PointerEventData e)  { _hover = false; }

        private void UpdateHoverNorm(PointerEventData eventData)
        {
            var rt = transform as RectTransform;
            if (rt == null) return;
            Vector2 local;
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
            float k = 1f - Mathf.Exp(-10f * Time.deltaTime);
            Vector2 hTarget = _hover ? _hoverNorm : Vector2.zero;
            _hoverSmooth = Vector2.Lerp(_hoverSmooth, hTarget, k);

            // 常态轻漂移（不动鼠标场景也是活的）
            Vector2 drift = new Vector2(
                Mathf.Sin(Time.unscaledTime * 0.4f) * 3f,
                Mathf.Cos(Time.unscaledTime * 0.31f) * 1.5f);

            if (_par != null)
            {
                float tRot = -_hoverSmooth.x * _tiltDeg;
                Vector3 e = _par.localEulerAngles;
                _par.localEulerAngles = new Vector3(e.x, e.y, Mathf.LerpAngle(e.z, tRot, k));
                _par.anchoredPosition = _hoverSmooth * _shiftPx + drift;
            }

            // 伪3D：远山慢、近景快 ， 悬浮/漂移时三层错动产生纵深
            if (_far != null)
                _far.rectTransform.anchoredPosition = _farBase +
                    new Vector2(_hoverSmooth.x, _hoverSmooth.y * 0.5f) * (_shiftPx * _farParallax)
                    + drift * _farParallax;
            if (_near != null)
                _near.rectTransform.anchoredPosition = _nearBase +
                    new Vector2(_hoverSmooth.x, _hoverSmooth.y * 0.5f) * (_shiftPx * _nearParallax)
                    + drift * _nearParallax;

            // 小旗轻微摆动
            if (_marker != null && _marker.gameObject.activeSelf)
            {
                float sway = 1.5f * Mathf.Sin(Time.unscaledTime * 2.6f);
                var e2 = _marker.rectTransform.localEulerAngles;
                _marker.rectTransform.localEulerAngles = new Vector3(e2.x, e2.y, sway);
            }

            // 当前节点光晕：呼吸（缩放 + 透明度）
            if (_nowGlow != null)
            {
                float ph = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.2f);
                _nowGlow.localScale = Vector3.one * (_nowGlowScale * (0.9f + 0.30f * ph));
                if (_nowGlowImg != null)
                {
                    var gc = _nowGlowImg.color;
                    gc.a = Mathf.Lerp(0.50f, 0.92f, ph);
                    _nowGlowImg.color = gc;
                }
            }

            // 归墟之门：终幕时轻微呼吸（没有就跳过）
            if (_finale != null && _finale.gameObject.activeSelf)
            {
                float br = 1f + 0.025f * Mathf.Sin(Time.unscaledTime * 1.7f);
                _finale.rectTransform.localScale = new Vector3(br, br, 1f);
            }
        }

        // ------------------------------------------------------------------ 工具
        private float StripW()
        {
            if (_terrain != null) return _terrain.rectTransform.rect.width;
            var rt = transform as RectTransform;
            return rt != null ? rt.rect.width : 1240f;
        }
    }
}
