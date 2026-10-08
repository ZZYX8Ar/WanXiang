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
            public int LeafCount = 16;
            /// <summary>落叶速度倍率（&gt;1 更快）。</summary>
            public float LeafSpeed = 1f;
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
        [SerializeField] private float _hold = 1.15f;         // 全部写完后的停留
        [SerializeField] private float _fadeOut = 0.55f;      // 整屏淡出

        /// <summary>四幕文案（= 幕号-1）。⛔ 只想改文案就改这里，别的都不用动。</summary>
        [Header("四幕文案（每幕一条，按幕号顺序）")]
        [SerializeField]
        private ActTitle[] Titles = new ActTitle[4]
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
            new ActTitle { Main = "寒渊归墟",  Sub = "第四幕 · 冬",
                           LeafTint = new Color(0.78f, 0.88f, 0.96f, 0.9f),
                           MainColor = new Color(0.42f, 0.52f, 0.62f, 1f) },   // 冬：冷灰蓝
        };

        // ------------------------------------------------------------------
        //  触发（静态，只负责"该不该播 + 记账"）
        // ------------------------------------------------------------------

        /// <summary>
        /// 该不该播；要播就顺手记账并**立刻落盘**。
        /// ⛔ **开面板由 <see cref="CampaignPanel"/> 自己做** —— `UIPanelBase.OpenPanelAsync` 是
        ///   `protected`，静态方法里既调不到自己的、更调不到别人的面板（试过，编译报 CS1540）。
        /// </summary>
        public static bool ShouldPlayAndMark(int act)
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return false;
            if (run.IntroShownAct == act) return false;   // 这一幕已经播过
            run.IntroShownAct = act;
            WanXiang.Run.RunSave.SaveCurrent();           // ⛔ 立刻落盘：否则读档回图会重播
            return true;
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

            Play(t).Forget();
            return UniTask.CompletedTask;
        }

        // ------------------------------------------------------------------
        //  播放
        // ------------------------------------------------------------------

        private async UniTaskVoid Play(ActTitle t)
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

            if (group != null) await FadeAsync(group, 0f, 1f, _fadeIn);
            await UniTask.Delay(Ms(_titleStart));

            SpawnLeaves(t);
            UpdateLeavesLoop().Forget();

            // ---- 逐字浮现（"写"出来）----
            int n = _tmpTitleMain != null ? _tmpTitleMain.text.Length : 0;
            for (int i = 1; i <= n; i++)
            {
                _tmpTitleMain.maxVisibleCharacters = i;
                await UniTask.Delay(Ms(_charInterval));
            }

            // ---- 小字 ----
            await UniTask.Delay(Ms(_subDelay));
            if (_tmpTitleSub != null) await FadeTextAsync(_tmpTitleSub, 0f, 1f, 0.35f);

            await UniTask.Delay(Ms(_hold));
            if (group != null) await FadeAsync(group, 1f, 0f, _fadeOut);

            _leaves.Clear();
            if (_rootLeaves != null) _rootLeaves.gameObject.SetActive(false);
            CloseSelf();
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
            float halfW = Mathf.Max(200f, _rootLeaves.rect.width * 0.5f);
            float halfH = Mathf.Max(200f, _rootLeaves.rect.height * 0.5f);
            int count = Mathf.Clamp(t.LeafCount, 0, 60);
            _leaves.Clear();

            for (int i = 0; i < count; i++)
            {
                var leaf = Instantiate(_itemLeaf, _rootLeaves);
                leaf.gameObject.SetActive(true);
                var rt = (RectTransform)leaf.transform;
                float x = Random.Range(-halfW, halfW);
                rt.anchoredPosition = new Vector2(x, halfH + 80f);
                rt.localScale = Vector3.one * Random.Range(0.6f, 1.35f);
                rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

                var c = t.LeafTint;
                float a = Random.Range(0.45f, 0.95f);
                c.a = a;
                leaf.color = c;

                float speed = Mathf.Max(0.1f, t.LeafSpeed);
                float vy = Random.Range(110f, 190f) * speed;
                _leaves.Add(new Leaf
                {
                    Rt = rt,
                    Img = leaf,
                    VY = -vy,
                    VX = Random.Range(-36f, 36f) * speed,
                    RotSpd = Random.Range(-90f, 90f) * speed,
                    Delay = Random.Range(0f, 1.2f),
                    Life = (halfH * 2f + 200f) / Mathf.Max(1f, vy),
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
                    p.x += (L.VX + Mathf.Sin((L.Age + i) * 2.2f) * 26f) * dt;
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
                    if (p.y < -halfH - 120f) L.Rt = null;
                    any = true;
                }
                if (!any) break;
                await UniTask.Yield();
            }
        }
    }
}
