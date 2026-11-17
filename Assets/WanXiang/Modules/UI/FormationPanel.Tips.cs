// ============================================================================
//  万相 · 编队悬停提示（2026-11-17 代码骨架重构）
//  从 FormationPanel.cs 原样搬入，逻辑零改动。
// ============================================================================
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanXiang.Battle.Core;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    public sealed partial class FormationPanel
    {
        private void BuildBondTip()
        {
            var found = transform.Find("Root_BondTip");
            if (found != null)
            {
                _bondTipPanel = (RectTransform)found;
                _bondTipGroup = found.GetComponent<CanvasGroup>();
                if (_bondTipGroup == null) _bondTipGroup = found.gameObject.AddComponent<CanvasGroup>();
                _bondTipText = found.GetComponentInChildren<TMP_Text>(true);
                found.gameObject.SetActive(false);
                Debug.Log("[Formation] 悬浮说明面板已接入 prefab（Root_BondTip） text=" + (_bondTipText != null) +
                          " group=" + (_bondTipGroup != null));
                return;
            }

            // ---- 兜底：prefab 里没有该控件时才运行时建（正常不应走到；打日志提醒补 prefab）----
            Debug.LogWarning("[Formation] Panel_Formation.prefab 缺 Root_BondTip，运行时兜底创建（请在 prefab 补上）");
            var go = new GameObject("Root_BondTip", typeof(RectTransform));
            _bondTipPanel = (RectTransform)go.transform;
            _bondTipPanel.SetParent(transform, false);
            _bondTipPanel.anchorMin = _bondTipPanel.anchorMax = new Vector2(0f, 0.5f);
            _bondTipPanel.pivot = new Vector2(0f, 0.5f);
            _bondTipPanel.sizeDelta = new Vector2(430f, 230f);
            _bondTipPanel.anchoredPosition = new Vector2(30f, 0f);

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.11f, 0.09f, 0.07f, 0.95f);

            _bondTipGroup = go.AddComponent<CanvasGroup>();
            _bondTipGroup.alpha = 0f;
            _bondTipGroup.blocksRaycasts = false;

            var trt = new GameObject("Tmp_BondTip", typeof(RectTransform)).GetComponent<RectTransform>();
            trt.SetParent(_bondTipPanel, false);
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(16f, 12f); trt.offsetMax = new Vector2(-16f, -12f);
            _bondTipText = trt.gameObject.AddComponent<TextMeshProUGUI>();
            _bondTipText.fontSize = 20;
            _bondTipText.color = new Color(0.97f, 0.94f, 0.88f, 1f);
            _bondTipText.alignment = TextAlignmentOptions.TopLeft;
            _bondTipText.raycastTarget = false;

            _bondTipPanel.gameObject.SetActive(false);
        }

        /// <summary>给 8 个羁绊徽章挂悬浮说明（共鸣 / 五行相生）。</summary>
        private void WireBondTips()
        {
            if (_imgBonds == null) return;
            int wired = 0;
            for (int i = 0; i < _imgBonds.Length; i++)
            {
                var img = _imgBonds[i];
                if (img == null) continue;
                img.raycastTarget = true;     // 保证悬浮能命中
                // ★ 名字文本浮在图标上方、TMP 默认 raycastTarget=true，会抢走射线 ， 图标收不到悬浮
                if (_tmpBonds != null && i < _tmpBonds.Length && _tmpBonds[i] != null)
                    _tmpBonds[i].raycastTarget = false;
                var trg = img.gameObject.GetComponent<EventTrigger>();
                if (trg == null) trg = img.gameObject.AddComponent<EventTrigger>();
                int slot = i;
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ => ShowBondTip(slot));
                trg.triggers.Add(enter);
                var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                exit.callback.AddListener(_ => HideBondTip());
                trg.triggers.Add(exit);
                wired++;
            }
            Debug.Log("[Formation] 羁绊悬浮已挂接 " + wired + "/" + _imgBonds.Length + " 个徽章");
        }

        /// <summary>给中宫（中间格，cell 4）挂悬浮说明：中宫土德 / 平息相冲 / 连携须站中宫。</summary>
        private void WireCenterTip()
        {
            if (_cells == null || _cells.Length <= 4 || _cells[4] == null) return;
            var cellImg = _cells[4].GetComponent<Image>();
            if (cellImg != null) cellImg.raycastTarget = true;   // 格子必须有可命中 Graphic 才能收到悬浮
            var trg = _cells[4].gameObject.GetComponent<EventTrigger>();
            if (trg == null) trg = _cells[4].gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => ShowCenterTip());
            trg.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => HideBondTip());
            trg.triggers.Add(exit);
        }

        /// <summary>给右上角天时行（Tmp_WeatherWarn）挂悬浮：本场天时效果 + 状态（谷/生机等）说明。</summary>
        private void WireWeatherTip()
        {
            if (_tmpWeatherWarn == null) return;
            _tmpWeatherWarn.raycastTarget = true;
            var trg = _tmpWeatherWarn.gameObject.GetComponent<EventTrigger>();
            if (trg == null) trg = _tmpWeatherWarn.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => ShowWeatherTip());
            trg.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => HideBondTip());
            trg.triggers.Add(exit);
        }

        /// <summary>
        /// 天时说明（含「谷/生机」等状态——状态说明拼在 WeatherDef.Describe 对应行后，56042ff 起）。
        /// </summary>
        private void ShowWeatherTip()
        {
            if (_bondTipPanel == null || _bondTipText == null) return;
            if (_bondTipSlot == 91 && _bondTipPanel.gameObject.activeSelf) return;
            _bondTipSlot = 91;
            string title, body;
            var w = WanXiang.Campaign.LiveWeather.Current;
            if (w != null)
            {
                title = "天时 · " + w.BuffName;
                body = w.Describe();
                if (string.IsNullOrEmpty(body)) body = "（本条天时没有数值效果）";
                body += "\n\n行内「——」后面是状态说明（谷=每层+1%全属性、生机=每层回血2%等）。\n星移余气也一并计入本场天时。";
            }
            else
            {
                title = "天时";
                body = "本场无天时（节点不在节气上或天气未翻译）。";
            }
            PresentBondTip("<size=24><b>" + title + "</b></size>\n" + body);
        }

        /// <summary>给敌方阵容缩略图挂悬浮：显示该敌人的介绍（名称 / 五行定位 / 四维 / 技能或首领机制）。
        /// ★ 2026-10-15 用户："敌人的悬浮能不能也改到中上的详细面板，不要这个在悬浮显示面板这里"
        ///   ⇒ 从「左下角 Root_BondTip 小浮层」改为「中上 Root_BeastDetail 详情区」，
        ///     与我方异兽的悬浮详情同一块面板（同一口径、同一位置，读起来不用换视线）。
        ///   ⚠ 同时去掉 PointerExit 清空 —— 保留最后一次内容（与我方格子悬浮同一纪律）。</summary>
        private void WireEnemyTips()
        {
            if (_imgEnemies == null) return;
            for (int i = 0; i < _imgEnemies.Length; i++)
            {
                var img = _imgEnemies[i];
                if (img == null) continue;
                img.raycastTarget = true;   // 缩略图必须能命中射线才能收到悬浮
                var trg = img.gameObject.GetComponent<EventTrigger>();
                if (trg == null) trg = img.gameObject.AddComponent<EventTrigger>();
                int idx = i;
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ => ShowEnemyDetail(idx));
                trg.triggers.Add(enter);
                // ⛔ 不挂 PointerExit：移开也保留最后一次（用户 2026-10-15 同批要求）
            }
        }

        /// <summary>
        /// 敌方单位详情 → 写进**中上详情面板**（与 <see cref="ShowBeastDetail"/> 同一块）。
        /// ★ 内容只放**战斗相关**：四维 + （首领）机制介绍 / （异兽）四个技能。
        ///   用户 2026-10-12：不要图鉴典故文案（"共工的臣属…"），要的是属性 / 技能 / 辅助攻击这类能指导出战的。
        /// </summary>
        private void ShowEnemyDetail(int i)
        {
            if (_detailPanel == null || _detailText == null) return;

            var d = (_enemyDefs != null && i >= 0 && i < _enemyDefs.Count) ? _enemyDefs[i] : null;
            if (d == null) return;   // 空槽位：保留上次内容（不清空）

            // ★ 四维走与实战同一口径（2026-10-14）：内容目录里 BaseHp 等是 0（占位字段），
            //   由 ApplyPlaceholderStats 按职业+稀有度生成；首领（b_）/召唤物（summon_）会被
            //   该函数跳过、保留内容表数值。⚠ Clone，不写共享定义。
            var s = d.Clone();
            WanXiang.Battle.Core.BattleConfig.Default.ApplyPlaceholderStats(s);

            if (_detailHead != null)
            {
                var sprite = _sprites != null ? _sprites.GetHead(d.Id) : null;
                _detailHead.enabled = true;
                if (sprite != null)
                {
                    _detailHead.sprite = sprite;
                    _detailHead.preserveAspect = true;
                    _detailHead.color = Color.white;
                }
            }

            bool isBoss = WanXiang.Battle.Core.BossCatalog.Get(d.Id) != null;
            var sb = new System.Text.StringBuilder();
            sb.Append("<size=28><b>").Append(d.DisplayName).Append("</b></size>　")
              .Append(Cn.Of(d.Element)).Append("属 · ").Append(Cn.Of(d.Role));
            if (isBoss) sb.Append(" · 首领");
            sb.Append(" · ").Append(Cn.Of(d.Rarity)).Append('\n');
            sb.Append("生命 ").Append(s.BaseHp)
              .Append("　攻 ").Append(s.BaseAtk)
              .Append("　防 ").Append(s.BaseDef)
              .Append("　速 ").Append(s.BaseSpeed);

            // ★ 敌方战术性格（2026-10-16）：悬浮就要能看到"这家伙会给我上什么 debuff"
            if (!string.IsNullOrEmpty(d.TacticName))
                sb.Append("\n\n【战术·").Append(d.TacticName).Append("】\n").Append(d.TacticNote);

            if (isBoss)
            {
                sb.Append("\n\n【首领机制】\n");
                string mech = WanXiang.Battle.Core.BossCatalog.MechanicText(d.Id);
                sb.Append(string.IsNullOrEmpty(mech) ? "（暂无机制说明）" : mech);
            }
            else
            {
                sb.Append("\n\n【技能】");
                bool enemySide2 = !string.IsNullOrEmpty(d.Id) && d.Id.StartsWith("b_");
                AppendSkillLineWithOverride(sb, "普攻", d.Basic, d.Id, 0, enemySide2);
                AppendSkillLineWithOverride(sb, "战技", d.Active, d.Id, 1, enemySide2);
                AppendSkillLineWithOverride(sb, "绝技", d.Ultimate, d.Id, 2, enemySide2);
                AppendSkillLine(sb, "觉醒", d.Awaken);
            }

            _detailText.text = sb.ToString();
            _detailPanel.gameObject.SetActive(true);
        }

        /// <summary>把一条技能的"名称 + 目标 + 效果"写进说明文本（目标优先取效果的目标，与战斗口径一致）。</summary>

        /// <summary>
        /// 详情面板的技能行：**优先读 CSV 覆盖表**（我方 Override / 敌方 GetEnemy），
        /// 否则退回内容表里的旧技能。⛔ 不读覆盖表时，玩家看到的是"没改过"的旧描述（用户实锤）。
        /// </summary>
        private static void AppendSkillLineWithOverride(System.Text.StringBuilder sb, string slotName,
            WanXiang.Battle.Core.SkillDef sk, string beastId, int slot, bool enemy)
        {
            // 敌方没有专属覆盖时，退回**同 id 的我方覆盖**（否则敌人在详情里永远是旧技能）
            var ov = enemy
                ? (WanXiang.Campaign.SkillOverrideCatalog.GetEnemy(beastId, slot)
                   ?? WanXiang.Campaign.SkillOverrideCatalog.Get(beastId, slot))
                : WanXiang.Campaign.SkillOverrideCatalog.Get(beastId, slot);
            if (ov != null)
            {
                string t = ov.Target switch
                {
                    "front" => "｜打最前排", "back" => "｜打最后排",
                    "mid" => "｜打中排", "lowest" => "｜打残血", "highest" => "｜打最肉", _ => ""
                };
                sb.Append("\n· ").Append(slotName).Append(' ').Append(ov.Name)
                  .Append(t).Append('：').Append(ov.Desc);
                return;
            }
            AppendSkillLine(sb, slotName, sk);
        }

        private static void AppendSkillLine(System.Text.StringBuilder sb, string slotName, WanXiang.Battle.Core.SkillDef sk)
        {
            if (sk == null) return;
            sb.Append("\n· ").Append(slotName).Append(' ').Append(sk.Name);
            var target = sk.PrimaryTarget;
            if (sk.Effects != null)
                for (int k = 0; k < sk.Effects.Length; k++)
                    if (sk.Effects[k].Target != WanXiang.Battle.Core.TargetSelector.Self)
                    { target = sk.Effects[k].Target; break; }
            string tgt = WanXiang.Battle.Core.SkillMath.TargetCn(target);
            if (!string.IsNullOrEmpty(tgt)) sb.Append(tgt);
            if (!string.IsNullOrEmpty(sk.Description)) sb.Append('：').Append(sk.Description);
        }

        // ================================================================
        //  我方异兽战斗详情（中上详情区，用户 2026-10-14 要求）
        //  ----------------------------------------------------------------
        //  口径与敌方情报（ShowEnemyTip）一致：四维 + 战力 + 劫象 + 四技能。
        //  用户 2026-10-12 定的调子：要属性/技能这类能指导出战的，不要图鉴典故文案。
        // ================================================================

        /// <summary>给九宫格格子挂悬浮：悬停已上阵单位 → 显示它的战斗详情。
        /// （与 WireCenterTip 的 PointerEnter 并存 —— EventTrigger 支持多 entry，互不冲突。）</summary>
        private void WireCellDetailTips()
        {
            if (_cells == null) return;
            int wired = 0;
            for (int i = 0; i < _cells.Length; i++)
            {
                var cell = _cells[i];
                if (cell == null) continue;
                var trg = cell.gameObject.GetComponent<EventTrigger>();
                if (trg == null) trg = cell.gameObject.AddComponent<EventTrigger>();
                int idx = i;
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                // ⛔⛔ 空格子**不要**调 ShowBeastDetailByIndex（用户 2026-10-15 报障：
                //    "我悬浮九宫格的异兽可以看到信息，不悬浮就消失了，保留最后一个悬浮的就行了"）。
                //    根因：空格子的 _deployed[idx] == -1 ⇒ 旧代码走 HideBeastDetail()
                //    ⇒ 鼠标从异兽滑到旁边的空格子，右边详情就被清空了。
                //    正确语义：悬停**已上阵**单位才替换内容；滑到空格/移出格子都**保留最后一次**。
                enter.callback.AddListener(_ =>
                {
                    int b = _deployed[idx];
                    if (b >= 0) ShowBeastDetailByIndex(b);
                });
                trg.triggers.Add(enter);
                wired++;
            }
            Debug.Log("[Formation] 格子详情悬浮已挂接 " + wired + "/" + _cells.Length + "（空格保留上次内容）");
        }

        /// <summary>按卡池序号显示详情（单击卡池条目；格子悬停则传 _deployed[cell]）。
        /// ⚠ index 非法时**不再隐藏**面板 —— 保留最后一次内容（见 WireCellDetailTips 注释）。</summary>
        public void ShowBeastDetailByIndex(int idx)
        {
            if (_all == null || idx < 0 || idx >= _all.Length) return;
            ShowBeastDetail(_all[idx]);
        }

        /// <summary>
        /// 异兽战斗详情：名称/五行定位/品阶 + 四维 + 战力（含遗物倍率）+ 劫象 + 四技能。
        /// 详情面板常驻：悬停/点击只**替换**内容，不移开鼠标也留着读（PointerExit 不清空）。
        /// </summary>
        public void ShowBeastDetail(WanXiang.Battle.Core.BeastDef d)
        {
            if (d == null || _detailPanel == null || _detailText == null) return;
            // ★ 四维必须走**与实战同一口径**：内容目录里 BaseHp/Atk/Def/Speed 都是 0（占位字段），
            //   实战数值由 BattleConfig.ApplyPlaceholderStats 按职业基准 + 稀有度乘数生成。
            //   ⚠ 必须 Clone：ApplyPlaceholderStats 会写 def 字段，_all 持有的是内容目录引用
            //   （BuildBeasts 每次新建，但 RosterDragItem 等仍持有下标），写共享定义会污染全局
            //   （理由同 BattleRequestFactory 的 Clone 注释）。
            var s = d.Clone();
            WanXiang.Battle.Core.BattleConfig.Default.ApplyPlaceholderStats(s);
            if (_detailHead != null)
            {
                var sprite = _sprites != null ? _sprites.GetHead(d.Id) : null;
                _detailHead.enabled = true;
                if (sprite != null)
                {
                    _detailHead.sprite = sprite;
                    _detailHead.preserveAspect = true;
                    _detailHead.color = Color.white;
                }
            }
            var sb = new System.Text.StringBuilder();
            sb.Append("<size=28><b>").Append(d.DisplayName).Append("</b></size>　")
              .Append(Cn.Of(d.Element)).Append("属 · ").Append(Cn.Of(d.Role))
              .Append(" · ").Append(Cn.Of(d.Rarity)).Append('\n');
            sb.Append("生命 ").Append(s.BaseHp)
              .Append("　攻 ").Append(s.BaseAtk)
              .Append("　防 ").Append(s.BaseDef)
              .Append("　速 ").Append(s.BaseSpeed)
              .Append("　战力 ").Append(WanXiang.Battle.Core.BattlePower.Of(d, _relicPlayerMul));
            if (!string.IsNullOrEmpty(d.Trait.Name))
            {
                sb.Append("\n【劫象】").Append(d.Trait.Name);
                if (!string.IsNullOrEmpty(d.Trait.Description)) sb.Append('：').Append(d.Trait.Description);
            }
            sb.Append("\n【技能】");
            bool enemySide1 = !string.IsNullOrEmpty(d.Id) && d.Id.StartsWith("b_");
            AppendSkillLineWithOverride(sb, "普攻", d.Basic, d.Id, 0, enemySide1);
            AppendSkillLineWithOverride(sb, "战技", d.Active, d.Id, 1, enemySide1);
            AppendSkillLineWithOverride(sb, "绝技", d.Ultimate, d.Id, 2, enemySide1);
            AppendSkillLine(sb, "觉醒", d.Awaken);
            _detailText.text = sb.ToString();
            _detailPanel.gameObject.SetActive(true);
        }

        private void HideBeastDetail()
        {
            if (_detailPanel != null) _detailPanel.gameObject.SetActive(false);
        }

        private void ShowBondTip(int i)
        {
            if (_bondTipPanel == null || _bondTipText == null) return;
            if (_bondTipSlot == i && _bondTipPanel.gameObject.activeSelf) return;
            _bondTipSlot = i;

            string title, body;
            if (i >= 0 && i < 5)
            {
                var g = WanXiang.Battle.Core.ElementMatrix.GenerateRing;
                string a = Cn.Of(g[i]), c = Cn.Of(g[(i + 1) % 5]);
                title = "五行相生 · " + a + "生" + c;
                // ⛔ 2026-11-01 用户："只说明对战斗的作用和怎么站位触发，别废话"
                body = "作用：这两只全队攻击 +8%。\n" +
                       "站位：把「" + a + "」和「" + c + "」放在**相邻格**（上下左右），这一格就亮。";
            }
            else
            {
                int tier = i - 5;
                int need = tier == 0 ? 2 : (tier == 1 ? 4 : 5);
                title = "同属共鸣 · " + need + " 只";
                body = "作用：全队攻击 +" + (need == 2 ? "8" : need == 4 ? "16" : "25") + "%。\n" +
                       "触发：上阵 " + need + " 只**同属性**异兽，自动生效。";
            }
            PresentBondTip("<size=24><b>" + title + "</b></size>\n" + body);
        }

        /// <summary>当前站在中宫（格 4）的那只异兽；没上阵返回 null。</summary>
        private WanXiang.Battle.Core.BeastDef CenterBeast()
        {
            if (_all == null || _deployed == null || _deployed.Length <= 4) return null;
            int idx = _deployed[4];
            return (idx >= 0 && idx < _all.Length) ? _all[idx] : null;
        }

        private void ShowCenterTip()
        {
            if (_bondTipPanel == null || _bondTipText == null) return;
            if (_bondTipSlot == 90 && _bondTipPanel.gameObject.activeSelf) return;
            _bondTipSlot = 90;
            // ⛔ 2026-11-01 用户："只写作用和怎么触发"
            var sb = new System.Text.StringBuilder();
            sb.Append("作用：站这里的异兽受伤 -8%，且相冲被化解；连携技必须由它发动。\n");
            sb.Append("触发：把主力放进**正中间那格**即生效。");
            var center = CenterBeast();
            if (center != null) sb.Append("\n当前：").Append(center.DisplayName);
            PresentBondTip("<size=24><b>中宫</b></size>\n" + sb);
        }

        private void PresentBondTip(string text)
        {
            CancelInvoke(nameof(ActuallyHideBondTip));   // 取消挂起的隐藏（防闪烁）
            _bondTipPanel.gameObject.SetActive(true);
            _bondTipText.text = text;
            // ★ 高度自适应：首领机制/技能列表可能很长，面板固定高会溢出。按文本实宽量高度再撑开。
            float w = _bondTipText.rectTransform.rect.width;
            if (w < 1f) w = Mathf.Max(_bondTipPanel.sizeDelta.x - 32f, 200f);
            float h = _bondTipText.GetPreferredValues(text, w, 0f).y + 32f;
            var sz = _bondTipPanel.sizeDelta;
            _bondTipPanel.sizeDelta = new Vector2(sz.x, Mathf.Min(Mathf.Max(h, 120f), Screen.height - 40f));
            _bondTipGroup.alpha = 1f;
        }

        /// <summary>
        /// 隐藏改**延迟 0.15s**（2026-11-01 用户："来回切换悬浮会一闪一闪"）。
        /// 指针在两个相邻徽章之间移动时，会先触发 A 的 Exit 再触发 B 的 Enter；
        /// 立即隐藏 ⇒ 面板先灭再亮 = 闪。延迟后 Enter 会把 Invoke 取消，面板一直亮着。
        /// </summary>
        private void HideBondTip()
        {
            if (_bondTipPanel == null) return;
            CancelInvoke(nameof(ActuallyHideBondTip));
            Invoke(nameof(ActuallyHideBondTip), 0.15f);
        }

        private void ActuallyHideBondTip()
        {
            if (_bondTipPanel == null || !_bondTipPanel.gameObject.activeSelf) return;
            _bondTipSlot = -1;
            _bondTipPanel.gameObject.SetActive(false);
            _bondTipGroup.alpha = 0f;
        }

        // ================================================================
        //  拖拽 / 点击布阵
        //  ----------------------------------------------------------------
        //  交互约定（与 GDD"布阵是最重要的决策界面"对齐）：
        //    · 点卡池条目 → 上阵到第一个空推荐位；再点一次（或点已上阵的卡）→ 下阵
        //    · 拖卡池条目到格子 → 上阵（原格的兽自动回卡池）
        //    · 拖格子上的兽到另一格 → 移动 / 交换
        //    · 点格子（空手）→ 下阵
        //    · 每次变动都重算羁绊与战力（相生相邻、共鸣档位实时变）
        // ================================================================

        private bool _rosterShifted;   // 列表右移只执行一次
        private Image _ghostImg;

        // ★ 卡池"长按拖动 = 上阵"：长按 0.28s 后 _holdBeast 生效，短拖仍是滚动
    }
}
