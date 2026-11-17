// ============================================================================
//  万相 · 地图构建与落印/连线演出（2026-11-17 代码骨架重构）
//  从 CampaignPanel.cs 原样搬入，逻辑零改动。
//  ⚠ 含落印动画"压透明期推迟、恢复后补播"逻辑（用户 11-16 实测修复），勿动。
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
        private void BuildNodeMap(WanXiang.Campaign.ActGraph payloadGraph = null)
        {
            _dotColorCache.Clear();   // 节点重建 ⇒ 图标底色缓存作废（否则选中恢复会用到旧色）
            if (_scrollNodes == null || _nodeItemTemplate == null) return;
            var content = _scrollNodes.content;
            if (content == null) return;

            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var old = content.GetChild(i).gameObject;
                old.SetActive(false);      // Destroy 要到帧末才生效，先失活避免同帧新旧共存
                Destroy(old);
            }
            _nodeItems.Clear();

            // ★★ 首帧强制刷新 Canvas：第一次打开本面板时，viewport/content 的 rect 还没被
            //    布局系统算出来，此时用它们的尺寸排版会错位（用户实测："第一次进不行、
            //    返回再进来就成功了"）。这里先强制算一次。
            UnityEngine.Canvas.ForceUpdateCanvases();

            // ★ 本图的实际层数（天阙图 5 层、普通幕 12 层）—— 摆位/高度/自动定位都用它。
            //  ⚠ 注意：本方法内部才建 `_graph`（见下方 BuildRoute），所以这里先给兜底值，
            //    建图之后必须**重新计算**一次 lc —— 否则永远拿到兜底的 12（用户实测：
            //    第一次进天阙图 content 高 2480、多出 1440 空白区）。
            int lc = Layers;

            // ---- 数据源：v1.2 路线图（Layers 层、层内 2~3、种子稳定）----
            var run = WanXiang.Run.RunSave.Current;
            // ★★ 存档自愈：早期幕推进判据用过 `>=`，可能把 Act 反复推到上限、
            //   或让 NodeOffset 越界，导致存档与节点图错位（用户实测"全部存档不能推进"）。
            //   这里把越界值钳回合理范围，保证存档一定能继续玩。
            if (run != null)
            {
                if (run.Act < 1 || run.Act > 5)
                {
                    run.Act = Mathf.Clamp(run.Act, 1, 5);
                    run.NodeOffset = -1;
                    Debug.LogWarning("[Campaign] 存档自愈：Act 越界 → 钳到 " + run.Act);
                }
                if (run.NodeOffset >= Layers * WanXiang.Campaign.MapRules.Width)   // 格号上限 = 层数 × 每层格数
                {
                    Debug.LogWarning("[Campaign] 存档自愈：NodeOffset 越界(" + run.NodeOffset + ") → 回到本幕起点");
                    run.NodeOffset = -1;
                }
                WanXiang.Run.RunSave.SaveCurrent();
            }

            int act = Mathf.Clamp(run != null ? run.Act : 1, 1, 5);
            // ⚠ 种子绑定**本局**（RunSeed）而不是槽位：局内重进是同一张图，
            //   重开一局 / 新档 → 新种子 → 全新路线图（用户：每局都要随机）。
            if (run != null && run.RunSeed == 0)
            {
                run.RunSeed = UnityEngine.Random.Range(1, int.MaxValue);
                WanXiang.Run.RunSave.SaveCurrent();
            }
            ulong seed = CoreMath.Fnv1a("route:" + (run != null ? run.RunSeed : 0) + ":" + act);

            // ★★ 秘境注入点（唯一一处）：有外部图就用它，其余流程一字不改。
    //   秘境图与主线图**同为 ActGraph**，所以这一句就完成了"复用现成节点地图"。
      var injected = _realmGraphPayload;
        _realmGraphPayload = null;          // ⚠ 用完清掉：面板复用，别把旧图带进下一次正常打开

        // ★★ 自愈（2026-10-17 用户实测）：编队/战斗/灵市/孵穴…这些面板返回时都只写了
        //   `OpenAsync<CampaignPanel>()`，**不带秘境图** ⇒ 面板按主线重开，直接把人踢出秘境。
        //   靠每个调用方记得"顺手带上秘境图"迟早会漏（已经有五个面板了），
        //   所以改成在这里按存档自愈：InRealm 就重建秘境图。
if (injected == null && run != null && run.InRealm && !RealmTransition.IsPlaying)
       {
        var __rl = WanXiang.Campaign.RealmCatalog.For(run.RealmId);
        var __m = WanXiang.Campaign.RealmMapBuilder.Build(__rl, RealmSession.RealmSeedFor(run));
        if (__m != null && __m.Graph != null && !__m.Graph.IsEmpty)
        {
            injected = __m.Graph;
            Debug.Log("[Campaign] 自愈：存档在秘境（" + (__rl != null ? __rl.Name : run.RealmId) +
                      "）但调用方没带秘境图 —— 自动重建");
        }
       }

if (injected != null && !injected.IsEmpty)
       {
        _realmMode = true;
     _graph = injected;
    Debug.Log("[Campaign] 秘境模式：注入外部节点图（" + _graph.Layers.Length + " 层，"
       + _graph.NodeCount + " 格）—— 排布/连线/选中全走主线同一套");
      }
         else
        {
  _realmMode = false;
            _graph = WanXiang.Campaign.SolarTermGraph.BuildRoute(act, seed, Layers);
        }

            // ★ 瘴雾重掷过的节点类型：覆盖回去（节点图每次都由种子重建，不覆盖就被冲掉了）
            // ★★ 越境（2026-10-18 用户："直接传送到下一幕的节点，续劫时才出现"）：
            //   只有**续劫过**（Ascension > 0）的主线局才有 —— 第一周目必须完整走一遍。
            //   位置按下标确定（同一存档同一幕永远同一格），放在**中段**（约 65% 深度），
            //   且避开结构位：首层招募 / 次层遗物 / 幕末守关。
            ApplyActSkipNode(run);

            if (run != null && run.RerolledKinds != null && run.RerolledKinds.Count > 0 && _graph.Kinds != null)
            {
                foreach (var rec in run.RerolledKinds)
                {
                    if (string.IsNullOrEmpty(rec)) continue;
                    int ci = rec.IndexOf(':');
                    if (ci <= 0) continue;
                    int off, val;
                    if (int.TryParse(rec.Substring(0, ci), out off) &&
                        int.TryParse(rec.Substring(ci + 1), out val) &&
                        off >= 0 && off < _graph.Kinds.Length)
                    {
                        // ⛔ 结构位（守关/终局 = 幕末格）不许被旧记录改写：
                        //   RerolledKinds 是按**当时的图**落盘的，图一变层数/格数，
                        //   旧记录就可能正好落在现在的守关格上，把首领改成"?"（实测）。
                        if (off == _graph.NodeCount - 1)
                        {
                            Debug.LogWarning("[Campaign] 丢弃指向守关格的旧重掷记录：" + rec);
                            continue;
                        }
                        _graph.Kinds[off] = (WanXiang.Campaign.NodeKind)val;
                    }
                }
            }

            // ★ 本幕"全 ？ 层"：整层都是未知节点（四幕里 1~2 幕会有 1~2 层）， 走进去必定起瘴雾
            _forcedQLayers.Clear();
            {
                var fl = new System.Collections.Generic.List<int>();
                ForcedQuestionLayers(run, _graph, fl);
                foreach (var L in fl)
                {
                    _forcedQLayers.Add(L);
                    if (L < 0 || L >= _graph.Layers.Length) continue;
                    foreach (var off in _graph.Layers[L])
                        if (off >= 0 && off < _graph.Kinds.Length && off != _graph.NodeCount - 1)
                            _graph.Kinds[off] = WanXiang.Campaign.NodeKind.Question;
                }
                if (_forcedQLayers.Count > 0)
                    Debug.Log("[Campaign] 本幕「全 ？ 层」共 " + _forcedQLayers.Count + " 层（进这些层必起瘴雾）");
            }

            // ★ 本幕「瘴雾」节点生成（未知节点 20% + 天象按概率 + 全？层必定；见 EnsureFogNodesGenerated）
            EnsureFogNodesGenerated(run, _graph);

            // ★★ 建图后重算实际层数（上面那个 lc 是兜底值！）
            lc = (_graph != null && _graph.Layers != null) ? _graph.Layers.Length : Layers;

            // ★ 幕推进已改为"通过最后一格时"触发（见上方 PendingCommit 落地处），
            //   这里不再用"人在最后一格"当判据 —— 它既会误跳幕，又会让可达性锚点失效。

            // ★★ 存档自愈：NodeOffset 必须落在"本幕可继续"的范围内。
            //    停在幕末格（== NodeCount-1）多半是上一幕的残留位置（修复前产生的存档），
            //    它会同时导致"全图锁定灰"，这里一律回到本幕起点。
            // ⚠ 秘境模式**跳过**：自愈是按"主线的 NodeOffset"判的，而秘境根本不用那个字段。
    //   在这里拿秘境图（格数与主线不同）去钳主线偏移，会把主线进度莫名其妙重置。
            // ⛔ 只在**越界**时才重置！原先判 `>= NodeCount - 1`，把"正站在守关格上"的玩家
            //   也一并踹回本幕起点（守关格本身就是合法停靠位 —— 你就是要去打它）。
            // ★ 2026-11-14 修：这里原来用 _graph.NodeCount 判越界，但此刻 _graph 可能还是
            //   **上一幕（或 Act1 默认）的旧图**（39 节点），而当前幕有 51 节点 → 合法的
            //   NodeOffset=50 被误判越界、清回起点（用户实测"存档总是第 1 层"）。
            //   改成按"层数 × 每层格数"的幕内上限，不依赖尚未重建的图。
            // ★ 2026-11-15：用**真图 NodeCount** 钳存档（上一版按 层×宽 的粗上限，
            //   45 格的图却允许 49，SelectNode 越界早退 → 详情空白/按钮灰/点不动）。
            if (!_realmMode && run != null && _graph != null && run.NodeOffset >= _graph.NodeCount)
            {
                int safe = Mathf.Max(0, _graph.NodeCount - 2);
                Debug.LogWarning("[Campaign] 自愈：NodeOffset(" + run.NodeOffset + ") >= 真图 "
                    + _graph.NodeCount + " 格 → 钳到倒数第二格 " + safe + "（不清回起点）");
                run.NodeOffset = safe;
                _currentOffset = safe;
                WanXiang.Run.RunSave.SaveCurrent();
            }

      // ★ 秘境模式：进度从秘境自己的字段读（RealmCursor / RealmCleared），
    //   绝不用主线的 NodeOffset / VisitedNodes / PassedNodes。
      //   这不只是"洁癖"：主线那三个字段是存档，借用就意味着中途退出后主线位置停在秘境格。
      if (_realmMode)
     {
    _currentOffset = run != null ? run.RealmCursor : -1;
       _visited.Clear();
        _passed.Clear();
   if (run != null && run.RealmCleared != null)
    {
 foreach (var o in run.RealmCleared) { _visited.Add(o); _passed.Add(o); }
       }
     if (_currentOffset >= 0) { _visited.Add(_currentOffset); _passed.Add(_currentOffset); }
     }
   else
 {
            _currentOffset = run != null ? run.NodeOffset : -1;    _visited.Clear();          // readonly 字段只能就地清空（不能 new）
 if (run != null && run.VisitedNodes != null)
       foreach (var v in run.VisitedNodes) _visited.Add(v);
   }

    // ★ "真·通过"集合 = PassedNodes ∩ VisitedNodes。
            //   换幕/重开会把 VisitedNodes 清空 ， 这边自动跟着空（不必去每处清 PassedNodes）。
       //   ⚠ 秘境模式在上面的分支里已经填好 _passed 了，这整段只服务主线。
            if (!_realmMode)
      {
            _fogWeather.Clear();
            if (run != null && run.FogWeatherNodes != null)
           foreach (var o in run.FogWeatherNodes) _fogWeather.Add(o);

       _passed.Clear();
 if (run != null)
          {
  if (run.PassedNodes != null && run.PassedNodes.Count > 0)
        {
    foreach (var o in run.PassedNodes)
        if (_visited.Contains(o)) _passed.Add(o);
       }
          else if (_visited.Count > 0)
{
    // ★ 一次性迁移（老存档没有 PassedNodes 字段）：把"走过的"里**还能前往**的
         //   （= 从当前节点还走得到的，也就是"进去过但没通过"的那几格）剔掉 ， 保留合法的通过格。
             foreach (var v in _visited)
            if (!IsReachable(v)) _passed.Add(v);
     if (_currentOffset >= 0) _passed.Add(_currentOffset);   // 当前格本身就是"已通过到这一格"
        run.PassedNodes = new System.Collections.Generic.List<int>(_passed);
     WanXiang.Run.RunSave.SaveCurrent();
          }
       }
            }


        if (_tmpActTitle != null) _tmpActTitle.text = BuildActTitle();   // ★ 单一来源

            // ★ 秘境「离开」按钮：只在秘境里出现（主线地图上它没有意义，
            //   露出来反而会让玩家以为是"退出游戏"）。
            {
                var qb = transform.Find("Btn_RealmQuit");
                if (qb != null && qb.gameObject.activeSelf != _realmMode)
                    qb.gameObject.SetActive(_realmMode);
            }
            if (_tmpJie != null)
                _tmpJie.text = run != null ? run.RealmText : "第一境 · 第一劫";

            if (_graph.IsEmpty) return;

            // ⚠ 手动排布前必须关掉 content 上的自动布局：
            //   VerticalLayoutGroup / ContentSizeFitter 会按"里面的元素"自己算高度，
            //   把我们设的 sizeDelta 覆盖掉 —— 表现出来是滚动范围不对、下面的层滑不到。
            DisableAutoLayout(content);

            // ---- 连线层：先建（渲染在节点之下）----
            var lineLayer = new GameObject("LineLayer", typeof(RectTransform));
            var lineRt = (RectTransform)lineLayer.transform;
            lineRt.SetParent(content, false);
            lineRt.anchorMin = lineRt.anchorMax = new Vector2(0.5f, 1f);
            lineRt.pivot = new Vector2(0.5f, 1f);
            lineRt.anchoredPosition = Vector2.zero;
            lineRt.sizeDelta = Vector2.zero;

            // ---- 节点：**从下往上**（第 0 层在底部，守关在顶部）----
            for (int layer = 0; layer < _graph.Layers.Length; layer++)
            {
                var row = _graph.Layers[layer];
                // ★★ 用【图的真实层数】而不是常量 Layers(12)：
                //    天阙图只有 5 层，用 12 会把节点摆到很下面、content 也算出 2480 的空白区，
                //    表现就是"往上滑一片空白、还滑不回来"（用户实测）。
                float y = -(lc - 1 - layer) * (NodeH + GapY);   // 层号越大越靠上
                float nw = NodeWidthFor(row.Length);
                for (int k = 0; k < row.Length; k++)
                {
                    var item = SpawnNodeItem(content, row[k], layer);
                    item.sizeDelta = new Vector2(nw, NodeH);
                    item.anchorMin = item.anchorMax = new Vector2(0.5f, 1f);
                    item.pivot = new Vector2(0.5f, 1f);
                    float x = row.Length <= 1
                        ? 0f
                        : (k - (row.Length - 1) * 0.5f) * (nw + GapX);
                    item.anchoredPosition = new Vector2(x, y);
                }
            }

            DrawEdges(lineRt);

            float totalH = lc * (NodeH + GapY) + 80f;      // lc = 实际层数
            // ★★ content 高度必须 >= 视口高度，否则 ScrollRect 的滚动范围会算错，
            //    表现是"往上滑过头就回不来了 / 一片空白"（用户实测：天阙图只有 5 个节点时）。
            float viewH = (_scrollNodes != null && _scrollNodes.viewport != null)
                ? _scrollNodes.viewport.rect.height : 0f;
            content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(totalH, viewH + 1f));

            // 打开时滚到当前层（12 层比一屏高，别让玩家自己找）
            float curY = (_currentOffset >= 0)
                ? -(lc - 1 - _graph.LayerOf(_currentOffset)) * (NodeH + GapY)
                : -(lc - 1) * (NodeH + GapY);
            // ⚠ content.sizeDelta 刚改过，布局要等下一次 Canvas 更新才算完 ——
            //   不 ForceUpdate 的话这行设置会被后续布局覆盖，玩家只能自己往上滑（用户实测）。
            UnityEngine.Canvas.ForceUpdateCanvases();
            if (_scrollNodes != null && _scrollNodes.viewport != null)
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_scrollNodes.viewport);
            UnityEngine.Canvas.ForceUpdateCanvases();

            // ★ 定位到"当前/刚通过"那一层并**居中**（用户：摄像机要定位到我刚刚通过的节点）
            {
                float dist = Mathf.Abs(curY);
                float range = Mathf.Max(1f, Mathf.Max(totalH, viewH + 1f) - viewH);
                _scrollNodes.verticalNormalizedPosition = Mathf.Clamp01(1f - (dist - viewH * 0.5f) / range);
            }

            // ★ 只对**真正通过**的那个节点挂起动画（等回到地图可见时才播，见 PlayPendingAnim）。
            //   _justPassedNode 只在 OnOpenAsync 把 NodeOffset 落地那刻置位 ， "进编队又返回""事件没做完就返回"
            //   都不会误播（用户实锤）。这里再挂起，是为了避开"还在战斗/选择弹层里"的时机。
            if (_justPassedNode >= 0)
            {
                _pendingNodes.Clear();
                _pendingNodes.Add(_justPassedNode);
                _pendingAnim = true;
            }
        }

        /// <summary>关掉 content 上的自动布局组件（手动排布的前提）。</summary>
        private static void DisableAutoLayout(RectTransform content)
        {
            // ★★ 修正视口 —— prefab 里 Viewport 的高度是 **负数**（-350），
            //    会导致 ScrollRect 的滚动范围完全算错：滑过头回不来、滚轮无效（用户实测）。
            //    负尺寸 = 布局炸裂（和 Dialog 的负 sizeDelta 同一个病）。
            // 本方法是静态的：用 content 的父级（ScrollRect 标准层级里就是 Viewport）取视口，
            // 不依赖 _scrollNodes 实例字段。
            var vp = content != null ? content.parent as RectTransform : null;
            if (vp != null && vp.rect.height <= 1f)
            {
                vp.offsetMin = Vector2.zero;
                vp.offsetMax = Vector2.zero;
                UnityEngine.Debug.LogWarning("[Campaign] 节点图视口高度异常(" + vp.rect.height +
                                             ") → 已铺满修正（否则滚动范围算错：滑过头回不来）");
            }

            var vlg = content.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            if (vlg != null) vlg.enabled = false;
            var hlg = content.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            if (hlg != null) hlg.enabled = false;
            var fitter = content.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
        }

        /// <summary>层间连线。走过的边描金，其余淡墨 —— 一眼看出自己的路线。</summary>
        private void DrawEdges(RectTransform lineLayer)
        {
            if (_graph == null) return;
            _edgeItems.Clear(); _edgeA.Clear(); _edgeB.Clear();

            for (int layer = 0; layer + 1 < _graph.Layers.Length; layer++)
            {
                var from = _graph.Layers[layer];
                var to = _graph.Layers[layer + 1];
                foreach (var a in from)
                {
                    // 按真拓扑边表画：图上看到的连线 = 真正能走的路（所见即所得）
                    if (_graph.Edges == null || a >= _graph.Edges.Count) continue;
                    foreach (var b in _graph.Edges[a])
                    {
                    Vector2 pa = NodePos(a);
                    Vector2 pb = NodePos(b);
                    bool walked = _passed.Contains(a) && _passed.Contains(b);   // ★ 描金实线 = 两端都"真·通过"
                    bool foggedEdge = InFog(a) || InFog(b);                     // ★ 瘴雾：雾里的边画得极淡

                    var go = new GameObject("Edge_" + a + "_" + b, typeof(RectTransform));
                    var rt = (RectTransform)go.transform;
                    rt.SetParent(lineLayer, false);
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                    rt.pivot = new Vector2(0f, 0.5f);
                    // 虚/实：未走通 = 虚线（Tiled 平铺；高度须与 line_dash 贴图一致=14，否则会被裁）；走通 = 实线
                    rt.sizeDelta = new Vector2(Vector2.Distance(pa, pb), 14f);
                    rt.anchoredPosition = pa;
                    float ang = Mathf.Atan2(pb.y - pa.y, pb.x - pa.x) * Mathf.Rad2Deg;
                    rt.localRotation = Quaternion.Euler(0f, 0f, ang);

                    var img = go.AddComponent<Image>();
                    var sa = SeasonAccent;
                    if (walked)
                    {
                        var seLine = SeasonLineSpriteFor(SeasonIdx);         // ★ 本幕季节笔触（实线）
                        img.sprite = seLine != null ? seLine : LineSprite();
                        img.type = Image.Type.Simple;
                        img.color = new Color(sa.r, sa.g, sa.b, 1f);
                    }
                    else
                    {
                        img.sprite = DashSprite();                           // 虚线笔触
                        img.type = Image.Type.Tiled;                         // 沿线平铺 ， 虚
                        img.color = new Color(sa.r, sa.g, sa.b, 0.5f);
                    }
                    img.raycastTarget = false;
                    if (foggedEdge) { var fc = img.color; fc.a = 0.10f; img.color = fc; }   // ★ 雾里的边几乎看不见
                    // 记录边（供"回到地图后"播画线动画用）
                    _edgeItems.Add(rt); _edgeA.Add(a); _edgeB.Add(b);
                    // ★ 本次新走通的边先藏起来，等 PlayPendingAnim 再"从虚线变实线"
                    if (walked && _justPassedNode >= 0 && (a == _justPassedNode || b == _justPassedNode))
                    { var cc = img.color; cc.a = 0f; img.color = cc; }
                    }
                }
            }
        }

/// <summary>该节点属于秘境第几层（层号+1：第 0 层是入口，对玩家显示为「第1层」）。</summary>

        /// <summary>
        /// 击败「镇守」的专属奖励（2026-10-16 用户："这些敌人的攻击设计很有意思，
        /// 打死了会有特殊奖励等等，这些在外面是没有的"）。
        /// <para>奖励做成**灵魂 + 星币**的组合：灵魂是秘境最独特的产出（外面只能用灵卵买），
        /// 星币则直接强化"多走一层就多赚一点"的正反馈。</para>
        /// <para>⛔ 幂等：同一个 offset 只发一次（重进面板会重跑这段，不判会无限刷）。</para>
        /// </summary>

        /// <summary>节点中心的本地坐标（与 BuildNodeMap 的排布公式必须一致）。</summary>
        private Vector2 NodePos(int offset)
        {
            int layer = _graph.LayerOf(offset);
            if (layer < 0) return Vector2.zero;
            var row = _graph.Layers[layer];
            int k = System.Array.IndexOf(row, offset);
            float x = row.Length <= 1 ? 0f : (k - (row.Length - 1) * 0.5f) * (NodeW + GapX);
            int lc2 = (_graph != null && _graph.Layers != null) ? _graph.Layers.Length : Layers;
            float y = -(lc2 - 1 - layer) * (NodeH + GapY) - NodeH * 0.5f;
            return new Vector2(x, y);
        }

        private static Sprite _white;
        private static Sprite WhiteSprite()
        {
            if (_white != null) return _white;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _white = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
            return _white;
        }

        private static Sprite _lineSprite;
        private static bool _lineTried;
        /// <summary>连线美术：笔触墨线（Resources/UI/Campaign/map_edge_ink）。缺图回退白 sprite，保证功能不受影响。</summary>
        private static Sprite LineSprite()
        {
            if (_lineTried) return _lineSprite;
            _lineTried = true;
            _lineSprite = UnityEngine.Resources.Load<Sprite>("UI/Campaign/map_edge_ink");
            if (_lineSprite == null) _lineSprite = WhiteSprite();
            return _lineSprite;
        }

        private static Sprite _stamp; private static bool _stampTried;
        /// <summary>朱砂「✕」印章（Resources/UI/Campaign/x_stamp）。缺图 = 不显示（不报错）。</summary>
        private static Sprite StampSprite()
        {
            if (_stampTried) return _stamp;
            _stampTried = true;
            _stamp = UnityEngine.Resources.Load<Sprite>("UI/Campaign/x_stamp");
            return _stamp;
        }

        private static Sprite _dash; private static bool _dashTried;
        /// <summary>虚线笔触（Resources/UI/Campaign/line_dash，可平铺；Image.type 必须 Tiled）。</summary>
        private static Sprite DashSprite()
        {
            if (_dashTried) return _dash;
            _dashTried = true;
            _dash = UnityEngine.Resources.Load<Sprite>("UI/Campaign/line_dash");
            if (_dash == null) _dash = WhiteSprite();
            return _dash;
        }

        /// <summary>落印：先等面板稳定，再从"很大"加速缩到默认 + 过冲回正 + 淡入（"啪"地盖上去，给视觉冲击）。</summary>
        private System.Collections.IEnumerator StampDropAnim(RectTransform rt, Image img)
        {
            if (rt == null || img == null) yield break;
            // 时长放缓（用户：动画可以慢一点）：0.30s → 0.55s
            float dur = 0.55f, e = 0f;
            rt.localScale = Vector3.one * 2.6f;                 // ★ 从很大开始 ， 缩放冲击明显
            rt.localRotation = Quaternion.Euler(0f, 0f, -18f);
            var c0 = img.color; c0.a = 0f; img.color = c0;
            while (e < dur)
            {
                if (rt == null || img == null) yield break;
                e += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(e / dur);
                float kk = k * k * (3f - 2f * k);                    // smoothstep：落下更柔、不突兀
                rt.localScale = Vector3.one * Mathf.Lerp(2.6f, 1f, kk);
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-18f, -2f, kk));
                var c = img.color; c.a = Mathf.Lerp(0f, 0.95f, Mathf.Min(1f, k * 1.5f)); img.color = c;
                yield return null;
            }
            if (rt != null) { rt.localScale = Vector3.one; rt.localRotation = Quaternion.Euler(0f, 0f, -2f); }
            if (img != null) { var c = img.color; c.a = 0.95f; img.color = c; }
        }

        /// <summary>画线：新走通的边从左端"长"出来 + 淡入（配合节点落印 ， "这条路刚打通"）。</summary>
        private System.Collections.IEnumerator LineDrawAnim(RectTransform rt, Image img)
        {
            if (rt == null || img == null) yield break;
            float dur = 0.9f, e = 0f;                          // 放慢（用户：动画可以慢一点；0.5 → 0.9）
            var c0 = img.color; c0.a = 0f; img.color = c0;
            rt.localScale = new Vector3(0.04f, 1f, 1f);
            while (e < dur)
            {
                if (rt == null || img == null) yield break;
                e += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(e / dur);
                rt.localScale = new Vector3(Mathf.Lerp(0.04f, 1f, k), 1f, 1f);
                var c = img.color; c.a = Mathf.Lerp(0f, 1f, Mathf.Min(1f, k * 2f)); img.color = c;
                yield return null;
            }
            if (rt != null) rt.localScale = Vector3.one;
            if (img != null) { var c = img.color; c.a = 1f; img.color = c; }
        }

        // ------------------------------------------------------------------
        //  "新通过"动画：只在**真正回到地图、看到画面**时才播
        //  （否则玩家还在战斗/选择弹层里，动画就在背后播完了）
        // ------------------------------------------------------------------
        protected override void OnOpened()
        {
            base.OnOpened();
            if (_ambientFx != null) _ambientFx.Running = true;   // 粒子只在面板可见时驱动
            if (_pendingAnim) StartCoroutine(PlayPendingAnimAfter(0.50f));
        }
        protected override void OnResume()
        {
            base.OnResume();
            if (_ambientFx != null) _ambientFx.Running = true;

            // ★ 地图常开：子面板（含裸 CloseSelf）关闭后 UISystem 都会 Resume 地图。
            //   ⛔ 不能只判"有没有 PendingCommit 待办"——弹窗类完成时数据当场落地，
            //   Resume 时已是 -1，判断会跳过重建（上一轮"要点两次才解锁"就是这么来的）。
            //   ⇒ **无条件落地 + 全量重建**（BuildNodeMap 本来就是开图必跑的重建，代价可接受）。
            // ★ 2026-11-15：奖励面板关闭时地图会 Resume，这里原来直接落地 → 与 OnRefresh 重复推进一幕。
            //   统一交给 DeferredCommitAsync（等奖励真正关闭、只跑一次）。
            if (ResultPanelIsOpen()) ScheduleDeferredCommit();
            else { LandPendingCommit(); BuildNodeMap(); ApplyViewAfterGraphChange(silentSelect: true); }
            if (_pendingAnim) StartCoroutine(PlayPendingAnimAfter(0.30f));   // 被上层弹层（如开场）盖过再露出 ， 这时才播
        }

        // ================================================================
        //  按幕背景 + 飘落粒子（2026-10-11）
        //  ------------------------------------------------------------------
        //  进图与换幕都会经过 OnOpenAsync ， 那里调 RefreshAmbient()，
        //  背景图与粒子就自动跟着幕变（春/夏/长夏/秋/冬）。
        // ================================================================
        private void RefreshAmbient()
        {
            if (_ambientFx == null) _ambientFx = GetComponent<CampaignAmbientFx>();
            if (_ambientFx == null) _ambientFx = gameObject.AddComponent<CampaignAmbientFx>();
            var run = WanXiang.Run.RunSave.Current;

            // ★ 秘境：换场域专属背景 + 粒子（2026-10-16 用户要求）。
            //   ⛔ 必须在主线分支**之前**判断，否则秘境会先被染上主线当幕的背景与落叶。
            if (_realmMode)
            {
                var realm = WanXiang.Campaign.RealmCatalog.For(run != null ? run.RealmId : null);
                _ambientFx.BuildRealm(realm, _imgMapBg, _rootMapParticles);
                return;
            }

            int act = run != null ? run.Act : 1;
            _ambientFx.Build(act, _imgMapBg, _rootMapParticles);
        }
        private System.Collections.IEnumerator PlayPendingAnimAfter(float d)
        {
            float e = 0f;
            while (e < d) { e += Time.unscaledDeltaTime; yield return null; }
            PlayPendingAnim();
        }
        /// <summary>播"新通过"的落印 + 画线（仅在面板可见且未被遮挡时；否则留到 OnResume）。</summary>
        private void PlayPendingAnim()
        {
            if (!_pendingAnim) return;
            if (State == UIPanelState.Paused) return;   // 还被弹层盖着 ， 等 OnResume
            // ★ 2026-11-16 用户："节点打X的动画没了，就感觉卡了一下/闪了一下"
            //   根因：转场期间地图被 HideMapForTransition 压成 alpha=0，
            //   而这段协程照常播放 ⇒ 动画在不可见时播完，恢复后只剩最终态。
            //   地图不可见就推迟（_pendingAnim 保持 true），由 ShowMapAfterTransition 补播。
            if (_transitionMapCg != null && _transitionMapCg.alpha < 0.5f)
            {
                Debug.Log("[Campaign] 落印动画推迟：地图仍被转场遮住");
                return;
            }
            _pendingAnim = false;
            _justPassedNode = -1;                        // 播完就消费掉（后续重绘不再重播）

            // 音效：落印 + 墨线延伸（与下面两个循环的顺序一致 —— 先盖章，再画线）。
            // 用节流播：一次通过可能带多个印章 / 多条边，不能每个都响一遍。
            SfxThrottled("sfx_map_seal", 0.9f);
            SfxThrottled("sfx_map_path_draw", 0.6f);

            for (int i = 0; i < _nodeItems.Count; i++)
            {
                var it = _nodeItems[i];
                if (it == null) continue;
                if (!_pendingNodes.Contains(OffsetOfName(it.name))) continue;
                var st = it.Find("Img_Stamp") as RectTransform;
                if (st == null || !st.gameObject.activeSelf) continue;
                var im = st.GetComponent<Image>();
                if (im != null) StartCoroutine(StampDropAnim(st, im));
            }
            for (int i = 0; i < _edgeItems.Count; i++)
            {
                var rt = _edgeItems[i];
                if (rt == null || !rt.gameObject.activeSelf) continue;
                if (!(_pendingNodes.Contains(_edgeA[i]) || _pendingNodes.Contains(_edgeB[i]))) continue;
                var im = rt.GetComponent<Image>();
                if (im != null) StartCoroutine(LineDrawAnim(rt, im));
            }
        }
    }
}
