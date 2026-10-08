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
//  文字/配色/落叶都在下面的 `Titles` 表里 —— **改文案只改这张表**。
//  美术替换：`Assets/Resources/UI/ActIntro/paper.png`（宣纸底）、`leaf.png`（落叶）⇒ 丢图即生效。
// ============================================================================

using Cysharp.Threading.Tasks;
using DG.Tweening;
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
            /// <summary>落叶速度倍率（>1 更快）。</summary>
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

        /// <summary>
        /// 进图/换幕时按需播放。**同一个幕只播一次**（记在存档 `RunState.IntroShownAct`）。
        /// 由 <see cref="CampaignPanel"/> 在 OnOpenAsync 里调用 —— 触发点集中在那一个口子，
        /// 免得"进场"和"换幕"两处各写一遍、行为还不一致。
        /// </summary>
        public static void PlayIfNeeded(int act)
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;
            if (run.IntroShownAct == act) return;      // 这一幕已经播过
            run.IntroShownAct = act;
            WanXiang.Run.RunSave.Save(run);            // ⛔ 立刻落盘：否则读档回图会重播
            OpenAsync();
        }

        private static void OpenAsync()
        {
            var host = UnityEngine.Object.FindFirstObjectByType<CampaignPanel>();
            if (host != null) host.OpenPanelAsync<ActIntroPanel>().Forget();
        }

        /// <summary>本面板没有需要预先挂钩的控件（动画全在 OnOpenAsync 里跑）—— 但 UIPanelBase 要求实现。</summary>
        protected override void OnCreate()
        {
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            var run = WanXiang.Run.RunSave.Current;
            int act = run != null ? run.CurrentAct : 1;
            var t = (Titles != null && act >= 1 && act <= Titles.Length && Titles[act - 1] != null)
                ? Titles[act - 1] : (Titles != null && Titles.Length > 0 ? Titles[0] : new ActTitle());

            Play(t, act).Forget();
            return UniTask.CompletedTask;
        }

        private async UniTaskVoid Play(ActTitle t, int act)
        {
            var group = GetComponent<CanvasGroup>();
            if (group != null) group.alpha = 0f;

            if (_imgPaper != null) _imgPaper.color = new Color(_imgPaper.color.r, _imgPaper.color.g, _imgPaper.color.b, 1f);
            if (_tmpTitleMain != null)
            {
                _tmpTitleMain.text = t.Main;
                _tmpTitleMain.color = t.MainColor;
                _tmpTitleMain.maxVisibleCharacters = 0;      // 从"一个字都没写"开始
            }
            if (_tmpTitleSub != null)
            {
                _tmpTitleSub.text = t.Sub;
                var c = _tmpTitleSub.color; c.a = 0f; _tmpTitleSub.color = c;
            }

            if (group != null) await group.DOFade(1f, _fadeIn).SetEase(Ease.OutQuad).ToUniTask();
            await UniTask.Delay(System.TimeSpan.FromSeconds(_titleStart));

            // ---- 逐字浮现（"写"出来）----
            SpawnLeaves(t);
            int n = _tmpTitleMain != null ? _tmpTitleMain.text.Length : 0;
            if (n > 0)
            {
                // 起笔时给整行一个轻微放大回落，像落笔的顿挫
                var tr = _tmpTitleMain.rectTransform;
                tr.localScale = Vector3.one * 1.06f;
                tr.DOScale(1f, n * _charInterval + 0.25f).SetEase(Ease.OutQuad);
                for (int i = 1; i <= n; i++)
                {
                    _tmpTitleMain.maxVisibleCharacters = i;
                    await UniTask.Delay(System.TimeSpan.FromSeconds(_charInterval));
                }
            }

            // ---- 小字 ----
            await UniTask.Delay(System.TimeSpan.FromSeconds(_subDelay));
            if (_tmpTitleSub != null) await _tmpTitleSub.DOFade(1f, 0.35f).SetEase(Ease.OutQuad).ToUniTask();

            await UniTask.Delay(System.TimeSpan.FromSeconds(_hold));
            if (group != null) await group.DOFade(0f, _fadeOut).SetEase(Ease.InQuad).ToUniTask();

            _rootLeaves?.gameObject.SetActive(false);
            CloseSelf();
        }

        /// <summary>落叶：从屏幕上方落下，带横向摇摆与自转，落到底部前淡出（用模板克隆，不新建布局）。</summary>
        private void SpawnLeaves(ActTitle t)
        {
            if (_rootLeaves == null || _itemLeaf == null) return;
            _rootLeaves.gameObject.SetActive(true);
            float halfW = Mathf.Max(200f, _rootLeaves.rect.width * 0.5f);
            float halfH = Mathf.Max(200f, _rootLeaves.rect.height * 0.5f);
            int count = Mathf.Clamp(t.LeafCount, 0, 60);

            for (int i = 0; i < count; i++)
            {
                var leaf = Instantiate(_itemLeaf, _rootLeaves);
                leaf.gameObject.SetActive(true);
                var rt = (RectTransform)leaf.transform;
                float x = Random.Range(-halfW, halfW);
                rt.anchoredPosition = new Vector2(x, halfH + 80f);
                rt.localScale = Vector3.one * Random.Range(0.6f, 1.35f);

                var c = t.LeafTint;
                c.a = Random.Range(0.45f, 0.95f);
                leaf.color = c;

                float dur = Random.Range(2.8f, 4.6f) / Mathf.Max(0.1f, t.LeafSpeed);
                float delay = Random.Range(0f, 1.2f);
                rt.DOAnchorPosY(-halfH - 100f, dur).SetEase(Ease.Linear).SetDelay(delay);
                rt.DOAnchorPosX(x + Random.Range(-160f, 160f), dur).SetEase(Ease.InOutSine).SetDelay(delay);
                rt.DORotate(new Vector3(0f, 0f, Random.Range(-420f, 420f)), dur, RotateMode.FastBeyond360)
                  .SetEase(Ease.Linear).SetDelay(delay);
                leaf.DOFade(0f, dur * 0.35f).SetDelay(delay + dur * 0.65f);
                Destroy(leaf.gameObject, delay + dur + 0.2f);
            }
        }
    }
}
