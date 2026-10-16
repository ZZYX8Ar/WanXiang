// ============================================================================
//  敌方招式库（FoeKit）—— 2026-10-16《精英首领与秘境设计 v1.0》
//  ---------------------------------------------------------------------------
//  动机：技能效果一直是「按职业 + 技能类型的占位规则自动生成」的（SkillConfigSO
//  文件头原话），所以每只兽的绝技听起来都差不多。这一层把精英/首领的绝技
//  **真写出来**，并引入三样东西：
//
//   ① 招式谱系（FoeFamily）：同类招式遵守同一规律 ⇒ 玩家第一次遇到陌生怪也有
//      可用的经验，不必死记每一招。
//   ② 弱点/抗性：打弱点 ×1.35、被抗 ×0.75（走既有 BattleHooks.ModifyIncoming）。
//   ③ 破绽/失衡：破掉破绽 → 若干回合内受伤 ×1.8。⭐ 白刃的破绽是「格挡 2 次」
//      而不是「打中 2 次」—— 让**防御行为本身**也能成为进攻路线。
//
//  ★ 借鉴与取舍（对齐设计文档 1.1）：
//    学 33 号远征队的「可预判」，不学它的「不可预判」。
//    它的快慢刀泛滥、怪无提示靠听声辨招被玩家明确批评为负面体验 ⇒
//    本层规定：**任何非快刀谱系起手 0.35s 内必给起手预告（Windup 事件）**。
//
//  ⚠ 零副作用纪律：
//    · 只读不改 —— 弱点/失衡/谱系全部不改任何已有结算路径。
//    · 未配置的普通敌人（FoeWeakElement == None 且无破绽）**逐位不变** ⇒
//      天气自检基准局指纹不受影响（这是 P0 验收的硬指标）。
// ============================================================================

namespace WanXiang.Battle.Core
{
    /// <summary>招式谱系。同谱系 = 同一套规律 = 玩家的经验可以迁移。</summary>
    public enum FoeFamily
    {
        None = 0,
        /// <summary>劈斩（赤）：快，2-2-1 收招，逐段格挡。</summary>
        Strike = 1,
        /// <summary>横扫（橙）：<b>不吃格挡</b>，只能靠护盾/后摇。</summary>
        Sweep = 2,
        /// <summary>砸地（黄）：慢，破架（完美格挡）反震收益最高。</summary>
        Quake = 3,
        /// <summary>贯穿（青）：直线串 2~3 个单位，只吃减伤不吃格挡。</summary>
        Pierce = 4,
        /// <summary>吐息（紫）：慢而固定的多段，可数拍 —— <b>唯一允许长连打</b>。</summary>
        Breath = 5,
    }

    /// <summary>破绽触发方式（怎么把破绽打出来）。</summary>
    public enum FoeFlawTrigger
    {
        None = 0,
        /// <summary>被指定元素命中 N 次。</summary>
        HitByElement = 1,
        /// <summary>自身某状态叠到 N 层。</summary>
        StatusStacks = 2,
        /// <summary>成功格挡它 N 次（防御即输出路线）。</summary>
        ParryCount = 3,
        /// <summary>把它打到 N% 血以下。</summary>
        HpBelow = 4,
        /// <summary>打碎 N 个部件（如玄溟的冰晶）。</summary>
        BreakParts = 5,
    }

    public sealed class FoeKitDef
    {
        public string Id;

        // ---- 招式 ----
        public string UltName;              // 绝技名
        public FoeFamily Family;
        public int Segments = 1;            // 段数（硬上限见 FoeRules.MaxSegments）
        public int HitsPerSegment = 1;      // 每段独立判定次数
        public string UltStatusId;          // 绝技附带的减益（可空）
        public int UltStatusStacks = 1;
        public int UltStatusTurns = 2;

        // ---- 弱点 ----
        public Element WeakElement = Element.None;   // None = 无弱点（混沌之母）
        public Element ResistElement = Element.None;

        // ---- 破绽 ----
        public string FlawName;             // 破绽名（空 = 无破绽）
        public FoeFlawTrigger FlawTrigger = FoeFlawTrigger.None;
        public Element FlawElement = Element.None;
        public string FlawStatusId;         // StatusStacks 用
        public int FlawNeed = 3;            // 触发阈值
        public int FlawHpPercent = 40;      // HpBelow 用（百分比）
        public int UnbalancedTurns = 2;     // 失衡回合
        public float UnbalancedMul = 1.8f;  // 失衡受伤倍率

        // ---- 文案 ----
        public string Tagline;              // 一句话定位（详情面板显示）
        public string UltNote;              // 绝技说明
    }

    public static class FoeRules
    {
        /// <summary>绝技段数硬上限（设计文档 2.3）。超出的一律钳到它。</summary>
        public const int MaxSegments = 6;

        public const float WeakMul = 1.35f;     // 命中弱点
        public const float ResistMul = 0.75f;   // 命中抗性
        public const float DefaultUnbalancedMul = 1.8f;

        /// <summary>起手预告时长（真实秒）。非快刀谱系强制提供（设计文档 2.2）。</summary>
        public const float WindupSeconds = 0.35f;

        private static readonly FoeKitDef[] Table =
        {
            // ================= 一幕 · 木 =================
            new FoeKitDef {
                Id = "b_fuman",
                UltName = "朽木压顶", Family = FoeFamily.Quake, Segments = 3,
                UltStatusId = StatusCatalog.Root, UltStatusTurns = 1,
                WeakElement = Element.Metal, ResistElement = Element.Wood,
                FlawName = "朽心", FlawTrigger = FoeFlawTrigger.HitByElement,
                FlawElement = Element.Metal, FlawNeed = 3, UnbalancedTurns = 2,
                Tagline = "站着不动也在杀死你 —— 每一次砸地都在给全场挂根缚",
                UltNote = "砸地 3 连，第 3 段落地时给相邻友军挂根缚",
            },
            new FoeKitDef {
                Id = "e_fumuwei",
                UltName = "枯枝连劈", Family = FoeFamily.Strike, Segments = 3,
                UltStatusId = StatusCatalog.Corrode, UltStatusTurns = 3,
                WeakElement = Element.Metal, ResistElement = Element.Wood,
                FlawName = "枯枝", FlawTrigger = FoeFlawTrigger.HitByElement,
                FlawElement = Element.Metal, FlawNeed = 2, UnbalancedTurns = 1,
                UnbalancedMul = 1.6f,
                Tagline = "教学级精英：招式谱系最干净，用来让玩家学会看前摇",
                UltNote = "三段劈砍，末段附带 1 层蚀",
            },

            // ================= 二幕 · 火 =================
            new FoeKitDef {
                Id = "b_chiba",
                UltName = "炎枪贯穿", Family = FoeFamily.Pierce, Segments = 2,
                HitsPerSegment = 3,
                UltStatusId = StatusCatalog.Burn, UltStatusTurns = 3,
                WeakElement = Element.Water, ResistElement = Element.Fire,
                FlawName = "焰心", FlawTrigger = FoeFlawTrigger.HitByElement,
                FlawElement = Element.Water, FlawNeed = 3, UnbalancedTurns = 2,
                Tagline = "一条直线穿过整排 —— 站位对它没有意义，除非你换排",
                UltNote = "直线贯穿，3 段独立判定，每段挂 1 层灼烧",
            },
            new FoeKitDef {
                Id = "b_jinjiao",
                UltName = "焚海长息", Family = FoeFamily.Breath, Segments = 6,
                UltStatusId = StatusCatalog.Burn, UltStatusTurns = 3,
                WeakElement = Element.Water, ResistElement = Element.Metal,
                FlawName = "逆鳞", FlawTrigger = FoeFlawTrigger.StatusStacks,
                FlawStatusId = StatusCatalog.Burn, FlawNeed = 4, UnbalancedTurns = 2,
                Tagline = "6 段吐息，节奏固定 —— 数拍就能全挡，但新手会死在这里",
                UltNote = "6 段持续，每段间隔 0.45s，每段挂灼烧",
            },
            new FoeKitDef {
                Id = "e_yanjudog",
                UltName = "裂焰三扑", Family = FoeFamily.Strike, Segments = 3,
                HitsPerSegment = 2,
                UltStatusId = StatusCatalog.Burn, UltStatusTurns = 3,
                WeakElement = Element.Water, ResistElement = Element.Fire,
                FlawName = "喘息", FlawTrigger = FoeFlawTrigger.ParryCount,
                FlawNeed = 2, UnbalancedTurns = 1, UnbalancedMul = 1.6f,
                Tagline = "高速抢先手，专打来不及格挡的新手",
                UltNote = "3 段速度递增，第 3 段是快刀（窗口 0.72 倍）",
            },
            new FoeKitDef {
                Id = "e_zhanglinwu",
                UltName = "起瘴", Family = FoeFamily.Breath, Segments = 3,
                UltStatusId = StatusCatalog.Miasma, UltStatusStacks = 1,
                UltStatusTurns = 3,
                WeakElement = Element.Metal, ResistElement = Element.Wood,
                FlawName = "巫骨", FlawTrigger = FoeFlawTrigger.HitByElement,
                FlawElement = Element.Metal, FlawNeed = 2, UnbalancedTurns = 1,
                UnbalancedMul = 1.6f,
                Tagline = "不攻击你，它给敌方上增益 —— 必须优先点杀",
                UltNote = "3 段向敌方全体叠瘴气（每层攻 -3%）",
            },

            // ================= 三幕 · 金 =================
            new FoeKitDef {
                Id = "b_bairen",
                UltName = "千刃回环", Family = FoeFamily.Sweep, Segments = 2,
                UltStatusId = StatusCatalog.ArmorBreak, UltStatusTurns = 3,
                WeakElement = Element.Fire, ResistElement = Element.Metal,
                FlawName = "刃口", FlawTrigger = FoeFlawTrigger.ParryCount,
                FlawNeed = 2, UnbalancedTurns = 1, UnbalancedMul = 2.2f,
                Tagline = "横扫不吃格挡 —— 它逼你用护盾和破防，而不是靠手速",
                UltNote = "横扫 2 次，每次剥离 1 层增益",
            },
            new FoeKitDef {
                Id = "e_wufang",
                UltName = "穿心刺", Family = FoeFamily.Pierce, Segments = 2,
                HitsPerSegment = 2,
                UltStatusId = StatusCatalog.ArmorBreak, UltStatusTurns = 3,
                WeakElement = Element.Fire, ResistElement = Element.Metal,
                FlawName = "接缝", FlawTrigger = FoeFlawTrigger.HitByElement,
                FlawElement = Element.Fire, FlawNeed = 3, UnbalancedTurns = 2,
                Tagline = "与五方杂兵共享骨架的召唤物，用来铺场而非主攻",
                UltNote = "2 段贯穿，每段 2 次判定，命中挂裂甲",
            },
            new FoeKitDef {
                Id = "e_jielvshi",
                UltName = "拍击三连", Family = FoeFamily.Strike, Segments = 3,
                UltStatusId = StatusCatalog.Marked, UltStatusTurns = 2,
                WeakElement = Element.None, ResistElement = Element.None,
                FlawName = "节拍", FlawTrigger = FoeFlawTrigger.ParryCount,
                FlawNeed = 3, UnbalancedTurns = 2,
                Tagline = "故意把节奏做成等距，专治靠感觉格挡的玩家",
                UltNote = "3 段，间隔完全等距（0.6s），第 3 段挂斩标",
            },

            // ================= 四幕 · 水/土 =================
            new FoeKitDef {
                Id = "b_xuanming",
                UltName = "玄溟冰封", Family = FoeFamily.Sweep, Segments = 3,
                UltStatusId = StatusCatalog.Freeze, UltStatusTurns = 1,
                WeakElement = Element.Fire, ResistElement = Element.Water,
                FlawName = "冰核", FlawTrigger = FoeFlawTrigger.BreakParts,
                FlawNeed = 3, UnbalancedTurns = 2,
                Tagline = "冰晶重生：打碎一次冰晶，它会在 3 回合后原地复活",
                UltNote = "3 段冰刃，第 3 段必定冻结被命中的 1 个单位",
            },
            new FoeKitDef {
                Id = "b_guixu",
                UltName = "归墟震", Family = FoeFamily.Quake, Segments = 4,
                UltStatusId = StatusCatalog.Silence, UltStatusTurns = 1,
                WeakElement = Element.Wood, ResistElement = Element.Earth,
                FlawName = "地脉", FlawTrigger = FoeFlawTrigger.HpBelow,
                FlawNeed = 40, UnbalancedTurns = 3, UnbalancedMul = 2.0f,
                Tagline = "血量最低但护甲最高 —— 破防流的天敌，也是它的软肋",
                UltNote = "4 连震，每震缩短一步，最后一击沉默前排",
            },

            // ================= 五幕 · 无属性 =================
            new FoeKitDef {
                Id = "b_hongmeng",
                UltName = "鸿蒙无名", Family = FoeFamily.Breath, Segments = 5,
                UltStatusId = StatusCatalog.Confuse, UltStatusTurns = 1,
                WeakElement = Element.None, ResistElement = Element.None,
                FlawName = "",   // ⛔ 无破绽（终局没有安全牌）
                Tagline = "无属性 ⇒ 没有弱点，且招式谱系每次换一套",
                UltNote = "5 段随机谱系，第 3 段起每段换一次预警色",
            },
        };

        public static System.Collections.Generic.IReadOnlyList<FoeKitDef> All => Table;

        public static FoeKitDef For(string beastId)
        {
            if (string.IsNullOrEmpty(beastId)) return null;
            for (int i = 0; i < Table.Length; i++)
                if (Table[i].Id == beastId) return Table[i];
            return null;
        }

        public static bool Has(string beastId) => For(beastId) != null;

        /// <summary>段数钳制到硬上限（设计文档 2.3：非吐息谱系不许超过 4）。</summary>
        public static int ClampSegments(int segs, FoeFamily fam)
        {
            if (segs < 1) segs = 1;
            int cap = fam == FoeFamily.Breath ? MaxSegments : 4;
            return segs > cap ? cap : segs;
        }

        // ---- 谱系文案 ----
        public static string FamilyCn(FoeFamily f)
        {
            switch (f)
            {
                case FoeFamily.Strike: return "劈斩";
                case FoeFamily.Sweep: return "横扫";
                case FoeFamily.Quake: return "砸地";
                case FoeFamily.Pierce: return "贯穿";
                case FoeFamily.Breath: return "吐息";
                default: return "普通";
            }
        }

        /// <summary>起手预警色（赤/橙/黄/青/紫）—— 固定映射，玩久了成条件反射。</summary>
        public static string WindupColorCn(FoeFamily f)
        {
            switch (f)
            {
                case FoeFamily.Strike: return "赤";
                case FoeFamily.Sweep: return "橙";
                case FoeFamily.Quake: return "黄";
                case FoeFamily.Pierce: return "青";
                case FoeFamily.Breath: return "紫";
                default: return "白";
            }
        }

        /// <summary>这个谱系的对策（给战斗提示条与详情面板）。</summary>
        public static string FamilyCounterCn(FoeFamily f)
        {
            switch (f)
            {
                case FoeFamily.Strike: return "逐段格挡，不要贪反击";
                case FoeFamily.Sweep: return "不吃格挡 —— 切护盾或抢后摇";
                case FoeFamily.Quake: return "完美格挡反震，或后撤一格";
                case FoeFamily.Pierce: return "直线贯穿 —— 分散站位别扎堆";
                case FoeFamily.Breath: return "间隔固定 —— 数拍连按";
                default: return "";
            }
        }

        /// <summary>谱系节奏（快/中/慢），决定起手预告的时长。</summary>
        public static float WindupSecondsFor(FoeFamily f)
        {
            switch (f)
            {
                case FoeFamily.Strike: return 0.20f;   // 快刀：预告短
                case FoeFamily.Pierce: return 0.25f;
                case FoeFamily.Sweep: return WindupSeconds;
                case FoeFamily.Quake: return 0.45f;    // 慢刀：预告长
                case FoeFamily.Breath: return 0.40f;
                default: return WindupSeconds;
            }
        }
    }
}