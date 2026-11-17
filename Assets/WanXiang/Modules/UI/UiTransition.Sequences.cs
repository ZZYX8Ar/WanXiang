// ============================================================================
//  万相 · UI 转场动画序列（2026-11-17 代码骨架重构）
//  从 UiTransition.cs 原样搬入，逻辑零改动。
//  ⚠ 共享字段（_canvas/_cover/_blocker/_inkCoverMat 等）保留在主文件，partial 直接访问。
// ============================================================================
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.Modules.UI
{
    public sealed partial class UiTransition
    {
        ///   "靠 image alpha 淡入"的转场会被 `_Progress=0` 弄成完全隐形。</para>
        /// <para>⛔ 找不回 shader 时直接切界面（宁可没有转场，也不能卡在半黑）。</para>
        /// </summary>
        private IEnumerator InkCoverSequence(int act, System.Action onSwitch, float tIn, float tHold, float tOut,
                                             System.Func<UniTask> preSequence)
        {
            if (_cover == null) { onSwitch?.Invoke(); yield break; }

            if (_inkCoverMat == null)
            {
                var sh = Shader.Find("WanXiang/UI/InkCover");
                if (sh != null) _inkCoverMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (_inkCoverMat == null)
            {
                Debug.LogWarning("[UiTransition] 找不到 WanXiang/UI/InkCover，直接切界面（跳过转场）");
                onSwitch?.Invoke();
                yield break;
            }

            _cover.sprite = null;
            _cover.material = _inkCoverMat;
            _cover.color = Color.white;           // 透明度交给 shader
            _cover.raycastTarget = true;
            _inkCoverMat.SetColor("_CoverColor", Color.black);
            _inkCoverMat.SetFloat(InkProgressId, 0f);

            // ① 墨从四边吃进来（★ 2026-11-04：走 easeInOut 曲线 —— 线性推进一眼就"机械"）
            float t = 0f;
            while (t < tIn && Time.realtimeSinceStartup < _deadline)
            {
                t += Time.unscaledDeltaTime;
                _inkCoverMat.SetFloat(InkProgressId, EaseInOutCubic(Mathf.Clamp01(t / Mathf.Max(0.01f, tIn))));
                yield return null;
            }
            _inkCoverMat.SetFloat(InkProgressId, 1f);

            // ② 换界面 + 预热（全在满屏墨之下，看不见）
            try { onSwitch?.Invoke(); }
            catch (System.Exception ex) { Debug.LogError("[UiTransition] onSwitch 异常：" + ex); }
            yield return null;
            if (preSequence != null)
            {
                var pre = preSequence();
                yield return pre.ToCoroutine();
            }

            // ③ 停一下
            float h = 0f;
            while (h < tHold) { h += Time.unscaledDeltaTime; yield return null; }

            // ④ 墨退回四边（同一条曲线，退得比进略"利落"：曲线右移）
            t = 0f;
            while (t < tOut && Time.realtimeSinceStartup < _deadline + 2f)
            {
                t += Time.unscaledDeltaTime;
                _inkCoverMat.SetFloat(InkProgressId, 1f - EaseInOutCubic(Mathf.Clamp01(t / Mathf.Max(0.01f, tOut))));
                yield return null;
            }
            _inkCoverMat.SetFloat(InkProgressId, 0f);

            // ⑤ 摘材质（见上面的 ⛔）
            _cover.material = null;
            _cover.color = new Color(1f, 1f, 1f, 0f);
            _cover.raycastTarget = false;
        }

        /// <summary>
        /// 预热一批 Sprite 的**纹理上传**（2026-10-14）：首次显示的贴图要在渲染那一帧上传，
        /// 写字笔顺这类"逐帧换图"的演出会因此一顿一顿（实测前几笔各卡 10~60ms）。
        /// 做法：在转场遮盖层**之下**铺一帧极小图（屏幕中央、透明 0.02），渲一帧即清。
        /// ⚠ 必须趁屏幕被盖住时调用（目前调用点：Start→主界面 的水墨转场遮盖期）。
        /// </summary>
        public static async UniTask WarmSpriteUploadsAsync(List<Sprite> sprites)
        {
            var inst = Ensure();
            if (inst == null || inst._root == null || sprites == null || sprites.Count == 0) return;
            var tmp = new List<GameObject>(sprites.Count);
            float cw = inst.CW;
            for (int i = 0; i < sprites.Count; i++)
            {
                var sp = sprites[i];
                if (sp == null) continue;
                var go = new GameObject("WarmUp", typeof(RectTransform));
                go.transform.SetParent(inst._root, false);
                var img = go.AddComponent<Image>();
                img.raycastTarget = false;
                img.sprite = sp;
                img.color = new Color(1f, 1f, 1f, 0.02f);
                var rt = img.rectTransform;
                rt.sizeDelta = new Vector2(4f, 4f);
                rt.anchoredPosition = new Vector2((i % 300) * 6f - cw * 0.5f, (i / 300) * 6f);   // 屏内条带（在遮盖之下，不可见）
                go.transform.SetSiblingIndex(0);   // 压到遮盖层底下
                tmp.Add(go);
            }
            await UniTask.DelayFrame(2);
            for (int i = 0; i < tmp.Count; i++)
            {
                if (tmp[i] != null) UnityEngine.Object.Destroy(tmp[i]);
            }
        }

        /// <summary>
        /// 等"最后停留"：至少 <paramref name="minHold"/> 秒，且**主线程安静**（连续 8 帧无 &gt;60ms 大帧）
        /// 才放行退场。2026-10-14 用户："第一次会有一点卡，后面就流畅了" ——
        /// 首开进图的一次性大帧（面板首建/首渲染，实测 0.4~0.6s）都发生在退场**之前**，
        /// 热路径没有这些帧、此等待立即通过。等到它们吃完再"拨开"，玩家看到的
        /// 退场/露出全程干净；代价只是首开时屏幕多盖住 ~1 秒（不可见）。
        /// </summary>
        private IEnumerator SettleWait(float minHold)
        {
            float e = 0f;
            int calm = 0;
            float maxT = minHold + 1.8f;                 // 最多多等 1.8s，防意外卡死
            float prev = Time.unscaledTime;
            while (true)
            {
                yield return null;
                float now = Time.unscaledTime;
                float dt = now - prev; prev = now;
                e += dt;
                if (dt < 0.06f) { calm++; } else { calm = 0; }
                if (e >= minHold && calm >= 8) break;
                if (e > maxT) break;
                if (Time.realtimeSinceStartup > _deadline) break;   // 硬超时保护
            }
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
            if (_root == null) yield break;   // onSwitch 里同步切了场景：局部元素已毁，跳过退场（Run 重建层走纸底淡出）
            yield return SettleWait(tHold);   // 退场门限：等主线程安静再"拨开"（见 SettleWait）

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
            // ★ 2026-11-04：上一次 InkCover 被打断时材质还挂在 _cover 上，不摘会让后面的
            //   Ink/Split 转场（靠 alpha 淡入）被 _Progress=0 弄成"完全不显示"。
            if (_cover.material != null && _inkCoverMat != null && _cover.material == _inkCoverMat)
                _cover.material = null;
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

        /// <summary>
        /// onSwitch 里同步 LoadScene 之后调用：转场层的场景内子级（_root/_cover/_blocker/池）
        /// 随旧场景销毁而消失（层本体挂在 DontDestroyOnLoad 根上，活着）。
        /// 重建一层并用目标幕的纸底立即盖满，让 <see cref="Run"/> 的收尾淡出露出新场景 ——
        /// 用户 2026-10-14："从编队进战斗老是闪一下主界面/节点地图，观感很差"。
        /// 没切场景（_root 还活着）时是空操作，原有退场动画不受影响。
        /// </summary>
        private void RebuildAfterSceneSwitch(int act)
        {
            if (_root != null && _cover != null && _blocker != null) return;
            Debug.Log("[UiTransition] 场景已切换 → 重建转场层并盖纸底（act=" + act + "）");
            _pool.Clear();
            _streaks.Clear();
            Build();
            if (_cover != null) _cover.color = SeasonCoverColor(Mathf.Clamp(act, 1, 5));   // 立即盖满
            if (_blocker != null) _blocker.raycastTarget = true;   // 继续防误触，收尾再关
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
        /// <summary>
        /// **粒子拨开**（2026-10-20 用户指定：替换原来的"粒子滑动"）
        /// 闭拢：按幕的季节粒子从屏幕四周向中心收拢，把画面盖满；
        /// 切换：屏幕全遮的一刻切界面；
        /// 拨开：粒子从中心向四周退开，露出新界面。
        /// </summary>
        private IEnumerator PartSequence(int act, System.Action onSwitch, float tIn, float tHold, float tOut)
        {
            var psp = Resources.Load<Sprite>(FallByAct[Mathf.Clamp(act, 1, 5) - 1]);
            if (psp == null) { if (onSwitch != null) onSwitch(); yield break; }

            int n = 150;
            var parts = new List<Image>();
            var dirs = new List<Vector2>();
            var starts = new List<Vector2>();
            var sizes = new List<float>();
            float maxR = Mathf.Max(CW, CH) * 0.62f;

            for (int i = 0; i < n; i++)
            {
                var p = NewImage("Pt" + i);
                p.sprite = psp;
                float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                // 起点：屏幕外一圈（不是正圆——加抖动免得太规整）
                float r = maxR * UnityEngine.Random.Range(1.02f, 1.45f);
                var start = dir * r + new Vector2(
                    UnityEngine.Random.Range(-CW * 0.10f, CW * 0.10f),
                    UnityEngine.Random.Range(-CH * 0.10f, CH * 0.10f));
                float sz = UnityEngine.Random.Range(96f, 260f);
                p.rectTransform.sizeDelta = new Vector2(sz, sz);
                p.rectTransform.anchoredPosition = start;
                p.color = new Color(1f, 1f, 1f, 0.95f);
                parts.Add(p); dirs.Add(dir); starts.Add(start); sizes.Add(sz);
            }

            Color paper = SeasonCoverColor(act);

            // ---- ① 闭拢：粒子向中心收拢，盖满屏幕 ----
            float t = 0f;
            while (t < tIn)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / tIn);
                float e = EaseOut(k);
                for (int i = 0; i < parts.Count; i++)
                {
                    if (parts[i] == null) continue;
                    // 终点：中心附近一小片（保留一点散布，否则全叠成一点）
                    var end = dirs[i] * UnityEngine.Random.Range(6f, 40f);
                    parts[i].rectTransform.anchoredPosition = Vector2.Lerp(starts[i], end, e);
                }
                _cover.color = new Color(paper.r, paper.g, paper.b, Mathf.Lerp(0f, 1f, e));
                yield return null;
            }
            // 全遮兜底（此时切界面，绝不能露馅）
            _cover.color = new Color(paper.r, paper.g, paper.b, 1f);

            // ---- ② 切换 ----
            try { if (onSwitch != null) onSwitch(); }
            catch (System.Exception ex) { Debug.LogError("[UiTransition] onSwitch 异常：" + ex.Message); }
            yield return null;

            // ---- ③ 停留（纸底还在，粒子压在中缝）----
            float hold = Mathf.Max(0.05f, tHold);
            for (t = 0f; t < hold; t += Time.deltaTime) yield return null;

            // ---- ④ 拨开：粒子向四周退开 + 纸底淡出 ----
            t = 0f;
            while (t < tOut)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / tOut);
                float e = EaseIn(k);
                for (int i = 0; i < parts.Count; i++)
                {
                    if (parts[i] == null) continue;
                    var end = dirs[i] * (maxR * UnityEngine.Random.Range(1.05f, 1.5f));
                    parts[i].rectTransform.anchoredPosition = Vector2.Lerp(dirs[i] * 20f, end, e);
                    var c = parts[i].color; c.a = Mathf.Lerp(0.95f, 0f, Mathf.Clamp01(k * 1.15f));
                    parts[i].color = c;
                }
                // 纸底前 35% 就淡掉，露出新界面；粒子继续退开
                _cover.color = new Color(paper.r, paper.g, paper.b,
                    Mathf.Lerp(1f, 0f, Mathf.Clamp01(k / 0.35f)));
                yield return null;
            }
            _cover.color = new Color(paper.r, paper.g, paper.b, 0f);
        }

        private static float EaseOut(float k) { return 1f - (1f - k) * (1f - k); }
        private static float EaseIn(float k) { return k * k; }

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
            if (_root == null) yield break;   // onSwitch 里同步切了场景：局部元素已毁，跳过退场（Run 重建层走纸底淡出）
            yield return SettleWait(tHold);   // 退场门限：等主线程安静再"拨开"（见 SettleWait）

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
            if (_root == null) yield break;   // onSwitch 里同步切了场景：局部元素已毁，跳过退场（Run 重建层走纸底淡出）
            yield return SettleWait(tHold);   // 退场门限：等主线程安静再"拨开"（见 SettleWait）

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
            if (_root == null) yield break;   // onSwitch 里同步切了场景：局部元素已毁，跳过退场（Run 重建层走纸底淡出）
            yield return SettleWait(tHold);   // 退场门限：等主线程安静再"拨开"（见 SettleWait）
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
