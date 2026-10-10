// ============================================================================
//  Panel_ActIntro —— 每一幕的开场 / 转场动画
//  ---------------------------------------------------------------------------
//  参考《择天记》片头：宣纸底上，大字**逐字浮现**（像写出来），下押一行小字（第 X 幕 · 季节），
//  配合该幕季节的**落叶**飘落。
//
//  触发（两处，都走 CampaignPanel.OnOpenAsync 这一个口子）：
//    · 从主界面出征进节点地图
//    · 打通守关、推进到新的一幕后回图
//  是否已播记在存档的 `RunState.IntroShownAct` ⇒ **同一幕只播一次**，读档回图不会重播。
//
//  幕↔季节的对应沿用五行（项目的幕本就是按五行分的）：
//    幕1 木=春 · 幕2 火=夏 · 幕3 金=秋 · 幕4 水=冬
//
//  ⛔ **本文件刻意不用 DOTween**：`DOFade` 对 TMP 文本、`DOAnchorPos` 对 RectTransform
//     都依赖 DOTween 的额外模块（TMP/UI），本工程只确认了 CanvasGroup.DOFade 可用。
//     为了不引入"编辑器里能编、出包才报错"的风险，动画一律用 UniTask + 手动插值实现。
//
//  文字/配色/落叶都在下面的 `Titles` 表里 —— **改文案只改这张表**。
//  美术替换：`Assets/Resources/UI/ActIntro/paper.png`（宣纸底）、`leaf.png`（落叶）⇒ 丢图即生效。
// ============================================================================

using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_ActIntro", Layer = UILayer.Popup, CachePolicy = UICachePolicy.Transient,
             FullScreen = true, CloseOnMaskClick = false)]
    public sealed class ActIntroPanel : UIPanelBase
    {
        /// <summary>一幕的开场文案与落叶参数。</summary>
        [System.Serializable]
        public sealed class ActTitle
        {
            /// <summary>大字主标题（逐字浮现）。</summary>
            public string Main = "过四季而见陵";
            /// <summary>小字副标题（幕号 · 季节）。</summary>
            public string Sub = "第一幕 · 春";
            /// <summary>落叶色调（每幕不同）。</summary>
            public Color LeafTint = new Color(0.65f, 0.74f, 0.40f, 0.9f);
            /// <summary>主标题颜色。</summary>
            public Color MainColor = new Color(0.78f, 0.60f, 0.24f, 1f);
            /// <summary>落叶数量（多了会糊，12~20 合适）。</summary>
            public int LeafCount = 30;
            /// <summary>落叶速度倍率（&gt;1 更快）。</summary>
            public float LeafSpeed = 1f;
            /// <summary>背景染色（乘在底图上）。每季不同 ⇒ 一眼能看出换季了。</summary>
            public Color BgTint = Color.white;
        }

        [SerializeField] private Image _imgPaper;        // Img_Paper（整屏宣纸底）
        [SerializeField] private TMP_Text _tmpTitleMain; // Tmp_TitleMain（大字）
        [SerializeField] private TMP_Text _tmpTitleSub;  // Tmp_TitleSub（小字）
        [SerializeField] private RectTransform _rootLeaves;  // Root_Leaves
        [SerializeField] private Image _itemLeaf;        // Item_Leaf（模板，运行时克隆）

        // ---- 跳过：双击左键 ----
        [SerializeField] private Button _btnSkip;                  // 全屏透明按钮（prefab 里）
        [SerializeField] private float _skipDoubleClickGap = 0.35f; // 双击间隔上限（秒）
        private float _lastSkipClick = -10f;
        /// <summary>⛔ 静态：静态的渐变辅助方法也要能感知"用户要跳过"。</summary>
        private static bool s_Skip;

        // ---- 诊断（用户要求：看不到效果时先看 Console）----
        [Header("诊断")]
        [SerializeField] private bool _debugLog = false;   // 排查完关掉（需要时在 Inspector 勾上）
        private void Dbg(string msg)
        {
            if (_debugLog) Debug.Log("[ActIntro] " + msg);
        }

        // ---- 时序（秒）----
        [Header("时序")]
        [SerializeField] private float _fadeIn = 0.30f;       // 宣纸底淡入
        [SerializeField] private float _titleStart = 0.15f;   // 起笔前的停顿
        [SerializeField] private float _charInterval = 0.16f; // 每个字出现的间隔（"写"的速度）
        [SerializeField] private float _subDelay = 0.25f;     // 主标题写完后，小字延迟多久出现
        [SerializeField] private float _hold = 0.45f;         // 全部写完后的停留（留够粒子飞出画面）
        [SerializeField] private float _fadeOut = 0.45f;      // 整屏淡出（长一点，收尾不生硬）

        /// <summary>四幕文案（= 幕号-1）。⛔ 只想改文案就改这里，别的都不用动。</summary>
        [Header("四幕文案（每幕一条，按幕号顺序）")]
        [SerializeField]
        private ActTitle[] Titles = new ActTitle[5]
        {
            new ActTitle { Main = "东风解冻",  Sub = "第一幕 · 春",
                           LeafTint = new Color(0.68f, 0.80f, 0.42f, 0.9f),
                           MainColor = new Color(0.72f, 0.66f, 0.24f, 1f) },   // 春：嫩绿
            new ActTitle { Main = "火云燎原",  Sub = "第二幕 · 夏",
                           LeafTint = new Color(0.95f, 0.52f, 0.26f, 0.9f),
                           MainColor = new Color(0.80f, 0.42f, 0.16f, 1f) },   // 夏：赤金
            new ActTitle { Main = "金风玉露",  Sub = "第三幕 · 秋",
                           LeafTint = new Color(0.86f, 0.66f, 0.28f, 0.9f),
                           MainColor = new Color(0.78f, 0.60f, 0.20f, 1f) },   // 秋：赭黄
            new ActTitle { Main = "寒渊凝冰",  Sub = "第四幕 · 冬",
                           LeafTint = new Color(0.78f, 0.88f, 0.96f, 0.9f),
                           MainColor = new Color(0.42f, 0.52f, 0.62f, 1f) },   // 冬：冷灰蓝
            new ActTitle { Main = "厚土归墟",  Sub = "第五幕 · 长夏",
                           LeafTint = new Color(0.80f, 0.68f, 0.42f, 0.9f),
                           MainColor = new Color(0.62f, 0.50f, 0.26f, 1f) },   // 长夏：土黄
        };

        // ------------------------------------------------------------------
        //  触发（静态，只负责"该不该播 + 记账"）
        // ------------------------------------------------------------------

        /// <summary>
        /// 该不该播；要播就顺手记账并**立刻落盘**。
        /// ⛔ **开面板由 <see cref="CampaignPanel"/> 自己做** —— `UIPanelBase.OpenPanelAsync` 是
        ///   `protected`，静态方法里既调不到自己的、更调不到别人的面板（试过，编译报 CS1540）。
        /// </summary>
        public static bool ShouldPlay(int act, bool fromMenu)
        {
            if (fromMenu) return true;                     // ★ 每次从主界面进节点地图都播（用户要的）
            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return true;
            return run.IntroShownAct != act;               // 换幕时也播
        }

        /// <summary>记一笔“这一幕播过了”并落盘。⛔ 必须在**面板真的打开后**调用 ——
        /// 放在 ShouldPlay 里的话，万一面板没开成功，也会被记成已播（用户实测：第一幕没看到却再也不播）。</summary>
        private static void MarkShown(int act)
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;
            run.IntroShownAct = act;
            WanXiang.Run.RunSave.SaveCurrent();
        }

        protected override void OnCreate()
        {
            s_Skip = false;                       // 每次创建归零（面板是 Transient，一般只开一次）
            if (_btnSkip != null)
            {
                _btnSkip.onClick.RemoveAllListeners();   // 幂等
                _btnSkip.onClick.AddListener(OnSkipClicked);
            }
        }

        /// <summary>
        /// 双击左键跳过整段开场。
        /// 走 UGUI 的 Button（GraphicRaycaster）⇒ **不依赖输入后端**（新旧 Input 都能用），
        /// 也不用在 Update 里轮询。单击不响应，必须两击间隔在 `_skipDoubleClickGap` 内。
        /// </summary>
        private void OnSkipClicked()
        {
            if (s_Skip) return;
            float now = Time.unscaledTime;
            if (now - _lastSkipClick <= _skipDoubleClickGap)
            {
                s_Skip = true;
                Dbg("双击跳过开场");
            }
            _lastSkipClick = now;
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            var run = WanXiang.Run.RunSave.Current;
            int act = run != null ? run.Act : 1;
            var t = (Titles != null && act >= 1 && act <= Titles.Length && Titles[act - 1] != null)
                ? Titles[act - 1]
                : (Titles != null && Titles.Length > 0 ? Titles[0] : new ActTitle());

            MarkShown(act);        // ★ 这里才记账（见 MarkShown 注释）
            s_Skip = false;        // 每次开场重置跳过标志
            Dbg("打开 act=" + act + " 标题=\"" + (t != null ? t.Main : "?") + "\""
                + " 主文本=" + (_tmpTitleMain != null ? "有" : "null✗")
                + (_tmpTitleMain != null ? (" 激活=" + _tmpTitleMain.gameObject.activeInHierarchy
                    + " enabled=" + _tmpTitleMain.enabled
                    + " 文本=\"" + _tmpTitleMain.text + "\""
                    + " 颜色=" + _tmpTitleMain.color
                    + " 字号=" + _tmpTitleMain.fontSize
                    + " 字体=" + (_tmpTitleMain.font != null ? _tmpTitleMain.font.name : "null✗")
                    + " rect=" + _tmpTitleMain.rectTransform.rect.width.ToString("F0") + "x" + _tmpTitleMain.rectTransform.rect.height.ToString("F0")
                    + " sizeDelta=" + _tmpTitleMain.rectTransform.sizeDelta.x.ToString("F0")) : ""));

            Play(t, act).Forget();
            return UniTask.CompletedTask;
        }

        // ------------------------------------------------------------------
        //  播放
        // ------------------------------------------------------------------

        /// <summary>
        /// 每幕专属美术：Resources/UI/ActIntro/bg_幕号.png（背景）、particle_幕号.png（粒子）。
        /// **按幕号取文件** ⇒ 以后加幕/换图只要丢文件，不用改 prefab、不用改代码。
        /// 缺图时：背景回退到通用的 paper，粒子回退到模板原有的图。
        /// </summary>
        private void ApplySeasonArt(int act, ActTitle t)
        {
            if (_imgPaper != null)
            {
                var bg = Resources.Load<Sprite>("UI/ActIntro/bg_" + act);
                if (bg == null) bg = Resources.Load<Sprite>("UI/ActIntro/paper");
                if (bg != null) _imgPaper.sprite = bg;
            }
            if (_imgPaper != null && t != null) _imgPaper.color = t.BgTint;   // ★ 季节染色
            // 四周边框也随幕换色（同一张母版 + 染色）—— 若存在  则优先用它
            var fr = transform.Find("Img_Frame");
            if (fr != null)
            {
                var fi = fr.GetComponent<UnityEngine.UI.Image>();
                if (fi != null)
                {
                    var perAct = Resources.Load<Sprite>("UI/ActIntro/frame_" + act);
                    if (perAct != null) fi.sprite = perAct;
                    if (t != null) fi.color = t.BgTint;
                }
            }
            var p = Resources.Load<Sprite>("UI/ActIntro/particle_" + act);
            _seasonParticle = p;
        }

        /// <summary>本幕粒子贴图（为空则用模板自带图）。</summary>
        private Sprite _seasonParticle;

        private async UniTaskVoid Play(ActTitle t, int act)
        {
            var group = GetComponent<CanvasGroup>();
            if (group != null) group.alpha = 0f;

            if (_tmpTitleMain != null)
            {
                _tmpTitleMain.text = t.Main;
                _tmpTitleMain.color = t.MainColor;
                _tmpTitleMain.maxVisibleCharacters = 0;      // 从"一个字都没写"开始
            }
            if (_tmpTitleSub != null)
            {
                _tmpTitleSub.text = t.Sub;
                var c = _tmpTitleSub.color;
                c.a = 0f;
                _tmpTitleSub.color = c;
            }

            ApplySeasonArt(act, t);   // ★ 每幕专属背景 + 粒子图 + 季节染色（缺图自动回退）

            // ⛔ 不再叠「花边框」层（2026-10-10 用户实锤）：它比背景晚 0.55s 淡入 ⇒ 观感是
            //    "先看到本幕季节背景、一下又被一层花纹盖住"（用户："每一幕的背景图有两张会变化"）。
            //    背景图 `bg_<act>` 本身已含四季装饰（竹/枫/雪/土 + 云纹），去掉这层后开场**只剩一张图**。
            var frameT = transform.Find("Img_Frame");
            if (frameT != null) frameT.gameObject.SetActive(false);
            if (group != null) await FadeAsync(group, 0f, 1f, _fadeIn);
            await UniTask.Delay(Ms(_titleStart));

            SpawnLeaves(t);
            UpdateLeavesLoop().Forget();
            SpawnInkBlots();
            UpdateInkBlotsLoop().Forget();

            // ---- 音效 + 缓慢推近（择天记那种"片头感"的两个来源）----
            BrushSfxLoopAsync(_writeTotal).Forget();   // 笔触沙沙（写的过程中隔一段放一次）
            SlowPushAsync().Forget();                  // 画面极缓慢推近

            // ---- 书写 ----
            //  用 **UGUI 内置 RectMask2D 从左到右扫过**（Unity 原生、零布局计算、零自定义 shader）。
            //  遮罩宽度增长 ⇒ 整行字被连续"写"出来（不是一个一个蹦），而且文本位置完全交给 UGUI，
            //  不会再出现"每一字自己算坐标算歪"的问题。
            await WriteTitleAsync(t);

            // ---- 小字 ----
            await UniTask.Delay(Ms(_subDelay));
            if (_tmpTitleSub != null) await FadeTextAsync(_tmpTitleSub, 0f, 1f, 0.35f);

            await UniTask.Delay(Ms(_hold * 0.5f));
            await PlaySealAsync();                     // 落款：印章落到纸上
            await UniTask.Delay(Ms(_hold * 0.5f));
            if (group != null) await FadeAsync(group, 1f, 0f, _fadeOut);

            _leaves.Clear();
            if (_rootLeaves != null) _rootLeaves.gameObject.SetActive(false);
            CloseSelf();
        }

        /// <summary>
        /// 落款：印章从上方加速落下、"啪"地盖上（带过冲与回正），落印瞬间放一记闷响。
        /// 位置/尺寸取自 prefab 的 `Img_Seal`；贴图运行时取 `Resources/UI/ActIntro/seal`（取不到就不显示印章，不影响别的）。
        /// </summary>
        private async UniTask PlaySealAsync()
        {
            var sealT = transform.Find("Img_Seal");
            if (sealT == null) return;
            var rt = sealT as RectTransform;
            var img = sealT.GetComponent<Image>();
            if (img == null) return;
            if (img.sprite == null) img.sprite = Resources.Load<Sprite>("UI/ActIntro/seal");
            if (img.sprite == null) return;                 // 没图就跳过，不报错

            var home = rt.anchoredPosition;
            sealT.gameObject.SetActive(true);
            rt.localScale = Vector3.one * 1.22f;
            rt.localRotation = Quaternion.Euler(0f, 0f, -11f);
            rt.anchoredPosition = new Vector2(home.x, home.y + 240f);
            var c0 = img.color; c0.a = 0f; img.color = c0;

            // 加速下落
            float dur = 0.34f, e = 0f;
            while (e < dur)
            {
                if (s_Skip) break;
                e += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(e / dur), kk = k * k;
                rt.anchoredPosition = new Vector2(home.x, Mathf.Lerp(home.y + 240f, home.y, kk));
                var c = img.color; c.a = k; img.color = c;
                await UniTask.Yield();
            }
            rt.anchoredPosition = home;
            PlaySfx("stamp");                                // 落印那一下

            // 回正 + 缩回原尺寸（盖下去的手感）
            float d2 = 0.26f; e = 0f;
            while (e < d2)
            {
                if (rt == null) return;                         // ⛔ 面板已销毁
                if (s_Skip) break;
                e += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(e / d2);
                rt.localScale = Vector3.one * Mathf.Lerp(1.22f, 1f, k);
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-11f, 0f, k));
                await UniTask.Yield();
            }
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            var cf = img.color; cf.a = 1f; img.color = cf;
        }

        /// <summary>写的过程中隔一段放一次笔触声（不循环，避免接缝）。</summary>
        private async UniTaskVoid BrushSfxLoopAsync(float total)
        {
            for (int i = 0; i < 3; i++)
            {
                if (this == null) return;                       // ⛔ 面板已销毁 ⇒ 别再放音效
                PlaySfx("brush", 0.75f);
                await UniTask.Delay(Ms(Mathf.Max(0.5f, total * 0.32f)));
            }
        }

        /// <summary>极缓慢推近（0.975 → 1.02）—— 片头"呼吸感"的来源。只缩标题层，不动整屏。</summary>
        private async UniTaskVoid SlowPushAsync()
        {
            var tt = transform.Find("Root_Title") as RectTransform;
            if (tt == null) return;
            float dur = Mathf.Max(1f, _writeTotal + _hold + 0.6f), e = 0f;
            while (e < dur)
            {
                // ⛔ 必须每帧判空：本循环活得比面板久（面板先被销毁时，tt 会变成"已销毁"对象），
                //   直接写 localScale 会抛 MissingReferenceException（用户实测过）。
                if (tt == null) return;
                e += Time.unscaledDeltaTime;
                tt.localScale = Vector3.one * Mathf.Lerp(0.975f, 1.02f, Mathf.Clamp01(e / dur));
                await UniTask.Yield();
            }
            if (tt == null) return;
            tt.localScale = Vector3.one * 1.02f;
        }

        /// <summary>放一段音效。走全局音频系统（自带音量/静音/池化）；资源缺失时静默跳过。</summary>
        private void PlaySfx(string name, float vol = 1f)
        {
            WanXiang.Framework.Audio.AudioSystem.PlaySfx(name, vol);
        }

        // ==================================================================
        //  背景墨韵：墨在纸上缓慢洇开
        //  ------------------------------------------------------------------
        //  静态宣纸显得"死"，所以叠几团**极淡**的墨晕，各自缓慢放大 + 游走 + 淡入淡出。
        //  贴图程序化生成（`_tools/_make_inkblot.py`）⇒ 换风格只要换
        //  `Resources/UI/ActIntro/inkblot.png`，代码不用动。
        //  ⚠ 透明度压得很低（默认 0.11）—— 它是氛围，不能抢标题。
        // ==================================================================
        [Header("背景墨韵")]
        [SerializeField] private int _inkBlotCount = 4;
        [SerializeField] private float _inkMaxAlpha = 0.11f;

        private Sprite _inkBlotSprite;

        private sealed class InkBlot
        {
            public RectTransform Rt;
            public Image Img;
            public Vector2 Vel;
            public float Age, Delay, Life, Peak, BaseScale;
        }

        private readonly List<InkBlot> _inkBlots = new List<InkBlot>(8);

        private void SpawnInkBlots()
        {
            var root = transform.Find("Root_Ink") as RectTransform;
            // ⛔ Transform.Find 返回的是 **Transform**，不能 `as Image`（不相关的两个类型 ⇒ CS0039）
            var tplT = transform.Find("Root_Ink/Item_InkBlot");
            var tpl = tplT != null ? tplT.GetComponent<Image>() : null;
            if (root == null || tpl == null) return;          // 缺节点就跳过，不报错
            root.gameObject.SetActive(true);

            var panelRt = transform as RectTransform;
            float hw = Mathf.Max(320f, (panelRt != null && panelRt.rect.width > 1f ? panelRt.rect.width : 1920f) * 0.5f);
            float hh = Mathf.Max(240f, (panelRt != null && panelRt.rect.height > 1f ? panelRt.rect.height : 1080f) * 0.5f);

            if (_inkBlotSprite == null) _inkBlotSprite = Resources.Load<Sprite>("UI/ActIntro/inkblot");
            int n = Mathf.Clamp(_inkBlotCount, 0, 8);
            _inkBlots.Clear();
            for (int i = 0; i < n; i++)
            {
                var clone = Instantiate(tpl, root);
                clone.gameObject.SetActive(true);
                if (_inkBlotSprite != null) clone.sprite = _inkBlotSprite;   // 运行时取图，不依赖 prefab 绑定
                var rt = clone.rectTransform;
                rt.anchoredPosition = new Vector2(Random.Range(-hw * 0.8f, hw * 0.8f),
                                                  Random.Range(-hh * 0.7f, hh * 0.7f));
                float s0 = Random.Range(0.55f, 0.95f);
                rt.localScale = Vector3.one * s0;
                var c = clone.color; c.a = 0f; clone.color = c;
                _inkBlots.Add(new InkBlot
                {
                    Rt = rt, Img = clone,
                    Vel = new Vector2(Random.Range(-16f, 16f), Random.Range(-11f, 11f)),
                    Delay = Random.Range(0f, 0.9f),
                    Life = Random.Range(5.5f, 8.5f),
                    Peak = _inkMaxAlpha * Random.Range(0.7f, 1.15f),
                    BaseScale = s0,
                });
            }
        }

        /// <summary>墨韵推进：延迟 → 放大（洇开）+ 漂移 + 淡入淡出（两端都柔）。</summary>
        private async UniTaskVoid UpdateInkBlotsLoop()
        {
            while (true)
            {
                bool any = false;
                float dt = Time.unscaledDeltaTime;
                for (int i = 0; i < _inkBlots.Count; i++)
                {
                    var b = _inkBlots[i];
                    if (b.Rt == null || b.Img == null) continue;   // ⛔ 面板销毁后这些都变"已销毁"，必须跳过
                    if (b.Delay > 0f) { b.Delay -= dt; any = true; continue; }

                    b.Age += dt;
                    float k = Mathf.Clamp01(b.Age / b.Life);
                    if (k >= 1f) { b.Rt = null; UnityEngine.Object.Destroy(b.Img.gameObject); continue; }

                    b.Rt.anchoredPosition += b.Vel * dt;
                    b.Rt.localScale = Vector3.one * (b.BaseScale * Mathf.Lerp(1f, 1.75f, k));  // 洇开
                    // 透明度：sin 包络（中段最浓），两端为 0 ⇒ 不出现硬边出现/消失
                    var c = b.Img.color;
                    c.a = b.Peak * Mathf.Sin(Mathf.PI * k);
                    b.Img.color = c;
                    any = true;
                }
                if (!any) break;
                await UniTask.Yield();
            }
        }

        /// <summary>逐笔书写用：克隆出来的单字图（每字一张，切 sprite 推进笔画）。</summary>
        private readonly List<Image> _titleCharImgs = new List<Image>(8);

        /// <summary>整句写完的目标总时长（秒）；实际按总笔画数分摊，单笔 0.04~0.18s。</summary>
        private float _writeTotal = 2.2f;   // 整段控制在 4 秒左右（用户：正常 3~5 秒）

        /// <summary>
        /// 书写：用**真实笔顺数据**逐笔写出。
        /// 帧图放在 `Resources/UI/TitleStroke/&lt;字&gt;/&lt;n&gt;.png`，**第 n 帧 = 画完前 n 笔**；
        /// 数据来源 Hanzi Writer / Make Me a Hanzi（1024 网格、y 向下，MIT）。
        /// ⛔ 任一字符缺帧 ⇒ **整体回退**到 TMP 的 `maxVisibleCharacters` 扫出（宁可不好看，也不能不显示）。
        /// </summary>
        private async UniTask WriteTitleAsync(ActTitle t)
        {
            if (_tmpTitleMain == null) return;
            _tmpTitleMain.text = t.Main;
            string text = t.Main ?? "";
            if (string.IsNullOrEmpty(text)) return;

            var tpl = transform.Find("Root_Title/Img_TitleChar") as RectTransform;
            if (tpl == null) { await WriteByTmpAsync(text); return; }

            // ---- 探帧：先数清每个字有多少帧，任一为 0 就整体回退 ----
            var counts = new List<int>(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                int c = 0;
                while (Resources.Load<Sprite>("UI/TitleStroke/" + text[i] + "/" + (c + 1)) != null) c++;
                counts.Add(c);
            }
            for (int i = 0; i < counts.Count; i++)
                if (counts[i] <= 0) { Dbg("字「" + text[i] + "」无笔顺帧 ⇒ 回退 TMP 扫出"); await WriteByTmpAsync(text); return; }

            // ---- 布局：CJK 等宽，按字号步进、整体居中，纵向与 TMP 同一基准 ----
            float step = _tmpTitleMain.fontSize * 1.18f;
            float baseY = _tmpTitleMain.rectTransform.anchoredPosition.y;
            float x0 = -(text.Length - 1) * 0.5f * step;
            _tmpTitleMain.enabled = false;                       // 关掉 TMP，改用帧图

            for (int i = 0; i < text.Length; i++)
            {
                // ⛔ Instantiate(tpl) 里 tpl 是 RectTransform ⇒ 返回的也是 RectTransform，
                //   **不能直接取 .sprite/.enabled**（那是 Image 的成员）⇒ 分开取。
                var clone = Instantiate(tpl, tpl.parent);
                clone.gameObject.SetActive(true);
                clone.anchoredPosition = new Vector2(x0 + i * step, baseY);
                var img = clone.GetComponent<Image>();
                if (img != null)
                {
                    img.enabled = true;
                    // 帧图是**白色**的 ⇒ 在这里用 Image.color 上该幕的颜色（改风格只改 Titles 里的 MainColor）
                    img.color = t.MainColor;
                    var s1 = Resources.Load<Sprite>("UI/TitleStroke/" + text[i] + "/1");
                    if (s1 != null) img.sprite = s1;
                }
                _titleCharImgs.Add(img);
            }

            int totalStrokes = 0;
            for (int i = 0; i < counts.Count; i++) totalStrokes += counts[i];
            float per = Mathf.Clamp(_writeTotal / Mathf.Max(1, totalStrokes), 0.07f, 0.30f);

            // ---- 逐字逐笔推进（第 1 笔创建时就已显示）----
            for (int i = 0; i < text.Length; i++)
            {
                for (int k = 2; k <= counts[i]; k++)
                {
                    var img = _titleCharImgs[i];
                    if (img == null) continue;
                    var s = Resources.Load<Sprite>("UI/TitleStroke/" + text[i] + "/" + k);
                    if (s != null) img.sprite = s;
                    // ⛔ 这里**绝不能再加缩放/位移动画**：整字图一动，之前画好的笔画会跟着抖，
                    //   观感就是"一弹一弹一抖一抖"（用户实测）。笔画推进只切 sprite，不动 transform。
                    await UniTask.Delay(Ms(per));
                }
            }
            Dbg("书写完成（真实笔顺） 文本=\"" + text + "\" 笔画总数=" + totalStrokes
                + " 单笔=" + per.ToString("F3") + "s 字数=" + text.Length);
        }

        /// <summary>回退方案：TMP 原生逐字扫出（没有笔顺帧时用，保证一定有东西显示）。</summary>
        private async UniTask WriteByTmpAsync(string text)
        {
            if (_tmpTitleMain == null) return;
            _tmpTitleMain.enabled = true;
            _tmpTitleMain.text = text;
            _tmpTitleMain.maxVisibleCharacters = 0;
            _tmpTitleMain.ForceMeshUpdate();
            int n = _tmpTitleMain.textInfo.characterCount;
            if (n <= 0) { _tmpTitleMain.maxVisibleCharacters = 9999; return; }
            float per = Mathf.Max(0.06f, _charInterval);
            for (int i = 1; i <= n; i++)
            {
                _tmpTitleMain.maxVisibleCharacters = i;
                await UniTask.Delay(Ms(per));
            }
            _tmpTitleMain.maxVisibleCharacters = 9999;
        }

        /// <summary>
        /// 秒 → 毫秒。跳过后统一压到 **6%** ⇒ 所有 await 几乎立刻返回，
        /// 于是一次检查就能让整段演出"快进到底"（不用在每个 await 上贴跳过判断）。
        /// </summary>
        private int Ms(float seconds)
        {
            return Mathf.Max(1, (int)(seconds * 1000f * (s_Skip ? 0.06f : 1f)));
        }

        private static async UniTask FadeAsync(CanvasGroup g, float from, float to, float dur)
        {
            if (g == null) return;
            g.alpha = from;
            if (dur <= 0.001f || s_Skip) { g.alpha = to; return; }
            float t = 0f;
            while (t < dur)
            {
                if (s_Skip) break;
                t += Time.unscaledDeltaTime;
                g.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / dur));
                await UniTask.Yield();
            }
            g.alpha = to;
        }

        private static async UniTask FadeImageAsync(Image img, float from, float to, float dur)
        {
            if (img == null) return;
            var c = img.color;
            if (dur <= 0.001f || s_Skip) { c.a = to; img.color = c; return; }
            float t = 0f;
            while (t < dur)
            {
                if (s_Skip) break;
                t += Time.unscaledDeltaTime;
                c.a = Mathf.Lerp(from, to, Mathf.Clamp01(t / dur));
                img.color = c;
                await UniTask.Yield();
            }
            c.a = to;
            img.color = c;
        }

        private static async UniTask FadeTextAsync(TMP_Text txt, float from, float to, float dur)
        {
            if (txt == null) return;
            var c = txt.color;
            if (dur <= 0.001f || s_Skip) { c.a = to; txt.color = c; return; }
            float t = 0f;
            while (t < dur)
            {
                if (s_Skip) break;
                t += Time.unscaledDeltaTime;
                c.a = Mathf.Lerp(from, to, Mathf.Clamp01(t / dur));
                txt.color = c;
                await UniTask.Yield();
            }
            c.a = to;
            txt.color = c;
        }

        // ------------------------------------------------------------------
        //  落叶（克隆模板 + 手动积分，不用 DOTween）
        // ------------------------------------------------------------------

        private sealed class Leaf
        {
            public RectTransform Rt;
            public Image Img;
            public float VX, VY, RotSpd, Age, Life, Delay, BaseAlpha;
        }

        private readonly List<Leaf> _leaves = new List<Leaf>(64);

        private void SpawnLeaves(ActTitle t)
        {
            if (_rootLeaves == null || _itemLeaf == null) return;
            _rootLeaves.gameObject.SetActive(true);
            // ⛔ 尺寸必须取**面板**的 rect，不能取容器自己的：容器是拉伸锚点，
            //   在某些上下文（未挂 Canvas 的实例）rect 会退化成 0。
            var panelRt = transform as RectTransform;
            float halfW = Mathf.Max(320f, (panelRt != null && panelRt.rect.width > 1f ? panelRt.rect.width : 1920f) * 0.5f);
            float halfH = Mathf.Max(240f, (panelRt != null && panelRt.rect.height > 1f ? panelRt.rect.height : 1080f) * 0.5f);
            int count = Mathf.Clamp(t.LeafCount, 0, 60);
            _leaves.Clear();

            for (int i = 0; i < count; i++)
            {
                var leaf = Instantiate(_itemLeaf, _rootLeaves);
                if (_seasonParticle != null) leaf.sprite = _seasonParticle;   // 本幕粒子图
                leaf.gameObject.SetActive(true);
                var rt = (RectTransform)leaf.transform;
                // ★ 从**左上角**来、往**右下角**走（用户要求）。
                //   出生点横跨左半边、纵向从顶部之上一点一直铺到画面中部偏下
                //   ⇒ 一部分**开场就在画面里**，不会像以前那样动画结束了还没飘进来。
                float x = -halfW + Random.Range(0f, halfW * 1.1f);
                float y = halfH * 0.85f - Random.Range(0f, halfH * 2.1f);
                rt.anchoredPosition = new Vector2(x, y);
                rt.localScale = Vector3.one * Random.Range(1.40f, 2.90f);
                rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

                var c = t.LeafTint;
                float a = Random.Range(0.80f, 1.00f);
                c.a = a;
                leaf.color = c;

                // ★ 1920×1080 下要从**左上角斜穿到右下角**：横向 1920、纵向 1080
                //   ⇒ 横向速度必须约为纵向的 2 倍。之前反了（纵向快、横向慢）⇒ 没到右边就到底（用户实测）。
                float speed = Mathf.Max(0.1f, t.LeafSpeed);
                float vx = Random.Range(420f, 620f) * speed;
                float vy = Random.Range(300f, 430f) * speed;     // 约 4s 内走完整屏高度
                _leaves.Add(new Leaf
                {
                    Rt = rt,
                    Img = leaf,
                    VY = -vy,
                    VX = vx,                                   // 一律朝右（左上→右下）
                    RotSpd = Random.Range(-90f, 90f) * speed,
                    Delay = Random.Range(0f, 0.30f),   // 必须小：Delay+Life 要装进动画总时长
                    Life = Mathf.Clamp((halfW * 2f + 200f) / Mathf.Max(1f, vx), 2.2f, 3.2f),  // 横向穿越时间，夹进动画窗口
                    BaseAlpha = a,
                });
            }
        }

        /// <summary>落叶推进：等 Delay、然后按速度积分；落到底部前 35% 开始淡出。</summary>
        private async UniTaskVoid UpdateLeavesLoop()
        {
            float halfH = _rootLeaves != null ? Mathf.Max(200f, _rootLeaves.rect.height * 0.5f) : 400f;
            while (true)
            {
                bool any = false;
                if (this == null) return;                       // ⛔ 面板已销毁 ⇒ 直接收工
                if (s_Skip) { _leaves.Clear(); break; }
                float dt = Time.unscaledDeltaTime;
                for (int i = 0; i < _leaves.Count; i++)
                {
                    var L = _leaves[i];
                    if (L.Rt == null) continue;
                    if (L.Delay > 0f) { L.Delay -= dt; any = true; continue; }

                    L.Age += dt;
                    var p = L.Rt.anchoredPosition;
                    // 横向再叠一层正弦摇摆，落得更像叶子
                    p.x += (L.VX + Mathf.Sin((L.Age + i) * 2.0f) * 46f) * dt;
                    p.y += L.VY * dt;
                    L.Rt.anchoredPosition = p;
                    L.Rt.localRotation = Quaternion.Euler(0f, 0f,
                        L.Rt.localRotation.eulerAngles.z + L.RotSpd * dt);

                    float fadeStart = L.Life * 0.65f;
                    if (L.Age > fadeStart && L.Img != null)
                    {
                        var c = L.Img.color;
                        c.a = Mathf.Lerp(L.BaseAlpha, 0f,
                            Mathf.Clamp01((L.Age - fadeStart) / Mathf.Max(0.01f, L.Life - fadeStart)));
                        L.Img.color = c;
                    }
                    if (p.y < -halfH - 120f)
                    {
                        // ⛔ 必须**销毁**，不能只把引用置空：否则 GameObject 留在层级里，
                        //   最后一个更新帧的位置被冻住 ⇒ 观感就是"粒子卡在半空"（用户截图里的现象）。
                        if (L.Rt != null) UnityEngine.Object.Destroy(L.Rt.gameObject);
                        L.Rt = null;
                    }
                    any = true;
                }
                if (!any) break;
                await UniTask.Yield();
            }
        }
    }
}
