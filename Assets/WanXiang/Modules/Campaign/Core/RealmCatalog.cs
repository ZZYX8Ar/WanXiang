// ============================================================================
//  秘境（Realm）—— 2026-10-16《精英首领与秘境设计 v1.0》第 5~7 章
//  ---------------------------------------------------------------------------
//  定位：**主线途中的高风险支线**，不是独立的第二款游戏。三条硬约束：
//    ① 产出直接进本局背包，**不进图鉴**（这是它区别于独立肉鸽模式的关键）
//    ② 打完**必回到原路线图同一节点**，路线图状态完全保留
//    ③ 每局上限 2 次 —— 它是「选择」不是「日常」
//
//  ★ 为什么它便宜：秘境**不新增结算路径**，只是三处已有机制的参数组合：
//    · enemyScale → 乘在 DeployEntry.StatMul（既有的敌方倍率通道）
//    · 场地效果   → 仿WeatherTurnStartHooks 的回合始钩子
//    · 禁 QTE     → 已有 ParryEnabled 开关（关 QTE 是零副作用路径）
//
//  ⚠ 最容易漏的一步是**回滚**（见RollbackLines）：场域会改临时状态，
//    返回时必须逐条还原，否则玩家带着「速度 -10%」回主线且再也不会消失。
// ============================================================================

using System.Collections.Generic;

namespace WanXiang.Campaign
{
    /// <summary>场域 id（用稳定字符串，便于存档与自检）。</summary>
    public static class RealmIds
    {
        public const string Marsh = "realm_marsh";    // 苍泽
        public const string Ember = "realm_ember";    // 烬岭
        public const string Gear = "realm_gear";      // 机枢
        public const string Silent = "realm_silent";  // 寂域
        public const string Thorn = "realm_thorn";    // 荆墟
    }

    /// <summary>场地效果的落点（回合始给谁挂什么）。</summary>
    public enum RealmHazardKind
    {
        None = 0,
        /// <summary>给**敌方全体**挂某状态（苍泽：湿；烬岭：灼烧）。</summary>
        FoeStatusAll = 1,
        /// <summary>给**敌方全体**加一层护盾（机枢）。</summary>
        FoeShieldAll = 2,
        /// <summary>给**我方全体**每回合加护盾（寂域禁 QTE 的替代）。</summary>
        SelfShieldAll = 3,
        /// <summary>
        /// 给**我方全体**每回合挂某状态（苍泽的水汽、烬岭的地火）。
        /// <para>⛔⛔ 2026-10-17 用户纠正：场地效果**必须打我方**。
        ///   原来的 <see cref="FoeStatusAll"/> 给敌方挂「湿」，语义上是**帮玩家**
        ///   （我方火攻打进湿身打出蒸发）—— 那不是"场地危害"，是场地福利，方向反了。
        ///   场地 = 这个环境在**折磨我**，所以状态要挂在我方身上。</para>
        /// </summary>
        PlayerStatusAll = 4,
    }

    /// <summary>
    /// 镇守（秘境专属精英）。**每个场域两名**，进图时按节点种子二选一 ——
    /// 同一秘境两次进去可能遇到不同的镇守（用户："只有一个太腻了"）。
    /// <para>⛔ <see cref="MechanicText"/> 必须与 <c>RealmChampionHooks</c> 里
    ///   <see cref="HookId"/> 对应的钩子**逐字一致** —— 写了不实现就是假字。</para>
    /// </summary>
    public sealed class RealmChampionDef
    {
        public string Name;
        public string Epithet;        // 一句人设（立绘提示词也用它定基调）
        public string MechanicText;   // 进战前展示的机制说明
        public string RewardText;     // 击败必得（必须与 <see cref="Eggs"/> 一致）
        public int Eggs = 3;
        public string HookId;         // 机制钩子 id（rc_*，见 Battle/Core/Boss/RealmChampionHooks）
    }

    /// <summary>按**节点种子**从场域的两名镇守里定一个（确定 ⇒ 信息卡与战斗看到的是同一个）。</summary>
    public static class RealmChampionPick
    {
        public static RealmChampionDef Pick(RealmDef realm, int offset, ulong realmSeed)
        {
            if (realm == null || realm.Champions == null || realm.Champions.Length == 0) return null;
            int roll = new WanXiang.Battle.Core.DeterministicRandom(
                realmSeed ^ WanXiang.Battle.Core.CoreMath.Fnv1a("champ:" + realm.Id + ":" + offset)
            ).NextInt(0, realm.Champions.Length);
            return realm.Champions[roll];
        }
    }

    public sealed class RealmDef
    {
        public string Id;
        public string Name;
        public string Element;              // 五行（仅用于展示与配色）
        public string Concept;              // 一句话

        // ---- 强度 ----
        public int Waves;                    // 波数
        /// <summary>秘境内小地图尺寸（设计文档 5.1 的 map_size）。</summary>
        public int MapCols = 9;
        public int MapRows = 7;
        public float EnemyScale;            // 敌方强度倍率（**不吃幕数加成**）
        /// <summary>本域**灵卵产出倍率**（战斗/镇守结算时乘在灵卵上）。改名自 StarRate ——
        ///   星币已删（它只是"灵卵+固定汇率"的中间步骤，用户拍板砍掉）。</summary>
        public float EggRate = 1f;

        // ---- 场地效果 ----
        public RealmHazardKind Hazard;
        public string HazardStatusId;       // 场地挂的状态 id
        public int HazardStacks = 1;
        public int HazardTurns = 2;
        public float HazardShieldPct = 0f;  // 护盾按施法者最大生命 x%

        // ---- 规则 ----
        public bool NoQte;                  // 禁格挡 QTE（寂域）

        /// <summary>
        /// **我方**全体速度乘数（1 = 不变）。这是"场地代价"唯一真正生效的轴。
        /// <para>⛔⛔ 2026-10-17 用户："减速度应该是我方的吧……场地效果和代价都是针对我方的"。
        ///   原来 <c>Cost</c> 只是一句文案，写着"全程速度 -10%，且不能回城补给"——
        ///   而"回城补给"在本工程里**根本不存在**，减速也没有任何代码读取，
        ///   是纯粹糊在界面上的假字。现在改成真字段、真生效。</para>
        /// <para>生效路径：<c>BattleRequestFactory</c> → <c>BattleState.RealmSpeedMulPlayer</c>
        ///   → <c>EffectiveSpeed()</c>（只影响出手排序，只乘我方）。</para>
        /// </summary>
        public float PlayerSpeedMul = 1f;

        // ---- 镇守（秘境专属精英战）----
        /// <summary>本域的两名镇守（进图时按节点种子二选一）。**必须恰好 2 个** ——
        ///   只有一个会腻，三个以上稀释出现率（自检会查）。</summary>
        public RealmChampionDef[] Champions;


        public int AppearFromAct = 1;       // 最早出现幕
        public int Weight = 20;             // 出现权重

        public string Boon;                 // 收益（展示）

        // ⛔ 这里**故意没有** `Cost` 字段（2026-10-17 删）。
        //   原来它是一句手写展示文案，写着"全程速度 -10%，且不能回城补给"——
        //   而"回城补给"本工程根本不存在，减速也没人读，等于一句会说谎的死字符串。
        //   现在代价一律由 `RealmText.Cost()` 从 **PlayerSpeedMul / PenalizesOnQuit 推导**，
        //   想看"这个场域的代价是什么"就去看那两个字段，改字段就等于改文案，不会脱节。

        /// <summary>
        /// 撤离是否**有代价**（2026-10-16 用户："点击离开的时候要弹出面板确定和说明
        /// 离开不会有什么惩罚，后续也可以设计有一下秘境有惩罚"）。
        /// <para>默认 false —— 绝大多数场域随时可以走，撤离只是"放弃没拿到的"，
        /// 不然玩家会因为怕损失而硬撑到团灭，那不是设计意图。</para>
        /// <para>将来给某个场域开 true 时，**必须同时填 <see cref="QuitPenaltyText"/>**
        /// （确认框要把代价原文讲清楚，不能让玩家靠猜）。</para>
        /// </summary>
        public bool PenalizesOnQuit;

        /// <summary>撤离代价的**原文**（PenalizesOnQuit 为 true 时必须有值）。</summary>
        public string QuitPenaltyText;

        /// <summary>撤离要**实扣**的灵卵数（0 = 不扣）。必须在 <see cref="RealmSession.ApplyQuitPenalty"/>
        ///   里真正读到 —— 有文案没扣数就是假代价（本项目已经吃过一次亏）。</summary>
        public int QuitPenaltyEggs = 0;

        public string[] Rooms;              // 地图分区名（展示 / 后续做地图用）
        public string Palette;              // 主题色（#RRGGBB）

        public bool AvailableIn(int act)
        {
            return act >= AppearFromAct;
        }
    }

    public static class RealmCatalog
    {
        public const int MaxPerRun = 2;      // 每局上限（硬约束 ③）

        /// <summary>
        /// 该场域撤离是否**有代价**。默认 false；<b>为 true 却没写文案时按 false 处理</b> ——
        /// 宁可不惩罚，也不能弹一个空白的代价说明（那和骗玩家没区别）。
        /// </summary>
        public static bool PenalizesOnQuit(RealmDef realm)
        {
            return realm != null && realm.PenalizesOnQuit
                && !string.IsNullOrEmpty(realm.QuitPenaltyText);
        }

        private static readonly RealmDef[] Table =
        {
            new RealmDef {
                Id = RealmIds.Marsh, Name = "苍泽", Element = "水",
                Concept = "积水腐草，减速 + 持续伤害，但掉落最好",
                Waves = 3, EnemyScale = 1.00f, EggRate = 1.5f,
                MapCols = 4, MapRows = 5,
                Hazard = RealmHazardKind.PlayerStatusAll,
                HazardStatusId = WanXiang.Battle.Core.StatusCatalog.Wet,
                HazardStacks = 1, HazardTurns = 2,
                // ⚠ 设计文档 5.3 的表格写「1 幕起」，但同节的**规则**写「第 1 幕不出现」。
                //   这里取**规则**（更硬，且理由充分：玩家没学会格挡就进秘境是纯挫败）。
                AppearFromAct = 2, Weight = 30,
                PlayerSpeedMul = 0.90f,
                Boon = "本域灵卵产出 ×1.5",
                Champions = new[]
                {
                    new RealmChampionDef {
                        Name = "泽殍", Epithet = "半沉半浮的溺者之首，专拖人下水", HookId = "rc_drown",
                        MechanicText = "沉沦：每 2 回合拖走全场速度最高的我方（下回合无法行动 + 减速）",
                        RewardText = "灵卵 +4、湿抗灵魂 +1", Eggs = 4 },
                    new RealmChampionDef {
                        Name = "涡姆", Epithet = "在泽心转了三百年的那个漩涡", HookId = "rc_tide",
                        MechanicText = "潮讯：每回合全体我方 +1「湿」（上限 4，湿层越高它打得越疼）；完美格挡一次清全场 1 层湿",
                        RewardText = "灵卵 +5、湿抗灵魂 +1", Eggs = 5 },
                },
                Rooms = new[] { "泥沼浅滩", "沉木林", "毒瘴洼地" },
                Palette = "#2F4A45",
            },
            new RealmDef {
                Id = RealmIds.Ember, Name = "烬岭", Element = "火",
                Concept = "地表开裂，灼烧常驻，逼你速战速决",
                Waves = 4, EnemyScale = 1.15f, EggRate = 1.2f,
                MapCols = 4, MapRows = 5,
                Hazard = RealmHazardKind.PlayerStatusAll,
                HazardStatusId = WanXiang.Battle.Core.StatusCatalog.Burn,
                HazardStacks = 1, HazardTurns = 2,
                AppearFromAct = 2, Weight = 25,
                PlayerSpeedMul = 0.95f,
                // ⚠ 原来写"每击败 1 波回血 8%"和"不可使用回复类遗物" —— 两条都**没有实现**
                //   （BanHealRelics 字段全工程从未被读取）。改成与实现一致的说法。
                Boon = "本域灵卵产出 ×1.5",
                Champions = new[]
                {
                    new RealmChampionDef {
                        Name = "赤髓", Epithet = "把自己当炉子的熔匠，血是铁水", HookId = "rc_bloodfury",
                        MechanicText = "薪火：每回合自焚 5% 最大生命，换攻击永久 +8%（无限叠加）",
                        RewardText = "灵卵 +5、火抗灵魂 +1", Eggs = 5 },
                    new RealmChampionDef {
                        Name = "灯奴", Epithet = "提着自己脑袋当灯笼的仆役", HookId = "rc_emberblade",
                        MechanicText = "燃刃：它的攻击**被你格挡**一次，攻击就永久 +10% —— 一味防守是在喂它",
                        RewardText = "灵卵 +6、火抗灵魂 +1", Eggs = 6 },
                },
                Rooms = new[] { "焦岩台", "熔痕谷", "灰烬海" },
                Palette = "#5A2A20",
            },
            new RealmDef {
                Id = RealmIds.Gear, Name = "机枢", Element = "金",
                Concept = "废弃机关城，敌人有护盾与机械减伤",
                Waves = 4, EnemyScale = 1.20f, EggRate = 1.3f,
                MapCols = 5, MapRows = 5,
                Hazard = RealmHazardKind.FoeShieldAll,
                HazardShieldPct = 0.15f, HazardTurns = 2,
                AppearFromAct = 3, Weight = 20,
                PlayerSpeedMul = 0.90f,
                // ⚠ "重铸技能""敌方多 1 只"都没有实现；改写为已生效的两条（场地护盾 + 减速）。
                Boon = "本域灵卵产出 ×1.5",
                Champions = new[]
                {
                    new RealmChampionDef {
                        Name = "校核者·零", Epithet = "机关城里最后一台还在按规程杀人的执法机", HookId = "rc_overload",
                        MechanicText = "过载校准：每 3 回合全体我方被标记（易伤），敌方全体获 15% 护盾",
                        RewardText = "灵卵 +5、金抗灵魂 +1", Eggs = 5 },
                    new RealmChampionDef {
                        Name = "执行者·壹", Epithet = "被写错了一行代码，从此只执行处决", HookId = "rc_execute",
                        MechanicText = "处决协议：每 4 回合锁定当前血量最低的我方 —— 锁定期间它对目标的伤害 ×2",
                        RewardText = "灵卵 +7、金抗灵魂 +1", Eggs = 7 },
                },
                Rooms = new[] { "齿轮回廊", "校准间", "核心舱" },
                Palette = "#3C4654",
            },
            new RealmDef {
                Id = RealmIds.Silent, Name = "寂域", Element = "无",
                Concept = "无声之地，禁止一切 QTE —— 纯数值硬仗",
                Waves = 3, EnemyScale = 1.35f, EggRate = 1.8f,
                MapCols = 5, MapRows = 5,
                Hazard = RealmHazardKind.SelfShieldAll,
                HazardShieldPct = 0.08f, HazardTurns = 3,
                NoQte = true,
                AppearFromAct = 4, Weight = 10,
                PlayerSpeedMul = 0.85f,
                Boon = "本域灵卵产出 ×1.8",
                Champions = new[]
                {
                    new RealmChampionDef {
                        Name = "无声者", Epithet = "守着这片哑地的守墓人，吞掉了自己的名字", HookId = "rc_mute",
                        MechanicText = "噤声：队伍灵力每回合归零 —— 战记/连携整场放不出来",
                        RewardText = "灵卵 +6、无属性灵魂 +1", Eggs = 6 },
                    new RealmChampionDef {
                        Name = "哑钟", Epithet = "敲不响的钟，替这片地守夜", HookId = "rc_pendulum",
                        MechanicText = "钟摆：奇数回合敌方全体攻击 +20%（受击也 +20%），偶数回合轮到我方 —— 挑回合出手",
                        RewardText = "灵卵 +8、无属性灵魂 +1", Eggs = 8 },
                },
                Rooms = new[] { "无声回廊", "哑石庭", "终点" },
                Palette = "#2B2530",
            },
            new RealmDef {
                Id = RealmIds.Thorn, Name = "荆墟", Element = "木",
                Concept = "赌坊型秘境：结算可加倍，但失败代价极高",
                Waves = 2, EnemyScale = 1.60f, EggRate = 2.2f,
                MapCols = 4, MapRows = 5,
                Hazard = RealmHazardKind.None,
                AppearFromAct = 2, Weight = 15,
                Boon = "本域灵卵产出 ×2.2",
                Champions = new[]
                {
                    new RealmChampionDef {
                        Name = "枯骨庄家", Epithet = "坐庄的那个从来不下场，直到你自己走进来", HookId = "rc_gamble",
                        MechanicText = "开赌：每回合开局掷骰，押中谁谁就被标记（易伤）",
                        RewardText = "灵卵 +7、木抗灵魂 +1", Eggs = 7 },
                    new RealmChampionDef {
                        Name = "荷官·蛊", Epithet = "手里永远有三张你没见过的牌", HookId = "rc_allin",
                        MechanicText = "梭哈：它的血量每跌破一档（75/50/25%）就翻一次牌 —— 一半全队回血 12%，一半全队受 12% 真伤",
                        RewardText = "灵卵 +9、木抗灵魂 +1", Eggs = 9 },
                },
                // ★ **唯一**一个撤离有代价的场域（赌坊型，中途抽身要留下买路钱）。
                //   ⛔ 文案与实扣必须同时改：QuitPenaltyText 是给玩家看的，
                //     QuitPenaltyEggs 才是真的会扣的数（RealmSession.ApplyQuitPenalty 读它）。
                PenalizesOnQuit = true,
                QuitPenaltyText = "撤离要留下 3 枚灵卵买路钱（不足则全部拿走）",
                QuitPenaltyEggs = 3,
                Rooms = new[] { "荆门", "赌桌", "收账处" },
                Palette = "#4A4330",
            },
        };

        public static IReadOnlyList<RealmDef> All => Table;

        public static RealmDef For(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Table.Length; i++)
                if (Table[i].Id == id) return Table[i];
            return null;
        }

        /// <summary>
        /// 按幕数抽一个可用的场域（确定性：只吃 act + seed，同局可复盘）。
        /// ⛔ 排除本局已经进过的（同局去重），也排除没到出现幕的。
        /// </summary>
        public static RealmDef Roll(int act, ulong seed, List<string> alreadyUsed)
        {
            int total = 0;
            var pool = new List<RealmDef>(Table.Length);
            for (int i = 0; i < Table.Length; i++)
            {
                var r = Table[i];
                if (!r.AvailableIn(act)) continue;
                if (alreadyUsed != null && alreadyUsed.Contains(r.Id)) continue;
                // 荆墟不劝死：全队血量过低时由调用方决定不弹，这里只管出现幕
                pool.Add(r);
                total += r.Weight;
            }
            if (pool.Count == 0 || total <= 0) return null;

            int roll = new WanXiang.Battle.Core.DeterministicRandom(
                seed ^ WanXiang.Battle.Core.CoreMath.Fnv1a("realm:" + act)).NextInt(0, total);
            for (int i = 0; i < pool.Count; i++)
            {
                roll -= pool[i].Weight;
                if (roll < 0) return pool[i];
            }
            return pool[pool.Count - 1];
        }
    }
}