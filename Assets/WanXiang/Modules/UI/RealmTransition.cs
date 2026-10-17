// ============================================================================
//  RealmTransition —— 秘境转场（粒子拨开 + 艺术字揭示）（2026-10-16）
//  ---------------------------------------------------------------------------
//  用户需求（原话）：
//    · "我说了进入这个秘境和离开都是有这个转场的…这个测试的按钮和正常进入是一样的啊"
//    · "你可以重新设计这个转场，我建议使用粒子拨开，在这个粒子拨开的背景上加一些字"
//    · "这个字你可以参考 D:\Unity_Project\Demo\InkAbyssWalker-Integration…
//       「墨渊行者」，是使用了 shader 的…它这个字好像是生成好的图片，不是真正的字体，
//       你也可以使用这个模式，这样这些艺术字好看一点"
//
//  ⇒ 演出分四拍：
//    ① 闭拢：两侧粒子**向中间合拢**成一堵粒子墙，同时纸色底压上来（盖住旧画面）
//    ② 换场：屏幕被盖满时调 onSwitch（真开/真关秘境面板，玩家看不到切换瞬间）
//    ③ 字现：艺术字图用 `WanXiang/UI/RealmTitleReveal` 的**噪声径向揭示**洇出来，
//            边缘带一道金涟漪（这是参考工程 LogoAppear 的精髓）
//    ④ 拨开：粒子墙**从中缝向两侧退开** + 字淡出，露出秘境地图
//
//  ⛔ 为什么这个类是"代码自建 UI"（与项目"UI 一律做 prefab"的铁律并存）：
//    它是**转场层**，与 <see cref="UiTransition"/> 同类 —— 挂在 UIRoot 之下的独立子 Canvas、
//    常驻、不参与面板栈、不吃业务数据。项目里已有这个先例（UiTransition.Build 全程代码建），
//    所以这里沿用同一套做法；**业务面板仍然一律走 prefab**。
//
//  ⛔ 不用 ParticleSystem（项目铁律：2D 管线下材质缺失 = 洋红）。全部用 Image 手推粒子。
//  ⛔ 独立 Canvas：sortingOrder = 32000，与 UiTransition 同档（转场永远在最上层）。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.Modules.UI
{
    /// <summary>秘境进/出的粒子拨开转场。用法：<c>await RealmTransition.PlayAsync(...)</c>。</summary>
    public sealed class RealmTransition : MonoBehaviour
    {
        /// <summary>正在播（重入保护：连点两次进秘境不该叠两场演出）。</summary>
        public static bool IsPlaying => _inst != null && _inst._busy;

        private static RealmTransition _inst;

        // ---- 时轴（秒）----
        private const float T_CLOSE = 0.55f;   // 粒子合拢
        private const float T_TITLE = 0.95f;   // 字洇开
        private const float T_HOLD = 0.45f;    // 字停留
        private const float T_OPEN = 0.65f;    // 粒子拨开

        /// <summary>
        /// 纸底最大不透明度。
        /// <para>⚠⚠ **在 Linear 色彩空间下，α 要远比直觉更接近 1。** 混合发生在**线性**空间：
        ///   结果 = (1−α)·底 再转 sRGB 显示，而 sRGB 的暗部被大幅提亮 ⇒
        ///   α=0.90 实测只压到 **35% 亮度**（节点文字、天时说明全都还读得清），
        ///   α=0.985 才压到 ~10%，屏幕才真的"黑下去"。</para>
        ///   <para>⛔ 而且**必须是 1.0**：哪怕留 1.5% 的底，浅色的纸面面板（信息卡、节点卡）
        ///   也会在近黑背景上"透"成可读的灰字（实测"第10节·秋分"仍能认出）。
        ///   线的层次交给**粒子帘子**去做，不靠给底留余量。</para>
        /// </summary>
        private const float COVER_A = 1.0f;

        /// <summary>
        /// 纸底颜色。⛔⛔ **必须用纯黑，不要写"看起来很深"的 RGB**。
        /// <para>本工程是 **Linear** 色彩空间（QualitySettings.activeColorSpace = Linear）——
        ///   `Image.color` 的数值被当作**线性值**，显示时会转成 sRGB。
        ///   0.06 线性 ≈ 0.28 sRGB ⇒ 实际画出来是**中灰罩**，不是黑幕。</para>
        /// <para>实测代价：原本写 (0.06,0.06,0.07,α) 想当"近黑"，结果 0.90 不透明度下
        ///   底下的节点图**依然清晰可读**（"遮不住"），排查半天才发现是色彩空间换算。
        ///   纯黑 (0,0,0) 在两种色彩空间下都是 0，所以只有它安全。</para>
        /// </summary>
        private static readonly Color CoverColor = Color.black;

        /// <summary>
        /// 调试慢放（1 = 正常）。整条时轴乘这个系数。
        /// <para>为什么要有：整场演出 2.6 秒，而远程调试工具一次往返就超过它 ——
        ///   **根本没机会看到中间帧**。设成 0.06 就变成 43 秒，可以逐个阶段截图核对
        ///   （粒子墙是否铺满、字的位置、涟漪强度、拨开方向）。</para>
        /// <para>⛔ 只用于调试，正式值必须回到 1；不要把它写进存档或从配置读。</para>
        /// </summary>
        public static float DebugSlowMul = 1f;

        /// <summary>把设计时轴换算成实际等待时长。</summary>
        private static float D(float seconds) => seconds / Mathf.Max(0.01f, DebugSlowMul);

        private Canvas _canvas;
        private RectTransform _root;
        private Image _cover;              // 纸色底（吃点击 + 盖旧画面）
        private Image _title;              // 艺术字（RealmTitleReveal 材质）
        private Material _titleMat;
        private RectTransform _particleRoot;

        private readonly List<RectTransform> _parts = new List<RectTransform>(48);
        private readonly List<float> _partX = new List<float>(48);
        private readonly List<float> _partY = new List<float>(48);
        private readonly List<float> _partRot = new List<float>(48);
        private readonly List<float> _partDelay = new List<float>(48);

        private bool _busy;
        private bool _built;

        // =====================================================================
        //  对外入口
        // =====================================================================

        /// <summary>
        /// 播一次秘境转场。
        /// <param name="realmId">场域 id —— 决定用哪张背景纸色、哪种粒子、哪种整体色调。</param>
        /// <param name="onSwitch">屏幕被盖满时执行（真开/真关面板）。</param>
        /// <param name="titleText">艺术字标题（null = 用通用的「万相秘境」图）。</param>
        /// </summary>
        public static async UniTask PlayAsync(string realmId, Action onSwitch, string titleText = null)
        {
            var inst = Ensure();
            if (inst == null) { onSwitch?.Invoke(); return; }
            if (inst._busy) { onSwitch?.Invoke(); return; }   // 重入：直接换，不叠演出

            bool done = false;
            inst.StartCoroutine(inst.Run(realmId, onSwitch, () => done = true, titleText));
            await UniTask.WaitUntil(() => done);
        }

        private static RealmTransition Ensure()
        {
            if (_inst != null) return _inst;
            // ⛔ 与 UiTransition 同款：必须挂到 UIRoot 主 Canvas 之下。
            //   根级 RectTransform 的 anchor 不生效、sizeDelta 才是实际尺寸，
            //   直接挂根会导致 Canvas 渲染区几乎为 0（UiTransition 2026-10-14 踩过）。
            var host = FindMainCanvas();
            var go = new GameObject("RealmTransition");
            var rt = go.AddComponent<RectTransform>();
            if (host != null)
            {
                rt.SetParent(host.transform, false);
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                rt.localScale = Vector3.one; rt.localPosition = Vector3.zero;
            }
            else
            {
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                rt.sizeDelta = new Vector2(1920f, 1080f);
            }

            _inst = go.AddComponent<RealmTransition>();
            _inst._built = false;
            return _inst;
        }

        private static Canvas FindMainCanvas()
        {
            // ⚠ 与 UiTransition 同一套口径：扫场景里的**根 Canvas** 取 sortingOrder 最小的那个
            //   （UIRoot 的 sortingOrder 最小，HUD/弹层都是它的子 Canvas）。
            //   ⛔ 不要用 UIBootstrap.UI.GetComponentInParent —— UISystem 不是 Component，编译不过。
            Canvas best = null;
            foreach (var cv in UnityEngine.Object.FindObjectsOfType<Canvas>())
            {
                if (!cv.isRootCanvas) continue;
                if (best == null || cv.sortingOrder < best.sortingOrder) best = cv;
            }
            return best;
        }

        // =====================================================================
        //  构建（代码自建，见文件头说明）
        // =====================================================================

        private void Build()
        {
            if (_built) return;
            _built = true;

            _canvas = gameObject.GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 32000;
            if (gameObject.GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
            _root = (RectTransform)transform;

            // ---- 纸色底（最底，盖旧画面 + 吃点击）----
            _cover = NewImage("Cover", 0);
            FullStretch(_cover.rectTransform);
            _cover.color = new Color(CoverColor.r, CoverColor.g, CoverColor.b, 0f);
            _cover.raycastTarget = true;

            // ---- 粒子层 ----
            var pr = new GameObject("Particles", typeof(RectTransform));
            _particleRoot = (RectTransform)pr.transform;
            _particleRoot.SetParent(_root, false);
            FullStretch(_particleRoot);

            // ---- 艺术字（最上）----
            _title = NewImage("Title", 2);
            var tr = _title.rectTransform;
            tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f);
            tr.pivot = new Vector2(0.5f, 0.5f);
            tr.sizeDelta = new Vector2(1360f, 655f);   // 字图 1524×734 ≈ 2.08:1；占屏宽约 70%
            tr.anchoredPosition = new Vector2(0f, 20f);
            _title.raycastTarget = false;
            _title.enabled = false;

            gameObject.SetActive(true);
            Debug.Log("[RealmTransition] 转场层已建 host=" + (_root.parent != null ? _root.parent.name : "(独立)"));
        }

        private Image NewImage(string name, int sibling)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_root, false);
            go.transform.SetSiblingIndex(Mathf.Min(sibling, _root.childCount - 1));
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            return img;
        }

        private static void FullStretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        // =====================================================================
        //  演出
        // =====================================================================

        private IEnumerator Run(string realmId, Action onSwitch, Action onDone, string titleText)
        {
            _busy = true;
            Build();
            _cover.raycastTarget = true;      // 演出期间吃掉所有点击

            float W = _root.rect.width, H = _root.rect.height;
            if (W <= 0f) W = 1920f;
            if (H <= 0f) H = 1080f;

            // ★ 实际时长（受 DebugSlowMul 影响）。⚠ 循环的**边界与除数必须是同一个值**，
            //   否则 k 走不到 1 —— 慢放时会变成"演到一半就切下一拍"。
            float dClose = D(T_CLOSE), dTitle = D(T_TITLE), dHold = D(T_HOLD), dOpen = D(T_OPEN);

            BuildParticles(realmId, W, H);
            PrepareTitle(titleText);

            // ---- ① 闭拢 ----
            _cover.color = new Color(CoverColor.r, CoverColor.g, CoverColor.b, 0f);
            SetParticlePhase(0f);
            for (float t = 0f; t < dClose; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.Clamp01(t / dClose);
                SetParticlePhase(EaseOut(k));
                _cover.color = new Color(CoverColor.r, CoverColor.g, CoverColor.b, Mathf.Lerp(0f, COVER_A, k));
                yield return null;
            }
            SetParticlePhase(1f);
            _cover.color = new Color(CoverColor.r, CoverColor.g, CoverColor.b, COVER_A);

            // ---- ② 换场（屏幕已盖满）----
            try { onSwitch?.Invoke(); }
            catch (Exception ex) { Debug.LogError("[RealmTransition] onSwitch 抛异常：" + ex); }
            yield return null;   // 让面板切换把这一帧走完

            // ---- ③ 字现（噪声径向揭示）----
            _title.enabled = true;
            if (_titleMat != null)
                _titleMat.SetFloat(ProgressId, 0f);
            Color tc = _title.color; tc.a = 0f;
            _title.color = tc;
            for (float t = 0f; t < dTitle; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.Clamp01(t / dTitle);
                if (_titleMat != null) _titleMat.SetFloat(ProgressId, k);
                tc = _title.color; tc.a = Mathf.Clamp01(k * 3f);   // 字图整体淡入更快
                _title.color = tc;
                yield return null;
            }
            if (_titleMat != null) _titleMat.SetFloat(ProgressId, 1f);

            // ---- 停留 ----
            for (float t = 0f; t < dHold; t += Time.unscaledDeltaTime) yield return null;

            // ---- ④ 拨开 ----
            for (float t = 0f; t < dOpen; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.Clamp01(t / dOpen);
                float e = EaseIn(k);
                SetParticleOpen(e);
                tc = _title.color; tc.a = Mathf.Clamp01(1f - k * 1.6f);
                _title.color = tc;
                // 纸底同步退掉，露出下面的秘境地图
                _cover.color = new Color(CoverColor.r, CoverColor.g, CoverColor.b, Mathf.Lerp(COVER_A, 0f, Mathf.Clamp01(k * 1.3f)));
                yield return null;
            }

            // ---- 收尾：务必把状态清干净（常驻对象，下一次演出不能带着上次的残留）----
            _cover.color = new Color(CoverColor.r, CoverColor.g, CoverColor.b, 0f);
            _cover.raycastTarget = false;      // ⛔ 必须放行点击，否则整个游戏点不动
            _title.enabled = false;
            _title.color = new Color(1f, 1f, 1f, 0f);
            ClearParticles();

            _busy = false;
            onDone?.Invoke();
        }

        // =====================================================================
        //  粒子（Image 手推，禁用 ParticleSystem）
        // =====================================================================

        private Sprite _partSprite;

        private void BuildParticles(string realmId, float W, float H)
        {
            ClearParticles();
            _partSprite = Resources.Load<Sprite>("UI/Realm/FX_" + FxNameOf(realmId));
            if (_partSprite == null) _partSprite = Resources.Load<Sprite>("UI/Realm/FX_realm_marsh");

            // ★ 数量/尺寸：要让这些粒子**真的能当帘子**。
            //   初版 46 个 × 90~190px 在 1920×1080 上是"点点繁星"，
            //   遮不住任何东西 ⇒ 看起来不像"拨开"，像"飘过"。
            //   改成 64 个 × 130~280px，重叠起来才有面。
            int n = 64;
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("P" + i, typeof(RectTransform));
                go.transform.SetParent(_particleRoot, false);
                var rt = (RectTransform)go.transform;
                var img = go.AddComponent<Image>();
                if (_partSprite != null) img.sprite = _partSprite;
                img.raycastTarget = false;
                img.preserveAspect = true;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);

                // 粒子墙：**等分列 + 抖动**铺满全屏（纯随机会有运气成分，某处稀得透光）
                int cols = 10, rows = 7;
                int cx = i % cols, cy = (i / cols) % rows;
                float size = UnityEngine.Random.Range(130f, 280f);
                rt.sizeDelta = new Vector2(size, size);

                // ⚠ 抖动必须给够，否则"等分列"会显出整齐的网格（实测能一眼看出行列）。
                //   取 ±0.42 个格宽 —— 足以破掉网格感，又不至于把某处抖出大空洞。
                float cellW = W / cols, cellH = H / rows;
                float x = (cx + 0.5f) * cellW - W * 0.5f + UnityEngine.Random.Range(-cellW * 0.42f, cellW * 0.42f);
                float y = (cy + 0.5f) * cellH - H * 0.5f + UnityEngine.Random.Range(-cellH * 0.42f, cellH * 0.42f);
                _partX.Add(x); _partY.Add(y);
                _partRot.Add(UnityEngine.Random.Range(0f, 360f));
                _partDelay.Add(UnityEngine.Random.Range(0f, 0.35f));
                rt.anchoredPosition = new Vector2(x, y);
                rt.localRotation = Quaternion.Euler(0f, 0f, _partRot[i]);

                var c = TintFor(realmId);
                c.a = 0f;
                img.color = c;
                _parts.Add(rt);
            }
        }

        private void ClearParticles()
        {
            for (int i = 0; i < _parts.Count; i++)
                if (_parts[i] != null) Destroy(_parts[i].gameObject);
            _parts.Clear(); _partX.Clear(); _partY.Clear(); _partRot.Clear(); _partDelay.Clear();
        }

        /// <summary>phase 0→1：从"屏幕外一圈"飞到"墙上的位置"（合拢）。</summary>
        private void SetParticlePhase(float phase)
        {
            // ⛔⛔ 起点必须落在**刚出屏的一圈**上，不能是"目标点再往外推固定 1400px"。
            //   初版就是后者：屏幕对角半径才 ~1100，推 1400 意味着**中途连屏都还没进**
            //   —— 实测 phase=0.55 时一个粒子都看不见，整场"合拢"看起来什么都没发生。
            //   正解：所有粒子都从半径 = 屏半对角 + 余量 的圆周上出发，朝自己的目标飞。
            float halfW = _root.rect.width * 0.5f;
            float halfH = _root.rect.height * 0.5f;
            if (halfW <= 0f) halfW = 960f;
            if (halfH <= 0f) halfH = 540f;
            float ring = Mathf.Sqrt(halfW * halfW + halfH * halfH) + 120f;

            for (int i = 0; i < _parts.Count; i++)
            {
                var rt = _parts[i];
                if (rt == null) continue;
                float d = _partDelay[i];
                float k = Mathf.Clamp01((phase - d) / Mathf.Max(0.05f, 1f - d));

                Vector2 target = new Vector2(_partX[i], _partY[i]);
                // 方向：从屏幕中心指向目标；目标就在正中心时给个确定性的斜向（别用随机，可复盘）
                Vector2 dir = target.sqrMagnitude > 25f
                    ? target.normalized
                    : new Vector2(0.6f, 0.8f).normalized;
                Vector2 start = dir * ring;

                rt.anchoredPosition = Vector2.Lerp(start, target, k);

                var img = rt.GetComponent<Image>();
                if (img != null)
                {
                    var c = img.color;
                    // 透明度：前 40% 就把粒子点亮（否则"飞进来"这段是隐形的，白飞）
                    c.a = Mathf.Lerp(0f, AlphaFor(c), Mathf.Clamp01(k * 2.5f));
                    img.color = c;
                }
            }
        }

        /// <summary>open 0→1：粒子从中缝**向两侧退开**（"拨开"的观感）。</summary>
        private void SetParticleOpen(float open)
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                var rt = _parts[i];
                if (rt == null) continue;
                // ⚠ 用**横向**退场而不是"沿原路回去"：原路回去是"收缩"，
                //   横向退开才是"拨开帘子"——用户要的就是这个动作。
                float side = _partX[i] >= 0f ? 1f : -1f;
                float d = _partDelay[i];
                float k = Mathf.Clamp01((open - d * 0.5f) / Mathf.Max(0.05f, 1f - d * 0.5f));
                float e = k * k;   // 加速退场，收尾干脆
                rt.anchoredPosition = new Vector2(_partX[i] + side * e * 1500f, _partY[i]);
                rt.localRotation = Quaternion.Euler(0f, 0f, _partRot[i] + side * e * 220f);

                var img = rt.GetComponent<Image>();
                if (img != null)
                {
                    var c = img.color;
                    c.a = Mathf.Lerp(AlphaFor(c), 0f, e);
                    img.color = c;
                }
            }
        }

        private static float AlphaFor(Color c) { return c.a > 0.01f ? c.a : 0.85f; }

        /// <summary>场域色调（粒子整体染色，让它和场域背景同一个色温）。</summary>
        private static Color TintFor(string realmId)
        {
            if (realmId == WanXiang.Campaign.RealmIds.Marsh) return new Color(0.78f, 0.92f, 0.86f, 0.85f);
            if (realmId == WanXiang.Campaign.RealmIds.Ember) return new Color(1f, 0.86f, 0.72f, 0.90f);
            if (realmId == WanXiang.Campaign.RealmIds.Gear) return new Color(0.88f, 0.90f, 0.86f, 0.85f);
            if (realmId == WanXiang.Campaign.RealmIds.Silent) return new Color(0.80f, 0.80f, 0.88f, 0.70f);
            return new Color(0.95f, 0.90f, 0.74f, 0.85f);
        }

        private static string FxNameOf(string realmId)
        {
            if (realmId == WanXiang.Campaign.RealmIds.Marsh) return "realm_marsh";
            if (realmId == WanXiang.Campaign.RealmIds.Ember) return "realm_ember";
            if (realmId == WanXiang.Campaign.RealmIds.Gear) return "realm_gear";
            if (realmId == WanXiang.Campaign.RealmIds.Silent) return "realm_silent";
            if (realmId == WanXiang.Campaign.RealmIds.Thorn) return "realm_thorn";
            return "realm_marsh";
        }

        // =====================================================================
        //  艺术字
        // =====================================================================

        private static readonly int ProgressId = Shader.PropertyToID("_Progress");

        private void PrepareTitle(string titleText)
        {
            var sp = Resources.Load<Sprite>("UI/Realm/realm_title");
            if (sp == null)
            {
                // 缺图就退化成"只有粒子拨开"：不能因为一张图让整个转场卡住
                Debug.LogWarning("[RealmTransition] 缺艺术字图 UI/Realm/realm_title，本次只播粒子");
                _title.enabled = false;
                return;
            }
            _title.sprite = sp;
            _title.type = Image.Type.Simple;
            _title.preserveAspect = true;

            var shader = Shader.Find("WanXiang/UI/RealmTitleReveal");
            if (shader == null)
            {
                Debug.LogWarning("[RealmTransition] 找不到 shader WanXiang/UI/RealmTitleReveal —— 退化为普通淡入" +
                                 "（检查 Assets/Resources/Shaders/RealmTitleReveal.shader 是否被 Unity 编译）");
                _titleMat = null;
                return;
            }
            if (_titleMat == null)
            {
                _titleMat = new Material(shader);
                _titleMat.hideFlags = HideFlags.HideAndDontSave;
            }
            // 噪声图：揭示边缘的"墨齿"全靠它。没有也不致命（会退化成圆形揭示）。
            var noise = Resources.Load<Texture2D>("UI/Realm/reveal_noise");
            if (noise != null) _titleMat.SetTexture("_NoiseTex", noise);
            _titleMat.SetVector("_RippleCenter", new Vector4(0.5f, 0.45f, 0f, 0f));
            _titleMat.SetFloat("_MaxRadius", 1.45f);
            _titleMat.SetFloat("_Feather", 0.17f);
            _titleMat.SetFloat("_NoiseStrength", 0.18f);
            _titleMat.SetColor("_EdgeColor", new Color(0.94f, 0.86f, 0.62f, 1f));
            _titleMat.SetFloat("_EdgeWidth", 0.035f);
            // ⛔ 反相成白墨：底色是黑幕，黑墨字会完全融进去（实测只剩边缘一圈白线）。
            //   白墨 + 金涟漪在暗底上才是"墨从水里洇出来"的观感。
            _titleMat.SetFloat("_Invert", 1f);
            _titleMat.SetFloat(ProgressId, 0f);
            _title.material = _titleMat;
        }

        private static float EaseOut(float k) { return 1f - (1f - k) * (1f - k); }
        private static float EaseIn(float k) { return k * k; }
    }
}
