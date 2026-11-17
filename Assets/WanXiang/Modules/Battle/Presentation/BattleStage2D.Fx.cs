// ============================================================================
//  万相 · 战斗特效池（2026-11-17 代码骨架重构）
//  从 BattleStage2D.cs 原样搬入，逻辑零改动。
//  ⚠ 共享字段（_views/_cam/_burstPool/_state 等）保留在主文件，partial 直接访问。
// ============================================================================
using System.Collections.Generic;
using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Battle.Presentation
{
    public sealed partial class BattleStage2D
    {
        /// <summary>反应名 + 颜色。名字要短（两个字最好读），颜色绑**反应自身的属性语义**。</summary>
        private static void ReactionInfo(ReactionKind k, out string name, out Color color, out string tag)
        {
            // 10 组五行两两配对（2026-10-31）：名字 / 颜色 / 组合标签
            switch (k)
            {
                case ReactionKind.Sheng:    name = "润 生"; color = new Color(0.55f, 0.90f, 0.62f); tag = "木×水"; break;
                case ReactionKind.Bulwark:  name = "熔 盾"; color = new Color(0.98f, 0.74f, 0.42f); tag = "火×土"; break;
                case ReactionKind.Steam:    name = "蒸 发"; color = new Color(1.00f, 0.58f, 0.24f); tag = "火×水"; break;
                case ReactionKind.Forge:    name = "锻 击"; color = new Color(1.00f, 0.82f, 0.35f); tag = "火×金"; break;
                case ReactionKind.Erode:    name = "侵 蚀"; color = new Color(0.72f, 0.80f, 0.45f); tag = "水×土"; break;
                case ReactionKind.Quench:   name = "感 电"; color = new Color(0.72f, 0.80f, 1.00f); tag = "金×水"; break;
                case ReactionKind.Kindle:   name = "助 燃"; color = new Color(1.00f, 0.42f, 0.18f); tag = "木×火"; break;
                case ReactionKind.TakeRoot: name = "扎 根"; color = new Color(0.58f, 0.84f, 0.50f); tag = "木×土"; break;
                case ReactionKind.Execute:  name = "肃 杀"; color = new Color(0.92f, 0.62f, 0.92f); tag = "木×金"; break;
                case ReactionKind.Bastion:  name = "坚 壁"; color = new Color(0.86f, 0.78f, 0.52f); tag = "金×土"; break;
                case ReactionKind.Consume:  name = "消 耗"; color = new Color(0.95f, 0.85f, 0.55f); tag = "同元素"; break;
                default:                    name = "反 应"; color = Color.white; tag = ""; break;
            }
        }


        /// <summary>
        /// 魇化受击：**预制 ParticleSystem**（2026-11-02 改为资产管线）。
        /// 资产：`Assets/Resources/FX/Particle_CorruptHit.prefab`（配 URP 粒子材质 M_CorruptHit）。
        /// <para>⭐ 为什么改成预制：策划/美术能在 Inspector 里直接调色、调寿命、加 Noise，
        /// 不用改代码 —— 这是商业项目的常用做法。</para>
        /// <para>⛔ 若资产缺失则退回运行时建 PS（老逻辑），保证不会没特效。</para>
        /// </summary>
        private void SpawnCorruptPs(UnitView2D v)
        {
            if (v == null || v.Root == null) return;
            Vector3 pos = v.Root.transform.position + new Vector3(0f, 0.8f, -1.2f);

            if (_corruptPsPrefab == null) _corruptPsPrefab = Resources.Load<GameObject>("FX/Particle_CorruptHit");
            if (_corruptPsPrefab != null)
            {
                var inst = Instantiate(_corruptPsPrefab, pos, Quaternion.identity, transform);
                inst.SetActive(true);
                var instPs = inst.GetComponent<ParticleSystem>();
                if (instPs != null) instPs.Play();
                Destroy(inst, 1.4f);
                return;
            }

            SpawnCorruptPsRuntime(v, pos);
        }

        /// <summary>兜底：资产缺失时运行时建一个（旧实现）。</summary>
        private void SpawnCorruptPsRuntime(UnitView2D v, Vector3 pos)
        {
            var go = new GameObject("CorruptPS");
            go.transform.SetParent(transform, false);
            go.transform.position = pos;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.35f; main.loop = false; main.playOnAwake = false;
            main.startLifetime = 0.65f; main.startSpeed = 1.5f; main.startSize = 0.42f;
            main.gravityModifier = -0.25f; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(0.62f, 0.32f, 0.95f, 0.85f);
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 10) });
            var shp = ps.shape; shp.shapeType = ParticleSystemShapeType.Sphere; shp.radius = 0.28f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            var m = Resources.Load<Material>("FX/M_CorruptHit");
            if (m != null) r.material = m;
            r.sortingOrder = 39;
            ps.Play();
            Destroy(go, 1.4f);
        }

        /// <summary>
        /// 魇化受击：敌方身上炸一小圈紫黑碎片（复用 <see cref="_bursts"/> 手写抛物线）。
        /// <para>⛔ 不用 ParticleSystem：本工程里 PS 会丢材质变洋红（踩过）。</para>
        /// </summary>
        private void SpawnCorruptBurst(UnitView2D v)
        {
            if (v == null || v.Root == null) return;
            Vector3 c = v.Root.transform.position + new Vector3(0f, 0.85f, -1.1f);
            for (int i = 0; i < 5; i++)
            {
                float a = Mathf.PI * 2f * i / 5f + 0.4f;
                var col = new Color(0.62f, 0.32f, 0.95f, 0.9f);
                var burst = AcquireBurst(SolidSprite(col, 12, 12, 0, col), 38, c, 0.18f);
                burst.Vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.8f + 0.6f) * 1.6f;
                burst.Life = 0.55f; burst.Max = 0.55f;
                _bursts.Add(burst);
            }
            while (_bursts.Count > 96)
            {
                RecycleBurst(_bursts[0]);
                _bursts.RemoveAt(0);
            }
        }

        /// <summary>
        /// 魇化氛围：敌方**普通/精英**脚下加一团缓慢呼吸的暗紫雾（与脚下元素圈叠加）。
        /// </summary>
        private void SetCorruptAura(UnitView2D v, bool on)
        {
            if (v == null || v.Root == null || v.PlayerSide) return;
            if (v.AuraCorrupt == null)
            {
                var go = new GameObject("CorruptAura");
                go.transform.SetParent(v.Root.transform, false);
                go.transform.localPosition = new Vector3(0f, 0.10f, 0.01f);
                float k = Mathf.Max(0.0001f, Mathf.Abs(v.Root.transform.lossyScale.y));
                go.transform.localScale = new Vector3(1.5f / k, 0.8f / k, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = Resources.Load<Sprite>("Battle/FX_motion_fog");
                sr.sortingOrder = 1;
                v.AuraCorrupt = sr;
            }
            v.AuraCorrupt.gameObject.SetActive(on);
            if (on)
            {
                float a = 0.20f + 0.08f * Mathf.Sin(Time.time * 1.6f);
                v.AuraCorrupt.color = new Color(0.55f, 0.30f, 0.85f, a);
            }
        }

        /// <summary>
        /// 元素反应演出：受击者身上弹**属性色的大字**（比伤害数字大一号、活得更久），
        /// 同时炸开一圈小方块碎片 + 受击弹一下。
        /// <para>⛔ 碎片用 SpriteRenderer（<see cref="SolidSprite"/>）手写抛物线，**不用 ParticleSystem**
        ///   —— 工程硬规矩：ParticleSystem 丢材质会变洋红。</para>
        /// </summary>
        private void SpawnReactionFx(UnitView2D v, ReactionKind kind)
        {
            if (v == null || v.Root == null) return;
            ReactionInfo(kind, out string name, out Color color, out string tag);

            SpawnNumberInternal(v, name, color, 1.25f, 0.95f, raise: 1.35f);          // 反应名最高
            if (!string.IsNullOrEmpty(tag)) SpawnNumberInternal(v, tag, color, 0.80f, 0.85f, raise: 0.55f);

            v.Body.color = color;
            v.HitFlash = 0.14f;
            v.ShakePulse = 0.22f;

            Vector3 c = v.Root.transform.position + new Vector3(0f, 0.9f, -1.2f);
            for (int i = 0; i < 8; i++)
            {
                float a = Mathf.PI * 2f * i / 8f;
                var burst = AcquireBurst(SolidSprite(color, 14, 14, 0, color), 40, c, 0.22f);
                burst.Vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 2.2f;
                burst.Life = 0.45f; burst.Max = 0.45f;
                _bursts.Add(burst);
            }
            while (_bursts.Count > 96)
            {
                RecycleBurst(_bursts[0]);
                _bursts.RemoveAt(0);
            }
        }

        /// <summary>
        /// 克制染色：受击者脚下的格子染成**攻击属性色**，0.55s 内渐隐回底色。
        /// <para>⛔ 只染格子、不动立绘/血条 —— 立绘已经有受击白闪与反应染色，
        ///   再给立绘上色会和它们打架；格子的语义就是"这块地被打成了什么属性"。</para>
        /// </summary>
        private void FlashCounterCell(UnitView2D v, Color color)
        {
            if (v == null) return;
            int idx = (v.PlayerSide ? 0 : 9) + Mathf.Clamp(v.CellIndex, 0, 8);
            _cellFlashT[idx] = CellFlashDur;
            _cellFlashC[idx] = color;
            _cellFlashing[idx] = true;
        }

        /// <summary>克制染色衰减（每帧，必须在技能预览高亮**之后**调用）。</summary>
        private void StepCellFlash(float dt)
        {
            for (int k = 0; k < 18; k++)
            {
                if (!_cellFlashing[k]) continue;
                bool player = k < 9;
                int i = player ? k : k - 9;
                var cells = player ? _cellsPlayer : _cellsEnemy;
                var baseC = player ? _cellBasePlayer : _cellBaseEnemy;
                if (cells == null || cells[i] == null) { _cellFlashing[k] = false; continue; }

                _cellFlashT[k] -= dt;
                if (_cellFlashT[k] <= 0f)
                {
                    cells[i].color = baseC[i];        // 精确还原（不能 Lerp 回近似值，否则格子会越闪越浅）
                    _cellFlashing[k] = false;
                    continue;
                }
                // ⚠ 包络必须**瞬亮 → 线性衰减**：t=1（刚触发）就该是最亮。
                //   写成"先爬升再回落"会让第一帧几乎看不见（实测 0.02s 时只混了 6%，
                //   格子在白底纸面上完全看不出变化）。
                float t = _cellFlashT[k] / CellFlashDur;          // 1 → 0
                float strength = Mathf.Clamp01(t / 0.75f);        // 前 25% 保持全亮，之后线性衰减
                cells[i].color = Color.Lerp(baseC[i], _cellFlashC[k], strength * 0.92f);
            }
        }

        /// <summary>碎片动画（每帧）：抛物线 + 缩小 + 淡出。由 <see cref="Step"/> 驱动。</summary>
        private void StepBursts(float dt)
        {
            for (int i = _bursts.Count - 1; i >= 0; i--)
            {
                var b = _bursts[i];
                if (b.T == null) { _bursts.RemoveAt(i); continue; }
                b.Life -= dt;
                if (b.Life <= 0f) { RecycleBurst(b); _burstFree.Push(b); _bursts.RemoveAt(i); continue; }
                float k = b.Life / b.Max;
                b.Vel.y -= 6.5f * dt;
                b.T.position += (Vector3)(b.Vel * dt);
                b.T.localScale = Vector3.one * (0.22f * k + 0.03f);
                var cc = b.Sr.color; cc.a = k; b.Sr.color = cc;
                _bursts[i] = b;
            }
        }

        private void SpawnNumber(UnitView2D v, string text, Color color)
        {
            SpawnNumberInternal(v, text, color, 1f);
        }

        /// <summary>一次受击的视觉反馈：飘伤害数字 + 白闪 + 沿受击方向踹一脚。</summary>
        private void ApplyDamageVisual(UnitView2D target, int amount, bool isMelt, bool reaction, bool crit, Vector2 actorHome)
        {
            if (target == null) return;

            // ★ 冲锋还没顶到目标 ⇒ 先压住数字与墨点（冲到"贴脸"那一刻再落）
            float remain = LungeRemainOfActing();
            if (remain > 0.001f)
            {
                _deferredDmg.Add(new DeferredDmg
                {
                    V = target, Amount = amount, Melt = isMelt, Reaction = reaction, Crit = crit,
                    ActorHome = actorHome, Due = _animT + remain,
                });
                return;      // ⛔ 白闪/顶退也一起押后：顶到位才算"打到"
            }
            SpawnDamageNumber(target, amount, isMelt, reaction, crit);
            ApplyHitReaction(target, actorHome, crit ? 0.16f : 0.10f);
        }

        /// <summary>命中反应：白闪 + 顶退（冲锋押后的伤害到期时调用）。</summary>
        private void ApplyHitReaction(UnitView2D target, Vector2 actorHome, float flashAlpha = 0.10f)
        {
            if (target == null || target.Body == null) return;
            target.Body.color = Color.white;
            target.HitFlash = 0.10f;
            target.ShakePulse = 0.16f;
            Vector2 dir = target.HomePos - actorHome;
            if (dir.sqrMagnitude > 0.0001f) target.KickVec = dir.normalized * 0.22f;
            target.KickT = 0.12f;
        }

        /// <summary>当前行动者是否还在"前冲"阶段；返回剩余秒数（0 = 已贴脸/没在冲）。</summary>
        private float LungeRemainOfActing()
        {
            if (string.IsNullOrEmpty(_actingId)) return 0f;
            if (!_views.TryGetValue(_actingId, out var a) || a == null) return 0f;
            if (!a.LungeActive) return 0f;
            if (a.LungeElapsed >= LungeForward) return 0f;      // 已经顶到了
            return Mathf.Max(0f, LungeForward - a.LungeElapsed) + 0.02f;
        }

        /// <summary>大招特写结束后，把押后的伤害/暴击反馈一次性补出来（起爆那一下）。</summary>
        private void FlushPendingDmg()
        {
            if (_pendingCritFloat != null)
            {
                SpawnNumber(_pendingCritFloat, "暴击!", new Color(1f, 0.85f, 0.35f));
                _pendingCritFloat = null;
            }
            for (int i = 0; i < _pendingDmg.Count; i++)
            {
                var p = _pendingDmg[i];
                ApplyDamageVisual(p.View, p.Amount, p.Melt, p.Reaction, p.Crit, p.ActorHome);
            }
            _pendingDmg.Clear();
        }

        private void SpawnDamageNumber(UnitView2D v, int amount, bool isMelt, bool reaction, bool crit)
        {
            // ★ T4 受击墨点溅射（2026-10-24）：命中点上炸一小团墨，和数字一起出现。
            //   用元素色偏暗的墨，暴击多两颗、稍大；同屏最多 6 个（由 InkBurst 自带寿命自然回收）。
            if (v != null && v.Root != null)
            {
                var hitPos = v.Root.transform.position + new Vector3(0f, 0.55f, -0.5f);
                var baseCol = isMelt ? BattlePalette.GoldRich : BattlePalette.Crimson;
                var ink = new Color(baseCol.r * 0.35f, baseCol.g * 0.35f, baseCol.b * 0.35f, 0.92f);
                InkBurst.Spawn(hitPos, crit ? 7 : 4, crit ? 0.62f : 0.45f, ink, 26);
                if (v.ShieldMat != null) v.ShieldMat.SetFloat("_HitPhase", 1f);   // 护盾膜涟漪
            }
            // 暴击：字号放大 1.6× + 换金白色；融冰保持鎏金；元素反应用紫罗兰高亮。
            // ★ 2026-10-17：反应**不再**在数字前加「反应!」——
            //   反应名现在由 SpawnReactionFx 的大字横幅负责（蒸发/感电/碎冰…），
            //   数字再喊一遍"反应"是重复的噪声，而且那串字一宽就和横幅叠在一起。
            float scale = crit ? 1.6f : (reaction ? 1.2f : 1f);
            Color col = crit ? new Color(1f, 0.92f, 0.55f)
                        : (isMelt ? BattlePalette.GoldRich
                        : (reaction ? new Color(0.72f, 0.50f, 1f)   // 紫：元素反应
                        : BattlePalette.Crimson));
            SpawnNumberInternal(v, amount.ToString(), col, scale);
        }

        private void SpawnNumberInternal(UnitView2D v, string text, Color color, float baseScale,
                                         float life = 0.7f, float raise = 0f)
   {
            if (!FxDamageNumbers) return;   // 设置：伤害/状态飘字
            var go = new GameObject("Dmg");
            go.transform.SetParent(transform, false);
            // raise：同一次演出里的多行字（反应名/属性组合/伤害）要用不同高度分层，
            //   否则全叠在同一个锚点上（实测「蒸发」「火×水」「504」三行糊成一团）。
            go.transform.localPosition = v.Root.transform.position + new Vector3(0f, 1.02f + raise, -1f);
            go.transform.localScale = Vector3.one * baseScale;   // 暴击更大（弹出动画在此基础上叠加）
            var f = LegacyFont();
            var tm = go.AddComponent<TextMesh>();
            if (f != null)
            {
                tm.font = f;
                go.GetComponent<MeshRenderer>().sharedMaterial = f.material;
            }
            tm.text = text;
            // ★ 2026-10-24 用户："数字再大一点" —— 0.045(≈0.29 世界高) → 0.062(≈0.40)
            tm.characterSize = 0.062f;
            tm.fontSize = 64;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
if (life < 0.2f) life = 0.2f;
            _floaters.Add(go, life, baseScale);
    }

        /// <summary>附着圆环：4 段圆弧（终末地式），点亮 filled 段，段间留缺口。</summary>
        private static Sprite AuraRing(int filled)
        {
            if (_auraRings == null) _auraRings = new Sprite[5];
            filled = Mathf.Clamp(filled, 0, 4);
            if (_auraRings[filled] != null) return _auraRings[filled];

            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color[S * S];
            float cx = S * 0.5f, cy = S * 0.5f;
            float rOut = S * 0.46f, rIn = S * 0.36f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 0.5f;
                    int seg = Mathf.Clamp((int)(ang * 4f), 0, 3);
                    float segFrac = ang * 4f - seg;
                    bool inRing = r >= rIn && r <= rOut;
                    bool inGap = segFrac < 0.10f || segFrac > 0.90f;
                    Color c = new Color(1f, 1f, 1f, 0f);
                    if (inRing && !inGap)
                        c = seg < filled ? new Color(1f, 1f, 1f, 1f)
                                         : new Color(0f, 0f, 0f, 0.55f);   // 空槽纯黑：染色后仍是黑，对比强
                    px[y * S + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
            _auraRings[filled] = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            return _auraRings[filled];
        }

        /// <summary>附着专用高饱和色（不复用美术调色板，避免水墨风格把颜色压灰）。</summary>
        private static Color AuraColorOf(Element el)
        {
            switch (el)
            {
                case Element.Water: return new Color(0.10f, 0.62f, 1.00f);   // 水：亮天蓝
                case Element.Fire:  return new Color(1.00f, 0.40f, 0.10f);   // 火：橙红
                case Element.Wood:  return new Color(0.22f, 0.82f, 0.35f);   // 木：翠绿
                case Element.Metal: return new Color(1.00f, 0.84f, 0.20f);   // 金：亮黄
                case Element.Earth: return new Color(0.95f, 0.62f, 0.18f);   // 土：琥珀
                default:            return Color.white;
            }
        }

        /// <summary>
        /// 魇化表现（2026-11-02）：敌方**普通/精英异兽**立绘压成暗紫，
        /// 与守关首领本体（保留原色）一眼区分。
        /// ⛔ 必须从 BaseColor 每帧重算，直接乘当前 color 会逐帧叠黑。
        /// </summary>
        private static void ApplyCorruptTint(UnitView2D v)
        {
            if (v == null || v.Body == null || v.PlayerSide) return;
            bool boss = !string.IsNullOrEmpty(v.DefId) && v.DefId.StartsWith("b_");
            if (boss) return;
            var b = v.BaseColor;
            v.Body.color = new Color(b.r * 0.62f, b.g * 0.52f, b.b * 0.92f, b.a);
        }
    }
}
