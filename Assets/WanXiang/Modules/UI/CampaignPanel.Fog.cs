// ============================================================================
//  万相 · 地图迷雾系统（2026-11-17 代码骨架重构）
//  从 CampaignPanel.cs 原样搬入，逻辑零改动。
// ============================================================================
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Battle.Core;
using WanXiang.Framework.UI;
using WanXiang.Fusion;

namespace WanXiang.Modules.UI
{
    public sealed partial class CampaignPanel
    {
        private int FogFromLayer()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null || run.FogLeft <= 0 || _graph == null) return int.MaxValue;
            int baseLayer = _currentOffset >= 0 ? _graph.LayerOf(_currentOffset) : -1;
            return baseLayer + 2;
        }
        private bool InFog(int offset)
        {
            int f = FogFromLayer();
            if (f == int.MaxValue || _graph == null) return false;
            // ⛔ 起雾的那一格（天象节点）**不盖雾** —— 用户 2026-10-10 定案："不要包括一开始的迷雾节点，
            //   要后续 3 个节点，不然这个天象节点怎么解释"（玩家要看得见、走得进去，雾从它之后开始）。
            return _graph.LayerOf(offset) >= f;
        }

        private static Sprite _fogCover; private static bool _fogCoverTried;
        /// <summary>盖住"被雾遮住"节点的整卡雾图（Resources/UI/Campaign/fog_card）。缺图 = 不显示。</summary>
        private static Sprite FogCoverSprite()
        {
            if (_fogCoverTried) return _fogCover;
            _fogCoverTried = true;
            _fogCover = UnityEngine.Resources.Load<Sprite>("UI/Campaign/fog_card");
            return _fogCover;
        }

        /// <summary>
        /// 【正式来源】本幕「瘴雾」节点生成：**天象节点按概率**挂 + **每幕再随机 0~2 个**（可能一个都没有）。
        /// 由 run seed 派生 ， 同一局同一幕结果稳定；生成一次即落盘（<c>FogGenAct</c> 记住已生成哪一幕），换幕重生成。
        /// </summary>
        private void EnsureFogNodesGenerated(WanXiang.Run.RunState run, WanXiang.Campaign.ActGraph g)
        {
            if (run == null || g == null || g.Kinds == null || g.Layers == null) return;
            if (run.FogGenAct == g.Act) return;                     // 本幕已生成过
            run.FogGenAct = g.Act;
            if (run.FogWeatherNodes == null)
                run.FogWeatherNodes = new System.Collections.Generic.List<int>();
            run.FogWeatherNodes.Clear();

            // ⛔ 独立随机源（别用 st.Random：会扰动战斗随机流）；用 run seed 派生 ， 稳定可复现
            var rng = new System.Random((int)(CoreMath.Fnv1a("fog:" + run.RunSeed + ":" + g.Act) & 0x7fffffffUL));
            int last = g.NodeCount - 1;

            // "全 ？ 层"（整层未知）， 该层每个节点**必定**起雾
            var forced = new System.Collections.Generic.List<int>();
            ForcedQuestionLayers(run, g, forced);
            var forcedSet = new System.Collections.Generic.HashSet<int>(forced);

            for (int off = 0; off < g.NodeCount; off++)
            {
                if (off == last) continue;                                           // 末层守关：不含
                if (forcedSet.Contains(g.LayerOf(off)))                              // 整层未知 ， 必定
                {
                    run.FogWeatherNodes.Add(off);
                    continue;
                }
                var k = g.Kinds[off];
                if (k == WanXiang.Campaign.NodeKind.Omen && rng.NextDouble() < 0.50)         // 天象：50%
                    run.FogWeatherNodes.Add(off);
                else if (k == WanXiang.Campaign.NodeKind.Question && rng.NextDouble() < 0.20) // 普通未知：~20%
                    run.FogWeatherNodes.Add(off);
            }
            Debug.Log("[Campaign] 本幕瘴雾节点生成：" + run.FogWeatherNodes.Count +
                      " 个（全？层必定 / 天象 50% / 未知 20%）｜Act=" + g.Act);
            WanXiang.Run.RunSave.SaveCurrent();
        }

        /// <summary>
        /// 本幕哪些层被强制成"**整层都是 ？**"：**四幕（1~4）里选 1~2 幕**，每幕再选 **1~2 层**
        /// （避开最前 4 层与末层、跳过单格结构层）。全部由 run seed 派生 ， 同一局稳定、不必落盘。
        /// ⛔ 只改 `Kinds`（"这格是什么"），**不动拓扑**。
        /// </summary>
        private static void ForcedQuestionLayers(WanXiang.Run.RunState run, WanXiang.Campaign.ActGraph g,
                                                System.Collections.Generic.List<int> outLayers)
        {
            outLayers.Clear();
            if (run == null || g == null || g.Layers == null) return;

            // ① 四幕里挑 1~2 幕"必定有迷雾"
            var a = new System.Random((int)(CoreMath.Fnv1a("fogacts:" + run.RunSeed) & 0x7fffffffUL));
            int nActs = 1 + a.Next(0, 2);
            var acts = new System.Collections.Generic.List<int>();
            for (int i = 0; i < nActs; i++) { int x = a.Next(1, 5); if (!acts.Contains(x)) acts.Add(x); }
            if (!acts.Contains(g.Act)) return;                          // 本幕不在名单 ， 没有全？层

            int lc = g.Layers.Length;
            if (lc < 8) return;                                         // 图太小（缺省图）跳过
            // ② 本幕挑 1~2 层
            var r2 = new System.Random((int)(CoreMath.Fnv1a("foglayer:" + run.RunSeed + ":" + g.Act) & 0x7fffffffUL));
            int nL = 1 + r2.Next(0, 2);
            for (int i = 0; i < nL; i++)
            {
                int L = r2.Next(4, lc - 1);                             // 避开最前 4 层与末层
                if (g.Layers[L] == null || g.Layers[L].Length <= 1) continue;   // 单格层 = 结构位，跳过
                if (!outLayers.Contains(L)) outLayers.Add(L);
            }
        }

        /// <summary>
        /// 触发瘴雾：接下来 <paramref name="nodes"/> 个节点内，图上只有"当前格 + 下一层"可见，
        /// 更远的层连类型都被雾盖住；**并把雾覆盖到、还没走过的节点类型重掷一遍**（关键节点除外）。
        /// 已经在雾里则只续期、不重复重掷（防节点反复变）。
        /// </summary>
        public void TriggerFog(int nodes)
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null || nodes <= 0) return;
            bool alreadyFoggy = run.FogLeft > 0;
            run.FogLeft = nodes;
            run.FogRerolled = true;
            if (!alreadyFoggy) RerollFoggedKinds(run);
            WanXiang.Run.RunSave.SaveCurrent();
            Debug.Log("[Campaign] 瘴雾来袭：接下来 " + nodes + " 节只看得见下一层" +
                      (alreadyFoggy ? "（续期，不重掷）" : "（并重掷了远处节点类型）"));
            BuildNodeMap();
            // 注：起雾的"当场提示"见 ShowFogBanner（下面）
            // ★ 开场提示：把"后续节点已经变了"讲明白（否则玩家会以为是 bug）
            ShowFogBanner(alreadyFoggy
                ? "瘴雾未散 · 视野依旧只剩下一层"
                : "瘴雾来袭 · 前路隐没，后续节点已非旧貌");
        }

        /// <summary>
        /// 【调试，模拟正式流程】把「瘴雾」天时挂到**下一层**的节点上：节点卡上会显示「·瘴雾」，
        /// 等你点「前往」真正走到那里时才起雾（不是一点按钮就变）。正式版应由节点生成/天气数据填这个表。
        /// </summary>
        public void ArmFogOnNextLayer()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null || _graph == null || _graph.Layers == null) return;
            if (run.FogWeatherNodes == null) run.FogWeatherNodes = new System.Collections.Generic.List<int>();

            int nextLayer = _currentOffset >= 0 ? _graph.LayerOf(_currentOffset) + 1 : 0;
            if (nextLayer < 0 || nextLayer >= _graph.Layers.Length) { ShowFogBanner("已经没有下一层了"); return; }

            int added = 0;
            foreach (var off in _graph.Layers[nextLayer])
                if (!run.FogWeatherNodes.Contains(off)) { run.FogWeatherNodes.Add(off); added++; }
            WanXiang.Run.RunSave.SaveCurrent();
            Debug.Log("[Campaign] 已把「瘴雾」天时挂到下一层 " + added + " 个节点（走到即起雾）");
            BuildNodeMap();
            ShowFogBanner("已把「瘴雾」天时挂到下一层 —— 走到那个节点就会起雾");
        }

        /// <summary>
        /// 重掷"雾里"的节点类型。只动**没走过、非当前格、非关键**的节点，且**拓扑不动**
        /// （层/边/节气序号全不变）， 所有按下标存的存档字段（NodeOffset/VisitedNodes/Path/QuestionRevealed）全部有效。
        /// 结果写进 <c>RunState.RerolledKinds</c> 落盘（节点图每次由种子重建，不落盘就被冲掉，见 BuildNodeMap）。
        /// </summary>
        private static void RerollFoggedKinds(WanXiang.Run.RunState run)
        {
            var graph = WanXiang.Campaign.SolarTermGraph.BuildRoute(
                run.Act, CoreMath.Fnv1a("route:" + run.RunSeed + ":" + run.Act), Layers);
            if (graph == null || graph.Kinds == null || graph.Layers == null) return;

            int baseLayer = run.NodeOffset >= 0 ? graph.LayerOf(run.NodeOffset) : -1;
            int fogFrom = baseLayer + 2;
            int last = graph.NodeCount - 1;

            // 重掷池：普通"计划类"节点（遭遇权重高一点）。精英/招募/遗物是结构位，不在池里。
            var pool = new[]
            {
                WanXiang.Campaign.NodeKind.Encounter, WanXiang.Campaign.NodeKind.Encounter,
                WanXiang.Campaign.NodeKind.Shop, WanXiang.Campaign.NodeKind.Nest,
                WanXiang.Campaign.NodeKind.Tale, WanXiang.Campaign.NodeKind.Forge,
                WanXiang.Campaign.NodeKind.Omen, WanXiang.Campaign.NodeKind.Question,
            };

            // ⛔ 独立随机源（绝不用 st.Random —— 那会扰动战斗随机流，项目铁律）
            var rng = new System.Random(System.Environment.TickCount ^ (run.RunSeed * 397) ^ run.Act);
            var recs = new System.Collections.Generic.List<string>();
            for (int off = 0; off < graph.NodeCount; off++)
            {
                if (graph.LayerOf(off) < fogFrom) continue;                                   // 只在雾里重掷
                if (off == last) continue;                                                    // 末层守关 Boss：锁死
                if (run.VisitedNodes != null && run.VisitedNodes.Contains(off)) continue;      // 走过的：锁死
                if (IsStructuralKind(graph.Kinds[off])) continue;                              // 招募/遗物/精英：锁死
                var k = pool[rng.Next(pool.Length)];
                recs.Add(off + ":" + (int)k);
            }
            run.RerolledKinds = recs;
        }

        /// <summary>结构位节点（不参与重掷）：遗物(次层) / 精英(含守关占位)。招募已删除。</summary>
        private static bool IsStructuralKind(WanXiang.Campaign.NodeKind k)
        {
            return k == WanXiang.Campaign.NodeKind.Relic
                || k == WanXiang.Campaign.NodeKind.Elite;
        }

        /// <summary>从 "Item_Node_&lt;offset&gt;" 取 offset。</summary>
        private static int OffsetOfName(string n)
        {
            if (string.IsNullOrEmpty(n)) return -1;
            int k = n.LastIndexOf('_');
            int v;
            return (k >= 0 && int.TryParse(n.Substring(k + 1), out v)) ? v : -1;
        }

        // ------------------------------------------------------------------
        //  顶部横幅提示（瘴雾来 / 散）—— prefab 里的 Root_FogBanner
        // ------------------------------------------------------------------
        private Coroutine _fogBannerCo;
        /// <summary>顶部横幅提示（瘴雾来/散）。被弹层盖着会先等着，等回到地图可见再显示。</summary>
        private void ShowFogBanner(string text)
        {
            if (!isActiveAndEnabled) return;
            if (_fogBannerCo != null) StopCoroutine(_fogBannerCo);
            _fogBannerCo = StartCoroutine(FogBannerAnim(text));
        }

        private System.Collections.IEnumerator FogBannerAnim(string text)
        {
            var t = transform.Find("Root_FogBanner");
            if (t == null) yield break;                                  // 缺节点 = 不提示（不报错）
            var txt = t.Find("Tmp_FogBanner") != null ? t.Find("Tmp_FogBanner").GetComponent<TMP_Text>() : null;
            if (txt != null)
            {
                txt.text = text;
                // ★ 2026-11-16 用户："先看有文字的面板底图是什么颜色，再决定文字颜色"。
                //   底图 = map_ui_card 浅米纸（白），原字色 (0.96,0.91,0.78) 浅米金 ⇒ 明度几乎相同看不清。
                //   改深墨色（sRGB #3A3026），Linear 下走 UiInk 转换，保证浅底深字。
                if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                    txt.color = UiInk.Hex("#3A3026");
            }
            var cg = t.GetComponent<CanvasGroup>();
            if (cg == null) cg = t.gameObject.AddComponent<CanvasGroup>();

            // 被上层弹层盖着（战斗/选择/开场）， 先等，别在背后一闪而过（用户：别没看到就结束了）
            float w = 0f;
            while (State == UIPanelState.Paused && w < 8f) { if (t == null) yield break; w += Time.unscaledDeltaTime; yield return null; }
            float d0 = 0f;
            while (d0 < 0.35f) { if (t == null) yield break; d0 += Time.unscaledDeltaTime; yield return null; }

            t.gameObject.SetActive(true);
            float d = 0.35f, e = 0f;
            while (e < d) { if (t == null) yield break; e += Time.unscaledDeltaTime; cg.alpha = Mathf.Clamp01(e / d); yield return null; }
            cg.alpha = 1f;
            yield return new WaitForSecondsRealtime(2.6f);
            e = 0f;
            while (e < d) { if (t == null) yield break; e += Time.unscaledDeltaTime; cg.alpha = 1f - Mathf.Clamp01(e / d); yield return null; }
            cg.alpha = 0f;
            if (t != null) t.gameObject.SetActive(false);
            _fogBannerCo = null;
        }

        private RectTransform SpawnNodeItem(RectTransform content, int offset, int layer)
        {
            var item = Instantiate(_nodeItemTemplate, content);
            item.name = "Item_Node_" + offset;
            item.gameObject.SetActive(true);

            var kind = _graph.KindOf(offset);
            bool isHere = offset == _currentOffset;
            bool canGo = IsReachable(offset);
            bool passed = _passed.Contains(offset);   // ★ "真·通过"（不是"进入"）：落印✕ / 标签描金 只看它
            bool fogged = InFog(offset);              // ★ 瘴雾：只在"当前格 + 下一层"内可见

            // ★ 节点卡：按【本幕季节】换底板（纸卡 + 四角季节装饰）；锁定节点压暗。
            var cardImg = item.GetComponent<Image>();
            if (cardImg != null)
            {
                var card = SeasonCardSpriteFor(SeasonIdx);
                if (card != null) { cardImg.sprite = card; cardImg.type = Image.Type.Sliced; }
                cardImg.color = fogged
                    ? new Color(0.78f, 0.80f, 0.82f, 0.95f)                      // ★ 瘴雾：冷灰"雾气卡"
                    : (passed || canGo || isHere) ? Color.white : new Color(1f, 1f, 1f, 1f);   // 未解锁实心（卡片图本身半透明）
            }

            var label = item.Find("Tmp_NodeText") != null
                ? item.Find("Tmp_NodeText").GetComponent<TMP_Text>() : null;
            if (label != null)
            {
                float cardW = ((RectTransform)item).sizeDelta.x;
                label.fontSize = cardW < 360f ? 17f : 20f;   // 4 节点收窄时字号同步缩小
            }
            if (label != null && fogged)
            {
                // ★ 瘴雾：遮住的层连类型都不透 —— 只显示"迷雾"（这就是迷雾的意义）
                label.text = "？？";
                label.color = new Color(0.52f, 0.50f, 0.48f, 0.9f);
            }
            else if (label != null)
            {
                // ★ 守卫格在图里都是【精英】占位（NodeKind 没有 Boss 类型），显示上必须讲明：
                //   · 幕 1~4 守关  → 「守关 · <首领名>」，走 BossSquadFor 抽 BossCatalog 首领
                //   · 幕 5 天阙末格 → 「终局 · 后土」，走 TrialPanel.BuildFinaleBattle
                // ⚠ 两个分支都要在这里判：只判 IsSeasonBossNode 会让**天阙终局格漏成【精英】**
                //   （实测截图：第 5 幕"小寒"格显示【精英】，玩家以为没有 boss 关）。
                bool isSeasonBossCell = IsSeasonBossNode(offset);
                bool isFinaleCell = _graph != null && _graph.Act >= 5 &&
                                    _graph.NodeCount > 0 && offset == _graph.NodeCount - 1;
                string name;
                // ⛔⛔ 守关/终局必须排在「?」**前面**（2026-10-17 用户实测又变回【精英】）：
                //   瘴雾重掷记录（RerolledKinds）是按**旧图形状**落盘的，图一变（层数/格数调整），
                //   旧记录就可能正好砸在守关格上把它改成 Question —— 而"已揭晓的?"会显示真实类型，
                //   于是守关显示成【精英】。守关/终局是**结构位**（由格子位置决定，不由类型决定），
                //   所以它的优先级必须高于一切类型改写。
                if (isFinaleCell)
                    name = "终局·" + (_graph.BossName ?? "后土");
                else if (isSeasonBossCell)
                    name = "守关·" + (_graph.BossName ?? "首领");
                else if (kind == WanXiang.Campaign.NodeKind.Question && !IsRevealed(offset))
                    name = "？未知";
                else if (kind == WanXiang.Campaign.NodeKind.Question)
                    name = WanXiang.Campaign.NodeKinds.Cn(RevealedKind(offset));   // 揭晓后显示真实类型
                else
                    name = WanXiang.Campaign.NodeKinds.Cn(kind);
// ★ 文案精简：去掉多余全角空格；"已过"不再用文字 —— 改用朱砂 ✕ 印章（见下方 Img_Stamp）
                // ⚠ 秘境模式：**不显示节气**。秘境图的 Terms 存的是"层号"（1,2,2,2,3…），
                //   当节气名渲染会得到"11.清明【灵市】"这种驴唇不对马嘴的标签。
                //   ★ 2026-10-16 用户要求秘境扩到 12+ 层 ⇒ 单位从"波"改成"层"
                //   （层是地图长度轴，波是战斗场次轴，扩层后两者已经不是一回事了，
                //     继续叫"第12波"会和"本秘境 3 场硬仗"自相矛盾）。
                string prefix = _realmMode
                    ? "第" + RealmLayerOf(offset) + "层"
                    // ★ 2026-11-02 用户："第一层是 123，应该是 111" ——
                    //   编号 = **层号**（同层所有候选共用同一个数字），不是全局节点序号。
                    : (_graph.LayerOf(offset) + 1) + "." + TermName(_graph.Terms[offset]);
            // ⛔ 2026-10-22 用户两次报障后**取消同层互斥**：
            //   "明明能去下一层的两个节点，怎么只能去一个""点一下节点就直接确定，不给点其他的"。
            //   互斥在**点击时**就写 ChosenNodes ⇒ 本层其余节点立刻变灰，像"一点就锁死"。
            //   现在：点击只选中（预览），能否前往只看图上的连线（IsReachable）。

     label.text = prefix + "【" + name + "】" +
     (isHere ? " ◀当前" : canGo ? " ←可前往" : "");
             label.color = isHere ? NodeVisited : canGo ? NodeReachable : passed ? PathGold : NodeLocked;
             label.outlineWidth = 0.28f;                                    // 深墨描边：彩叶/雪景/树枝底都读得清
             label.outlineColor = new Color(0.22f, 0.18f, 0.14f, 0.95f);
            }

            var dot = item.Find("Img_Dot") != null ? item.Find("Img_Dot").GetComponent<Image>() : null;
            if (dot != null)
            {
                dot.gameObject.SetActive(!fogged);           // ★ 雾里的节点连季节徽都不露
                if (!fogged)
                {
                    bool q = kind == WanXiang.Campaign.NodeKind.Question && !IsRevealed(offset);
                    // ★ 2026-11-14 用户：每类节点要有自己的图标（原来全用同一张季节徽分不出来）
                    bool isBossCell = IsSeasonBossNode(offset)
                        || (_graph != null && _graph.Act >= 5 && offset == _graph.NodeCount - 1);
                    var kindIcon = KindIconSprite(isBossCell ? "Boss" : kind.ToString());
                    var em = kindIcon != null ? kindIcon : SeasonNodeSprite();
                    if (em != null) dot.sprite = em;
                    // ★ 节点按【本幕季节】上色；锁定态压暗 = "去不了"。"当前/可前往/已过"另有文字标签按状态上色。
                    var sc = SeasonAccent;
                    bool lit = isHere || canGo || passed;
                    // ★ 新图标自带配色（墨绿双环），不能再被季节色 tint（否则变成一团茶色空圆）
                    dot.color = kindIcon != null
                        ? (lit ? Color.white : new Color(1f, 1f, 1f, 0.72f))
                        : (q ? QuestionInk
                             : lit ? sc
                             : new Color(sc.r * 0.62f, sc.g * 0.62f, sc.b * 0.62f, 0.75f));
                }
            }

            // ★ 瘴雾：不再盖美术图（用户 2026-10-10："整个美术就不是那么重要了，这个美术图先去了吧"）
            //   被雾遮住的节点只显示「？？」，靠"看不见 + 未知"表达（prefab 里的 Img_FogCover 留着备用，不启用）。

            var btn = item.GetComponent<Button>();
            if (btn == null) btn = item.gameObject.AddComponent<Button>();
            btn.targetGraphic = item.GetComponent<Image>();
            // 已过的节点：不可再点 + 变灰 + 右上角一个 ✕（用户要求"通过了就不能再点"）
            btn.interactable = canGo || isHere;

            // ★ 已过：盖一个朱砂「✕」印（用户要求：去掉"已过"文字，改用落印 + 落印动画）
            var stampT = item.Find("Img_Stamp") as RectTransform;
            if (stampT == null)
            {
                stampT = new GameObject("Img_Stamp", typeof(RectTransform)).GetComponent<RectTransform>();
                stampT.SetParent(item, false);
                stampT.anchorMin = stampT.anchorMax = new Vector2(0f, 0.5f);   // 盖在左侧季节徽上
                stampT.pivot = new Vector2(0.5f, 0.5f);
                stampT.anchoredPosition = new Vector2(94f, 0f);
                stampT.sizeDelta = new Vector2(86f, 86f);
                var si = stampT.gameObject.AddComponent<Image>();
                si.raycastTarget = false;
                si.sprite = StampSprite();
            }
            var stampImg = stampT.GetComponent<Image>();
            if (stampImg != null && stampImg.sprite == null) stampImg.sprite = StampSprite();
            // ★ 只判 passed（含"当前"格）：刚通过的节点**就是**当前格，若排除 isHere 会"晚一格"才盖印（用户实锤）
            bool showStamp = passed && !fogged;
            stampT.gameObject.SetActive(showStamp);
            if (showStamp && stampImg != null)
            {
                // ★ 本次新通过的那枚先"藏起来"(alpha 0)：等真正回到地图、看到画面时才由 PlayPendingAnim 落印
                //   （否则玩家还在战斗/选择弹层里，印章就在背后盖完了 —— 用户实锤）
                bool fresh = (offset == _justPassedNode);
                stampT.localScale = Vector3.one;
                stampT.localRotation = Quaternion.Euler(0f, 0f, -2f);
                var sc0 = stampImg.color; sc0.a = fresh ? 0f : 0.92f; stampImg.color = sc0;
            }
            var captured = offset;
            btn.onClick.AddListener(() => SelectNode(captured, silent: false));

            if (isHere) item.SetAsFirstSibling();
            _nodeItems.Add(item);
            return item;
        }

        /// <summary>读揭晓结果（没揭晓返回 Question 本身）。</summary>
        private WanXiang.Campaign.NodeKind RevealedKind(int offset)
        {
            var run = WanXiang.Run.RunSave.Current;
            // ⚠ 旧存档兼容（2026-11-07）：历史档里可能有「？揭晓成天象」的记录，
            //   天象已并入异闻 ⇒ 读出来时统一改成 Tale，避免旧档卡在已废弃的节点上。
            if (run != null && run.QuestionRevealed != null)
                for (int qi = 0; qi < run.QuestionRevealed.Count; qi++)
                {
                    var rec0 = run.QuestionRevealed[qi];
                    if (string.IsNullOrEmpty(rec0)) continue;
                    int cpos = rec0.LastIndexOf(':');
                    if (cpos < 0) continue;
                    int rk;
                    if (!int.TryParse(rec0.Substring(cpos + 1), out rk)) continue;
                    if (rk == (int)WanXiang.Campaign.NodeKind.Omen)
                        run.QuestionRevealed[qi] = rec0.Substring(0, cpos) + ":" + (int)WanXiang.Campaign.NodeKind.Tale;
                }

            if (run != null && run.QuestionRevealed != null)
                foreach (var rec in run.QuestionRevealed)
                    if (rec != null && rec.StartsWith(offset + ":"))
                        return (WanXiang.Campaign.NodeKind)int.Parse(rec.Substring(rec.IndexOf(':') + 1));
            return WanXiang.Campaign.NodeKind.Question;
        }

        /// <summary>问号是否已揭晓（读存档的 "offset:kind" 记录）。</summary>
        private bool IsRevealed(int offset)
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null || run.QuestionRevealed == null) return false;
            foreach (var rec in run.QuestionRevealed)
                if (rec != null && rec.StartsWith(offset + ":")) return true;
            return false;
        }

        /// <summary>可达 = 起点层，或"与当前节点有边相连"（真拓扑，杀戮尖塔式解锁）。</summary>
        private bool IsReachable(int offset)
        {
            if (_graph == null) return false;
            if (_currentOffset < 0) return _graph.LayerOf(offset) == 0;
            return _graph.HasEdge(_currentOffset, offset);
        }
    }
}
