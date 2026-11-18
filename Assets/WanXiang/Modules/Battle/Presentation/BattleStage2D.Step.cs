// ============================================================================
//  万相 · 每帧推进（2026-11-18 从 BattleStage2D.cs 拆出）
//  行为零变化：仅移动方法。共享字段在 Fields.cs，partial 内互通。
// ============================================================================
using System.Collections.Generic;
using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Battle.Presentation
{
    public sealed partial class BattleStage2D
    {
        public void Step(float dt, float fxDt = -1f)
        {
            // 入场：0.85 → 1.0 缩放弹出（缓出），落地瞬间撒一圈尘
            foreach (var kv in _views)
            {
                var uv = kv.Value;
                if (uv == null || uv.Root == null || uv.SpawnT == null) continue;
                if (uv.SpawnT <= 0f) continue;
                uv.SpawnT -= dt;
                float k = Mathf.Clamp01(1f - Mathf.Max(0f, uv.SpawnT) / 0.28f);
                float sc = Mathf.Lerp(0.82f, 1f, Mathf.SmoothStep(0f, 1f, k));
                uv.Root.transform.localScale = new Vector3(sc, sc, 1f) * uv.BaseScale;
                if (uv.SpawnT <= 0f)
                {
                    uv.Root.transform.localScale = Vector3.one * uv.BaseScale;
                    SpawnDust(uv);
                }
            }

            StepTurnBanner(dt);

            PollInspectClick();

            // 冲锋到位 → 补飘伤害数字（见 PlaySkillFx 的 OnImpact）
            if (_flushDelay > 0f)
            {
                _flushDelay -= dt;
                if (_flushDelay <= 0f) { _flushDelay = -1f; FlushPendingDmg(); }
            }
            if (fxDt < 0f) fxDt = dt;
            // 舞台自己的动画时钟：由 dt 累加（**不用 `Time.time`**）——
            // 这样表现层动画与"推进了多少帧"严格对应，编辑器里连调 Step 也能被验证到。
            _animT += dt;
            StepBursts(dt);

            if (_turnArrow != null) _turnArrow.enabled = false;   // 每帧默认隐藏，循环里再为行动者点亮

            // 前景轻微摇曳（呼吸感；幅度很小，不干扰战斗阅读）
            // (背景动效已停用)
            // ⛔ 2026-10-23 用户："轻微呼吸晃动去掉" —— 背景不再做视差漂移。
            //   （BG 保持 1.06 超铺，以后换视频/静图都留了余量。）
            if (_fgDecor != null)
            {
                _fgDecor.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(_animT * 0.9f) * 0.5f);
                _fgDecor.localPosition = new Vector3(Mathf.Sin(_animT * 0.6f) * 0.06f, Mathf.Sin(_animT * 1.1f) * 0.05f, 0f);
            }

            // 环境粒子（同一动画时钟，别用 Time.time）
            if (_ambient != null) _ambient.Step(dt);

            // 终结技 / 觉醒技 特效 + 镜头震动（用 **fxDt，不含倍速** —— cut-in 不被倍速带快）
            if (_skillFx != null) _skillFx.Step(fxDt);
            StepShake(fxDt);
            StepZoom(fxDt);
            _burstPool.Step(dt);   // 格挡爆闪（跟舞台时钟，倍速下一起变快）
   StepVerdict(dt);    // 判定横幅（弹入/停留/淡出）——用 fxDt 之外的 dt，让它不被倍速带没

      // QTE 提示条淡入淡出（有文案就淡入保持，清掉就淡出隐藏）
            _qtePrompt.Step(dt);

            foreach (var v in _views.Values)
            {
                if (v.Root == null) continue;
                float bob = Mathf.Sin((Time.realtimeSinceStartup + v.Phase) * 2.6f) * 0.035f;
                float sink = 0f;
                if (!v.Alive)
                {
                    bool justDied = v.DeadBlend <= 0.0001f;
                    v.DeadBlend = Mathf.Min(1f, v.DeadBlend + dt / 0.75f);
                    sink = -0.5f * v.DeadBlend;

                    // ★ 死亡瞬间炸一团墨点（配合溶解，才有"被打散"的实感）
                    if (justDied)
                    {
                        var wp = v.Root.transform.position + new Vector3(0f, 0.55f, 0f);
                        var inkCol = v.PlayerSide
                            ? new Color(0.16f, 0.15f, 0.14f, 0.95f)
                            : new Color(0.10f, 0.09f, 0.09f, 0.95f);
                        InkBurst.Spawn(wp, 10, 1.0f, inkCol, 26);

                    }

                    // ★ T13 墨蚀溶解：死亡不是淡出，而是墨从头像边缘啃进来
                    if (EnsureDissolveMat(v))
                    {
                        v.DissolveMat.SetFloat("_Progress", v.DeadBlend);
                        // 溶解期只压一点灰，不再做 alpha 淡出（alpha 交给 shader）
                        var cd = v.Body.color;
                        float gd = Mathf.Lerp(1f, 0.55f, v.DeadBlend);
                        v.Body.color = new Color(v.BaseColor.r * gd, v.BaseColor.g * gd, v.BaseColor.b * gd, 1f);
                    }
                    else
                    {
                        // 兜底：shader 不可用就退回原来的淡出
                        var c = v.Body.color;
                        float g = Mathf.Lerp(1f, 0.35f, v.DeadBlend);
                        v.Body.color = new Color(v.BaseColor.r * g, v.BaseColor.g * g, v.BaseColor.b * g, 1f - 0.5f * v.DeadBlend);
                    }
                }
                else if (v.HitFlash > 0f)
                {
                    // 受击白闪：往白色插值，闪完再回基色（用户 2026-10-13："受击闪白"）
                    v.HitFlash -= dt;
                    v.Body.color = Color.Lerp(v.Body.color, Color.white, Mathf.Clamp01(dt * 20f));
                }
                else if (v.Body.color != v.BaseColor)
                {
                    v.Body.color = Color.Lerp(v.Body.color, v.BaseColor, dt * 10f);
                }

                // ---- 出手冲锋：前冲 → 短暂停留 → 回位 ----
                Vector2 lunge = Vector2.zero;
                if (v.LungeActive)
                {
                    v.LungeElapsed += dt;
                    Vector2 delta = v.LungeTo - v.HomePos;
                    if (v.LungeElapsed < LungeForward) lunge = delta * (v.LungeElapsed / LungeForward);
                    else if (v.LungeElapsed < LungeForward + LungeHold) lunge = delta;
                    else if (v.LungeElapsed < LungeForward + LungeHold + LungeBack)
                    {
                        float k = (v.LungeElapsed - LungeForward - LungeHold) / LungeBack;
                        lunge = delta * (1f - k);
                    }
                    else
                    {
                        v.LungeActive = false;
                        if (v.Body != null) v.Body.sortingOrder = v.BaseSortingOrder;
                    }
                }

                // 受击位移：沿受击方向小踹一脚，快速衰减（打击"推力"感）
                Vector2 kick = Vector2.zero;
                if (v.KickT > 0f)
                {
                    v.KickT -= dt;
                    float kk = Mathf.Clamp01(v.KickT / 0.12f);
                    kick = v.KickVec * kk;
                }

                v.Root.transform.localPosition = new Vector3(
                    v.HomePos.x + lunge.x + kick.x, v.HomePos.y + FootOffset + lunge.y + bob + sink + kick.y, 0f);

                // 当前行动者头顶向下箭头（跟随其头部，带轻微浮动；颜色按阵营）
                if (_turnArrow != null && v.UnitId == _actingId && v.Alive)
                {
                    float bodyH = (v.Body != null && v.Body.sprite != null)
                        ? v.Body.sprite.bounds.size.y * Mathf.Abs(v.Root.transform.localScale.y) : UnitHeight;
                    float topY = v.Root.transform.position.y + bodyH * 0.5f + 0.28f;
                    float bobA = Mathf.Sin(_animT * 4.5f) * 0.07f;
                    _turnArrow.transform.position = new Vector3(v.Root.transform.position.x, topY + bobA, 0f);
                    bool isPlayer = v.HomePos.x < 0f;
                    _turnArrow.color = isPlayer ? new Color(0.36f, 0.86f, 0.92f) : new Color(0.97f, 0.46f, 0.40f);
                    _turnArrow.enabled = true;
                }

                // 格挡提示圈（QTE）：环收缩、套上目标圈的那一拍 = 完美区间（判定在面板）
                if (_parryRing != null && _parryActorId != null && v.UnitId == _parryActorId && v.Alive)
                {
                    float bodyH2 = (v.Body != null && v.Body.sprite != null)
                        ? v.Body.sprite.bounds.size.y * Mathf.Abs(v.Root.transform.localScale.y) : UnitHeight;
                    float cx = v.Root.transform.position.x;
                    float cy = v.Root.transform.position.y + bodyH2 * 0.42f;
                    float baseS = bodyH2 * 0.62f;   // 环直径 ≈ 0.62 个身位（太大会贴屏缘被裁）
                    // 环：1.0 → 0.40（到 k=0.8 正好收缩到目标圈大小，随后略过冲）
                    float ringS = Mathf.Lerp(1.0f, 0.40f, Mathf.Clamp01(_parryK / 0.80f));
                    _parryRing.transform.position = new Vector3(cx, cy, -0.05f);
                    _parryRing.transform.localScale = Vector3.one * (baseS * ringS);
                    // 进入完美区间时环变亮（给玩家一眼可读的"就是现在"信号）
                    bool inPerfect = Mathf.Abs(_parryK - 0.80f) <= 0.06f;
                    _parryRing.color = inPerfect
                        ? new Color(1f, 0.97f, 0.72f, 1f)
                        : new Color(1f, 0.90f, 0.55f, 0.85f);
                    _parryRing.enabled = true;

                    if (_parryTarget != null)
                    {
                        _parryTarget.transform.position = new Vector3(cx, cy, -0.06f);
                        _parryTarget.transform.localScale = Vector3.one * (baseS * 0.40f);
                        _parryTarget.color = inPerfect
                            ? new Color(0.72f, 1f, 0.72f, 0.85f)
                            : new Color(1f, 1f, 1f, 0.45f);
                        _parryTarget.enabled = true;
                    }
                }

                // 血条跟随立绘（相机 2D 朝 -Z，直接摆即可）
                if (v.HpBg != null) v.HpBg.transform.localPosition = new Vector3(0f, HpBarY, -0.01f);
                // ⛔ 填充条**只改 y，必须保留 x**：x 是 SetHpBar2D 算的"左端固定"偏移，
                //   这里每帧归零的话填充就永远居中 ， 观感变成"血从两边一起掉"（用户实测两轮的真因）。
                if (v.HpFill != null)
                {
                    var flp = v.HpFill.transform.localPosition;
                    v.HpFill.transform.localPosition = new Vector3(flp.x, HpBarY, -0.02f);
                }
                float shX2 = HpBarWidth * 0.5f + ShieldGap + ShieldPlateW * 0.5f;
                if (v.ShieldPlate != null) v.ShieldPlate.transform.localPosition = new Vector3(shX2, 0f, -0.01f);
                if (v.ShieldText != null) v.ShieldText.transform.localPosition = new Vector3(shX2, 0f, -0.02f);

                // 状态特效的**整体摇摆**（shader 管波形/呼吸，这里只管左右晃 —— 冰封几乎不晃、燃烧晃得厉害）
                if (v.Overlay != null)
                {
                    float swayDeg = (v.Overlay.enabled && v.OverlayDef != null) ? v.OverlayDef.SwayDeg : 0f;
                    if (swayDeg > 0.01f)
                    {
                        float sp = Mathf.Max(0.01f, v.OverlayDef.SwaySpeed);
                        v.Overlay.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(_animT * sp) * swayDeg);
                    }
                    else if (v.Overlay.transform.localRotation != Quaternion.identity)
                    {
                        v.Overlay.transform.localRotation = Quaternion.identity;
                    }
                }
            }

            // 克制染色：放在**预览高亮之后**执行（预览每帧写底色，这里再覆盖上去）
            StepCellFlash(dt);

            // 冲锋押后的伤害：到点就落（顺序与事件一致）
            if (_deferredDmg.Count > 0)
            {
                for (int i = _deferredDmg.Count - 1; i >= 0; i--)
                {
                    var d = _deferredDmg[i];
                    if (_animT < d.Due) continue;
                    if (d.V != null && d.V.Alive)     // 阵亡单位不再补飘字（召唤物/Escort 死亡很常见）
                    {
                        SpawnDamageNumber(d.V, d.Amount, d.Melt, d.Reaction, d.Crit);
                        ApplyHitReaction(d.V, d.ActorHome, d.Crit ? 0.16f : 0.10f);
                    }
                    _deferredDmg.RemoveAt(i);
                }
            }

            // 伤害数字上浮 + 弹出缩放 + 回收
            // ⛔ 2026-10-24 用户："数字怎么变小了 / 4 倍速看不到伤害"
            //   原来用**倍速后的 dt** 推进 ⇒ 4 倍速下 0.7s 寿命只剩 0.17s，弹出也一闪而过，
            //   读起来就是"又小又快、几乎看不见"。飘字是读数信息，应按真实时间走。
            float numDt = fxDt > 0f ? Mathf.Min(fxDt, dt > 0f ? dt * 4f : fxDt) : dt;
            if (numDt < 0.0001f) numDt = dt;
            _floaters.Step(numDt);
        }
    }
}
