// ============================================================================
//  万相 · 灵感 / 天气 / 事件数据（2026-11-17 代码骨架重构）
//  从 CampaignPanel.cs 原样搬入，逻辑零改动。
// ============================================================================
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Battle.Core;
using WanXiang.Battle.Presentation;
using WanXiang.Framework.UI;
using WanXiang.Fusion;

namespace WanXiang.Modules.UI
{
    public sealed partial class CampaignPanel
    {
        /// <summary>五行 → 中文（用于天时标题里标注属性）。</summary>
        private static string ElementCn(WanXiang.Battle.Core.Element e)
        {
            switch (e)
            {
                case WanXiang.Battle.Core.Element.Wood: return "木";
                case WanXiang.Battle.Core.Element.Fire: return "火";
                case WanXiang.Battle.Core.Element.Earth: return "土";
                case WanXiang.Battle.Core.Element.Metal: return "金";
                case WanXiang.Battle.Core.Element.Water: return "水";
                default: return "";
            }
        }

        /// <summary>
        /// 选节点时合成「本场真实天时」：节点天时 + 重放 run.Path 还原的上一幕余气，
        /// 结果存进 <see cref="WanXiang.Campaign.LiveWeather.Current"/>（非存档容器，工厂读取）。
        /// 重放用与活链路**完全相同**的 <c>BuildRoute(act, Fnv1a("route:"+RunSeed+":"+act), Layers)</c>，
        /// 保证天气/余气与玩家实际走过的图逐位一致（可背版、可复盘）。
        /// <para>可复现性红线：算不出（run 空 / offset 非法）→ 存 null，战斗退回旧行为。</para>
        /// </summary>
        private void ComposeLiveWeatherFor(int offset)
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null || offset < 0) { WanXiang.Campaign.LiveWeather.Current = null; return; }

            // 五幕图：与 RebuildGraphFor 同一公式重建（act≥5 走 BuildFinale 固定图）。
            var acts = new WanXiang.Campaign.ActGraph[5];
            for (int a = 1; a <= 5; a++)
                acts[a - 1] = WanXiang.Campaign.SolarTermGraph.BuildRoute(
                    a, CoreMath.Fnv1a("route:" + run.RunSeed + ":" + a), Layers);

            var rs = new WanXiang.Campaign.RunState(acts, WanXiang.Battle.Core.WeatherCatalog.GetSolarTerm);

            // 重放历史走过的节点（run.Path 存的是各幕节点 offset）。
            // 遇到非法移动（重复/越界）跳过不崩；到达守关点则推进幕再继续重放。
            if (run.Path != null)
                foreach (var off in run.Path)
                    if (!rs.EnterNode(off) && rs.AtBoss) { rs.DefeatBoss(); rs.EnterNode(off); }

            // 预览当前选中的节点（只算天气、不写回 run.Path）：
            // 先把可能跨幕的点推进，再进入；仍失败 = 异常（如还没通过的守关后续幕节点）， 落到安全兜底。
            if (!rs.EnterNode(offset) && rs.AtBoss) { rs.DefeatBoss(); rs.EnterNode(offset); }

            // ★ 合成「节点天时 + 幕间余气（rs.Lingers）+ 玩家星移余气（LiveWeather.Lingers）」。
            //   星移余气是会话级、玩家主动注入，与幕间余气叠加（都减半强度、各自计节点）。
            var nodeW = rs.CurrentOffset >= 0
                ? WanXiang.Battle.Core.WeatherCatalog.GetSolarTerm(rs.CurrentGraph.Terms[rs.CurrentOffset])
                : null;
            var ext = new System.Collections.Generic.List<WanXiang.Battle.Core.WeatherDef>();
            foreach (var l in rs.Lingers) ext.Add(l.Weather);
            foreach (var l in WanXiang.Campaign.LiveWeather.Lingers) ext.Add(l.Weather);
            WanXiang.Campaign.LiveWeather.Current = ext.Count == 0 ? nodeW
                : WanXiang.Campaign.WeatherComposer.Compose(nodeW, ext);
        }

        /// <summary>
        /// 星移（Batch 3 玩家注入余气）：把「当前节点天时」减半，作为一条余气注入
        /// <see cref="WanXiang.Campaign.LiveWeather.Lingers"/>，带入后续 <see cref="XINGYI_NODES"/> 节。
        /// 与幕间余气（rs.Lingers）同一套合成口径（WeatherComposer.Compose），叠加生效。
        /// <para>规则（用户 2026-09-27 定案）：消耗 <see cref="XINGYI_COST"/> 灵卵；**每节点限一次**
        /// （写存档：换节点/进出编队/重启都不重置）；**未出发前再点一次 = 撤销**（退还灵卵、
        /// 移除本节点注入的余气；出发扣减 NodesLeft 后锁定不可撤）。</para>
        /// </summary>
        private void OnXingyiClicked()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null || _selected < 0) return;
            int key = XingyiKey(_graph != null ? _graph.Act : 1, _selected);
            bool used = run.XingyiUsed != null && run.XingyiUsed.Contains(key);

            // ---- 撤销：本节点已星移且尚未出发（NodesLeft 未被扣 = 仍 == XINGYI_NODES）----
            if (used && HasCancellableXingyi(key))
            {
                var L = WanXiang.Campaign.LiveWeather.Lingers;
                for (int i = L.Count - 1; i >= 0; i--)
                    if (L[i].OwnerKey == key) L.RemoveAt(i);
                run.XingyiUsed.Remove(key);
                // ★ 同步移除持久化镜像（跨存档也不残留）
                if (run.XingyiLingers != null)
                    for (int i = run.XingyiLingers.Count - 1; i >= 0; i--)
                        if (XingyiKey(run.XingyiLingers[i].Act, run.XingyiLingers[i].Offset) == key)
                            run.XingyiLingers.RemoveAt(i);
                run.Eggs += XINGYI_COST;
                WanXiang.Run.RunSave.SaveCurrent();
                RefreshEggsLabel();
                RefreshXingyiUI();
                Debug.Log("[Campaign] 星移已撤销：退还灵卵 " + XINGYI_COST + "，星移余气数=" + L.Count);
                return;
            }

            // ---- 注入 ----
            var w = WanXiang.Campaign.LiveWeather.Current;
            if (w == null) { Debug.Log("[Campaign] 星移：当前节点无天时可移。"); return; }
            if (used)
            {
                Debug.Log("[Campaign] 星移：本节点已用过（每节点限一次，换个节点才能再移）。");
                UpdateXingyiButton(run, key, true);
                return;
            }
            if (run.Eggs < XINGYI_COST)
            {
                Debug.Log("[Campaign] 星移：灵卵不足（需 " + XINGYI_COST + "，现有 " + run.Eggs + "）。");
                UpdateXingyiButton(run, key, true);
                return;
            }

            // ★ 星移 = 把「本节点天时」减半带走。用节点本身的节气天气（确定性、可序列化），
            //   与重进游戏后 RehydrateXingyi 重建的是同一条公式，保证跨存档一致。
            int term = (_graph != null && _graph.Terms != null && _selected >= 0 && _selected < _graph.Terms.Length)
                ? _graph.Terms[_selected] : 0;
            var baseW = WanXiang.Battle.Core.WeatherCatalog.GetSolarTerm(term);
            if (baseW == null) { Debug.Log("[Campaign] 星移：本节点节气无天气可移。"); return; }

            run.Eggs -= XINGYI_COST;
            if (run.XingyiUsed == null) run.XingyiUsed = new System.Collections.Generic.List<int>();
            run.XingyiUsed.Add(key);
            WanXiang.Campaign.LiveWeather.Lingers.Add(new WanXiang.Campaign.RunState.LingerEntry
            {
                Weather = baseW.ScaledHalf("xingyi_" + baseW.Id + "_" + key),
                NodesLeft = XINGYI_NODES,
                FromTerm = term,
                OwnerKey = key,
            });
            // ★ 持久化镜像：跨存档恢复用（重进游戏按 term 重建上面的 LingerEntry）
            if (run.XingyiLingers == null) run.XingyiLingers = new System.Collections.Generic.List<WanXiang.Run.RunState.XingyiLingerSave>();
            run.XingyiLingers.Add(new WanXiang.Run.RunState.XingyiLingerSave
            {
                Act = _graph != null ? _graph.Act : 1,
                Offset = _selected,
                NodesLeft = XINGYI_NODES,
                Term = term,
            });
            WanXiang.Run.RunSave.SaveCurrent();

            RefreshEggsLabel();
            RefreshXingyiUI();
            Debug.Log("[Campaign] 星移已注入：-" + XINGYI_COST + " 灵卵，持续 " + XINGYI_NODES +
                      " 节，星移余气数=" + WanXiang.Campaign.LiveWeather.Lingers.Count);
        }

        /// <summary>
        /// 跨存档恢复星移余气：把持久化的 RunState.XingyiLingers 重建为会话级 LiveWeather.Lingers。
        /// WeatherDef 不可 Json 序列化，故存档只存来源节气 + 剩余节点数；这里重拉天气并减半，
        /// 与注入时同一公式（baseW.ScaledHalf），保证「关游戏→重进」星移天气仍在、不白花灵卵。
        /// </summary>
        private void RehydrateXingyi()
        {
            var run = WanXiang.Run.RunSave.Current;
            WanXiang.Campaign.LiveWeather.Lingers.Clear();
            if (run == null || run.XingyiLingers == null) return;
            foreach (var s in run.XingyiLingers)
            {
                var w = WanXiang.Battle.Core.WeatherCatalog.GetSolarTerm(s.Term);
                if (w == null) continue;
                int key = XingyiKey(s.Act, s.Offset);
                WanXiang.Campaign.LiveWeather.Lingers.Add(new WanXiang.Campaign.RunState.LingerEntry
                {
                    Weather = w.ScaledHalf("xingyi_" + w.Id + "_" + key),
                    NodesLeft = s.NodesLeft,
                    FromTerm = s.Term,
                    OwnerKey = key,
                });
            }
        }

        /// <summary>把会话级 LiveWeather.Lingers（含星移余气，已随节点通过 -1）写回存档镜像。</summary>
        private void SyncXingyiSaveFromLive()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;
            if (run.XingyiLingers == null) run.XingyiLingers = new System.Collections.Generic.List<WanXiang.Run.RunState.XingyiLingerSave>();
            run.XingyiLingers.Clear();
            foreach (var l in WanXiang.Campaign.LiveWeather.Lingers)
            {
                int act = l.OwnerKey / 1000;
                int off = l.OwnerKey % 1000;
                run.XingyiLingers.Add(new WanXiang.Run.RunState.XingyiLingerSave
                {
                    Act = act, Offset = off, NodesLeft = l.NodesLeft, Term = l.FromTerm,
                });
            }
        }

        /// <summary>星移节点 key：act*1000+offset（跨幕不撞号）。</summary>
        private static int XingyiKey(int act, int offset) { return act * 1000 + offset; }

        /// <summary>本节点注入的星移余气还可撤销 = 存在 OwnerKey 匹配且没被出发扣过（NodesLeft 仍 == XINGYI_NODES）。</summary>
        private static bool HasCancellableXingyi(int key)
        {
            foreach (var l in WanXiang.Campaign.LiveWeather.Lingers)
                if (l.OwnerKey == key && l.NodesLeft == XINGYI_NODES) return true;
            return false;
        }

        private void SetXingyiLabel(string text)
        {
            var label = _btnXingyi != null ? _btnXingyi.transform.Find("Tmp_Label") : null;
            var t = label != null ? label.GetComponent<TMP_Text>() : null;
            if (t != null) t.text = text;
        }

        /// <summary>星移扣/退灵卵后同步顶栏「灵卵 N 枚」（拼装式与 BuildNodeMap 同口径）。</summary>
        private void RefreshEggsLabel()
        {
            if (_tmpActTitle == null || _graph == null) return;
            _tmpActTitle.text = BuildActTitle();   // ★ 单一来源（别在这里另拼简版）
        }

        /// <summary>
        /// 星移区统一刷新：合成当前天时 → 写信息卡（标题 + 效果行 + 星移摘要）→ 按钮状态。
        /// 无天时节点回落到节点类型说明（WeatherHint）；按钮只在有天时或可撤销时显示。
        /// </summary>
        private void RefreshXingyiUI()
        {
            ComposeLiveWeatherFor(_selected);
            var wLinger = WanXiang.Campaign.LiveWeather.Current;
            var run = WanXiang.Run.RunSave.Current;
            int key = XingyiKey(_graph != null ? _graph.Act : 1, _selected);

            if (wLinger != null)
            {
                string title = "天时 " + wLinger.BuffName + "（" + ElementCn(wLinger.Element) + "）";
                if (_current != null) _current.Weather = title;
                if (_tmpWeather != null)
                {
                string text = title + "\n" + wLinger.Describe();
                text += BuildXingyiLingerSummary();
                    _tmpWeather.text = text;
                }
            }
            else
            {
                // ⚠ 回落文案也要带守关标志：否则选中守关格而本场无天时节点时，
                //   又会退回"精英：规模 +1"那句（同一个坑，见 IsSeasonBossNode 注释）。
                bool fbBoss = IsSeasonBossNode(_selected);
                var fb = WeatherHint(_graph != null ? _graph.KindOf(_selected) : WanXiang.Campaign.NodeKind.Encounter,
                                     fbBoss, false);
                if (_current != null) _current.Weather = fb;
                if (_tmpWeather != null) _tmpWeather.text = fb;
            }

            UpdateXingyiButton(run, key, wLinger != null);
        }

        /// <summary>星移余气明细：列出每条仍在生效的星移注入（来源天时名 + 剩余节点数），
        /// 让玩家一眼看出「哪些是星移带来的、还剩几节」。无则空串。</summary>
        private string BuildXingyiLingerSummary()
        {
            var lingers = WanXiang.Campaign.LiveWeather.Lingers;
            if (lingers == null || lingers.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            sb.Append("\n· 星移余气 ").Append(lingers.Count).Append(" 条（减半·耗 ")
              .Append(XINGYI_COST).Append(" 灵卵·未出发可撤销）");
            foreach (var l in lingers)
            {
                string nm = (l.Weather != null && !string.IsNullOrEmpty(l.Weather.BuffName)) ? l.Weather.BuffName : "？";
                sb.Append("\n   - ").Append(nm).Append(" 剩 ").Append(l.NodesLeft).Append(" 节");
            }
            return sb.ToString();
        }

        private void UpdateXingyiButton(WanXiang.Run.RunState run, int key, bool hasWeather)
        {
            if (_btnXingyi == null) return;

            // ★★ 秘境里**不显示星移**（2026-10-17 用户要求）。
            //   理由不只是"界面要干净"：天时是**天穹历法**的产物，秘境不在历法里 ——
            //   秘境节点本来就没有天时（信息卡已改成场域特性），星移在这里无事可做。
            //   ⛔ 必须在这里挡，不能只在开面板时挡一次：每次选中节点都会重跑本函数，
            //     只挡一次的话点两下节点它就又冒出来了。
            if (_realmMode)
            {
                _btnXingyi.gameObject.SetActive(false);
                return;
            }

            bool used = run != null && run.XingyiUsed != null && run.XingyiUsed.Contains(key);
            bool canCancel = used && HasCancellableXingyi(key);
            bool afford = run != null && run.Eggs >= XINGYI_COST;
            _btnXingyi.gameObject.SetActive(hasWeather || canCancel);
            SetXingyiLabel(canCancel ? "撤销星移" : "星移");
            _btnXingyi.interactable = canCancel || (hasWeather && !used && afford);
        }

        /// <summary>
        /// 休整节点（NodeKind.Nest）—— ★ 2026-11-02 改为「事件面板三选一」。
        /// ⛔ 为什么改：旧实现是「孵穴」拾蛋弹窗；蛋正在孵化时只弹一句"这里只有一枚暖着的蛋，先走吧"，
        ///   这个节点就白给了（用户截图反馈）。现在固定三选一：
        ///   生火回血 / 拾掇行囊（+1 进化素材） / 拾一枚灵兽蛋（2 个节点后出壳）。
        /// ⭐ 回血比例吃天时地图修正（木令 +15%、润泽 +10%、小雪禁疗 -100 ⇒ 选项直接失效）——
        ///   这个字段以前**没有任何消费点**，等于天时对休整毫无影响。
        /// </summary>
        private void NestChoose(WanXiang.Run.RunState run)
        {
            if (run == null) return;
            int heal = 35 + WanXiang.Campaign.WeatherMapEffects.Current.HealBonusPct;
            if (run.ActiveFloorRule == "hard_noheal") heal = 0;   // 后段「守关在望」：地图禁疗
            TalePanel.OpenEvent(BuildRestEvent(heal, run.EggHatchIn > 0), null);
        }

        /// <summary>
        /// 休整事件的数据。拆成静态方法是为了能在编辑器里直接验文案/选项数，
        /// 不用真的在节点图上走到一个「休整」（地图是种子生成的，很难点中）。
        /// </summary>
        /// <summary>命运所指：代价换本局增益（通用 CostEffect + GrantGrowth，表驱动）。</summary>
        internal static WanXiang.Campaign.EventDef BuildCrossroadEvent()
        {
            return new WanXiang.Campaign.EventDef
            {
                Id = "ev_node_crossroad",
                Title = "命运所指",
                Story = "两条路在此分开。一条要你留下点什么，另一条只给你安稳。",
                Choices = new[]
                {
                    new WanXiang.Campaign.EventChoice
                    {
                        Text = "折损 20% 当前生命，换本局全队 +15% 攻击（永久成长）",
                        CostEffect = WanXiang.Campaign.EventEffect.DamageTeamPct, CostValue = 20,
                        Effect = WanXiang.Campaign.EventEffect.GrantGrowth, Value = 15,
                    },
                    new WanXiang.Campaign.EventChoice
                    {
                        Text = "稳妥通过：得 2 灵卵",
                        Effect = WanXiang.Campaign.EventEffect.GainEggs, Value = 2,
                    },
                }
            };
        }

        /// <summary>博戏：投 1 灵卵，按 gamble_tables.csv 确定性掷一次。</summary>
        internal static WanXiang.Campaign.EventDef BuildGambleEvent()
        {
            return new WanXiang.Campaign.EventDef
            {
                Id = "ev_node_gamble",
                Title = "博戏",
                Story = "路边的摊主把三只碗扣在桌上。押一枚灵卵，翻开才知道是什么。",
                Choices = new[]
                {
                    new WanXiang.Campaign.EventChoice
                    {
                        Text = "投 1 灵卵试试手气（50% +3 灵卵 / 30% 全队回血 20% / 20% 空手）",
                        CostEffect = WanXiang.Campaign.EventEffect.SpendEggs, CostValue = 1,
                        Effect = WanXiang.Campaign.EventEffect.Gamble, ScopeId = "gamble_basic",
                    },
                    new WanXiang.Campaign.EventChoice
                    {
                        Text = "押高桌：投 2 灵卵（10% +10 卵 / 40% 回本 2 卵 / 50% 空手）",
                        CostEffect = WanXiang.Campaign.EventEffect.SpendEggs, CostValue = 2,
                        Effect = WanXiang.Campaign.EventEffect.Gamble, ScopeId = "gamble_high",
                    },
                    new WanXiang.Campaign.EventChoice
                    {
                        Text = "不赌，安然路过",
                        Effect = WanXiang.Campaign.EventEffect.Nothing,
                    },
                }
            };
        }

        internal static WanXiang.Campaign.EventDef BuildRestEvent(int healPct, bool eggIncubating)
        {
            string healText = healPct > 0
                ? "生火休整（全队回复 " + healPct + "% 生命）"
                : "生火休整（当前禁疗，回复无效）";
            string eggText = eggIncubating
                ? "拾起巢中的蛋（你已有一枚在孵化 —— 这次只能空手）"
                : "拾起巢中的蛋（2 个节点后出壳，孵化时才知道是谁）";
            return new WanXiang.Campaign.EventDef
            {
                Id = "ev_node_rest",
                Title = "扎营",
                Story = "风停了，队伍在火堆边坐下。\n这一晚只够做一件事。",
                Choices = new[]
                {
                    new WanXiang.Campaign.EventChoice
                    {
                        Text = healText,
                        Effect = WanXiang.Campaign.EventEffect.HealTeamPct, Value = healPct,
                    },
                    new WanXiang.Campaign.EventChoice
                    {
                        Text = "拾掇行囊（+1 进化素材）",
                        Effect = WanXiang.Campaign.EventEffect.GrantMaterial, Value = 1,
                    },
                    new WanXiang.Campaign.EventChoice
                    {
                        Text = eggText,
                        Effect = WanXiang.Campaign.EventEffect.PickEgg,
                    },
                },
            };
        }

        private static string CnNum(int n)
        {
            switch (n)
            {
                case 1: return "一";
                case 2: return "二";
                case 3: return "三";
                case 4: return "四";
                case 5: return "五";
                default: return n.ToString();
            }
        }

        /// <summary>24 节气名（下标 1..24，来自 GDD 3.3 的节气序列）。</summary>
        private static string TermName(int term)
        {
            string[] names =
            {
                "", "立春", "雨水", "惊蛰", "春分", "清明", "谷雨",
                "立夏", "小满", "芒种", "夏至", "小暑", "大暑",
                "立秋", "处暑", "白露", "秋分", "寒露", "霜降",
                "立冬", "小雪", "大雪", "冬至", "小寒", "大寒",
            };
            return term >= 1 && term < names.Length ? names[term] : ("第 " + term + " 节");
        }
    }
}
