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

        // ---- 时序（秒）----
        [Header("时序")]
        [SerializeField] private float _fadeIn = 0.45f;       // 宣纸底淡入
        [SerializeField] private float _titleStart = 0.25f;   // 起笔前的停顿
        [SerializeField] private float _charInterval = 0.16f; // 每个字出现的间隔（"写"的速度）
        [SerializeField] private float _subDelay = 0.25f;     // 主标题写完后，小字延迟多久出现
        [SerializeField] private float _hold = 1.45f;         // 全部写完后的停留（留够粒子飞出画面）
        [SerializeField] private float _fadeOut = 0.75f;      // 整屏淡出（长一点，收尾不生硬）

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

        /// <summary>本面板没有需要预先挂钩的控件（动画全在 OnOpenAsync 里跑）—— 但 UIPanelBase 要求实现。</summary>
        protected override void OnCreate()
        {
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            var run = WanXiang.Run.RunSave.Current;
            int act = run != null ? run.Act : 1;
            var t = (Titles != null && act >= 1 && act <= Titles.Length && Titles[act - 1] != null)
                ? Titles[act - 1]
                : (Titles != null && Titles.Length > 0 ? Titles[0] : new ActTitle());

            MarkShown(act);        // ★ 这里才记账（见 MarkShown 注释）
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
            if (group != null) await FadeAsync(group, 0f, 1f, _fadeIn);
            await UniTask.Delay(Ms(_titleStart));

            SpawnLeaves(t);
            UpdateLeavesLoop().Forget();

            // ---- 书写 ----
            //  用 **UGUI 内置 RectMask2D 从左到右扫过**（Unity 原生、零布局计算、零自定义 shader）。
            //  遮罩宽度增长 ⇒ 整行字被连续"写"出来（不是一个一个蹦），而且文本位置完全交给 UGUI，
            //  不会再出现"每一字自己算坐标算歪"的问题。
            await WriteTitleAsync(t);

            // ---- 小字 ----
            await UniTask.Delay(Ms(_subDelay));
            if (_tmpTitleSub != null) await FadeTextAsync(_tmpTitleSub, 0f, 1f, 0.35f);

            await UniTask.Delay(Ms(_hold));
            if (group != null) await FadeAsync(group, 1f, 0f, _fadeOut);

            _leaves.Clear();
            if (_rootLeaves != null) _rootLeaves.gameObject.SetActive(false);
            CloseSelf();
        }

        /// <summary>
        /// 书写：把 `Root_Title/Img_TitleMask`（RectMask2D）的宽度从 0 推到整幅，字就被连续"扫"出来。
        /// 宽度从**遮罩的父级**（Root_Title）取，不写死，改名/改尺寸都不用动代码。
        /// 收笔时多给 8px，避免最后一列像素被裁掉。
        /// </summary>
        private async UniTask WriteTitleAsync(ActTitle t)
        {
            var maskT = transform.Find("Root_Title/Img_TitleMask");
            var maskRt = maskT as RectTransform;
            if (maskRt == null) return;
            var parentRt = maskRt.parent as RectTransform;
            float fullW = parentRt != null ? parentRt.rect.width : 1200f;

            int n = _tmpTitleMain != null && !string.IsNullOrEmpty(t.Main) ? t.Main.Length : 4;
            float total = Mathf.Max(0.8f, n * _charInterval * 1.7f);   // 整行写完的总时长

            maskRt.sizeDelta = new Vector2(0f, maskRt.sizeDelta.y);
            float elapsed = 0f;
            while (elapsed < total)
            {
                elapsed += Time.unscaledDeltaTime;
                maskRt.sizeDelta = new Vector2(fullW * Mathf.Clamp01(elapsed / total), maskRt.sizeDelta.y);
                await UniTask.Yield();
            }
            maskRt.sizeDelta = new Vector2(fullW + 8f, maskRt.sizeDelta.y);
        }

        private static int Ms(float seconds) { return Mathf.Max(1, (int)(seconds * 1000f)); }

        private static async UniTask FadeAsync(CanvasGroup g, float from, float to, float dur)
        {
            if (g == null) return;
            g.alpha = from;
            if (dur <= 0.001f) { g.alpha = to; return; }
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                g.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / dur));
                await UniTask.Yield();
            }
            g.alpha = to;
        }

        private static async UniTask FadeTextAsync(TMP_Text txt, float from, float to, float dur)
        {
            if (txt == null) return;
            var c = txt.color;
            if (dur <= 0.001f) { c.a = to; txt.color = c; return; }
            float t = 0f;
            while (t < dur)
            {
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
