// ============================================================================
//  万相 · 战斗技能/受击演出（2026-11-18 代码骨架重构）
//  从 BattleStage2D.cs 原样搬入，逻辑零改动。
// ============================================================================
using System.Collections.Generic;
using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Battle.Presentation
{
    public sealed partial class BattleStage2D
    {
        /// 返回演出时长（秒）；0 = 本场没有 Boss（调用方不用等）。
        /// ⚠ 只负责**表现**：不暂停模拟（暂停由 BattlePanel 在开局 await 这个时长实现）。
        /// </summary>
        public float PlayBossEntrance()
        {
            if (_views == null) return 0f;
            foreach (var kv in _views)
            {
                var v = kv.Value;
                if (v == null || v.Root == null || v.PlayerSide) continue;
                var defId = v.DefId;
                // ⛔ 秘境首领 id 是 `rc_<hookId>`，不是 b_ 前缀 —— 用 IsBoss 会一个都进不来
                bool isBossUnit = !string.IsNullOrEmpty(defId) &&
                                  (WanXiang.Battle.Core.BossCatalog.IsBoss(defId) || IsRealmChampion(defId));
                if (!isBossUnit) continue;
                if (!v.Alive) continue;

                EnsureSkillFx();
                if (_skillFx == null) return 0f;

                var portrait = v.Body != null ? v.Body.sprite : null;
                var beastName = v.NameText != null ? v.NameText.text : null;
                // 元素色：从 BossCatalog 按 id 查（没有就退化为金，不抛异常）
                var el = WanXiang.Battle.Core.Element.None;
                try
                {
                    foreach (var bd in WanXiang.Battle.Core.BossCatalog.All)
                        if (bd != null && bd.Id == defId) { el = bd.Element; break; }
                }
                catch { }
                var col = BattlePalette.OfElement(el);
                _skillFx.OnImpact = null;                 // 登场不冲锋、不补伤害
                // ★ 每个 Boss 的登场"抬头"文案区分层级（2026-10-25）：
                //   秘境镇守 / 终局 / 普通首领；配合元素色环，至少让 25 个 Boss 不完全雷同。
                // 元素专属抬头（2026-10-25 第 2 项：让 25 个 Boss 有辨识度的第一步）
                string kicker;
                if (!string.IsNullOrEmpty(_realmId)) kicker = "镇守登场";
                else
                {
                    switch (el)
                    {
                        case WanXiang.Battle.Core.Element.Wood:  kicker = "藤蔓缠生"; break;
                        case WanXiang.Battle.Core.Element.Fire:  kicker = "烈焰涌起"; break;
                        case WanXiang.Battle.Core.Element.Metal: kicker = "兵刃出鞘"; break;
                        case WanXiang.Battle.Core.Element.Water: kicker = "寒渊漫上"; break;
                        case WanXiang.Battle.Core.Element.Earth: kicker = "山岳压境"; break;
                        default: kicker = "首领登场"; break;
                    }
                }
                if (defId == "b_guixu" || defId == "b_hongmeng") kicker = "终局降临";
                else if (defId == "b_bairen" || defId == "b_shuangfeng") kicker = "双刃登场";
                else if (defId == "b_xuanming" || defId == "b_shuangying") kicker = "寒渊降临";

                // 终局 / 觉醒级体量：觉醒参数会让环更多、闪更强、镜头更狠
                bool bigEntrance = defId == "b_guixu" || defId == "b_hongmeng" || defId == "b_wangliang";
                // ⛔ 名字/抬头传 null：由 BossIntroFx 那层排版负责，
                //   否则 cut-in 自己也印一遍 ⇒ 字重影（用户截图就是这个）
                _skillFx.Play(v.Root.transform.position, defId, col, bigEntrance,
                              portrait, null, null, faceLeft: true);

                // 元素异象：按五行撒方向性粒子（藤蔓窜起 / 火星上腾 / 刃风横扫 / 逆流 / 压顶）
                EntranceSpark.Burst(v.Root.transform.position + new Vector3(0f, 0.15f, -0.2f),
                                    el, col, bigEntrance ? 26 : 18);

                // 压迫感三件套：压暗 + 名字砸下 + 镜头推近/震屏（用户："没有冲击力"）
                // 独立排版层：元素标签 / 名字 / 数据（不碰终结技 cut-in）
                string tagLine = string.IsNullOrEmpty(_realmId) ? ("◆ " + kicker) : ("◆ " + kicker + " · 秘境");
                string statLine = "HP " + v.MaxHp;
                BossIntroFx.Play(v.Root.transform.position, tagLine, beastName ?? "首领", statLine, col);
                TriggerZoom(bigEntrance ? 0.9f : 0.6f, 0.55f);
                TriggerShake(bigEntrance ? 0.16f : 0.09f, 0.5f);
                Debug.Log("[Stage2D] Boss 登场演出：" + beastName + " (" + defId + ")");
                return _skillFx.LastBusyDuration;
            }
            return 0f;
        }

        /// <summary>触发一次终结技/觉醒技演出。仅 Ultimate/Awaken 调用（大招要稀有）。</summary>
        private void PlaySkillFx(string actorId, string targetId, Element element, bool isAwaken, string skillName)
        {
            if (string.IsNullOrEmpty(actorId)) return;
            if (!_views.TryGetValue(actorId, out var v) || v.Root == null || !v.Alive) return;
            EnsureSkillFx();
            if (_skillFx == null) { StartLunge(actorId, targetId); return; }
            if (!FxSkillCutIn)
            {
                // 设置关闭特写：只冲锋 + 播命中特效（伤害数字/震屏仍按各自开关）
                StartLunge(actorId, targetId);
                var c0 = BattlePalette.OfElement(element);
                if (!string.IsNullOrEmpty(targetId) && _views.TryGetValue(targetId, out var tv0) && tv0.Root != null)
                    BossSkillBurst.Play(tv0.Root.transform.position, c0, 1.4f, 8, 0.35f, 0);
                return;
            }
            var col = BattlePalette.OfElement(element);
            // P5R 式 cut-in：把该异兽的立绘 + 名字 + 终结技名传进去，先特写再特效
            var portrait = v.Body != null ? v.Body.sprite : null;
            var beastName = v.NameText != null ? v.NameText.text : null;
            // ★ 冲锋等到"起爆"那一下才冲（否则特写期间就冲完 ⇒ 玩家看到"攻击完才出现冲击波"）；
            //   同时把特写期间押后的伤害数字在这里补飘（用户 2026-10-14）。
            string a = actorId, t = targetId;
            // ★ 2026-10-30 用户："数字比立绘前顶还早出现" ——
            //   OnImpact 里同时冲+出数字，数字立刻生效 ⇒ 看起来数字先于冲锋。
            //   改成：先 StartLunge，等**前冲时长**（LungeForward）到了再 Flush。
            _skillFx.OnImpact = () => { StartLunge(a, t); _flushDelay = LungeForward; };
            // faceLeft：敌方在右侧、面向我方 ⇒ 特写立绘要镜像（用户 2026-10-18 报"头像反了"）
            _skillFx.Play(v.Root.transform.position, actorId, col, isAwaken, portrait, beastName, skillName,
                          faceLeft: !v.PlayerSide);

            // ★ 首领专属爆发：敌方首领放大招时，在目标位置加一圈加色冲击（环形 + 放射条纹）
            bool bossCaster = !v.PlayerSide &&
                              (!string.IsNullOrEmpty(v.DefId) &&
                               (WanXiang.Battle.Core.BossCatalog.IsBoss(v.DefId) || (v.DefId ?? "").StartsWith("rc_")));
            if (bossCaster)
            {
                Vector3 at = v.Root.transform.position;
                if (!string.IsNullOrEmpty(targetId) && _views.TryGetValue(targetId, out var tv) && tv.Root != null)
                    at = Vector3.Lerp(at, tv.Root.transform.position, 0.6f);
                bool big = !string.IsNullOrEmpty(skillName) && skillName.Contains("终");
                // 五行各有一套爆发"体型"：木宽而缓、火小而躁、金细密、水大而慢、土短促沉重
                float radius, dur; int streaks; int shape = 0;
                switch (element)
                {
                    case Element.Wood:  radius = 2.8f; dur = 0.60f; streaks = 10; break;   // 蔓生扩散
                    case Element.Fire:  radius = 2.1f; dur = 0.34f; streaks = 22; break;   // 爆燃
                    case Element.Metal: radius = 2.4f; dur = 0.28f; streaks = 32; break;   // 刃风密
                    case Element.Water: radius = 3.0f; dur = 0.70f; streaks = 8;  break;   // 潮涌
                    case Element.Earth: radius = 2.2f; dur = 0.40f; streaks = 14; break;   // 坠石
                    default:            radius = 2.3f; dur = 0.45f; streaks = 16; break;
                }
                if (big) { radius *= 1.35f; dur *= 1.3f; streaks += 8; }

                // 单个 Boss 的个性化覆盖（同一元素也要有区别）
                var st = BossFxStyle.For(v.DefId);
                if (st.HasValue)
                {
                    radius *= st.Value.RadiusMul;
                    dur = st.Value.Dur;
                    streaks = st.Value.Streaks;
                    col = new Color(Mathf.Min(1f, col.r * st.Value.Brightness),
                                    Mathf.Min(1f, col.g * st.Value.Brightness),
                                    Mathf.Min(1f, col.b * st.Value.Brightness), col.a);
                    shape = st.Value.Shape;
                }
                BossSkillBurst.Play(at, col, radius, streaks, dur, shape);
                TriggerShake(big ? 0.10f : 0.06f, 0.28f);
            }

        }

        /// <summary>
        /// 连携特写（2026-10-18）：主兽居中放大，其余**连携成员**的头像缩小环绕。
        /// <para>成员口径：我方其余存活单位（最多 4 个，按棋盘顺序）——连携本来就是全队参与。</para>
        /// </summary>
        private void PlayComboFx(string actorId, Element element, string comboName)
        {
            if (actorId == null) return;
            EnsureSkillFx();                 // ⛔ 不建就没有播放器（第一版漏了这句，特写完全不播）
            if (_skillFx == null) return;
            if (!_views.TryGetValue(actorId, out var host) || host.Root == null) return;
            var hostSr = host.Body != null ? host.Body.sprite : null;
            if (hostSr == null) return;

            var extras = new System.Collections.Generic.List<Sprite>(4);
            foreach (var kv in _views)
            {
                var v = kv.Value;
                if (v == null || v == host || !v.Alive || !v.PlayerSide) continue;
                var sp = v.Body != null ? v.Body.sprite : null;
                if (sp == null) continue;
                extras.Add(sp);
                if (extras.Count >= 4) break;
            }
            var hostName = host.NameText != null ? host.NameText.text : null;
            var col = BattlePalette.OfElement(element);
            _skillFx.PlayCombo(host.Root.transform.position, actorId, col, hostSr, hostName,
                               string.IsNullOrEmpty(comboName) ? "连携" : comboName, extras.ToArray());
        }

        /// <summary>镜头震动（由 SkillFxPlayer 在大招起爆点回调）。</summary>
        public void TriggerShake(float amp, float dur)
        {
            if (!FxScreenShake) return;   // 设置：屏幕震动
            if (_cam == null) return;
            _shakeAmp = Mathf.Max(_shakeAmp, amp);
            _shakeDur = Mathf.Max(_shakeDur, dur);
            _shakeT = 0f;
        }

        /// <summary>推进震动衰减（在 Step 内调用）。</summary>
        private void StepShake(float dt)
        {
            if (_cam == null || _shakeDur <= 0f) return;
            _shakeT += dt;
            float k = 1f - Mathf.Clamp01(_shakeT / _shakeDur);
            float a = _shakeAmp * k * k;
            float nx = (Mathf.PerlinNoise(_animT * 42f, 0.5f) - 0.5f) * 2f;
            float ny = (Mathf.PerlinNoise(0.5f, _animT * 42f) - 0.5f) * 2f;
            _cam.transform.localPosition = _camBase + new Vector3(nx * a, ny * a, 0f);
            if (_shakeT >= _shakeDur)
            {
                _shakeDur = 0f; _shakeAmp = 0f;
                _cam.transform.localPosition = _camBase;
            }
        }

        /// <summary>起爆点 Zoom Punch：相机迅速推近再弹回（由 SkillFxPlayer 在爆发那下调用）。</summary>
        public void TriggerZoom(float dip, float dur)
        {
            if (_cam == null || !_cam.orthographic) return;
            _zoomDip = Mathf.Max(_zoomDip, dip);
            _zoomDur = Mathf.Max(_zoomDur, dur);
            _zoomT = 0f;
        }

        /// <summary>推进 Zoom Punch 衰减（在 Step 内调用，与震动叠加）。</summary>
        private void StepZoom(float dt)
        {
            if (_cam == null || _zoomDur <= 0f) return;
            _zoomT += dt;
            float k = Mathf.Clamp01(_zoomT / _zoomDur);
            // sin 曲线：0→峰值(k=0.5)→0，推近再平滑弹回
            float dip = _zoomDip * Mathf.Sin(k * Mathf.PI);
            _cam.orthographicSize = _zoomBase - dip;
            if (_zoomT >= _zoomDur)
            {
                _zoomDur = 0f; _zoomDip = 0f;
                _cam.orthographicSize = _zoomBase;
            }
        }
    }
}
