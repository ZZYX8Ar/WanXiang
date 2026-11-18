// ============================================================================
//  万相 · 单位视图构建（2026-11-18 从 BattleStage2D.cs 拆出）
//  行为零变化：仅移动方法。共享字段在 Fields.cs，partial 内互通。
// ============================================================================
using System.Collections.Generic;
using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Battle.Presentation
{
    public sealed partial class BattleStage2D
    {
        private void BuildUnits() { BuildUnitsFor(_state.AllUnitsEver); }

        /// <summary>
        /// 按名册建 view。<paramref name="units"/> 平时传 <c>AllUnitsEver</c>（开场名册 + 中途增员）；
        /// 中途增员也可以只传**单个单位**（见 <see cref="EnsureView"/>）。
        ///
        /// ★ 中途增员（召唤 / 分身）的 view **先建好、但先隐藏**，等它在帧流里第一次出现
        ///   （= 真的入场那一刻）再显示，见 <see cref="ApplyFrame"/>。
        /// ⚠⚠ **但只靠"开演时预建"是不够的**：手动模式（非 PvP 的正常战斗）下
        ///   <c>BattlePlayback</c> 构造函数只 <c>AdvanceSim()</c> 跑到**第一个决策点**，
        ///   开演那一刻冰晶还不在名册里 ， 预建无从谈起（用户 2026-10-04 报"还是什么都没有"就是这个原因）。
        ///   ， 所以 <see cref="ApplyFrame"/> 里还有一条 <see cref="EnsureView"/> 惰性补建兜底。
        /// </summary>
        private void BuildUnitsFor(IReadOnlyList<BattleUnit> units)
        {
            var catalog = FindCatalog();
            bool hasFrame0 = _state.Frames.Count > 0 && _state.Frames[0].Changed != null;
            var bornAtStart = new HashSet<string>();
            if (hasFrame0)
                for (int k = 0; k < _state.Frames[0].Changed.Length; k++)
                    bornAtStart.Add(_state.Frames[0].Changed[k].UnitId);
            int unitIndex = 0;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (!u.Pos.IsValid) continue;

                var view = new UnitView2D
                {
                    UnitId = u.RuntimeId,
                    DefId = u.Def != null ? u.Def.Id : null,
                    Hp = u.Hp, MaxHp = u.MaxHp,
                    Alive = u.IsAlive,
                };

                var root = new GameObject($"Unit_{u.RuntimeId}");
                root.transform.SetParent(transform, false);
                bool player = u.Side == TeamSide.Player;
                view.HomePos = CellPos(player, u.Pos.Index);
                view.CellIndex = u.Pos.Index;
                view.PlayerSide = player;
                // Pivot=底部中 + Y 上偏 = 立绘脚踩格子中心（遮挡关系由 sortingOrder 管）
                root.transform.localPosition = new Vector3(view.HomePos.x, view.HomePos.y + FootOffset, 0f);
                view.Root = root;

                // 立绘：SpriteCatalog 按 BeastDef.Id 查；查不到回退五行色块
                var bodySr = root.AddComponent<SpriteRenderer>();
                // ★ 2026-11-02 觉醒立绘：已进化的我方异兽换金色描边/流光材质
                // ⛔ 不能用 RunSave（Battle 程序集不引用 Run）—— 集合由组队侧每场重建，见工厂 Clear()
                // B5-2：异色名单走 BattleState.ShinyIds（工厂折算，Battle 程序集仍不引用 Meta）
                bool isShiny = player && u.Def != null && _state != null && _state.ShinyIds != null && _state.ShinyIds.Contains(u.Def.Id);
                if (player && u.Def != null && (AwakenedDefIds.Contains(u.Def.Id) || isShiny))
                {
                    var awSh = Shader.Find("WanXiang/AwakenSprite");
                    if (awSh != null)
                    {
                        var am = StageMaterialFactory.Create(StageMaterialKind.Awaken,
                            new StageMaterialContext { Shader = awSh });
                        if (am != null)
                        {
                            bodySr.sharedMaterial = am;
                            // B5-2：品级表现（描边色/呼吸/神品流光）—— 唯一收口 BeastVisual
                            BeastVisual.ApplyTier(am, (int)u.Def.Rarity);
                            // B5-2：异色叠加（hue ±18° + 周期扫光；非觉醒异色 _Amount=0 只走异色通道）
                            if (isShiny) BeastVisual.ApplyShiny(am);
                        }
                    }
                }

                // ★ 2026-11-02 魇化立绘：敌方普通/精英的**本体材质**换成 CorruptSprite
                //   （裂痕 + 脏污 + 紫染 + 脉动都在 shader 里，同 mesh 同 uv 不会错位）。
                // ⛔ 2026-11-02 紧急回退：换材质后敌方立绘**整个不显示**（shader 未通过验证），
                //   先把开关关掉，恢复用顶点色染色保证可见；shader 验证通过后再打开。
                const bool UseCorruptShader = true;
                if (UseCorruptShader && !player && u.Def != null && !u.Def.Id.StartsWith("b_"))
                {
                    var corruptSh = Shader.Find("WanXiang/CorruptSprite");
                    if (corruptSh != null)
                    {
                        var cm = StageMaterialFactory.Create(StageMaterialKind.Corrupt,
                            new StageMaterialContext { Shader = corruptSh });
                        if (cm != null)
                        {
                            bodySr.sharedMaterial = cm;
                            view.CorruptMat = cm;
                        }
                    }
                }

                var sprite = catalog != null ? catalog.Get(u.Def.Id) : null;
                // ★ 秘境首领：这些 id 不在 SpriteCatalog 里，靠外部注册的覆盖图
                if (PortraitOverrides.TryGetValue(u.Def.Id, out var ov) && ov != null) sprite = ov;
                // ⚠ 测试阵容 Id（pw/eg 等）对不上 catalog 里的真实异兽 id ——
                //   查不到就**按 catalog 序号分配**：Player 取前半、Enemy 取后半，
                //   这样立绘立刻出现在棋盘上（后续真实内容接入后 Id 会对上）。
                //
                // ⛔ 但**召唤物必须跳过这段兜底**（id 以 summon_ 开头，如熔岩幼体/镜影/
                //   守卫卵/造化兽）：它们没有专属立绘，若走这里会被随机分配成**别的异兽** ——
                //   玩家看到战场上凭空多出一只"某异兽"，会以为是第二个 boss / 敌人
                //   （白魍双子"只有一个立绘"的实测就是这类症状）。
                //   无专属立绘的召唤物统一走下面的五行色块，语义明确 = 临时召唤物。
                bool isSummon = u.Def.Id != null && u.Def.Id.StartsWith("summon_");
                if (sprite == null && !isSummon && catalog != null && catalog.Entries.Count > 0)
                {
                    int idx = catalog.Entries.Count > 5 && !player
                        ? 5 + (unitIndex % (catalog.Entries.Count - 5))
                        : unitIndex % System.Math.Min(5, catalog.Entries.Count);
                    sprite = catalog.Entries[idx].Body;
                }
                if (sprite != null)
                {
                    bodySr.sprite = sprite;
                    float h = sprite.bounds.size.y;
                    if (h > 0.001f)
                    {
                        // ★ 首领 2.5 倍身量（用户 2026-10-03："boss 整的大一点，起码是普通异兽的 2-3 倍"）。
                        //   ⚠ 放大后可能超出单格视觉范围 —— 中宫（格 4）左右都有空档，正是给它腾的；
                        //   sortingOrder 用行号，中宫=row1 ， 会盖住后排(row0)、被前排(row2)压，符合"大个子"直觉。
                        //
                        // ⚠⚠ 判据必须用 `BossScaleIds`（= 14 守关本体 + 双子），**不能**用 `IsBoss(id)`：
                        //   白魍双子复用 `b_suren` 当立绘 id（否则 summon_ 前缀查不到图会"乱分配"），
                        //   它也是 b_ 前缀 —— 用 IsBoss 会把**所有** b_ 前缀都放大（含真分身，太宽）。
                        //   ★ 用户 2026-10-03 更正："他们两个都是 Boss 啊，不是召唤出来的，他们是一起出现的"
                        //     ， 双子（b_suren）与本体同级放大，不再用 1.15 的小尺寸。
                        // ★ 秘境首领（rc_*）也按首领身量（它们不在 BossScaleIds 里）
                        bool isBossTier = u.Side == TeamSide.Enemy
                                          && (BossScaleIds.Contains(u.Def.Id) || IsRealmChampion(u.Def.Id));
                        float unitH = isBossTier ? UnitHeight * BossScaleMul
                                  : IsBossSidekick(u.Def.Id) ? UnitHeight * BossSidekickScaleMul
                                  : UnitHeight;
                        float k = unitH / h;   // 统一按显示高度换算缩放
                        root.transform.localScale = new Vector3(k, k, 1f);
                    }
                }
                else
                {
                    // fallback 色块：96px PPU=100 = 0.96 世界单位，直接可用
                    bodySr.sprite = SolidSprite(BattlePalette.OfElement(u.Element), 96, 128, 3,
                                                new Color(0.16f, 0.13f, 0.09f));
                }
                bodySr.sortingOrder = u.Pos.Index / 3;   // row 0=后 1=中 2=前
                // 敌方整体镜像：立绘原画统一朝右，敌阵镜像后朝左 ， 面向我方，自然。
                // ⛔ **例外：素刃（白魍双子）不镜像**。它的原画是**刻意朝左**画的
                //   （早期为双子"背靠背"并排设计的），再统一镜像就变成朝右 ， **背对玩家**
                //   （用户实测："b_suren 这个的朝向反了"）。它现在站后排中(格7)，
                //   与中宫本体是前后关系，本来就该朝前 ， 跳过镜像即可与其它敌方朝向一致。
                bodySr.flipX = !player && u.Def.Id != BossCatalog.TwinBeastId;
                // 敌方压暗一档（§4.4 敌我同源 + 浊化的轻量版）
                if (!player) bodySr.color = new Color(0.72f, 0.72f, 0.80f);
                view.BodyMaterial = bodySr.sharedMaterial;
                view.Body = bodySr;
                view.BaseColor = bodySr.color;

                // ---- 状态外观层（冰封 / 束缚 / 燃烧…）----
                //  挂在 **Body 之下** ， 自动跟随立绘的位置、缩放与左右镜像，不用另算。
                //  sortingOrder 比立绘大 1 ， 盖在立绘上。缺图时 `SetOverlay2D` 用半透明色块兜底。
                //  ★ 2026-10-09 用户定案：StatusArtEnabled=false 时**连对象都不创建**——
                //    防止"Play 中途热重载/异常中断后旧视觉残留"（SetOverlay2D 的 enabled 闸门
                //    只在每帧 Tick 时生效，建对象时就不给才是彻底的关）。
                if (StatusArtEnabled)
                {
                    var ovGo = new GameObject("Overlay");
                    ovGo.transform.SetParent(bodySr.transform, false);
                    var ovSr = ovGo.AddComponent<SpriteRenderer>();
                    ovSr.sprite = SolidSprite(Color.white, 32, 64, 0, Color.white);
                    ovSr.sortingOrder = bodySr.sortingOrder + 1;
                    ovSr.enabled = false;
                    view.Overlay = ovSr;
                }

                // ★ 鼠标命中框（世界空间）—— 用户定案：悬停*棋盘上的异兽*看状态列表。
                //   尺寸取立绘的世界尺寸（没立绘时用兜底块）。
                //   ⚠ 命中判定用的是 <c>collider.bounds</c> 做**代码点测试**，不是 Physics2D.OverlapPoint：
                //     少一层"2D 物理设置 / 层 / 查询开关"的依赖，而命中框在 Inspector 里照样可见可调。
                var hitCol = root.AddComponent<BoxCollider2D>();
                hitCol.isTrigger = true;
                // ⚠ 尺寸必须用 **SpriteRenderer.bounds（渲染器实际世界尺寸）**换算回 root 的局部空间：
                //   `sprite.bounds.size` 是**贴图原始尺寸**（本例 10.24），而立绘被缩放显示成 ~1.5 ，
                //   直接拿它当命中框会**误选邻居**（格子间距才 1.75）。bounds 由渲染器算，不依赖物理。
                {
                    var b = bodySr.bounds;
                    Vector3 sc = root.transform.lossyScale;
                    float sx = Mathf.Max(0.0001f, Mathf.Abs(sc.x)), sy = Mathf.Max(0.0001f, Mathf.Abs(sc.y));
                    hitCol.size = new Vector2(Mathf.Max(0.5f, b.size.x / sx), Mathf.Max(0.6f, b.size.y / sy));
                    hitCol.offset = new Vector2(0f, (b.center.y - root.transform.position.y) / sy);
                }

                // ★★ 头顶信息容器（2026-10-20 定案：**以九宫格为基准**）
                //   单元格尺寸恒定（1.75×1.26），所以头顶信息也按**世界尺寸**摆：
                //   位置 ÷ k、缩放取 1/k ⇒ 血条/名字/状态图标在任何单位上都一样大、
                //   离格中心一样高。用户实测过的"巨大/飘远"是因为当时给了 3.4 世界宽
                //   这种离谱数值，不是归一化本身的问题。
                float kHead = Mathf.Max(0.0001f, Mathf.Abs(root.transform.lossyScale.y));
                var headGo = new GameObject("Head");
                headGo.transform.SetParent(root.transform, false);
                headGo.transform.localPosition = new Vector3(0f, HpBarY / kHead, 0f);
                headGo.transform.localScale = new Vector3(1f / kHead, 1f / kHead, 1f);
                view.Head = headGo.transform;

                view.HpBg = MakeChildSprite(headGo, "HpBg", new Color(0.10f, 0.08f, 0.06f),
                                            new Vector2(HpBarWidth, HpBarBackHeight), Vector2.zero, 2);
                view.HpFill = MakeChildSprite(headGo, "HpFill", BattlePalette.Vital,
                                              new Vector2(HpBarWidth - 0.06f, HpBarBackHeight - 0.06f),
                                              Vector2.zero, 3);
                SetHpBar2D(view, u.Hp, u.MaxHp);

                // ★ 2026-10-30：首领血条加**阶段刻度**（75/50/25% 之类）
                if (u.Side == WanXiang.Battle.Core.TeamSide.Enemy && u.Def != null && !string.IsNullOrEmpty(u.Def.Id))
                {
                    var ticks = BossPhaseTicks.For(u.Def.Id);
                    if (ticks != null)
                    {
                        float inner = Mathf.Max(0.05f, HpBarWidth - 0.06f);
                        for (int ti = 0; ti < ticks.Length; ti++)
                        {
                            var tick = MakeChildSprite(headGo, "PhaseTick" + ti,
                                                       new Color(0.10f, 0.08f, 0.06f, 0.85f),
                                                       new Vector2(0.014f, HpBarBackHeight + 0.03f),
                                                       new Vector2((ticks[ti] - 0.5f) * inner, 0f), 5);
                            if (tick != null) tick.transform.localPosition = new Vector3((ticks[ti] - 0.5f) * inner, 0f, 0f);
                        }
                    }
                }

                // ---- 元素附着：脚下一圈光（2026-11-01 v10）----
                //   用户："脚下的这个圈能不能改成附着" —— 状态光环那套视觉本来就好用，
                //   直接复用同一 shader（Battle/StatusAura）与尺寸算法，**比状态光圈大一圈**，
                //   颜色 = 元素色，亮度随层数。两层圈同时存在：内圈状态、外圈附着。
                var auraSh2 = Resources.Load<Shader>("Battle/StatusAura");
                var auraHex = Resources.Load<Sprite>("Battle/FX_shield_hex");
                var auraNoise = Resources.Load<Sprite>("Battle/FX_motion_fog");
                if (auraSh2 != null && auraHex != null && auraSh2.isSupported && bodySr != null && bodySr.sprite != null)
                {
                    var ag = new GameObject("AuraFoot");
                    ag.transform.SetParent(bodySr.transform, false);
                    var asr = ag.AddComponent<SpriteRenderer>();
                    asr.sprite = auraHex;
                    asr.sortingOrder = bodySr.sortingOrder - 2;   // 比状态光环再靠后一点

                    // R2：布局计算收口到纯函数（宽度/高度倍率是这两类光环唯一的差异）
                    var auraLayout = StageAuraLayout.Compute(bodySr.sprite,
                        bodySr.transform.lossyScale, auraHex, 1.55f, 0.95f);
                    ag.transform.localPosition = auraLayout.LocalPosition;
                    ag.transform.localScale = auraLayout.LocalScale;

                    var amat = StageMaterialFactory.Create(StageMaterialKind.Aura,
                        new StageMaterialContext {
                            Shader = auraSh2,
                            NoiseTexture = auraNoise != null ? auraNoise.texture : null });
                    if (amat != null)
                    {
                        asr.material = amat;
                        ag.SetActive(false);
                        view.AuraDot = asr;
                        view.AuraMat = amat;
                    }
                }

                // 4 段小圈：只负责"几层"（亮段数），放在血条右侧，尺寸固定 0.22 世界单位
                var stackGo = new GameObject("AuraStacks");
                stackGo.transform.SetParent(headGo.transform, false);
                stackGo.transform.localPosition = new Vector3(HpBarWidth * 0.5f + 0.28f, 0.02f, 0f);
                float sk = Mathf.Max(0.0001f, Mathf.Abs(headGo.transform.lossyScale.y));
                float sSize = 0.27f / sk;
                stackGo.transform.localScale = new Vector3(sSize, sSize, 1f);
                var stackSr = stackGo.AddComponent<SpriteRenderer>();
                stackSr.sprite = AuraRing(1);
                stackSr.sortingOrder = 9;
                stackSr.color = new Color(1f, 1f, 1f, 0f);
                stackGo.SetActive(false);
                view.AuraStacksRing = stackSr;

                // ---- 护盾指示（血条**右侧**）----
                //  护盾**不会自然消退**（只被伤害消耗）， 不显示就等于一管隐形的血。
                // ★ 2026-10-20 用户："护盾的位置还是差一点" —— 直接压进血条**右端内侧**：
                //   与血条同高、不出格、不随列位飘（原来挂在血条右侧外面，靠边的格子会溢出）。
                // ★ 2026-10-23 二次修正：改到**血条正上方居中**（贴左端永远像"压着血条"）
                float shX = 0f;
                view.ShieldPlate = MakeChildSprite(headGo, "ShieldPlate", ShieldColor,
                                                   new Vector2(ShieldPlateW, ShieldPlateH),
                                                   new Vector2(shX, ShieldLiftY), 6);   // ★ 抬到血条上方（2026-10-23）
                var shTxtGo = new GameObject("ShieldText");
                shTxtGo.transform.SetParent(headGo.transform, false);  // ⛔ 挂 Head（不挂蓝牌，避免继承牌面缩放）
                shTxtGo.transform.localPosition = new Vector3(shX, ShieldLiftY, -0.02f);
                view.ShieldText = shTxtGo.AddComponent<TextMesh>();
                var shFont = LegacyFont();   // ⚠ 本方法里 `f` 在后面才声明，这里自己取一次
                if (shFont != null)
                {
                    view.ShieldText.font = shFont;
                    var shMr = shTxtGo.GetComponent<MeshRenderer>();
                    if (shMr != null) { shMr.sharedMaterial = shFont.material; shMr.sortingOrder = 7; }
                }
                view.ShieldText.text = "";
                view.ShieldText.characterSize = ShieldTextSize;
                view.ShieldText.fontSize = 48;
                view.ShieldText.anchor = TextAnchor.MiddleCenter;
                view.ShieldText.alignment = TextAlignment.Center;
                view.ShieldText.color = ShieldTextColor;
                SetShield2D(view, 0);   // 开场无护盾 ， 收起

                var nameGo = new GameObject("Name");
                nameGo.transform.SetParent(headGo.transform, false);
                // Head 在 HpBarY 高度 ⇒ 容器内 y 用 (NameY - HpBarY)，世界高度与原来一致
                nameGo.transform.localPosition = new Vector3(0f, NameY - HpBarY, 0f);
                var f = LegacyFont();
                view.NameText = nameGo.AddComponent<TextMesh>();
                if (f != null)
                {
                    view.NameText.font = f;
                    nameGo.GetComponent<MeshRenderer>().sharedMaterial = f.material;
                }
                view.NameText.text = u.DisplayName;
                view.NameText.characterSize = NameSize;
                view.NameText.fontSize = 48;
                view.NameText.anchor = TextAnchor.LowerCenter;
                view.NameText.alignment = TextAlignment.Center;
                view.NameText.color = new Color(0.16f, 0.13f, 0.09f);

                // ---- Boss 机制提示（玄溟冰晶期"还剩 N 回合"等）----
                //  做法与 NameText 同一套（世界空间 TextMesh，美术换字体/字号/描边改这里即可）。
                //  ⛔ 内容一律来自**帧流**（UnitSnapshot.StateHint），不要在这里读 _state 里的单位：
                //     自动模式 state 是整场跑完的终局态（终局提示会从第 1 帧就贴上）；
                //     手动模式又是分片推进的（读到的"当前值"与回放进度对不上）。
                var hintGo = new GameObject("Hint");
                hintGo.transform.SetParent(root.transform, false);
                // 文字**世界尺寸恒定**（Boss 放大 2.5× 时不被一起拉飞），位置贴在立绘上沿之上。
                float rsHint = Mathf.Max(0.001f, root.transform.localScale.y);
                float spriteWorldH = bodySr.sprite != null ? bodySr.sprite.bounds.size.y * rsHint : 1f;
                hintGo.transform.localScale = Vector3.one / rsHint;
                hintGo.transform.localPosition = new Vector3(0f, (spriteWorldH * 0.5f + HintRaise) / rsHint, 0f);
                view.Hint = hintGo.AddComponent<TextMesh>();
                if (f != null)
                {
                    view.Hint.font = f;
                    hintGo.GetComponent<MeshRenderer>().sharedMaterial = f.material;
                }
                view.Hint.text = "";
                view.Hint.characterSize = 0.075f * Mathf.Max(0.01f, HintSize);
                view.Hint.fontSize = 48;
                view.Hint.anchor = TextAnchor.LowerCenter;
                view.Hint.alignment = TextAlignment.Center;
                view.Hint.color = HintColor;

                // ---- 起手预警文字（比 Boss 提示**更低**，位置贴着提示条，不与它打架）----
                var telGo = new GameObject("Telegraph");
                telGo.transform.SetParent(root.transform, false);
                telGo.transform.localScale = Vector3.one / rsHint;
                // ⚠ 位置：贴**立绘上沿**，而不是按 HintRaise 比例往上顶 ——
                //   实测按比例会飘到棋盘上方、和"行动顺序"面板打架（截图确认过）。
                //   用一个小的**固定世界高度**留白，任何体型都稳。
                telGo.transform.localPosition = new Vector3(0f, (spriteWorldH * 0.5f + TelegraphRaise) / rsHint, 0f);
                view.Telegraph = telGo.AddComponent<TextMesh>();
                if (f != null)
                {
                    view.Telegraph.font = f;
                    telGo.GetComponent<MeshRenderer>().sharedMaterial = f.material;
                }
                view.Telegraph.text = "";
                view.Telegraph.characterSize = 0.082f * Mathf.Max(0.01f, HintSize);
                view.Telegraph.fontSize = 56;
                view.Telegraph.anchor = TextAnchor.LowerCenter;
                view.Telegraph.alignment = TextAlignment.Center;
                view.Telegraph.color = TelegraphColor;

                // ---- 破绽 / 失衡条（2026-10-17）----
                //  挂在 **HP 条正下方**（同一套 root 本地坐标，缩放行为一致）。
                //  数据一律来自帧流（UnitSnapshot.FlawText / FlawProgress / UnbalancedTurns）。
                //  平时隐藏；出现破绽进度后显示青色条；进入失衡整条变金并显示剩余回合。
                {
                    float flawY = HpBarY - 0.52f;                    // HP 条正下方（含 0.72 高的间隙）
                    float flawLocalY = flawY - HpBarY;                 // Head 内相对高度
                    view.FlawBg = MakeChildSprite(headGo, "FlawBg", FlawBackColor,
                                                  new Vector2(HpBarWidth, FlawBarHeight),
                                                  new Vector2(0f, flawLocalY), 4);
                    view.FlawFill = MakeChildSprite(headGo, "FlawFill", FlawFillColor,
                                                    new Vector2(HpBarWidth - 0.06f, FlawBarHeight - 0.05f),
                                                    new Vector2(0f, flawLocalY), 5);
                    var flGo = new GameObject("FlawLabel");
                    flGo.transform.SetParent(headGo.transform, false);  // ⛔ 挂 Head，不挂条
                    // ⚠ 文字画在**条内**（居中、亮字）：条浮在立绘身上，深浅不定，
                    //   "条上留白放深字"会被深色立绘吃掉（实测赤魃身上看不见）；
                    //   底板是深色(0.10,0.08,0.06)，亮字压上去任何立绘前都读得清。
                    flGo.transform.localPosition = new Vector3(0f, flawLocalY + FlawBarHeight * 0.5f, -0.02f);
                    view.FlawLabel = flGo.AddComponent<TextMesh>();
                    if (f != null)
                    {
                        view.FlawLabel.font = f;
                        flGo.GetComponent<MeshRenderer>().sharedMaterial = f.material;
                    }
                    view.FlawLabel.text = "";
                    view.FlawLabel.characterSize = 0.090f * Mathf.Max(0.01f, HintSize);   // 用户："看不清字"
                    view.FlawLabel.fontSize = 64;
                    view.FlawLabel.anchor = TextAnchor.MiddleCenter;
                    view.FlawLabel.alignment = TextAlignment.Center;
                    view.FlawLabel.color = FlawLabelColor;
                    // ⛔ TextMesh 默认 sortingOrder=0，会被条(bg=4/fill=5)盖住 —— 实测文字完全不可见。
                    //   提到 6（条之上），文字才算"写在条上"。
                    flGo.GetComponent<MeshRenderer>().sortingOrder = 6;
                    SetFlawBar2D(view, "", -1f, 0);                  // 初始收起
                }

                int h2 = u.RuntimeId != null ? u.RuntimeId.GetHashCode() : i * 7919;
                view.Phase = (Mathf.Abs(h2) % 1000) / 1000f * Mathf.PI * 2f;

                // ---- 状态图标排（头顶）----
                //  数据只来自帧流的 UnitSnapshot.StatusIds（`id:层数|id:层数`），
                //  ⛔ 不读 _state 里的单位（同 Hint 的理由：自动模式是终局态、手动模式是分片推进）。
                {
                    var rowGo = new GameObject("StatusRow");
                    // ★ 挂进 Head 容器（世界缩放已归一）：状态图标永远在血条上方 0.72，
                    //   随每帧自愈一起校正，任何单位高度一致（用户定案：图标/血条/名字 自上而下）。
                    rowGo.transform.SetParent(headGo.transform, false);
                    rowGo.transform.localScale = Vector3.one;
                    // 以九宫格为基准：状态图标固定在血条上方 0.30 世界高（head 已归一化）
                    rowGo.transform.localPosition = new Vector3(0f, 0.34f, 0f);
                    view.StatusRow = rowGo;
                    int slots = (StatusIcons != null && StatusIcons.Length > 0) ? Mathf.Min(8, StatusIcons.Length) : 1;
                    view.StatusPlates = new SpriteRenderer[slots];
                    view.StatusLabels = new TextMesh[slots];
                    float plateScale = StatusIconSize / 0.32f;                      // 底图是 32px @ PPU100
                    for (int s = 0; s < slots; s++)
                    {
                        // ⛔ 底板与文字**必须分放两个子对象**：同一个 GameObject 上先 AddComponent<SpriteRenderer>
                        //    再加 TextMesh 时，AddComponent<TextMesh>() 会**静默返回 null**（实测），
                        //    随后 tm.xxx 直接 NPE。名字那处之所以没事，是因为它那个对象只有 TextMesh。
                        var slotGo = new GameObject("S" + s);                   // 整格的容器（负责缩放/定位）
                        slotGo.transform.SetParent(rowGo.transform, false);
                        slotGo.transform.localScale = new Vector3(plateScale, plateScale, 1f);

                        var plateGo = new GameObject("Plate");
                        plateGo.transform.SetParent(slotGo.transform, false);
                        var sr = plateGo.AddComponent<SpriteRenderer>();
                        sr.sprite = SolidSprite(Color.white, 32, 32, 2, new Color(0.10f, 0.08f, 0.06f));
                        sr.sortingOrder = 12;

                        var labGo = new GameObject("Label");
                        labGo.transform.SetParent(slotGo.transform, false);
                        var tm = labGo.AddComponent<TextMesh>();
                        var mr = labGo.GetComponent<MeshRenderer>();
                        if (f != null && mr != null) { tm.font = f; mr.sharedMaterial = f.material; }
                        // ⛔ 文字的 sortingOrder 必须**高于底板**（底板 12）—— 否则字被底板盖住
                        //   表现为"字看不见"（字号大时因为溢出底板边缘反而看得见，会误导成字号问题）。
                        if (mr != null) mr.sortingOrder = 14;
                        // 字号实测（TextMesh: characterSize × fontSize=48）：0.022→0.165 宽 / 0.032→0.24 宽 /
                        // 0.05→0.375 宽（已超过 0.36 的底板）， 取 0.032（约 0.27，含格容器缩放）。
                        tm.characterSize = 0.032f * Mathf.Max(0.01f, StatusLabelSize);
                        tm.fontSize = 48;
                        tm.anchor = TextAnchor.MiddleCenter;
                        tm.alignment = TextAlignment.Center;
                        tm.color = Color.white;
                        tm.text = "";
                        slotGo.SetActive(false);
                        view.StatusPlates[s] = sr;
                        view.StatusLabels[s] = tm;
                    }
                }

                view.Def = u.Def;
                _views[u.RuntimeId] = view;
                // 开场就有的单位立刻可见；中途增员先隐藏 —— 由 ApplyFrame 在它第一次出现在帧流里时唤起。
                if (hasFrame0 && !bornAtStart.Contains(u.RuntimeId)) root.SetActive(false);
                unitIndex++;
            }
        }
    }
}
