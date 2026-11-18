// ============================================================================
//  万相 · 2D 战场声明区（字段/常量/结构/小工具）（2026-11-18 从 BattleStage2D.cs 拆出）
//  行为零变化：仅移动成员定义。BattleStage2D 是 partial class，所有成员互通。
//  ⚠ 这里含 public 序列化字段（Inspector 可调）—— 移动不影响 prefab 序列化，字段名未变。
// ============================================================================
using System.Collections.Generic;
using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Battle.Presentation
{
    public sealed partial class BattleStage2D
    {
        private float _lastClickAt = -1f;   // 检查点击限流
        /// <summary>已进化的异兽 DefId 集合（组队侧写入，舞台据此换觉醒材质）。</summary>
        public static readonly System.Collections.Generic.HashSet<string> AwakenedDefIds
            = new System.Collections.Generic.HashSet<string>();

        private const float Cell = 1.75f;        // 格子边长（横向）
        /// <summary>行间距 = Cell × 本值。⛔ BuildBoard 的格子尺寸与 CellPos 的落位**共用**它。</summary>
        public const float BoardRowGap = 0.72f;
        /// <summary>立绘枢轴在底部中点，抬高让脚踩在格子中心（Build 与 Step 必须用同一个值）。</summary>
        private const float FootOffset = 0.7f;

        /// <summary>立绘显示高度（世界单位）。格子 1.75 —— 立绘略高于格，气势更足。</summary>
        private const float UnitHeight = Cell * 0.60f;   // ★ 与行距(0.66×Cell)匹配：既在美术网格内，又不会上下排重叠

        /// <summary>
        /// 首领立绘的额外身量倍数（用户 2026-10-03 定案："boss 整的大一点，起码是普通异兽的 2-3 倍"）。
        /// 取 2.5：够醒目又不会溢出中宫格太多（2.5×0.60×1.75 ≈ 2.6 世界单位 ≈ 1.5 格高）。
        /// </summary>
        private const float BossScaleMul = 1.85f;   // 2026-10-25 用户："比九宫格大一点点就行"（原 2.5 太大）

        /// <summary>
        /// 首领**召唤物/分身**的身量倍数（如镜影等，非双子）。
        /// ⚠ 白魍双子**不走这里** —— 用户 2026-10-03 明确："他们两个都是 Boss 啊，
        ///   不是召唤出来的，他们是一起出现的" ， 双子按 <see cref="BossScaleMul"/> 同级放大。
        /// 这里留给真正的"分身/小召唤物"（1.15 = 比普通异兽略大，表明是 boss 阵营）。
        /// </summary>
        private const float BossSidekickScaleMul = 1.15f;

        /// <summary>
        /// 白魍双子（与本体一起出场、同为 Boss 级，但不在守关抽签池里）。
        /// ⛔⛔ **声明顺序不能挪到 BossScaleIds 后面**：C# 静态字段按声明顺序初始化，
        ///   而 `BossScaleIds = BuildBossScaleIds()` 内部要读 `TwinIds` ——
        ///   若 TwinIds 在后面，此刻还是 null ， `TypeInitializationException`
        ///   ， BattleStage2D 整个类型初始化失败 ， **舞台搭建失败、只有 HUD、看不到战斗场景**
        ///   （用户实测"看不到战斗场景了 / No cameras rendering"）。
        /// </summary>
        private static readonly string[] TwinIds = { "b_suren" };

        /// <summary>
        /// 「与首领同级放大」的 id 集合 = <c>BossCatalog.All</c>（14 个守关本体）+ **双子**
        /// （<see cref="TwinIds"/>）。⚠ 必须在 <see cref="TwinIds"/> **之后**声明。
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<string> BossScaleIds = BuildBossScaleIds();

        /// <summary>召唤物/分身（复用 b_ 立绘 id，但既不是本体也不是双子）。</summary>
        private static bool IsBossSidekick(string id)
            => BossCatalog.IsBoss(id) && !BossScaleIds.Contains(id);

        /// <summary>秘境首领（试炼/秘境战斗用 `rc_(&lt;hookId&gt;)` 作 id）：与主线首领同级放大。</summary>
        private static bool IsRealmChampion(string id)
            => !string.IsNullOrEmpty(id) && id.StartsWith("rc_");

        private static System.Collections.Generic.HashSet<string> BuildBossScaleIds()
        {
            var set = new System.Collections.Generic.HashSet<string>();
            var all = WanXiang.Battle.Core.BossCatalog.All;
            if (all != null)
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null && !string.IsNullOrEmpty(all[i].Id)) set.Add(all[i].Id);
            // 双子与本体同级放大（用户："他们两个都是 Boss"）
            for (int i = 0; i < TwinIds.Length; i++) set.Add(TwinIds[i]);
            return set;
        }

        // ---- 技能预览的格位高亮（只给已有格子换配色，不新建对象）----
        private static readonly Color TintEnemyCell = new Color(0.95f, 0.52f, 0.42f, 1f);   // 会被打到的敌方格
        private static readonly Color TintAllyCell = new Color(0.55f, 0.85f, 0.62f, 1f);    // 会被治疗/加盾的我方格
        private readonly SpriteRenderer[] _cellsPlayer = new SpriteRenderer[9];
        private readonly SpriteRenderer[] _cellsEnemy = new SpriteRenderer[9];
        private readonly Color[] _cellBasePlayer = new Color[9];
        private readonly Color[] _cellBaseEnemy = new Color[9];

        // ---- 属性突破 / 克制染色（2026-10-18）----
        //   用户要的"克制属性的格子颜色表现"：打出克制时，受击者**脚下的格子**染成攻击属性色再渐隐。
        //   与技能预览高亮共用同一批 SpriteRenderer，靠**写入顺序**保证本函数最后生效
        //   （Step 里预览先写、染色后写）。
        private const float CellFlashDur = 0.85f;   // 0.55 → 0.85（用户："没看到格子变色"）
        private readonly float[] _cellFlashT = new float[18];    // 0~8 我方，9~17 敌方
        private readonly Color[] _cellFlashC = new Color[18];
        private readonly bool[] _cellFlashing = new bool[18];
                                                        //   （原来写死 1.9 > 行距 1.155 ， 视觉挤在一起）

        // ---- 单位标签（名字 / 血条）的美术点位：直接在 Inspector 里改，不用动代码、不用改 prefab ----
        /// <summary>血条相对立绘容器的 y 偏移。</summary>
        // ★ 2026-11-17 用户："血条能往下偏移一下吗"：0.46 → 0.30（世界单位，下移 0.16 ≈ 16px）
        //   注意：这是默认值，场景里若有序列化值会覆盖它 —— 见 BattleStageWindow 的 Inspector。
        public float HpBarY = 0.30f;      // 世界高度（格中心往上）
        /// <summary>血条满宽（世界单位）。</summary>
        public float HpBarWidth = 1.25f;

        [Header("破绽/失衡条（2026-10-17，挂 HP 条正下方）")]
        [Tooltip("条高（世界单位）")]
        public float FlawBarHeight = 0.44f;
        [Tooltip("条上文字与条顶的间距")]
        public float FlawLabelRaise = 0.20f;
        [Tooltip("破绽进度条填充色（青——状态引信的中性冷色）")]
        public Color FlawFillColor = new Color(0.42f, 0.78f, 0.74f, 1f);
        [Tooltip("失衡条填充色（金——失衡是玩家最该盯的窗口）")]
        public Color FlawStaggerColor = new Color(1.00f, 0.76f, 0.28f, 1f);
        [Tooltip("条底色（与 HP 条底同调）")]
        public Color FlawBackColor = new Color(0.10f, 0.08f, 0.06f, 1f);
        [Tooltip("条上文字色（纸白——文字压在深色底板上，任何立绘前都读得清）")]
        public Color FlawLabelColor = new Color(0.93f, 0.90f, 0.82f, 1f);
        /// <summary>血条底衬高（世界单位）。</summary>
        public float HpBarBackHeight = 0.22f;
        /// <summary>名字字号（TextMesh.characterSize）。</summary>
        public float NameSize = 0.055f;
        /// <summary>名字相对立绘容器的 y 偏移。</summary>
        public float NameY = 0.16f;

        // ---- 护盾指示（血条**右侧**的蓝牌 + 数值）----
        //  为什么值得显示：护盾**不会自然消退**（只被伤害消耗，见 BattleUnit.TakeDamage / AddShield），
        //  所以它是"还剩多少"的真实信息，不给玩家看就是隐形的第二管血。
        /// <summary>护盾牌宽度（世界单位）。</summary>
        /// <summary>护盾牌与数字相对血条中线的**抬高量**（2026-10-23 用户："护盾位置还要往上"）。</summary>
        public float ShieldLiftY = 0.42f;

        public float ShieldPlateW = 0.46f;
        /// <summary>护盾牌高度（世界单位）。</summary>
        public float ShieldPlateH = 0.18f;
        /// <summary>护盾牌与血条之间的间隙（世界单位）。</summary>
        public float ShieldGap = 0.10f;
        /// <summary>护盾数值的字号（TextMesh.characterSize）。
        /// ⚠ 实测口径：该值是**root 局部单位**，而 root 有缩放（普通单位≈0.10、首领 2.5×≈0.26）
        ///   ， 0.012 时文字只有 0.02 世界宽（等于看不见）。0.14 时普通单位约 0.05/字、首领约 0.13/字，
        ///   三位的护盾值正好落在牌内。</summary>
        public float ShieldTextSize = 0.035f;
        /// <summary>护盾牌底色。</summary>
        public Color ShieldColor = new Color(0.30f, 0.55f, 0.82f);
        /// <summary>护盾数值颜色。</summary>
        public Color ShieldTextColor = new Color(0.96f, 0.98f, 1f);

        // ====================================================================
        //  状态外观（冰封 / 束缚 / 燃烧…）—— 覆盖在立绘上的那一层
        //  -------------------------------------------------------------------
        //  用户诉求："被冻结要做出冰块冻住异兽、木条束缚、燃烧等等"。
        //  · 每个状态可挂一张 **PNG**（文件名 = 状态 id，放 ArtRes/UI/StatusArt/）， 后期替换美术；
        //  · **没挂图也能跑**：自动用该状态的**半透明色块**兜底（冰蓝 / 苔绿 / 火橙）， 现在就看得见；
        //  · 同一单位同时多个状态时，按 `Priority`（小者优先）**只显示最重要的一个**。
        // ====================================================================
        [System.Serializable]
        public sealed class StatusOverlay
        {
            public string Id;          // = StatusCatalog 常量（也是 PNG 文件名）
            public Sprite Art;         // ← 把冰晶/藤蔓/火焰的 PNG 拖到这里
            public Color Tint = new Color(0.6f, 0.8f, 1f, 0.45f);   // 没图时的兜底色
            public float Scale = 1f;   // 相对立绘尺寸的倍率（美术图留白多时调它）
            /// <summary>相对立绘中心的 y 偏移，单位是**立绘高度的百分比**（-0.1 = 下移 10% 身高）。</summary>
            public float OffsetY = 0f;
            public int Priority = 50;  // 同时多个时取小者

            // ---- 动画（走 Resources/Shaders/StatusOverlay，见该文件注释）----
            /// <summary>横向摆动幅度（越靠上摆得越大）。0 = 不摆。</summary>
            public float WaveAmp = 0.012f;
            /// <summary>摆动的空间频率（几个波）。</summary>
            public float WaveFreq = 16f;
            /// <summary>摆动速度。</summary>
            public float WaveSpeed = 2.2f;
            /// <summary>明度/透明的呼吸幅度。火焰给大、冰块给小。</summary>
            public float PulseAmp = 0.12f;
            public float PulseSpeed = 3.0f;
            /// <summary>纵向拉伸幅度（火焰"往上舔一下"）。</summary>
            public float RiseAmp = 0.05f;
            public float RiseSpeed = 2.6f;
            /// <summary>整体左右摇摆角度（度）。</summary>
            public float SwayDeg = 2.5f;
            public float SwaySpeed = 1.6f;
        }

        /// <summary>状态外观表（缺图回退色块）。优先级：冻结 &gt; 束缚/混乱 &gt; 燃烧 &gt; 瘴气 &gt; 湿/霜…</summary>
        public StatusOverlay[] StatusOverlays = new[]
        {
            // 冻结：慢、稳、冷 —— 只有轻微呼吸与极小的摆，别让它像活物
            new StatusOverlay { Id = "freeze",      Tint = new Color(0.60f, 0.86f, 1.00f, 0.55f), Priority = 0,
                                WaveAmp = 0.005f, WaveSpeed = 1.1f, PulseAmp = 0.06f, PulseSpeed = 1.5f,
                                RiseAmp = 0.015f, RiseSpeed = 1.2f, SwayDeg = 0.8f, SwaySpeed = 0.7f,
                                Scale = 1.0f, OffsetY = 0f },
            // 束缚：藤蔓被拉扯的摇曳感
            new StatusOverlay { Id = "root",        Tint = new Color(0.34f, 0.60f, 0.26f, 0.48f), Priority = 1,
                                WaveAmp = 0.011f, WaveFreq = 11f, WaveSpeed = 1.5f, PulseAmp = 0.07f,
                                PulseSpeed = 1.8f, RiseAmp = 0.03f, RiseSpeed = 1.6f, SwayDeg = 3.2f, SwaySpeed = 1.0f,
                                Scale = 0.95f, OffsetY = -0.05f },
            new StatusOverlay { Id = "confuse",     Tint = new Color(0.74f, 0.45f, 0.86f, 0.38f), Priority = 2,
                                WaveAmp = 0.016f, WaveSpeed = 3.0f, PulseAmp = 0.14f, PulseSpeed = 4.0f,
                                Scale = 0.9f, OffsetY = 0f },
            // 燃烧：**静态图**（用户 2026-10-08："燃烧的动画给我删了，效果不好"）。
            //   动画参数全 0 ， shader 不起作用、只剩一张静态火焰；其它状态不受影响。
            new StatusOverlay { Id = "burn",        Tint = new Color(1.00f, 0.44f, 0.14f, 0.34f), Priority = 3,
                                WaveAmp = 0f, PulseAmp = 0f, RiseAmp = 0f, SwayDeg = 0f,
                                Scale = 0.80f, OffsetY = -0.10f },
            // 瘴气：翻滚的雾
            new StatusOverlay { Id = "miasma",      Tint = new Color(0.42f, 0.56f, 0.24f, 0.40f), Priority = 4,
                                WaveAmp = 0.014f, WaveFreq = 8f, WaveSpeed = 1.2f, PulseAmp = 0.10f,
                                PulseSpeed = 1.3f, RiseAmp = 0.04f, RiseSpeed = 1.0f, SwayDeg = 2.0f, SwaySpeed = 0.9f,
                                Scale = 0.95f, OffsetY = 0f },
            new StatusOverlay { Id = "wet",         Tint = new Color(0.34f, 0.60f, 0.90f, 0.32f), Priority = 5,
                                PulseAmp = 0.09f, PulseSpeed = 2.0f, SwayDeg = 1.2f, SwaySpeed = 1.0f, Scale = 0.9f },
            new StatusOverlay { Id = "frost",       Tint = new Color(0.70f, 0.88f, 1.00f, 0.32f), Priority = 6,
                                WaveAmp = 0.007f, PulseAmp = 0.07f, PulseSpeed = 1.8f, SwayDeg = 1.0f, Scale = 0.9f },
            new StatusOverlay { Id = "ice_erosion", Tint = new Color(0.55f, 0.80f, 0.95f, 0.32f), Priority = 7,
                                WaveAmp = 0.008f, PulseAmp = 0.08f, PulseSpeed = 2.0f, Scale = 0.9f },
            new StatusOverlay { Id = "armor_break", Tint = new Color(0.86f, 0.64f, 0.34f, 0.30f), Priority = 8,
                                PulseAmp = 0.10f, PulseSpeed = 2.4f, SwayDeg = 1.5f, Scale = 0.9f },
            new StatusOverlay { Id = "marked",      Tint = new Color(0.90f, 0.34f, 0.30f, 0.32f), Priority = 9,
                                PulseAmp = 0.18f, PulseSpeed = 4.4f, SwayDeg = 1.5f, SwaySpeed = 2.0f, Scale = 0.9f },
        };

        /// <summary>状态特效材质缓存（按状态 id 共享；单位各自颜色走顶点色，不用每只 new 一份）。</summary>
        private static readonly System.Collections.Generic.Dictionary<string, Material> OverlayMats =
            new System.Collections.Generic.Dictionary<string, Material>();
        private static Shader _overlayShader;

        /// <summary>取状态特效 shader（Resources 优先 ， 保证进包；再退回 Shader.Find）。</summary>
        private static Shader OverlayShader()
        {
            if (_overlayShader != null) return _overlayShader;
            _overlayShader = Resources.Load<Shader>("Shaders/StatusOverlay");
            if (_overlayShader == null) _overlayShader = Shader.Find("WanXiang/StatusOverlay");
            return _overlayShader;
        }

        /// <summary>
        /// 按状态取（并缓存）材质并把动画参数下发。
        /// ⚠ 取不到 shader 时返回 null —— 调用方退回默认 sprite 材质：**特效变成静态图，但不会崩**。
        /// </summary>
        private static Material OverlayMaterial(StatusOverlay d)
        {
            Material m;
            if (OverlayMats.TryGetValue(d.Id, out m) && m != null) return m;
            var sh = OverlayShader();
            if (sh == null) return null;
            m = StageMaterialFactory.Create(StageMaterialKind.StatusOverlay,
                new StageMaterialContext { Shader = sh });
            if (m == null) return null;
            m.SetColor("_Tint", Color.white);
            m.SetFloat("_WaveAmp", d.WaveAmp);
            m.SetFloat("_WaveFreq", d.WaveFreq);
            m.SetFloat("_WaveSpeed", d.WaveSpeed);
            m.SetFloat("_PulseAmp", d.PulseAmp);
            m.SetFloat("_PulseSpeed", d.PulseSpeed);
            m.SetFloat("_RiseAmp", d.RiseAmp);
            m.SetFloat("_RiseSpeed", d.RiseSpeed);
            OverlayMats[d.Id] = m;
            return m;
        }

        // ====================================================================
        //  状态图标（单位头顶那一排）—— 美术替换点位
        //  -------------------------------------------------------------------
        //  替换美术：把 `Assets/ArtRes/UI/Status/<状态id>.png` 拖到下面**对应那一行**的 Icon 上即可；
        //           不拖任何图也能跑（自动回退成「色块 + 缩写」，不会空、也不会报错）。
        //  新增状态：加一行（Id 填 StatusCatalog 里的 id）即可。
        //  尺寸/间距/位置：StatusIconSize / StatusIconGap / StatusRowY，全在 Inspector 里调。
        // ====================================================================
        [System.Serializable]
        public sealed class StatusIcon
        {
            public string Id;          // = StatusCatalog 的常量（也是图标文件名）
            public Sprite Icon;        // ← 拖 PNG 到这里替换美术（可留空）
            public Color Color = new Color(0.85f, 0.35f, 0.30f);   // 缺图时的底色
            public string Abbr;        // 缺图时的 1~2 字缩写
        }

        /// <summary>一站配齐 StatusCatalog 里的全部状态（缺图则回退色块+缩写）。</summary>
        public StatusIcon[] StatusIcons = new[]
        {
            new StatusIcon { Id = "burn",        Color = new Color(0.90f, 0.42f, 0.20f), Abbr = "灼" },
            new StatusIcon { Id = "ice_erosion", Color = new Color(0.45f, 0.72f, 0.92f), Abbr = "蚀" },
            new StatusIcon { Id = "corrode",     Color = new Color(0.55f, 0.60f, 0.35f), Abbr = "腐" },
            new StatusIcon { Id = "wet",         Color = new Color(0.35f, 0.62f, 0.85f), Abbr = "湿" },
            new StatusIcon { Id = "frost",       Color = new Color(0.62f, 0.80f, 0.92f), Abbr = "霜" },
            new StatusIcon { Id = "freeze",      Color = new Color(0.46f, 0.80f, 0.96f), Abbr = "冻" },
            new StatusIcon { Id = "armor_break", Color = new Color(0.72f, 0.58f, 0.36f), Abbr = "裂" },
            new StatusIcon { Id = "marked",      Color = new Color(0.88f, 0.36f, 0.34f), Abbr = "标" },
            new StatusIcon { Id = "confuse",     Color = new Color(0.70f, 0.45f, 0.75f), Abbr = "乱" },
            new StatusIcon { Id = "silence",     Color = new Color(0.60f, 0.55f, 0.70f), Abbr = "默" },
            new StatusIcon { Id = "root",        Color = new Color(0.42f, 0.66f, 0.36f), Abbr = "缚" },
            new StatusIcon { Id = "miasma",      Color = new Color(0.50f, 0.55f, 0.32f), Abbr = "瘴" },
            new StatusIcon { Id = "qi",          Color = new Color(0.55f, 0.80f, 0.55f), Abbr = "气" },
            new StatusIcon { Id = "vigor",       Color = new Color(0.50f, 0.82f, 0.60f), Abbr = "生" },
            new StatusIcon { Id = "grain",       Color = new Color(0.85f, 0.76f, 0.40f), Abbr = "谷" },
            new StatusIcon { Id = "bounty",      Color = new Color(0.90f, 0.80f, 0.45f), Abbr = "穰" },
            new StatusIcon { Id = "haste",       Color = new Color(0.60f, 0.85f, 0.90f), Abbr = "凝" },
        };

        /// <summary>状态图标边长（世界单位）。</summary>
        public float StatusIconSize = 0.38f;
        /// <summary>状态图标之间的间距（世界单位）。
        /// ⚠ 必须**大于字宽**，否则相邻两格的缩写会视觉上连成一片（用户截图反馈"气和裂糊在一起"）。</summary>
        public float StatusIconGap = 0.16f;
        /// <summary>状态图标排相对立绘容器的 y 偏移。**局部单位**（立绘自身高 10.24，头顶在 +5.12）， 7.4 = 刚好贴在头顶上方。</summary>
        public float StatusRowY = 7.4f;
        /// <summary>状态图标文字大小倍率。</summary>
        public float StatusLabelSize = 1f;

        /// <summary>★ 状态特效（罩在异兽身上的大图）总开关。
        /// 2026-10-09 用户定案：关掉它，战斗里只显示**头顶状态小图标**（StatusIcon 那一套），
        /// 不再把 burn/裂甲/湿… 的 PNG 罩在异兽身上（此前显示过大、观感差）。
        /// 想恢复身体特效 → 在 Inspector 把本项勾回 true 即可，无需改代码。</summary>
        public bool StatusArtEnabled = false;

        /// <summary>技能名飘字的颜色（只有战技/绝技会飘，普攻不飘）。</summary>
        public Color SkillNameColor = new Color(0.96f, 0.87f, 0.52f);

        private const float BoardY = -0.2f;      // 棋盘（3×3 网格）中心 y —— 网格与单位**必须共用**
        private const float PlayerX = -4.3f;
        private const float EnemyX = 4.3f;

        public Sprite BgSpring;        // ArtRes/Battle/BG_spring.png
        public Sprite CatalogSpriteFallback;

        // ---- Boss 机制提示（单位头顶的世界空间文字：冰晶期"还有 N 回合复活"等）----
        //  美术点位：直接在 Inspector 里改这三个值，不用动代码、也不用改 prefab。
        /// <summary>提示文字颜色（默认朱红）。</summary>
        public Color HintColor = new Color(0.80f, 0.24f, 0.15f);
        /// <summary>起手预警默认色（无谱系时用）。</summary>
        public Color TelegraphColor = new Color(0.92f, 0.86f, 0.70f);
        /// <summary>起手预警相对立绘上沿的抬升（世界单位，固定值 —— 见建节点处的说明）。</summary>
        public float TelegraphRaise = 0.30f;
        /// <summary>提示文字字号倍率（1 = 默认；Boss 放大 2.5× 时文字**不跟着放大**）。</summary>
        public float HintSize = 1f;
        /// <summary>提示相对立绘上沿再往上抬的距离（世界单位）。</summary>
        public float HintRaise = 1.05f;

        private BattleState _state;
        private readonly Dictionary<string, UnitView2D> _views = new Dictionary<string, UnitView2D>(16);
        /// <summary>R4：飘字生命周期池（原来 4 个并行 List 靠下标对齐，易错位）。</summary>
        private readonly FloatingTextPool _floaters = new FloatingTextPool();
        // ---- 判定横幅（2026-10-16，用户："判断成功和失败、加强和减弱能在醒目一点吗，
        //   还是说是太快了，导致看的不是很清楚"）——
        //   之前只有头顶一个小飘字（0.7s 寿命、characterSize 0.13），在棋盘上又小又短，
        //   玩家基本扫一眼就没了。现在加一条**屏幕中央的大横幅**：弹入 + 停留 + 淡出，
        //   成功（金/青白）与失败（暗红）颜色对立，一眼能分清。
        /// <summary>R3-a：判定横幅（弹入/停留/淡出 + 底衬自适应）整体抽出。</summary>
        private readonly VerdictBanner _verdictBanner = new VerdictBanner();
        /// <summary>横幅停留时长（弹入 0.12s + 停留 + 淡出 0.35s 的总账）。</summary>
        private Camera _cam;
        /// <summary>舞台动画时钟（Step 里按 dt 累加）。表现层所有周期动画都用它，别用 Time.time。</summary>
        private float _animT;
        private string _actingId;
        private SpriteRenderer _turnArrow;          // 当前行动者头顶向下箭头（Build 时创建）
        private static Sprite _turnArrowSprite;     // 向下箭头贴图（战斗间共享，懒构建）
        private bool _critNext;        // 下一条伤害数字是否按暴击放大（Crit 事件先于 Damage 到）

        // ★ 2026-10-24 用户："大招的伤害要等异兽往前顶一下才出现，闪过头像就出不合理"
        //   ⇒ 冲锋前段（LungeForward 0.14s）里到的伤害先压住，冲到目标面前再落数字/墨点。
        private struct DeferredDmg
        {
            public UnitView2D V;
            public int Amount;
            public bool Melt, Reaction, Crit;
            public Vector2 ActorHome;
            public float Due;          // _animT 到期时刻
        }
        private readonly List<DeferredDmg> _deferredDmg = new List<DeferredDmg>(8);

        // 大招特写期间押后的伤害反馈（cut-in 播完、起爆那一下再统一飘出来）
        private struct PendDmg { public UnitView2D View; public int Amount; public bool Melt; public bool Reaction; public bool Crit; public Vector2 ActorHome; }
        private readonly List<PendDmg> _pendingDmg = new List<PendDmg>(8);
        private UnitView2D _pendingCritFloat;

        // ---- 四周前景装饰（2026-10-11 战斗美化：随幕变化 + 轻微摇曳）----
        private Transform _fgDecor;

        /// <summary>背景层（档 1 极缓视差漂移用；2026-10-23）。</summary>
        private Transform _bgDecor;
        private BattleBgMotion _bgMotion;
        private Material _bgMat;
        private bool _dissolveWarned;
        private bool _dissolveLogDone;
        private Vector3 _bgHomePos;
        /// <summary>环境粒子（按幕：春花瓣/夏萤火/长夏花粉/秋落叶/冬雪花）。</summary>
        private BattleAmbientParticles _ambient;

        // ---- 终结技 / 觉醒技 大演出 + 镜头震动（2026-10-13）----
        /// <summary>表现开关（UI 层开战前按设置写入；默认全开。表现层不引用 UI，故用静态注入）。</summary>
        public static bool FxScreenShake = true;
        public static bool FxDamageNumbers = true;
        public static bool FxSkillCutIn = true;

        private SkillFxPlayer _skillFx;
        private Vector3 _camBase;                 // 相机基准位（震动围绕它抖）
        private float _shakeAmp, _shakeT, _shakeDur;
        private float _zoomBase = 5.4f;           // 相机基准 orthographicSize（BuildCamera 覆写）
        private float _lastHintPop;   // 机制飘字限流（0.7s 一条，避免两行叠在一起）
        private float _flushDelay = -1f;   // >0 = 等冲锋到位后再飘伤害数字
        private float _zoomDip, _zoomT, _zoomDur; // 起爆点 Zoom Punch（推近再弹回）

        public Camera Camera => _cam;

        // ================================================================
        //  搭建
        // ================================================================

        private string _realmId;   // 本场所属场域（空 = 主线）；Boss 登场文案要用

        /// <summary>外部注册的立绘覆盖（Def.Id → Sprite）。秘境首领用 `Resources/UI/Realm/Champion/*`。</summary>
        public static readonly System.Collections.Generic.Dictionary<string, Sprite> PortraitOverrides =
            new System.Collections.Generic.Dictionary<string, Sprite>();

    }
}
