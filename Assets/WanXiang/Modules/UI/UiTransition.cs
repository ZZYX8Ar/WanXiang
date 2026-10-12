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
    public sealed class UiTransition : MonoBehaviour
    {
        public enum Kind
        {
            Ink,      // 水墨铺开（墨团从中心扩散 + 墨滴飞溅）
            Sweep,    // 粒子横扫（按幕的粒子从右往左刷满全屏）
            Split,    // 双帘拨开（两幅墨帘从两侧合拢，再从中缝向两侧拨开）
            Cloud,    // 云过（几朵云从右往左飘过）
        }

        private static UiTransition _inst;

        private Canvas _canvas;
        private RectTransform _root;
        private Image _blocker;              // 全屏拦截层（吃点击）
        private Image _cover;                // 前置遮盖层（进场先盖住旧界面）
        private readonly List<Image> _pool = new List<Image>();
        private bool _busy;
        private float _deadline;
        private bool _exitStarted;
        private readonly List<Streak> _streaks = new List<Streak>();   // 横扫转场的粒子尾迹
        private static Shader _streakShader;                            // WanXiang/UI/Streak（拖尾）

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
                case 1: return Kind.Sweep;   // ⚠ 2026-10-12 用户：第一幕不要水墨，和其他幕一样
                case 3: return Kind.Split;
                case 5: return Kind.Split;
                default: return Kind.Sweep;
            }
        }

        /// <summary>按幕自动选方式播放。</summary>
        public static void PlayAuto(int act, System.Action onSwitch, string coverSprite = null,
                                    float total = 2.4f, string tag = "")
        {
            Play(KindForAct(act), act, onSwitch, coverSprite, total, tag);
        }

        /// <summary>播放转场；到中点时回调 onSwitch（在那里打开目标面板）。</summary>
        /// <param name="coverSprite">
        /// 遮盖层用的背景图路径（Resources.Load）。**传目标界面的背景图**，
        /// 转场盖住时就是同一张底图、退场淡出露出的也是它 ， 全程一张底图，不跳变
        /// （用户 2026-10-12："因为转场完又有一个背景图，能不能把这个背景图贯穿"）。
        /// 传 null 则用按幕的纯色纸底兜底。
        /// </param>
        public static void Play(Kind kind, int act, System.Action onSwitch, string coverSprite = null,
                                float total = 2.4f, string tag = "")
        {
            var inst = Ensure();
            if (inst == null) { if (onSwitch != null) onSwitch(); return; }
            if (inst._busy) { if (onSwitch != null) onSwitch(); return; }   // 防重入
            inst.StartCoroutine(inst.Run(kind, Mathf.Clamp(act, 1, 5), onSwitch, Mathf.Max(1.2f, total), null, coverSprite, tag));
        }

        private static UiTransition Ensure()
        {
            if (_inst != null) return _inst;
            var go = new GameObject("UiTransitionLayer");
            DontDestroyOnLoad(go);
            _inst = go.AddComponent<UiTransition>();
            _inst.Build();
            return _inst;
        }

        private void Build()
        {
            var rt = gameObject.AddComponent<RectTransform>();

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

            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 32000;          // 盖过所有面板
            gameObject.AddComponent<GraphicRaycaster>();
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
        public static async UniTask PlayAsync(Kind kind, int act, System.Action onSwitch,
                                              string coverSprite = null, float total = 2.4f, string tag = "")
        {
            var inst = Ensure();
            if (inst == null) { if (onSwitch != null) onSwitch(); return; }
            if (inst._busy) { if (onSwitch != null) onSwitch(); return; }
            bool done = false;
            inst.StartCoroutine(inst.Run(kind, Mathf.Clamp(act, 1, 5), onSwitch, Mathf.Max(1.2f, total), () => done = true, coverSprite, tag));
            await UniTask.WaitUntil(() => done);
        }

        private IEnumerator Run(Kind kind, int act, System.Action onSwitch, float total,
                                System.Action onDone, string coverSprite, string tag)
        {
            string tg = string.IsNullOrEmpty(tag) ? "" : ("[" + tag + "]");
            _busy = true;
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

            switch (kind)
            {
                case Kind.Ink: yield return InkSequence(act, onSwitch, tIn, tHold, tOut); break;
                case Kind.Split: yield return SplitSequence(act, onSwitch, tIn, tHold, tOut); break;
                case Kind.Cloud: yield return CloudSequence(act, onSwitch, tIn, tHold, tOut); break;
                default: yield return SweepSequence(act, onSwitch, tIn, tHold, tOut); break;
            }

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

            ClearExtra();
            _blocker.raycastTarget = false;
            _busy = false;
            _exitStarted = false;
            Debug.Log("[UiTransition]" + tg + " 转场结束 " + kind);
            if (onDone != null) onDone();
        }

        // ------------------------------------------------------------------ 水墨铺开
        private IEnumerator InkSequence(int act, System.Action onSwitch, float tIn, float tHold, float tOut)
        {
            var spA = Resources.Load<Sprite>("UI/Trans/ui_ink_blot_a");
            var spB = Resources.Load<Sprite>("UI/Trans/ui_ink_blot_b");
            var spD = Resources.Load<Sprite>("UI/Trans/ui_ink_drop");
            if (spA == null) { if (onSwitch != null) onSwitch(); yield break; }

            // 按幕微调墨色（春青 / 夏浓 / 秋赭 / 冬冷 / 长夏焦）
            Color ink = InkColorOf(act);

            // 主墨团（中心 → 覆盖全屏）
            var main = NewImage("InkMain");
            main.sprite = spA;
            main.color = ink;
            var mrt = main.rectTransform;
            mrt.anchoredPosition = Vector2.zero;

            // 几滴飞溅墨滴（从中心向外）
            int drops = 5;
            var drips = new List<Image>();
            var dirs = new List<Vector2>();
            for (int i = 0; i < drops; i++)
            {
                var d = NewImage("InkDrop" + i);
                d.sprite = spD;
                d.color = ink;
                d.rectTransform.sizeDelta = new Vector2(180f, 180f);
                d.rectTransform.anchoredPosition = UnityEngine.Random.insideUnitCircle * 20f;
                drips.Add(d);
                float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                dirs.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
            }

            // ★ 季节粒子随墨团一起向外飞（用户："最好和这个季节相关一点"）：
            //   春·花瓣 / 夏·萤火 / 秋·落叶 / 冬·飞雪 / 长夏·金尘。
            var psp = Resources.Load<Sprite>(FallByAct[act - 1]);
            var petals = new List<Image>();
            var pdirs = new List<Vector2>();
            if (psp != null)
            {
                int pn = 16;
                for (int i = 0; i < pn; i++)
                {
                    var p = NewImage("InkPet" + i);
                    p.sprite = psp;
                    float ps = UnityEngine.Random.Range(40f, 110f);
                    p.rectTransform.sizeDelta = new Vector2(ps, ps);
                    p.rectTransform.anchoredPosition = Vector2.zero;
                    p.color = new Color(1f, 1f, 1f, 0f);
                    petals.Add(p);
                    float a = (Mathf.PI * 2f / pn) * i + UnityEngine.Random.Range(-0.25f, 0.25f);
                    pdirs.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
                }
            }

            float cover = Mathf.Sqrt(CW * CW + CH * CH) * 1.25f;   // 覆盖对角的最大边长
            float e = 0f;
            while (e < tIn)
            {
                e += Time.deltaTime;
                if (Time.realtimeSinceStartup > _deadline) break;   // 超时保护
                float k = Mathf.Clamp01(e / tIn);
                float ke = 1f - Mathf.Pow(1f - k, 2.4f);            // ease-out
                float s = Mathf.Lerp(0.05f, cover, ke);
                mrt.sizeDelta = new Vector2(s, s);
                for (int i = 0; i < drips.Count; i++)
                {
                    var rt = drips[i].rectTransform;
                    rt.anchoredPosition = dirs[i] * (cover * 0.42f * ke);
                    float ds = Mathf.Lerp(40f, 300f, ke);
                    rt.sizeDelta = new Vector2(ds, ds);
                    var c = drips[i].color; drips[i].color = new Color(c.r, c.g, c.b, 1f - ke * 0.15f);
                }
                // 季节粒子：从中心向外飞散并渐显
                for (int i = 0; i < petals.Count; i++)
                {
                    petals[i].rectTransform.anchoredPosition = pdirs[i] * (cover * 0.46f * ke);
                    petals[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, ke * 220f * (i % 2 == 0 ? 1f : -1f));
                    petals[i].color = new Color(1f, 1f, 1f, Mathf.Clamp01(ke * 1.4f));
                }
                yield return null;
            }

            if (onSwitch != null) onSwitch();
            yield return new WaitForSeconds(tHold);

            // ★ 退场起点：通知外部可以开始后续演出（与拨开重叠，观感连贯）
            _exitStarted = true;
            // 退场：继续放大并淡出
            e = 0f;
            while (e < tOut)
            {
                e += Time.deltaTime;
                if (Time.realtimeSinceStartup > _deadline) break;   // 超时保护
                float k = Mathf.Clamp01(e / tOut);
                float s = Mathf.Lerp(cover, cover * 1.7f, k);
                mrt.sizeDelta = new Vector2(s, s);
                float a = 1f - k;
                main.color = new Color(ink.r, ink.g, ink.b, a);
                for (int i = 0; i < drips.Count; i++)
                {
                    var c = drips[i].color; drips[i].color = new Color(c.r, c.g, c.b, a * 0.85f);
                    var rt = drips[i].rectTransform;
                    rt.anchoredPosition = dirs[i] * (cover * (0.42f + 0.5f * k));
                }
                for (int i = 0; i < petals.Count; i++)
                {
                    petals[i].rectTransform.anchoredPosition = pdirs[i] * (cover * (0.46f + 0.6f * k));
                    petals[i].color = new Color(1f, 1f, 1f, a * 0.9f);
                }
                yield return null;
            }
        }

        /// <summary>
        /// 完整重置遮盖层：sprite / 颜色 / transform 全部归位。
        /// ⚠ 转场层是**常驻**对象，两条链路（开始→主界面、主界面→节点地图）共用它。
        ///   用户 2026-10-12 两次强调："从开始界面到主界面的转场是独立的，不要混用"。
        ///   ， 每次播放前必须把上一场残留（尤其是 sprite 和 transform）清干净，
        ///     否则主界面→节点地图设的背景图会"残留到"开始→主界面那场。
        /// </summary>
        private void ResetCover()
        {
            if (_cover == null) return;
            _cover.sprite = null;
            _cover.type = Image.Type.Simple;
            _cover.preserveAspect = false;
            _cover.color = new Color(1f, 1f, 1f, 0f);
            var rt = _cover.rectTransform;
            if (rt != null)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;
                rt.anchoredPosition = Vector2.zero;
            }
        }

        /// <summary>遮盖层的"纸/墨"底色（按幕），盖住旧界面时不像纯黑那么硬。</summary>
        private static Color SeasonCoverColor(int act)
        {
            switch (act)
            {
                case 1: return new Color(0.905f, 0.915f, 0.898f, 1f);  // 春·青纸
                case 2: return new Color(0.855f, 0.862f, 0.845f, 1f);  // 夏·灰纸
                case 3: return new Color(0.925f, 0.900f, 0.858f, 1f);  // 秋·暖纸
                case 4: return new Color(0.885f, 0.900f, 0.920f, 1f);  // 冬·冷纸
                default: return new Color(0.915f, 0.888f, 0.808f, 1f); // 长夏·麦纸
            }
        }

        private static Color InkColorOf(int act)
        {
            switch (act)
            {
                case 1: return new Color(0.16f, 0.20f, 0.18f, 1f);   // 春·青墨
                case 2: return new Color(0.10f, 0.12f, 0.12f, 1f);   // 夏·浓墨
                case 3: return new Color(0.22f, 0.16f, 0.10f, 1f);   // 秋·赭墨
                case 4: return new Color(0.14f, 0.17f, 0.21f, 1f);   // 冬·冷墨
                default: return new Color(0.20f, 0.15f, 0.09f, 1f);  // 长夏·焦墨
            }
        }

        // ------------------------------------------------------------------ 粒子横扫（右 → 左）
        private IEnumerator SweepSequence(int act, System.Action onSwitch, float tIn, float tHold, float tOut)
        {
            var psp = Resources.Load<Sprite>(FallByAct[act - 1]);
            var wash = Resources.Load<Sprite>("UI/Trans/ui_paper_wipe");
            if (psp == null && wash == null) { if (onSwitch != null) onSwitch(); yield break; }

            // 纸色底帘（横向铺满，从左往右排，整体右→左平移）
            var washImgs = new List<Image>();
            if (wash != null)
            {
                float tileW = CW;
                for (int i = 0; i < 2; i++)
                {
                    var w = NewImage("Wash" + i);
                    w.sprite = wash;
                    w.color = new Color(0.93f, 0.91f, 0.86f, 0f);
                    w.rectTransform.sizeDelta = new Vector2(tileW, CH + 10f);
                    w.rectTransform.anchoredPosition = new Vector2(CW + i * tileW, 0f);
                    washImgs.Add(w);
                }
            }

            // 粒子墙：几百个按幕的粒子上半屏密度由右向左刷过
            int n = 130;
            var parts = new List<Image>();
            var xs = new List<float>();
            var ys = new List<float>();
            var szs = new List<float>();
            var spds = new List<float>();
            for (int i = 0; i < n; i++)
            {
                var p = NewImage("Sw" + i);
                p.sprite = psp;
                float sz = UnityEngine.Random.Range(46f, 150f);
                p.rectTransform.sizeDelta = new Vector2(sz, sz);
                // 起始位置：右侧一列（超出屏幕），纵向铺满
                float x = CW * 0.5f + UnityEngine.Random.Range(0f, CW * 0.9f);
                float y = UnityEngine.Random.Range(-CH * 0.54f, CH * 0.54f);
                p.rectTransform.anchoredPosition = new Vector2(x, y);
                p.color = new Color(1f, 1f, 1f, 0.95f);
                parts.Add(p);
                xs.Add(x); ys.Add(y); szs.Add(sz);
                spds.Add(UnityEngine.Random.Range(1.15f, 1.75f));
            }

            // ★ 给每个粒子配一条尾迹（用户："横扫转场能不能让这些粒子带一些尾迹"）。
            //   尾迹是横向拉长的图 + UIStreak shader（头部实、尾部渐隐），
            //   位置钉在粒子**后方**（粒子向左运动 ， 尾迹拖在右侧）。
            var streakSp = Resources.Load<Sprite>("UI/Trans/trans_streak");
            for (int i = 0; i < parts.Count; i++)
            {
                if (streakSp == null || StreakShader() == null) break;
                float sz = szs[i];
                // v1 用 sz*2.6~4.4 长 / sz*0.55 粗 ， 在 1280 画布上能拖出 600px 白棍。
                // v2 收紧到 sz*1.5~2.6 长 / sz*0.20 粗，并按幕染色。
                var st = NewStreak("St" + i, sz * Random.Range(1.5f, 2.6f), Mathf.Max(4f, sz * 0.20f),
                                   StreakColorOf(act));
                st.Img.sprite = streakSp;
                st.T.SetSiblingIndex(100 + i);          // 尾迹在粒子之下
                _streaks.Add(st);
            }

            float travel = CW * 2.1f;      // 需要移动的总距离（从右外到左外）
            float e = 0f;
            while (e < tIn)
            {
                e += Time.deltaTime;
                if (Time.realtimeSinceStartup > _deadline) break;   // 超时保护
                float k = Mathf.Clamp01(e / tIn);
                float ke = k * k;                                   // ease-in（越刷越快）
                for (int i = 0; i < parts.Count; i++)
                {
                    float d = travel * ke * spds[i];
                    parts[i].rectTransform.anchoredPosition = new Vector2(xs[i] - d, ys[i]);
                    // 尾迹跟着粒子，并随速度拉长
                    if (i < _streaks.Count && _streaks[i].T != null)
                    {
                        float grow = 1f + ke * 0.7f;
                        _streaks[i].T.anchoredPosition = new Vector2(xs[i] - d + _streaks[i].Len * 0.5f * grow, ys[i]);
                        _streaks[i].T.sizeDelta = new Vector2(_streaks[i].Len * grow, _streaks[i].T.sizeDelta.y);
                    }
                }
                for (int i = 0; i < washImgs.Count; i++)
                {
                    float d = travel * ke;
                    washImgs[i].rectTransform.anchoredPosition = new Vector2(CW + i * CW - d, 0f);
                    washImgs[i].color = new Color(0.93f, 0.91f, 0.86f, Mathf.Clamp01(k * 1.25f));
                }
                yield return null;
            }

            if (onSwitch != null) onSwitch();
            yield return new WaitForSeconds(tHold);

            // ★ 退场起点：通知外部可以开始后续演出（与拨开重叠，观感连贯）
            _exitStarted = true;
            // 退场：继续左移并淡出
            e = 0f;
            while (e < tOut)
            {
                e += Time.deltaTime;
                if (Time.realtimeSinceStartup > _deadline) break;   // 超时保护
                float k = Mathf.Clamp01(e / tOut);
                float d = travel * (1f + k * 0.9f);
                float a = 1f - k;
                for (int i = 0; i < parts.Count; i++)
                {
                    parts[i].rectTransform.anchoredPosition = new Vector2(xs[i] - d * spds[i], ys[i]);
                    var c = parts[i].color; parts[i].color = new Color(c.r, c.g, c.b, a);
                    if (i < _streaks.Count && _streaks[i].T != null)
                    {
                        _streaks[i].T.anchoredPosition = new Vector2(xs[i] - d * spds[i] + _streaks[i].Len * 0.5f, ys[i]);
                        var mc = _streaks[i].Img.material;
                        if (mc != null) mc.SetFloat("_StreakAlpha", 0.85f * a);
                    }
                }
                for (int i = 0; i < washImgs.Count; i++)
                {
                    washImgs[i].rectTransform.anchoredPosition = new Vector2(CW + i * CW - d, 0f);
                    washImgs[i].color = new Color(0.93f, 0.91f, 0.86f, a);
                }
                yield return null;
            }
        }

        // ------------------------------------------------------------------ 双帘拨开
        /// <summary>
        /// 两幅墨帘：进场从屏幕两侧向中间合拢（关门遮住）→ 切换 → 退场从中缝向两侧拨开。
        /// 用户："从中间拨开也行"。
        /// </summary>
        private IEnumerator SplitSequence(int act, System.Action onSwitch, float tIn, float tHold, float tOut)
        {
            var sp = Resources.Load<Sprite>("UI/Trans/ui_ink_curtain");
            if (sp == null) { if (onSwitch != null) onSwitch(); yield break; }

            Color ink = InkColorOf(act);
            float halfW = CW * 0.5f;
            float target = CW * 0.25f;     // 合拢后每帘中心离屏幕中心的距离

            // ★ 季节粒子（用户："最好和这个季节相关"）：随两帘从两侧涌入
            var psp = Resources.Load<Sprite>(FallByAct[act - 1]);
            var petals = new List<Image>();
            var pxs = new List<float>();
            var pys = new List<float>();
            if (psp != null)
            {
                for (int i = 0; i < 26; i++)
                {
                    var p = NewImage("SpPet" + i);
                    p.sprite = psp;
                    float ps = UnityEngine.Random.Range(46f, 130f);
                    p.rectTransform.sizeDelta = new Vector2(ps, ps);
                    bool fromLeft = (i % 2 == 0);
                    float px = fromLeft ? -CW * UnityEngine.Random.Range(0.7f, 1.3f)
                                        : CW * UnityEngine.Random.Range(0.7f, 1.3f);
                    float py = UnityEngine.Random.Range(-CH * 0.52f, CH * 0.52f);
                    p.rectTransform.anchoredPosition = new Vector2(px, py);
                    p.color = new Color(1f, 1f, 1f, 0f);
                    petals.Add(p); pxs.Add(px); pys.Add(py);
                }
            }

            var left = NewImage("CurtainL");
            left.sprite = sp;
            left.color = ink;
            left.rectTransform.sizeDelta = new Vector2(halfW * 1.18f, CH + 8f);
            left.rectTransform.anchoredPosition = new Vector2(-CW * 1.2f, 0f);

            var right = NewImage("CurtainR");
            right.sprite = sp;
            right.color = ink;
            right.rectTransform.sizeDelta = new Vector2(halfW * 1.18f, CH + 8f);
            right.rectTransform.localScale = new Vector3(-1f, 1f, 1f);   // 镜像
            right.rectTransform.anchoredPosition = new Vector2(CW * 1.2f, 0f);

            float e = 0f;
            while (e < tIn)
            {
                e += Time.deltaTime;
                if (Time.realtimeSinceStartup > _deadline) break;   // 超时保护
                float k = Mathf.Clamp01(e / tIn);
                float ke = 1f - Mathf.Pow(1f - k, 3f);                     // ease-out（合拢减速）
                left.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-CW * 1.2f, -target, ke), 0f);
                right.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(CW * 1.2f, target, ke), 0f);
                for (int i = 0; i < petals.Count; i++)
                {
                    petals[i].rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(pxs[i], pxs[i] * 0.18f, ke), pys[i]);
                    petals[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, ke * 190f * (i % 2 == 0 ? 1f : -1f));
                    petals[i].color = new Color(1f, 1f, 1f, Mathf.Clamp01(ke * 1.5f));
                }
                yield return null;
            }

            if (onSwitch != null) onSwitch();
            yield return new WaitForSeconds(tHold);

            // ★ 退场起点：通知外部可以开始后续演出（与拨开重叠，观感连贯）
            _exitStarted = true;
            // 退场：从中缝向两侧拨开 + 淡出
            e = 0f;
            while (e < tOut)
            {
                e += Time.deltaTime;
                if (Time.realtimeSinceStartup > _deadline) break;   // 超时保护
                float k = Mathf.Clamp01(e / tOut);
                float ke = k * k;                                         // ease-in（拨开加速）
                float a = 1f - k;
                left.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-target, -CW * 1.35f, ke), 0f);
                right.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(target, CW * 1.35f, ke), 0f);
                left.color = new Color(ink.r, ink.g, ink.b, a);
                right.color = new Color(ink.r, ink.g, ink.b, a);
                for (int i = 0; i < petals.Count; i++)
                {
                    petals[i].rectTransform.anchoredPosition = new Vector2(pxs[i] * 0.18f * (1f - ke), pys[i]);
                    petals[i].color = new Color(1f, 1f, 1f, a * 0.9f);
                }
                yield return null;
            }
        }

        // ------------------------------------------------------------------ 云过
        private IEnumerator CloudSequence(int act, System.Action onSwitch, float tIn, float tHold, float tOut)
        {
            string[] kinds = { "UI/Home/home_cloud_a", "UI/Home/home_cloud_b", "UI/Home/home_cloud_c" };
            var clouds = new List<Image>();
            var xs = new List<float>();
            var ys = new List<float>();
            var spds = new List<float>();
            for (int i = 0; i < 16; i++)
            {
                var sp = Resources.Load<Sprite>(kinds[i % 3]);
                if (sp == null) continue;
                var c = NewImage("Cl" + i);
                c.sprite = sp;
                float sc = UnityEngine.Random.Range(1.1f, 1.9f);
                c.rectTransform.sizeDelta = new Vector2(sp.rect.width * sc, sp.rect.height * sc);
                float x = CW * 0.5f + UnityEngine.Random.Range(0f, CW * 1.1f);
                float y = UnityEngine.Random.Range(-CH * 0.5f, CH * 0.5f);
                c.rectTransform.anchoredPosition = new Vector2(x, y);
                c.color = new Color(1f, 1f, 1f, 0f);
                clouds.Add(c); xs.Add(x); ys.Add(y);
                spds.Add(UnityEngine.Random.Range(1.0f, 1.5f));
            }
            if (clouds.Count == 0) { if (onSwitch != null) onSwitch(); yield break; }

            float travel = CW * 2.2f;
            float e = 0f;
            while (e < tIn)
            {
                e += Time.deltaTime;
                if (Time.realtimeSinceStartup > _deadline) break;   // 超时保护
                float k = Mathf.Clamp01(e / tIn);
                for (int i = 0; i < clouds.Count; i++)
                {
                    clouds[i].rectTransform.anchoredPosition = new Vector2(xs[i] - travel * k * spds[i], ys[i]);
                    clouds[i].color = new Color(1f, 1f, 1f, Mathf.Clamp01(k * 1.3f));
                }
                yield return null;
            }
            if (onSwitch != null) onSwitch();
            yield return new WaitForSeconds(tHold);
            _exitStarted = true;   // ★ 退场起点（云过）
            e = 0f;
            while (e < tOut)
            {
                e += Time.deltaTime;
                if (Time.realtimeSinceStartup > _deadline) break;   // 超时保护
                float k = Mathf.Clamp01(e / tOut);
                for (int i = 0; i < clouds.Count; i++)
                {
                    clouds[i].rectTransform.anchoredPosition = new Vector2(xs[i] - travel * (1f + k * 0.8f) * spds[i], ys[i]);
                    clouds[i].color = new Color(1f, 1f, 1f, 1f - k);
                }
                yield return null;
            }
        }
    }
}