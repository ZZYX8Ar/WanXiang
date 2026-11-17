// ============================================================================
//  万相 · UI 转场（水墨铺开 / 粒子横扫 / 云过）
//  ---------------------------------------------------------------------------
//  2026-10-11 用户需求：
//    · "从开始界面进入主界面太生硬，加一点转场，比如一点或几滴水墨铺开" → Ink
//    · "从主界面进入节点地图也生硬" + "粒子快速从右往左滑满整个屏幕，
//      然后才开始写字转场" → Sweep（按幕换粒子）
//    · "根据不同的幕，转场前有不同的开场"
//
//  实现要点：
//   · 独立 Canvas（overrideSorting + sortingOrder=32000）， 永远盖在所有面板之上，
//     不受 UIRoot 内 sibling 顺序影响。
//   · 时间轴：进场(42%) → 切换面板(onSwitch) → 停留(10%) → 退场(48%)，
//     默认总时长 2.4s（用户要"2-3 秒"）。
//   · 转场期间有一层全屏透明 Image 吃点击（防误触），其余元素 raycastTarget=false。
//   · ⛔ 本项目禁用 ParticleSystem ， 全部用 Image。
// ============================================================================

using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.Modules.UI
{
    /// <summary>UI 转场特效层。</summary>
    public sealed partial class UiTransition : MonoBehaviour
    {
        public enum Kind
        {
            Ink,      // 水墨铺开（墨团从中心扩散 + 墨滴飞溅）
            Sweep,    // 粒子横扫（按幕的粒子从右往左刷满全屏）
            Part,     // 粒子**拨开**（按幕的粒子从四周向中心收拢遮屏 → 从中缝向四周退开）——
                      // 2026-10-20 用户："把滑动改为拨开"，滑动只在历史里保留
            Split,    // 双帘拨开（两幅墨帘从两侧合拢，再从中缝向两侧拨开）
            Cloud,    // 云过（几朵云从右往左飘过）
            InkCover, // 墨从四边洇入（复用 WanXiang/UI/InkCover 材质；2026-11-04，存档→主界面用）
        }

        private static UiTransition _inst;

        private Canvas _canvas;
        private RectTransform _root;
        private Image _blocker;              // 全屏拦截层（吃点击）
        private Image _cover;                // 前置遮盖层（进场先盖住旧界面）
        private readonly List<Image> _pool = new List<Image>();
        private bool _busy;
        private float _busySince;      // 本场开始时间（unscaled），用于识别"协程死了但 _busy 还挂着"
        private const float STALL_SECONDS = 12f;
        private float _deadline;
        private bool _exitStarted;
        private readonly List<Streak> _streaks = new List<Streak>();   // 横扫转场的粒子尾迹
        private static Shader _streakShader;                            // WanXiang/UI/Streak（拖尾）
        private Material _inkCoverMat;                                  // WanXiang/UI/InkCover（四边洇入）
        private static readonly int InkProgressId = Shader.PropertyToID("_Progress");


        /// <summary>粒子尾迹：一个 Image（材质用 UIStreak shader）。</summary>
        private struct Streak { public RectTransform T; public Image Img; public float Len; }

        /// <summary>是否正在播转场（供外部等转场结束再触发后续演出，如写字开场）。</summary>
        public static bool IsPlaying { get { return _inst != null && _inst._busy; } }

        /// <summary>
        /// 转场是否已进入"退场/拨开"阶段。**写字这类后续演出应该等这个而不是等
        /// IsPlaying 变 false** —— 在退场那一刻就开始淡入，才能与"粒子拨开/双帘退去"
        /// 重叠，观感连贯（用户 2026-10-12："粒子拨开转场就立马到写字了，没有衔接起来"）。
        /// </summary>
        public static bool ExitStarted { get { return _inst != null && _inst._exitStarted; } }

        private static readonly string[] FallByAct =
        {
            "Battle/FX_particle_act1_petal", "Battle/FX_particle_act2_firefly",
            "Battle/FX_particle_act3_leaf", "Battle/FX_particle_act4_snow",
            "Battle/FX_particle_act5_pollen",
        };

        /// <summary>
        /// 按幕自动选转场方式（用户："根据不同的幕，这个转场前有不同的开场"）。
        /// 春·水墨铺开 / 夏·粒子横扫 / 秋·双帘拨开 / 冬·粒子横扫(雪) / 长夏·双帘拨开。
        /// 粒子图本身也随幕变（见 FallByAct）。
        /// </summary>
        public static Kind KindForAct(int act)
        {
            switch (Mathf.Clamp(act, 1, 5))
            {
                // ★ 2026-10-20 用户最终口径："参考第三幕的转场" ——
                //   第三幕就是双帘拨开（Split）。所以**所有幕统一用 Split**，
                //   滑动（Sweep）不再出现在任何链路里。（Part 保留但不再选用。）
                default: return Kind.Split;
            }
        }

        /// <summary>按幕自动选方式播放。</summary>
        public static void PlayAuto(int act, System.Action onSwitch, string coverSprite = null,
                                    float total = 2.4f, string tag = "",
                                    System.Func<UniTask> preSequence = null)
        {
            Play(KindForAct(act), act, onSwitch, coverSprite, total, tag, preSequence);
        }

        /// <summary>播放转场；到中点时回调 onSwitch（在那里打开目标面板）。</summary>
        /// <param name="coverSprite">
        /// 遮盖层用的背景图路径（Resources.Load）。**传目标界面的背景图**，
        /// 转场盖住时就是同一张底图、退场淡出露出的也是它 ， 全程一张底图，不跳变
        /// （用户 2026-10-12："因为转场完又有一个背景图，能不能把这个背景图贯穿"）。
        /// 传 null 则用按幕的纯色纸底兜底。
        /// </param>
        public static void Play(Kind kind, int act, System.Action onSwitch, string coverSprite = null,
                                float total = 2.4f, string tag = "",
                                System.Func<UniTask> preSequence = null)
        {
            var inst = Ensure();
            if (inst == null) { if (onSwitch != null) onSwitch(); return; }
            if (inst._busy) { if (onSwitch != null) onSwitch(); return; }   // 防重入
            inst.StartCoroutine(inst.Run(kind, Mathf.Clamp(act, 1, 5), onSwitch, Mathf.Max(1.2f, total), null, coverSprite, tag, preSequence));
        }

        private static UiTransition Ensure()
        {
            if (_inst != null) return _inst;
            var go = new GameObject("UiTransitionLayer");
            DontDestroyOnLoad(go);
            _inst = go.AddComponent<UiTransition>();
            _inst.Build();

            // 把「是否在转场」告诉框架层：转场期间 UI 面板不播开合音
            // （否则"关旧面板 + 开新面板"会连响两声，用户 2026-11-07 反馈转场音效很怪）。
            WanXiang.Framework.UI.UIPanelBase.TransitionProbe = () => IsPlaying;

            return _inst;
        }

        private void Build()
        {
            // ★ 幂等化（2026-10-14）：onSwitch 里同步 LoadScene 后会重建层（见
            //   RebuildAfterSceneSwitch）， RectTransform / Canvas / GraphicRaycaster 都在
            //   DontDestroyOnLoad 的层本体上，重复 Add 会挂双份。
            var rt = GetComponent<RectTransform>();
            if (rt == null) rt = gameObject.AddComponent<RectTransform>();

            // ⛔⛔ 上一版的错（用户截图："Scene 场景左下角怎么这么小一个"）：
            //    new GameObject 的 RectTransform 挂在 **DontDestroyOnLoad 根**下 ， parent 为 null。
            //    **根级 RectTransform 的 anchorMin/anchorMax 不生效**，实际尺寸由 sizeDelta 决定，
            //    而它还是默认 (0,0) ， Canvas 渲染区域几乎为 0，墨团/粒子绝大部分被裁掉，
            //    只剩中间一小点（所以"开始→主界面只能看到一点"）。
            // ✅ 正解：把转场层**挂到 UIRoot 主 Canvas 之下**，由子 Canvas(overrideSorting) 独立置顶。
            //    这样尺寸/缩放口径完全继承主 Canvas（1280x720 参考分辨率），不用自己猜。
            var host = FindMainCanvas();
            if (host != null)
            {
                rt.SetParent(host.transform, false);
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.localScale = Vector3.one;
                rt.localPosition = Vector3.zero;
            }
            else
            {
                // 兜底：找不到主 Canvas 就自己做，但**必须显式给尺寸**
                // （根级 RectTransform 的 sizeDelta 就是实际尺寸）。
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                rt.sizeDelta = new Vector2(1280f, 720f);
            }

            if (_canvas == null)
            {
                _canvas = gameObject.AddComponent<Canvas>();
                _canvas.overrideSorting = true;
                _canvas.sortingOrder = 32000;          // 盖过所有面板
                gameObject.AddComponent<GraphicRaycaster>();
            }
            _root = rt;
            gameObject.SetActive(true);
            Debug.Log("[UiTransition] 层已建 host=" + (host != null ? host.name : "(独立Canvas)") +
                      " size=" + rt.rect.width + "x" + rt.rect.height);

            // ★ 前置遮盖层（用户 2026-10-12："从主界面到节点地图转场时，我不想看到这个主界面"）。
            //   双帘/墨团合拢前屏幕是空的，旧界面会从缝里露出来 ， 进场第一帧就用一层
            //   不透明"纸/墨"底色把旧界面盖掉，转场效果在这层之上演，退场最后才撤。
            _cover = NewImage("Cover", 0);
            _cover.rectTransform.anchorMin = Vector2.zero;
            _cover.rectTransform.anchorMax = Vector2.one;
            _cover.rectTransform.offsetMin = Vector2.zero;
            _cover.rectTransform.offsetMax = Vector2.zero;
            _cover.raycastTarget = false;                 // 不吃点击（拦截层负责）
            _cover.color = new Color(0f, 0f, 0f, 0f);

            // 全屏拦截层（透明但吃点击，防止转场期间误触）
            _blocker = NewImage("Blocker", 1);
            _blocker.rectTransform.anchorMin = Vector2.zero;
            _blocker.rectTransform.anchorMax = Vector2.one;
            _blocker.rectTransform.offsetMin = Vector2.zero;
            _blocker.rectTransform.offsetMax = Vector2.zero;
            _blocker.color = new Color(0f, 0f, 0f, 0f);
            _blocker.raycastTarget = false;        // 平时不吃；转场时打开
        }

        /// <summary>找 UIRoot 的主 Canvas（sortingOrder 最高的那个激活 Canvas）。</summary>
        private Canvas FindMainCanvas()
        {
            Canvas best = null;
            foreach (var c in Object.FindObjectsOfType<Canvas>())
            {
                if (c == null || !c.isActiveAndEnabled) continue;
                if (c == _canvas) continue;
                if (best == null || c.sortingOrder > best.sortingOrder) best = c;
            }
            return best;
        }

        private Image NewImage(string name, int order)
        {
            // ⛔ 这里原本有一行 `while (_pool.Count > order) { }`（空循环体）——
            //   第二次调用起条件恒真 ， **主线程死循环**，转场一播就整个卡住
            //   （用户现象："开始→主界面只有一片黑"，因为墨团铺开后卡死）。
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            go.transform.SetSiblingIndex(order);
            _pool.Add(img);
            return img;
        }

        private static Shader StreakShader()
        {
            if (_streakShader == null) _streakShader = Shader.Find("WanXiang/UI/Streak");
            if (_streakShader == null) Debug.LogWarning("[UiTransition] 找不到 shader WanXiang/UI/Streak");
            return _streakShader;
        }

        /// <summary>建一条尾迹（透明底 + UIStreak shader + 横向拉长）。</summary>
        private Streak NewStreak(string name, float length, float thick, Color col)
        {
            var sh = StreakShader();
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            // ⛔ preserveAspect 必须 false：v1 用 true 时源图 8:1 比例会接管尺寸，
            //    sizeDelta 形同虚设（和闪电缩成小方块同一个坑，用户截图"又粗又实"）。
            img.preserveAspect = false;
            if (sh != null)
            {
                var mat = new Material(sh);
                mat.SetColor("_Color", col);          // ★ 按幕染色，不再是死白
                mat.SetFloat("_FadePow", 2.6f);       // 尾多长（越大越短促）
                mat.SetFloat("_HeadFloor", 0.85f);    // 头部保底
                mat.SetFloat("_StreakAlpha", 0.75f);
                mat.SetFloat("_TailGlow", 0.25f);     // 尾部提亮（速度感）
                img.material = mat;
            }
            img.color = new Color(1f, 1f, 1f, 1f);
            img.rectTransform.sizeDelta = new Vector2(length, thick);
            return new Streak { T = img.rectTransform, Img = img, Len = length };
        }

        /// <summary>尾迹颜色按幕（春粉/夏青/秋赭/冬冷/长夏金），与当幕粒子同调。</summary>
        private static Color StreakColorOf(int act)
        {
            switch (act)
            {
                case 1: return new Color(1.00f, 0.78f, 0.84f, 1f);   // 春·花粉
                case 2: return new Color(0.72f, 0.92f, 0.80f, 1f);   // 夏·萤青
                case 3: return new Color(1.00f, 0.76f, 0.52f, 1f);   // 秋·落叶橙
                case 4: return new Color(0.82f, 0.90f, 1.00f, 1f);   // 冬·雪蓝
                default: return new Color(1.00f, 0.88f, 0.58f, 1f);  // 长夏·金尘
            }
        }

        private Image NewImage(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            return img;
        }

        private void ClearExtra()
        {
            // ⚠ _cover（前置遮盖层）和 _blocker（拦截层）是常驻基础设施，不能清。
            for (int i = _pool.Count - 1; i >= 0; i--)
            {
                if (_pool[i] == null) { _pool.RemoveAt(i); continue; }
                GameObject g = _pool[i].gameObject;
                if (g == null) { _pool.RemoveAt(i); continue; }
                if (_blocker != null && g == _blocker.gameObject) continue;
                if (_cover != null && g == _cover.gameObject) continue;
                Destroy(g);
                _pool.RemoveAt(i);
            }
            _streaks.Clear();
        }

        private float CW { get { return _root.rect.width > 1f ? _root.rect.width : 1280f; } }
        private float CH { get { return _root.rect.height > 1f ? _root.rect.height : 720f; } }

        /// <summary>UniTask 版（给 MainSceneEntry 这类 async 流程用）：播完才返回。</summary>
        /// <param name="preSequence">
        /// 遮盖完成后的"预热段"（可选）：屏幕已被盖满，在这里做重活**不会**被玩家看到，
        /// 且因为是在动画开始**之前** await 掉，也不会撞到退场。
        /// ⛔ 不要用"另起一个 async void 并发跑"代替它 —— 那正是 2026-10-14
        ///    "转场第一次变卡"的根因（重活与退场拨开并行，主线程被占满）。
        /// </param>
        public static async UniTask PlayAsync(Kind kind, int act, System.Action onSwitch,
                                              string coverSprite = null, float total = 2.4f, string tag = "",
                                              System.Func<UniTask> preSequence = null)
        {
            var inst = Ensure();
            if (inst == null) { if (onSwitch != null) onSwitch(); return; }
            if (inst._busy) { if (onSwitch != null) onSwitch(); return; }
            bool done = false;
            inst.StartCoroutine(inst.Run(kind, Mathf.Clamp(act, 1, 5), onSwitch, Mathf.Max(1.2f, total), () => done = true, coverSprite, tag, preSequence));
            await UniTask.WaitUntil(() => done);
        }

        private IEnumerator Run(Kind kind, int act, System.Action onSwitch, float total,
                                System.Action onDone, string coverSprite, string tag,
                                System.Func<UniTask> preSequence)
        {
            string tg = string.IsNullOrEmpty(tag) ? "" : ("[" + tag + "]");
            _busy = true;
            _busySince = Time.unscaledTime;
            _blocker.raycastTarget = true;
            ClearExtra();          // 清掉上一场的临时元素
            ResetCover();          // ⛔ 清掉上一场的遮盖层状态（sprite/transform）
            _deadline = Time.realtimeSinceStartup + total * 3f + 3f;   // ⛔ 硬超时保护
            _exitStarted = false;
            Debug.Log("[UiTransition]" + tg + " 播放 " + kind + " act=" + act + " 总时长=" + total + "s");

            float tIn = total * 0.42f;
            float tHold = total * 0.10f;
            float tOut = total * 0.48f;

            // ---- 阶段 0：盖住旧界面（0.12s，越快越看不出"旧界面残留"）----
            //  ★ 优先用**目标界面的背景图**当遮盖 ， 全程一张底图、不跳变；
            //    拿不到图才退到按幕的纯色纸底。
            if (_cover != null)
            {
                var cs = coverSprite != null ? Resources.Load<Sprite>(coverSprite) : null;
                if (cs != null)
                {
                    _cover.sprite = cs;
                    _cover.type = Image.Type.Simple;
                    _cover.preserveAspect = false;
                    _cover.color = new Color(1f, 1f, 1f, 0f);
                }
                else
                {
                    // ⛔ 不传背景图时必须**清掉上次的 sprite**！
                    //   _cover 是常驻对象，上一场（主界面→节点地图）可能已把 sprite 设成
                    //   map_bg 图；不清理的话，这次走纯色分支仍会把那张图染浅显示出来
                    //   —— 用户 2026-10-12：'导致后面背景图残留到前面的了'。
                    _cover.sprite = null;
                    _cover.type = Image.Type.Simple;
                    _cover.preserveAspect = false;
                }
                Color cc = cs != null ? Color.white : SeasonCoverColor(act);
                float e0 = 0f;
                while (e0 < 0.12f)
                {
                    e0 += Time.deltaTime;
                    if (Time.realtimeSinceStartup > _deadline) break;
                    _cover.color = new Color(cc.r, cc.g, cc.b, Mathf.Clamp01(e0 / 0.12f));
                    yield return null;
                }
                _cover.color = new Color(cc.r, cc.g, cc.b, 1f);
                Debug.Log("[UiTransition]" + tg + " 遮盖层 " + (cs != null ? ("图 " + coverSprite) : "纯色纸底"));
            }

            // ---- ★ 首开"环境级"预热（2026-10-14）：遮盖已完成，屏幕完全被盖住 ----
            //   实测：首次进节点地图的两刀（切换帧 ~0.4s + 开场首帧 ~0.5s）不是面板内容预热问题，
            //   而是"完整游戏环境（主界面 3D + 本转场粒子墙都在渲染时）的首次组合渲染"——
            //   放在更早的转场里预热不会触发（已被多轮实测证伪）。
            //   这里趁本转场自己的遮盖期，先把冷面板真开一遍（开→渲染→关），代价全在看不见的时候。
            if (preSequence != null)
            {
                var pre = preSequence();
                yield return pre.ToCoroutine();
            }

            // ★ Kind.InkCover（2026-11-04）：墨从四边洇入。
            //   它自己负责「遮盖 → 切界面 → 预热 → 拨开」整条时间轴，所以**跳过**上面那段
            //   0.12s 纯色垫底与 preSequence —— 否则预热会在墨还没铺满时就跑（会从缝里露出来）。
            if (kind == Kind.InkCover)
            {
                yield return InkCoverSequence(act, onSwitch, tIn, tHold, tOut, preSequence);
                RebuildAfterSceneSwitch(act);
                FinishRun(tg, kind, onDone);
                yield break;
            }

            switch (kind)
            {
                case Kind.Ink: yield return InkSequence(act, onSwitch, tIn, tHold, tOut); break;
                case Kind.Part: yield return PartSequence(act, onSwitch, tIn, tHold, tOut); break;
                case Kind.Split: yield return SplitSequence(act, onSwitch, tIn, tHold, tOut); break;
                case Kind.Cloud: yield return CloudSequence(act, onSwitch, tIn, tHold, tOut); break;
                default: yield return SweepSequence(act, onSwitch, tIn, tHold, tOut); break;
            }

            // ---- onSwitch 里同步切了场景（SceneFlow.Load）→ 场景内子级已毁，重建后盖纸底 ----
            RebuildAfterSceneSwitch(act);

            // ---- 收尾：撤掉遮盖层（最后 0.3s 淡出，露出新界面）----
            if (_cover != null)
            {
                Color cc = _cover.color;   // 已是 white(有图) 或纸色(纯色)，直接用
                float e2 = 0f;
                while (e2 < 0.30f)
                {
                    e2 += Time.deltaTime;
                    if (Time.realtimeSinceStartup > _deadline + 2f) break;
                    _cover.color = new Color(cc.r, cc.g, cc.b, 1f - Mathf.Clamp01(e2 / 0.30f));
                    yield return null;
                }
                _cover.color = new Color(cc.r, cc.g, cc.b, 0f);
            }

            FinishRun(tg, kind, onDone);
        }

        /// <summary>easeInOutCubic：起手慢、中段快、收笔稳（线性推进最像 PPT）。</summary>
        private static float EaseInOutCubic(float x)
        {
            x = Mathf.Clamp01(x);
            return x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) * 0.5f;
        }

        /// <summary>
        /// 兜底：协程被外部打断（宿主失活 / 异常）后 `_busy` 会**永远停在 true**，
        /// 于是遮罩（尤其 InkCover 材质那层）留在屏幕上不动，后续转场也全被"重入"挡掉。
        /// 这里超过 STALL_SECONDS 判定卡死，强制收尾。
        /// </summary>
        private void Update()
        {
            if (!_busy) return;
            if (Time.unscaledTime - _busySince < STALL_SECONDS) return;

            Debug.LogWarning("[UiTransition] 转场卡死超过 " + STALL_SECONDS + "s，强制收尾");
            AbortRun();
        }

        /// <summary>宿主失活 ⇒ 协程被停，立刻回到干净状态。</summary>
        private void OnDisable() => AbortRun();

        /// <summary>强制收尾：清遮罩材质/alpha、放行点击、复位标志。</summary>
        private void AbortRun()
        {
            if (_cover != null)
            {
                if (_inkCoverMat != null && _cover.material == _inkCoverMat) _cover.material = null;
                _cover.color = new Color(1f, 1f, 1f, 0f);
                _cover.raycastTarget = false;
            }
            if (_blocker != null) _blocker.raycastTarget = false;
            ClearExtra();
            _busy = false;
            _exitStarted = false;
        }

        /// <summary>转场收尾（唯一出口）：清临时元素、放行点击、清标志。</summary>
        private void FinishRun(string tg, Kind kind, System.Action onDone)
        {
            ClearExtra();
            if (_blocker != null) _blocker.raycastTarget = false;
            _busy = false;
            _exitStarted = false;
            Debug.Log("[UiTransition]" + tg + " 转场结束 " + kind);
            if (onDone != null) onDone();
        }

        /// <summary>
        /// 「墨从四边洇入」序列（2026-11-04）。
        /// 用 `_cover` 挂 `WanXiang/UI/InkCover` 材质，推 `_Progress`：0（透明）→1（满屏墨）→0。
        /// <para>⛔ 结束时必须把 `_cover.material` 摘回 null：否则后续 Ink/Split 那些
    }
}