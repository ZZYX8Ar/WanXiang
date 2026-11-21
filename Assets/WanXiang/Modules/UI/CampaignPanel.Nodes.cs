// ============================================================================
//  万相 · 节点流程（事件/工坊/出征）（2026-11-17 代码骨架重构）
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
        /// <summary>R5-c：异闻节点（事件按 seed 抽取 + 去重）。由 NodeRouter 注册调用。</summary>
        private void RunTaleNode(WanXiang.Run.RunState run)
        {
                // 前段规则「前路尚稳」：本段事件额外得 1 灵卵（表驱动 ruleId）
                var taleRun = WanXiang.Run.RunSave.Current;
                if (taleRun != null && taleRun.ActiveFloorRule == "calm_egg")
                {
                    taleRun.Eggs += 1;
                    WanXiang.Run.RunSave.SaveCurrent();
                }
                // ★ 2026-11-01「不期而遇」：Tale 节点改为**事件节点**（复用 Panel_Tale）。
                //   事件按 seed+offset 固定抽取，同一局同一格永远是同一个事件（可复盘）。
                EnsureEventsLoadedFromCsv();
                var prun2 = WanXiang.Run.RunSave.Current;
                ulong evSeed = WanXiang.Battle.Core.CoreMath.Fnv1a(
                    "evt:" + (prun2 != null ? prun2.RunSeed : 0) + ":" + (prun2 != null ? prun2.NodeOffset : 0));
                var pool = WanXiang.Campaign.EventCatalog.All;
                WanXiang.Campaign.EventDef ev = null;
                if (pool != null && pool.Length > 0)
                {
                    // ★ 2026-11-02：同一局不重复 —— 从种子位置往后找第一个没出现过的
                    int start = (int)(evSeed % (ulong)pool.Length);
                    for (int k = 0; k < pool.Length; k++)
                    {
                        var cand = pool[(start + k) % pool.Length];
                        if (cand == null) continue;
                        if (prun2 != null && prun2.SeenEvents != null && prun2.SeenEvents.Contains(cand.Id)) continue;
                        ev = cand;
                        break;
                    }
                    if (ev == null) ev = pool[start];      // 全见过了就允许重复
                    if (prun2 != null && ev != null)
                    {
                        if (prun2.SeenEvents == null) prun2.SeenEvents = new System.Collections.Generic.List<string>();
                        prun2.SeenEvents.Add(ev.Id);
                    }
                }
                if (ev != null)
                {
                    Debug.Log("[Campaign] 不期而遇 → " + ev.Title);
                    TalePanel.OpenEvent(ev, null);
                }
                else
                {
                    OpenPanelAsync<TalePanel>().Forget();   // 兜底：数据缺失时走旧异闻
                }
        }

        /// <summary>R5-c：工坊节点（三选一事件构造）。由 NodeRouter 注册调用。</summary>
        private void RunForgeNode(WanXiang.Run.RunState run)
        {
                // ★ 2026-11-02 工坊改为「专精 / 进化」三选一：复用事件面板，不新建 prefab。
                //   （旧 ForgePanel 是"融合两只异兽"，与新的局内养成不是一回事。）
                var _runM = WanXiang.Run.RunSave.Current;
                int _mat = _runM != null ? _runM.Materials : 0;
                var forge = new WanXiang.Campaign.EventDef
                {
                    Id = "ev_node_forge",
                    Title = "铸工坊",
                    Story = "炉火正旺。老匠人问：你想让谁变强一点？",
                    Choices = new[]
                    {
                        new WanXiang.Campaign.EventChoice
                        {
                            Text = "淬炼（随机一只上阵异兽的随机技能 专精 +1）",
                            Effect = WanXiang.Campaign.EventEffect.GrantMastery, Value = 1,
                        },
                        new WanXiang.Campaign.EventChoice
                        {
                            Text = "化形（消耗 1 进化素材，现有 " + _mat + "；随机一只未进化的异兽进化）",
                            Effect = WanXiang.Campaign.EventEffect.Evolve, Value = 0,
                        },
                        new WanXiang.Campaign.EventChoice
                        {
                            Text = "只取炉灰（+40 灵卵）",
                            Effect = WanXiang.Campaign.EventEffect.GainEggs, Value = 40,
                        },
                    },
                };
                TalePanel.OpenEvent(forge, null);
        }



        private void FillTeamPreview()
        {
            if (_imgAvatars == null) return;
            var all = _contentCatalog != null ? ContentLibrary.BuildBeasts(_contentCatalog) : null;
            for (int i = 0; i < _imgAvatars.Length; i++)
            {
                if (_imgAvatars[i] == null) continue;
                var sprite = (all != null && i < all.Length && _sprites != null)
                    ? _sprites.GetHead(all[i].Id) : null;
                if (sprite != null)
                {
                    _imgAvatars[i].sprite = sprite;
                    _imgAvatars[i].color = Color.white;
                    _imgAvatars[i].preserveAspect = true;
                }
            }
        }

        /// <summary>
        /// 「越境」确认 + 推进（2026-10-18）。用户："方便玩家快速续劫、快速通过"。
        /// <para>⛔ 推进逻辑必须与"通过守关"那条**逐字一致**（换幕要清 5 类按下标存的记录），
        ///   否则会留下上一幕的 VisitedNodes / 重掷记录 / 瘴雾状态污染新幕。</para>
        /// </summary>
        private void ConfirmActSkip()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null || _graph == null) return;
            if (run.Act >= 5) { Debug.Log("[Campaign] 已在最后一幕，越境无效"); return; }
            int cur = run.Act;
            ConfirmActSkipAsync(cur).Forget();
        }

        private async UniTaskVoid ConfirmActSkipAsync(int curAct)
        {
            bool ok = await DialogPanel.Confirm(
                "越境 · 直入第 " + (curAct + 1) + " 幕",
                "跳过本幕剩余节点（含守关首领与它的奖励），直接进入下一幕。\n\n" +
                "本幕已走过的节点收益保留。越境不消耗灵卵。");
            if (!ok) return;

            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;
            if (run.Act != curAct) return;                 // 确认期间局面变了：放弃

            run.Act++;
            WanXiang.Modules.UI.InspirationBonus.GrantActEggs(run);   // ★ 2026-11-20 广陌
            run.NodeOffset = -1;
            if (run.VisitedNodes != null) run.VisitedNodes.Clear();
            if (run.PassedNodes != null) run.PassedNodes.Clear();
            if (run.RerolledKinds != null) run.RerolledKinds.Clear();
            if (run.FogWeatherNodes != null) run.FogWeatherNodes.Clear();
            run.FogGenAct = 0;
            run.FogTriggerNode = -1;
            run.FogLeft = 0; run.FogRerolled = false;
            WanXiang.Run.RunSave.SaveCurrent();
            Debug.Log("[Campaign] 越境：第 " + curAct + " 幕 → 第 " + run.Act + " 幕");

            RebuildGraphFor(run.Act, run.RunSeed);
            _currentOffset = -1;
            _visited.Clear();
            BuildNodeMap();
            SelectNode(0, true);
        }

        private void OnNextClicked()
        {
            if (_selected < 0 || _graph == null)
            {
                // ★ 2026-11-21 实机玩发现：只有Console日志，界面没反应，新手会以为点坏了
                Dialog.Tip("先选节点", "点地图上带「可前往」标记的节点，再点出征。").Forget();
                return;
            }
            if (!IsReachable(_selected))
            {
                Dialog.Tip("走不了这里", "只能走相邻下一层的节点。\n地图上「可前往」的格子才可选。").Forget();
                return;
            }

            // ★★ 同层互斥（2026-10-18 用户定案，2026-10-22 修正口径）：
            //   "要我真的点右下角『前往』才互斥" —— 点击=预览不算数；
            //   判定依据改成 **本层是否已经有别的节点真的通过**（_passed），
            //   而不是 ChosenNodes（那是"点过就算"的旧账，会把上一局的残留带进来，
            //   连"当前这一格"都被当成已占，导致点『前往』永远被拦 —— 用户 2026-10-22 实测）。
            if (!_realmMode)
            {
                int myLayer = -1;
                for (int L = 0; L < _graph.Layers.Length && myLayer < 0; L++)
                    if (System.Array.IndexOf(_graph.Layers[L], _selected) >= 0) myLayer = L;
                if (myLayer >= 0)
                {
                    foreach (var off in _graph.Layers[myLayer])
                    {
                        if (off == _selected) continue;
                        if (off == _currentOffset) continue;      // 当前所在格不算"占用了本层"
                        if (_passed.Contains(off))
                        {
                            Debug.Log("[Campaign] 本层已经通过过其他节点（同层互斥），不能再选。");
                            return;
                        }
                    }
                }
            }

            var kind = _graph.KindOf(_selected);
            SfxThrottled("sfx_map_node_confirm");   // 确认前往（上面两处提前 return 的错误分支不会响）

            // 进度落盘：走到哪一节记在存档里，下次打开还在这一节
            var run = WanXiang.Run.RunSave.Current;
            if (run != null)
            {
                run.Act = _graph.Act;

                // ★★ 「访问过」必须在**进入节点时**就记，不能跟着"通过"一起延后！
                //    节点图的可达性依赖 VisitedNodes——之前把这段一起延后了，导致它永远是空的，
                //    节点图认为"哪儿都没去过" ， 只有第 0 层可达 ， **点不动任何节点**（用户实测）。
                //    「访问」与「通过」是两件事：进入即访问，胜利/处理完才算通过。
                if (run.VisitedNodes != null && !run.VisitedNodes.Contains(_selected))
                    run.VisitedNodes.Add(_selected);
                WanXiang.Run.RunSave.SaveCurrent();
                // ★ 事件类节点（灵市/孵穴/铸魂台/异闻/天象）**延后推进**：
                //   点进去就写 NodeOffset 的话，玩家还没处理完就关游戏，再进来节点已通过 ， 奖励丢失。
                //   改为"离开事件面板、回到节点图时"才落地（见 OnOpenAsync 的 PendingCommit）。
                //   战斗类保持立即推进（打完/撤退都会回到节点图，语义一致）。
                // ★ **所有**节点都延后推进：点节点只是"进入"，真正通过要等
                //   ① 事件类处理完回地图（OnOpenAsync 落地）
                //   ② 战斗类点了「出征」（FormationPanel 写入）
                //   —— 否则"进编阵看一眼再返回"会被算作通过（用户实测：白嫖节点）。
                PendingCommit = _selected;

                // ⛔ 2026-10-22：不再在点击时写 ChosenNodes（那就是"一点就锁死"的来源）。
                //   真正"进入本格"的记账交给出征/事件确认那一刻；这里只做选中与预览。

                // ★★ NodeOffset 只表示"已经通过到哪一节"（可达性锚点 + 进度），
                //    这里**不能**写它 —— 否则"点开战斗节点、再点返回地图"也被算作通过
                //    （用户实测："这个战斗节点没有打，点击返回地图却通过了"）。
                //    真正推进在通过时落地：见 OnOpenAsync 里对 PendingCommit 的处理。
                if (run.VisitedNodes != null && !run.VisitedNodes.Contains(_selected))
                    run.VisitedNodes.Add(_selected);
                if (run.Path != null) run.Path.Add(_selected);
                // ★ 注意：星移余气（Lingers）的「-1」不再在此处扣减！
                //   出征只是「进入节点」，战斗/事件尚未结算；若此刻就扣，玩家进了编队又返回
                //   会被当成「已通过」，导致星移无法撤销（用户实测）。余气扣减统一推迟到
                //   节点真正通过时（见 OnOpenAsync 里对 PendingCommit 的落地）。
                WanXiang.Run.RunSave.SaveCurrent();

                // ★ 瘴雾天时：走到挂了「瘴雾」的节点 ， **当场起雾**（跟真的一样：节点卡上就标着「·瘴雾」）
                if (run.FogWeatherNodes != null && run.FogWeatherNodes.Contains(_selected))
                {
                    run.FogWeatherNodes.Remove(_selected);
                    if (run.FogLeft > 0)
                    {
                        // ★ 已经在雾里 ， **不叠加、不续期**（用户定案：不该出现叠加）
                        Debug.Log("[Campaign] 已在瘴雾中 ， 这一格不再起雾（不叠加）");
                    }
                    else
                    {
                        run.FogTriggerNode = _selected;   // ★ 记下"起雾那一格"（它自己也显示成「？？迷雾」）
                        TriggerFog(3);
                    }
                    WanXiang.Run.RunSave.SaveCurrent();
                }
            }

            // ---- 问号节点：走上去这一刻揭晓（v1.2 核心）----
            // 揭晓池（用户定案 2026-09-26）：纳入 普通遭遇 + 精英 —— ？也要能开出战斗。
            //   休整类权重高（惊喜为主）、战斗类权重低（惩罚为辅）：精英只占 1/12≈8%，
            //   避免 ？变成「常遇精英战」。揭晓结果写存档（QuestionRevealed），
            //   否则玩家靠「退出重进」无限重抽（这类机制的头号坑）。
            if (kind == WanXiang.Campaign.NodeKind.Question)
            {
                var weighted = new System.Collections.Generic.List<WanXiang.Campaign.NodeKind>();
                System.Action<WanXiang.Campaign.NodeKind, int> addW = (k, w) =>
                    { for (int i = 0; i < w; i++) weighted.Add(k); };
                // ★ 2026-11-07：**移除「天象（Omen）」**（设计 v2 §5.1：与异闻语义重叠，已并入异闻）。
                //   问号池现在只出新 8 种（作战/精英/休整/工坊/市集/异闻/秘境 + 后面的固定位）。
                addW(WanXiang.Campaign.NodeKind.Shop, 2);      // 市集
                addW(WanXiang.Campaign.NodeKind.Nest, 3);      // 休整（惊喜为主）
                addW(WanXiang.Campaign.NodeKind.Tale, 3);      // 异闻（原天象的权重并到这里）
                addW(WanXiang.Campaign.NodeKind.Forge, 2);     // 工坊
                addW(WanXiang.Campaign.NodeKind.Encounter, 2); // 作战
                addW(WanXiang.Campaign.NodeKind.Elite, 1);     // 精英（惩罚为辅）
                // ★ 秘境（2026-10-16）：问号也能开出秘境，但**低频**（权重 1）——
                //   路线图上本来就有专门的【秘境】节点（NodeKind.Realm），问号只是另一条路。
                //   第 1 幕不投（玩家没学会格挡，进秘境是纯挫败 —— 设计文档 5.3）。
                if (run != null && run.Act >= 2 && RealmSession.EntriesLeft(run) > 0)
                    addW(WanXiang.Campaign.NodeKind.Realm, 1);
                var rng = new WanXiang.Battle.Core.DeterministicRandom(
                    CoreMath.Fnv1a("reveal:" + (run != null ? run.RunSeed : 0) + ":" + _selected));
                var revealed = weighted[rng.NextInt(0, weighted.Count)];
                if (run != null)
                {
                    if (run.QuestionRevealed == null) run.QuestionRevealed = new System.Collections.Generic.List<string>();
                    run.QuestionRevealed.Add(_selected + ":" + (int)revealed);
                    WanXiang.Run.RunSave.SaveCurrent();
                }
                kind = revealed;
                // ★ 揭晓成战斗类（遭遇/精英）时，必须把真实 kind 写回 _current，
                //   否则 FormationPanel 仍按 Question 建敌 ， 精英缩放失效、战斗甚至不触发。
                //   （图里的原始 kind 仍是 Question，揭晓结果只存在 QuestionRevealed 存档里，
                //     所以这里必须显式用揭晓后的值覆盖。）
                _current.Kind = kind;
                // ★ 不再弹揭晓弹窗（用户要求）：这个 Dialog 在实战里关不掉，索性不弹。
                //   揭晓结果通过节点图本身呈现——该节点会按 RevealedKind 显示成真实类型（见 RefreshNodes）。
                Debug.Log("[Campaign] ？节点揭晓 → " + WanXiang.Campaign.NodeKinds.Cn(revealed));
            }

            // ★ 2026-10-19 用户："打开这个界面后面是主界面不是节点底图"（招募/遗物/异闻…）
            //   ⇒ **非战斗节点不关地图**：子面板打开在后，同层天然盖在地图上；
            //   战斗/问号仍关（要切场景/进全屏编队）。
            bool _battleKind = WanXiang.Campaign.NodeKinds.IsBattle(kind)
                            || kind == WanXiang.Campaign.NodeKind.Question;
            if (_battleKind) CloseSelf();

            // 非战斗节点直接路由到已有面板 —— 面板本身承担该节点的玩法
            // ★ R5-b（2026-11-08）：简单节点交给注册表；只有含业务逻辑的 Tale/Forge 还走 switch。
            EnsureNodeRoutes();
            if (_nodeRouter.TryRoute(kind, run)) return;
            // R5-c：全部节点路由已收进 _nodeRouter；未注册的 kind 默认走战斗。

            // 战斗节点 → 布阵
            OpenPanelAsync<FormationPanel>(_current).Forget();
        }

        /// <summary>
        /// 开秘境面板（独立的 <see cref="NodeKind.Realm"/> 节点 / 问号揭晓成秘境都走这里）。
        /// ⚠ 条件与揭晓口径一致：第 2 幕起 + 本局还有次数。
        /// ⚠ 进秘境**不算**本节点通过：收益走星币/灵魂，不该同时吃节点通过奖励。
        /// </summary>
        // ================================================================
        //  2026-10-20 新增养成节点（用户："其他节点太一般，没有养成升级的感觉"）
        //  统一用 DialogPanel 二选一，不用新面板 —— 文案即规则，零新 UI。
        // ================================================================

        /// <summary>灵泉：饮泉（本局永久 +5% 全属性）／汲水（灵卵 +3）。
        ///  B4-3：幕 1 且未持一印时，首选项变为「饮下泉水」—— 印记一的授予点（用资源换结局线）。</summary>

        /// <summary>修行：磨砺剑心（本局永久 +8% 全属性）／敛财（灵卵 +4）。</summary>

        /// <summary>古战场：三选一遗物，代价是下一场敌人 +15%。</summary>


    }
}
