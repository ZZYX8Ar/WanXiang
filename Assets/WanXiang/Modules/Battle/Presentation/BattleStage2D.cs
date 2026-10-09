// ============================================================================
//  万相 · 2D 战场（美术方案 V2 形态：立绘 + Sprite 全套）
//  ---------------------------------------------------------------------------
//  2D 正交视角。与 3D 灰盒（BattleStageGraybox）共用同一套数据契约
//  （只读 ViewFrame 快照 + BattleEvent 事件流），差别只在"画的东西"：
//      · 背景一张季别远景（ArtRes/Battle/BG_xxx.png）
//      · 每方一块 3×3 棋盘（格子 Sprite，可整体换图）
//      · 单位 = 立绘 SpriteRenderer（SpriteCatalog 按 BeastDef.Id 查，
//        查不到回退五行色块 —— 新异兽没图也不会穿帮）
//      · 血条 = 双 Sprite、伤害数字 = TextMesh 上浮、死亡 = 灰化+下沉
//
//  ⚠ 用户替换素材的点位（全部命名规范，换 Sprite/Image 即可，不动代码）：
//      BG_Spring / BG_Summer        背景
//      BoardP/Cell0..8、BoardE/...  棋盘格（含中宫）
//      Unit_xxx/Body                单位立绘
//      Canvas/WeatherBar、Btn_*     UI 贴图
// ============================================================================
//
//  ⚠ 相机与布局（2D 正交）：
//    相机 orthographicSize 5.4、位于 (0,0,-10)，宣纸色清屏；
//    我方棋盘中心 (-2.95, -0.2)、敌方 (2.95, -0.2)，格距 1.15，单位高 ~1.7；
//    UI 用 Overlay Canvas 1920×1080（见 Battle2DSceneBuilder）。

using System.Collections.Generic;
using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Battle.Presentation
{
    internal sealed class UnitView2D
    {
        public string UnitId;
        public GameObject Root;        // 立绘容器
        public SpriteRenderer Body;    // 立绘
        public SpriteRenderer HpBg;
        public SpriteRenderer HpFill;
        /// <summary>护盾指示（血条右侧的蓝牌）。护盾 &gt; 0 才显示。</summary>
        public SpriteRenderer ShieldPlate;
        /// <summary>护盾数值（独立对象，**不能挂在蓝牌下** —— 蓝牌有缩放，会把字一起放大）。</summary>
        public TextMesh ShieldText;
        /// <summary>状态外观层（冰封/束缚/燃烧…），是 <see cref="Body"/> 的子节点 ⇒ 跟着立绘走。</summary>
        public SpriteRenderer Overlay;
        /// <summary>当前覆盖层显示的状态 id（变了才重设，别每帧换 sprite）。</summary>
        public string OverlayShown;
        /// <summary>当前覆盖层用的那条配置（Step 里的摇摆要从它取角度/速度）。
        /// ⚠ 必须写全限定名：`UnitView2D` 与 `BattleStage2D` 是文件里**两个并列的类**，
        ///   嵌套在 BattleStage2D 里的 `StatusOverlay` 不写限定名会 CS0246。</summary>
        public BattleStage2D.StatusOverlay OverlayDef;
        public TextMesh NameText;
        /// <summary>Boss 机制提示（冰晶期「还剩 N 回合」等，来源 <c>BattleUnit.StateHint</c>，
        /// 由帧流携带 —— 见 <c>UnitSnapshot.StateHint</c>）。空串 = 不显示。</summary>
        public TextMesh Hint;
        public int Hp, MaxHp;
        public bool Alive = true;
        public Vector2 HomePos;        // 站位（未加浮动）
        public float Phase;            // 待机浮动相位（随机，方案 §最易做错三件事之一）
        public float DeadBlend;
        public float ShakePulse;         // 受击回弹（>0 时向基色回退）
        public Color BaseColor = Color.white;

        // ---- 出手冲锋（表现层演出，不改战斗数据）----
        // 战斗核心只有事件流，没有"动画"概念；冲上去再回来完全由表现层决定。
        public bool LungeActive;
        public float LungeElapsed;
        public Vector2 LungeTo;          // 冲锋落点（目标前方一点，避免盖住目标立绘）
        public int BaseSortingOrder = 0; // 冲锋时临时置顶，回位要还原

        // ---- 状态图标排（头顶）----
        public GameObject StatusRow;            // 整排的容器（跟着单位走）
        public SpriteRenderer[] StatusPlates;   // 每格底板（有图标时用图标，没图标用纯色块）
        public TextMesh[] StatusLabels;         // 每格文字（缩写 + 层数）
        public string StatusShown = "";         // 当前已画的状态串（变了才重画）
    }

    public sealed class BattleStage2D : MonoBehaviour
    {
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
        private const float BossScaleMul = 2.5f;

        /// <summary>
        /// 首领**召唤物/分身**的身量倍数（如镜影等，非双子）。
        /// ⚠ 白魍双子**不走这里** —— 用户 2026-10-03 明确："他们两个都是 Boss 啊，
        ///   不是召唤出来的，他们是一起出现的" ⇒ 双子按 <see cref="BossScaleMul"/> 同级放大。
        /// 这里留给真正的"分身/小召唤物"（1.15 = 比普通异兽略大，表明是 boss 阵营）。
        /// </summary>
        private const float BossSidekickScaleMul = 1.15f;

        /// <summary>
        /// 白魍双子（与本体一起出场、同为 Boss 级，但不在守关抽签池里）。
        /// ⛔⛔ **声明顺序不能挪到 BossScaleIds 后面**：C# 静态字段按声明顺序初始化，
        ///   而 `BossScaleIds = BuildBossScaleIds()` 内部要读 `TwinIds` ——
        ///   若 TwinIds 在后面，此刻还是 null ⇒ `TypeInitializationException`
        ///   ⇒ BattleStage2D 整个类型初始化失败 ⇒ **舞台搭建失败、只有 HUD、看不到战斗场景**
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
                                                        //   （原来写死 1.9 > 行距 1.155 ⇒ 视觉挤在一起）

        // ---- 单位标签（名字 / 血条）的美术点位：直接在 Inspector 里改，不用动代码、不用改 prefab ----
        /// <summary>血条相对立绘容器的 y 偏移。</summary>
        public float HpBarY = 2.35f;
        /// <summary>血条满宽（世界单位）。</summary>
        public float HpBarWidth = 3.40f;
        /// <summary>血条底衬高（世界单位）。</summary>
        public float HpBarBackHeight = 0.72f;
        /// <summary>名字字号（TextMesh.characterSize）。</summary>
        public float NameSize = 0.10f;
        /// <summary>名字相对立绘容器的 y 偏移。</summary>
        public float NameY = 1.62f;

        // ---- 护盾指示（血条**右侧**的蓝牌 + 数值）----
        //  为什么值得显示：护盾**不会自然消退**（只被伤害消耗，见 BattleUnit.TakeDamage / AddShield），
        //  所以它是"还剩多少"的真实信息，不给玩家看就是隐形的第二管血。
        /// <summary>护盾牌宽度（世界单位）。</summary>
        public float ShieldPlateW = 3.20f;
        /// <summary>护盾牌高度（世界单位）。</summary>
        public float ShieldPlateH = 1.10f;
        /// <summary>护盾牌与血条之间的间隙（世界单位）。</summary>
        public float ShieldGap = 0.80f;
        /// <summary>护盾数值的字号（TextMesh.characterSize）。
        /// ⚠ 实测口径：该值是**root 局部单位**，而 root 有缩放（普通单位≈0.10、首领 2.5×≈0.26）
        ///   ⇒ 0.012 时文字只有 0.02 世界宽（等于看不见）。0.14 时普通单位约 0.05/字、首领约 0.13/字，
        ///   三位的护盾值正好落在牌内。</summary>
        public float ShieldTextSize = 0.22f;
        /// <summary>护盾牌底色。</summary>
        public Color ShieldColor = new Color(0.30f, 0.55f, 0.82f);
        /// <summary>护盾数值颜色。</summary>
        public Color ShieldTextColor = new Color(0.96f, 0.98f, 1f);

        // ====================================================================
        //  状态外观（冰封 / 束缚 / 燃烧…）—— 覆盖在立绘上的那一层
        //  -------------------------------------------------------------------
        //  用户诉求："被冻结要做出冰块冻住异兽、木条束缚、燃烧等等"。
        //  · 每个状态可挂一张 **PNG**（文件名 = 状态 id，放 ArtRes/UI/StatusArt/）⇒ 后期替换美术；
        //  · **没挂图也能跑**：自动用该状态的**半透明色块**兜底（冰蓝 / 苔绿 / 火橙）⇒ 现在就看得见；
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
            //   动画参数全 0 ⇒ shader 不起作用、只剩一张静态火焰；其它状态不受影响。
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

        /// <summary>取状态特效 shader（Resources 优先 ⇒ 保证进包；再退回 Shader.Find）。</summary>
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
            m = new Material(sh);
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
        public float StatusIconSize = 0.36f;
        /// <summary>状态图标之间的间距（世界单位）。
        /// ⚠ 必须**大于字宽**，否则相邻两格的缩写会视觉上连成一片（用户截图反馈"气和裂糊在一起"）。</summary>
        public float StatusIconGap = 0.16f;
        /// <summary>状态图标排相对立绘容器的 y 偏移。**局部单位**（立绘自身高 10.24，头顶在 +5.12）⇒ 7.4 = 刚好贴在头顶上方。</summary>
        public float StatusRowY = 7.4f;
        /// <summary>状态图标文字大小倍率。</summary>
        public float StatusLabelSize = 1f;

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
        /// <summary>提示文字字号倍率（1 = 默认；Boss 放大 2.5× 时文字**不跟着放大**）。</summary>
        public float HintSize = 1f;
        /// <summary>提示相对立绘上沿再往上抬的距离（世界单位）。</summary>
        public float HintRaise = 1.05f;

        private BattleState _state;
        private readonly Dictionary<string, UnitView2D> _views = new Dictionary<string, UnitView2D>(16);
        private readonly List<GameObject> _tempTexts = new List<GameObject>(16);
        private readonly List<float> _tempLife = new List<float>(16);
        private Camera _cam;
        /// <summary>舞台动画时钟（Step 里按 dt 累加）。表现层所有周期动画都用它，别用 Time.time。</summary>
        private float _animT;
        private string _actingId;

        public Camera Camera => _cam;

        // ================================================================
        //  搭建
        // ================================================================

        public void Build(BattleState st, Sprite springBg, SpriteCatalog catalog = null)
        {
            _catalog = catalog;
            Clear();
            _state = st;

            var bg = new GameObject("BG_Spring");
            bg.transform.SetParent(transform, false);
            var bgSr = bg.AddComponent<SpriteRenderer>();
            if (springBg != null)
            {
                bgSr.sprite = springBg;
                // 铺满相机视野（ortho 5.4 → 高 10.8、宽 19.2 @16:9）
                var b = springBg.bounds;
                float sx = 19.2f / b.size.x, sy = 10.8f / b.size.y;
                bg.transform.localScale = new Vector3(sx, sy, 1f);
            }
            bgSr.sortingOrder = -10;

            BuildBoard("BoardP", PlayerX, BoardY, true);
            BuildBoard("BoardE", EnemyX, BoardY, false);
            BuildUnits();
            BuildCamera();

            // ⚠ 必须套第 0 帧（初始快照）。BattleState 传进来时**这场战斗已经跑完了**
            //   （BattlePlayback 构造里一次 Run 到底），所以 BuildUnits 拿到的是终局
            //   HP/存活状态 —— 不套初帧的话，开局画面就是"一半人已经死了"。
            if (st.Frames.Count > 0) ApplyFrame(st.Frames[0]);
        }

        private void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);
            _views.Clear();
            _tempTexts.Clear();
            _tempLife.Clear();
            _state = null;
            _cam = null;
        }

        // player=true 我方（左，不镜像）；player=false 敌方（右，列镜像 ⇒ 与我方对称）
        private void BuildBoard(string boardName, float cx, float cy, bool player)
        {
            var board = new GameObject(boardName);
            board.transform.SetParent(transform, false);
            for (int i = 0; i < 9; i++)
            {
                bool center = i == 4;
                var cell = new GameObject($"Cell_{boardName[5]}{i}" + (center ? "_center" : ""));
                cell.transform.SetParent(board.transform, false);
                var sr = cell.AddComponent<SpriteRenderer>();
                // 格子：程序化纯色 + 墨色描边（用户换图点位：Cell_x 的 sprite 直接换）
                // 格子：**尺寸必须与行间距一致**，否则上下行互相压边、看着"九宫格大小不齐"
                //（原先是 1.75×1.75 正方形 + 1.26 行距 ⇒ 每行压掉 0.49，用户实测就是这个现象）。
                // ⚠ 单位落位用同一套公式（（1-row)*Cell*BoardRowGap），改这里必须同步改 CellPos。
                // 🔧 换美术：把 `Assets/Resources/UI/Board/cell.png`（+ `cell_center.png`）丢进去即可替换，缺图回退程序化纯色。
                var cellArt = Resources.Load<Sprite>("UI/Board/" + (center ? "cell_center" : "cell"));
                sr.sprite = cellArt != null
                    ? cellArt
                    : SolidSprite(
                        center ? new Color(0.94f, 0.89f, 0.78f) : new Color(0.91f, 0.89f, 0.82f),
                        (int)(Cell * 100), (int)(Cell * BoardRowGap * 100), 3,
                        center ? new Color(0.79f, 0.63f, 0.39f) : new Color(0.16f, 0.13f, 0.09f));
                sr.sortingOrder = -5;

                // 缓存格子渲染器与底色：技能预览高亮用（还原时用缓存值，不硬编码颜色）
                if (player) { _cellsPlayer[i] = sr; _cellBasePlayer[i] = sr.color; }
                else { _cellsEnemy[i] = sr; _cellBaseEnemy[i] = sr.color; }
                int col = i % 3;
                if (!player) col = 2 - col;   // 敌方列镜像，与我方对称
                int row = i / 3;
                cell.transform.localPosition = new Vector3(
                    cx + (col - 1) * Cell, cy + (1 - row) * Cell * BoardRowGap, 0f);
            }
        }

        /// <summary>
        /// 技能预览：把"会被作用到的九宫格格子"点亮。cell = 0..8（与 Pos.Index 同一套编号）。
        /// 敌方格 → 暖红（要挨打）；我方格 → 柔绿（吃治疗/护盾/增益）。传 null = 该侧不亮。
        /// 两侧都传 null/空 = 全部还原。
        /// ⚠ 只给**已有格子**换配色（数据填充），不新建任何对象；格子的美术仍可在场景里换。
        /// </summary>
        public void HighlightCells(List<int> playerCells, List<int> enemyCells)
        {
            for (int i = 0; i < 9; i++)
            {
                bool onP = playerCells != null && playerCells.Contains(i);
                bool onE = enemyCells != null && enemyCells.Contains(i);
                if (_cellsPlayer[i] != null)
                    _cellsPlayer[i].color = onP ? TintAllyCell : _cellBasePlayer[i];
                if (_cellsEnemy[i] != null)
                    _cellsEnemy[i].color = onE ? TintEnemyCell : _cellBaseEnemy[i];
            }
        }

        private void BuildUnits() { BuildUnitsFor(_state.AllUnitsEver); }

        /// <summary>
        /// 按名册建 view。<paramref name="units"/> 平时传 <c>AllUnitsEver</c>（开场名册 + 中途增员）；
        /// 中途增员也可以只传**单个单位**（见 <see cref="EnsureView"/>）。
        ///
        /// ★ 中途增员（召唤 / 分身）的 view **先建好、但先隐藏**，等它在帧流里第一次出现
        ///   （= 真的入场那一刻）再显示，见 <see cref="ApplyFrame"/>。
        /// ⚠⚠ **但只靠"开演时预建"是不够的**：手动模式（非 PvP 的正常战斗）下
        ///   <c>BattlePlayback</c> 构造函数只 <c>AdvanceSim()</c> 跑到**第一个决策点**，
        ///   开演那一刻冰晶还不在名册里 ⇒ 预建无从谈起（用户 2026-10-04 报"还是什么都没有"就是这个原因）。
        ///   ⇒ 所以 <see cref="ApplyFrame"/> 里还有一条 <see cref="EnsureView"/> 惰性补建兜底。
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
                    Hp = u.Hp, MaxHp = u.MaxHp,
                    Alive = u.IsAlive,
                };

                var root = new GameObject($"Unit_{u.RuntimeId}");
                root.transform.SetParent(transform, false);
                bool player = u.Side == TeamSide.Player;
                view.HomePos = CellPos(player, u.Pos.Index);
                // Pivot=底部中 + Y 上偏 = 立绘脚踩格子中心（遮挡关系由 sortingOrder 管）
                root.transform.localPosition = new Vector3(view.HomePos.x, view.HomePos.y + FootOffset, 0f);
                view.Root = root;

                // 立绘：SpriteCatalog 按 BeastDef.Id 查；查不到回退五行色块
                var bodySr = root.AddComponent<SpriteRenderer>();
                var sprite = catalog != null ? catalog.Get(u.Def.Id) : null;
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
                        //   sortingOrder 用行号，中宫=row1 ⇒ 会盖住后排(row0)、被前排(row2)压，符合"大个子"直觉。
                        //
                        // ⚠⚠ 判据必须用 `BossScaleIds`（= 14 守关本体 + 双子），**不能**用 `IsBoss(id)`：
                        //   白魍双子复用 `b_suren` 当立绘 id（否则 summon_ 前缀查不到图会"乱分配"），
                        //   它也是 b_ 前缀 —— 用 IsBoss 会把**所有** b_ 前缀都放大（含真分身，太宽）。
                        //   ★ 用户 2026-10-03 更正："他们两个都是 Boss 啊，不是召唤出来的，他们是一起出现的"
                        //     ⇒ 双子（b_suren）与本体同级放大，不再用 1.15 的小尺寸。
                        bool isBossTier = u.Side == TeamSide.Enemy && BossScaleIds.Contains(u.Def.Id);
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
                // 敌方整体镜像：立绘原画统一朝右，敌阵镜像后朝左 ⇒ 面向我方，自然。
                // ⛔ **例外：素刃（白魍双子）不镜像**。它的原画是**刻意朝左**画的
                //   （早期为双子"背靠背"并排设计的），再统一镜像就变成朝右 ⇒ **背对玩家**
                //   （用户实测："b_suren 这个的朝向反了"）。它现在站后排中(格7)，
                //   与中宫本体是前后关系，本来就该朝前 ⇒ 跳过镜像即可与其它敌方朝向一致。
                bodySr.flipX = !player && u.Def.Id != BossCatalog.TwinBeastId;
                // 敌方压暗一档（§4.4 敌我同源 + 浊化的轻量版）
                if (!player) bodySr.color = new Color(0.72f, 0.72f, 0.80f);
                view.Body = bodySr;
                view.BaseColor = bodySr.color;

                // ---- 状态外观层（冰封 / 束缚 / 燃烧…）----
                //  挂在 **Body 之下** ⇒ 自动跟随立绘的位置、缩放与左右镜像，不用另算。
                //  sortingOrder 比立绘大 1 ⇒ 盖在立绘上。缺图时 `SetOverlay2D` 用半透明色块兜底。
                var ovGo = new GameObject("Overlay");
                ovGo.transform.SetParent(bodySr.transform, false);
                var ovSr = ovGo.AddComponent<SpriteRenderer>();
                ovSr.sprite = SolidSprite(Color.white, 32, 64, 0, Color.white);
                ovSr.sortingOrder = bodySr.sortingOrder + 1;
                ovSr.enabled = false;
                view.Overlay = ovSr;

                // ★ 鼠标命中框（世界空间）—— 用户定案：悬停*棋盘上的异兽*看状态列表。
                //   尺寸取立绘的世界尺寸（没立绘时用兜底块）。
                //   ⚠ 命中判定用的是 <c>collider.bounds</c> 做**代码点测试**，不是 Physics2D.OverlapPoint：
                //     少一层"2D 物理设置 / 层 / 查询开关"的依赖，而命中框在 Inspector 里照样可见可调。
                var hitCol = root.AddComponent<BoxCollider2D>();
                hitCol.isTrigger = true;
                // ⚠ 尺寸必须用 **SpriteRenderer.bounds（渲染器实际世界尺寸）**换算回 root 的局部空间：
                //   `sprite.bounds.size` 是**贴图原始尺寸**（本例 10.24），而立绘被缩放显示成 ~1.5 ⇒
                //   直接拿它当命中框会**误选邻居**（格子间距才 1.75）。bounds 由渲染器算，不依赖物理。
                {
                    var b = bodySr.bounds;
                    Vector3 sc = root.transform.lossyScale;
                    float sx = Mathf.Max(0.0001f, Mathf.Abs(sc.x)), sy = Mathf.Max(0.0001f, Mathf.Abs(sc.y));
                    hitCol.size = new Vector2(Mathf.Max(0.5f, b.size.x / sx), Mathf.Max(0.6f, b.size.y / sy));
                    hitCol.offset = new Vector2(0f, (b.center.y - root.transform.position.y) / sy);
                }

                view.HpBg = MakeChildSprite(root, "HpBg", new Color(0.10f, 0.08f, 0.06f),
                                            new Vector2(HpBarWidth, HpBarBackHeight), new Vector2(0f, HpBarY), 2);
                view.HpFill = MakeChildSprite(root, "HpFill", BattlePalette.Vital,
                                              new Vector2(HpBarWidth - 0.06f, HpBarBackHeight - 0.06f),
                                              new Vector2(0f, HpBarY), 3);
                SetHpBar2D(view, u.Hp, u.MaxHp);

                // ---- 护盾指示（血条**右侧**）----
                //  护盾**不会自然消退**（只被伤害消耗）⇒ 不显示就等于一管隐形的血。
                float shX = HpBarWidth * 0.5f + ShieldGap + ShieldPlateW * 0.5f;
                view.ShieldPlate = MakeChildSprite(root, "ShieldPlate", ShieldColor,
                                                   new Vector2(ShieldPlateW, ShieldPlateH),
                                                   new Vector2(shX, HpBarY), 2);
                var shTxtGo = new GameObject("ShieldText");
                shTxtGo.transform.SetParent(root.transform, false);   // ⛔ 挂 root，不挂蓝牌（否则继承缩放）
                shTxtGo.transform.localPosition = new Vector3(shX, HpBarY, -0.02f);
                view.ShieldText = shTxtGo.AddComponent<TextMesh>();
                var shFont = LegacyFont();   // ⚠ 本方法里 `f` 在后面才声明，这里自己取一次
                if (shFont != null)
                {
                    view.ShieldText.font = shFont;
                    var shMr = shTxtGo.GetComponent<MeshRenderer>();
                    if (shMr != null) { shMr.sharedMaterial = shFont.material; shMr.sortingOrder = 6; }
                }
                view.ShieldText.text = "";
                view.ShieldText.characterSize = ShieldTextSize;
                view.ShieldText.fontSize = 48;
                view.ShieldText.anchor = TextAnchor.MiddleCenter;
                view.ShieldText.alignment = TextAlignment.Center;
                view.ShieldText.color = ShieldTextColor;
                SetShield2D(view, 0);   // 开场无护盾 ⇒ 收起

                var nameGo = new GameObject("Name");
                nameGo.transform.SetParent(root.transform, false);
                nameGo.transform.localPosition = new Vector3(0f, NameY, 0f);
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

                int h2 = u.RuntimeId != null ? u.RuntimeId.GetHashCode() : i * 7919;
                view.Phase = (Mathf.Abs(h2) % 1000) / 1000f * Mathf.PI * 2f;

                // ---- 状态图标排（头顶）----
                //  数据只来自帧流的 UnitSnapshot.StatusIds（`id:层数|id:层数`），
                //  ⛔ 不读 _state 里的单位（同 Hint 的理由：自动模式是终局态、手动模式是分片推进）。
                {
                    var rowGo = new GameObject("StatusRow");
                    rowGo.transform.SetParent(root.transform, false);
                    rowGo.transform.localScale = Vector3.one / rsHint;              // 世界尺寸恒定
                    // ⛔ 位置用**局部单位**（与 NameY / HpBarY 同一套语义）。
                    //   原来写的是 `StatusRowY / rsHint` ⇒ 除了一次缩放 ⇒ 实际飞到 2.05 世界高，
                    //   而立绘顶只有 0.53 ⇒ 徽标飘在头顶 1.5 个身位（用户实测："状态太高了"）。
                    rowGo.transform.localPosition = new Vector3(0f, StatusRowY, 0f);
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
                        // 0.05→0.375 宽（已超过 0.36 的底板）⇒ 取 0.032（约 0.27，含格容器缩放）。
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

                _views[u.RuntimeId] = view;
                // 开场就有的单位立刻可见；中途增员先隐藏 —— 由 ApplyFrame 在它第一次出现在帧流里时唤起。
                if (hasFrame0 && !bornAtStart.Contains(u.RuntimeId)) root.SetActive(false);
                unitIndex++;
            }
        }

        private void BuildCamera()
        {
            var go = new GameObject("Cam2D");
            go.transform.SetParent(transform, false);
            _cam = go.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = 5.4f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = BattlePalette.Paper;
            go.transform.localPosition = new Vector3(0f, 0f, -10f);
        }

        // ================================================================
        //  回放（与 3D 灰盒同契约：ApplyFrame + ApplyEvent + Step）
        // ================================================================

        public void ApplyFrame(ViewFrame frame)
        {
            if (frame.Changed == null) return;
            for (int i = 0; i < frame.Changed.Length; i++)
            {
                var s = frame.Changed[i];
                if (!_views.TryGetValue(s.UnitId, out var v))
                {
                    // ★ 中途增员（召唤 / 分身）**惰性补建**：它「此刻」才进入名册。
                    //   ⚠ 不能只靠开演时的预建：手动模式下 BattlePlayback 是**分片推进**的，
                    //     开演那一刻还没跑到"冰晶出现"的回合（用户 2026-10-04 报"还是什么都没有"的真因）。
                    v = EnsureView(s.UnitId);
                    if (v == null) continue;
                }
                // 该单位第一次出现在帧流里 = 真的入场了 ⇒ 把（预建但隐藏 / 刚补建的）view 显出来。
                if (v.Root != null && !v.Root.activeSelf) v.Root.SetActive(true);
                // Boss 机制提示（冰晶期倒数等）：随帧流更新（空串 = 隐藏）
                if (v.Hint != null)
                {
                    string hint = s.StateHint ?? "";
                    if (v.Hint.text != hint) v.Hint.text = hint;
                }
                // 状态图标排：变了才重画（每秒都在刷的东西，别每帧重建 sprite）
                // ⛔ 阵亡单位必须**清空**图标排 —— 否则尸体头上还挂着一串状态（用户 2026-10-07 截图反馈）
                string sids = s.Alive ? (s.StatusIds ?? "") : "";
                if (v.StatusRow != null && v.StatusShown != sids) UpdateStatusRow(v, sids);
                v.Hp = s.Hp; v.MaxHp = s.MaxHp;
                SetHpBar2D(v, s.Hp, s.MaxHp);
                SetShield2D(v, s.Shield);
                SetOverlay2D(v, s.Alive ? s.StatusIds : null);   // 阵亡不挂状态外观
                if (v.Alive && !s.Alive) v.DeadBlend = 0f;
                v.Alive = s.Alive;
            }
        }

        /// <summary>
        /// **世界空间点选**：鼠标世界坐标落在哪个单位上（用户定案：不用 UGUI 行悬停）。
        /// 命中口径 = 单位身上 `BoxCollider2D` 的**世界包围盒**（不是 Physics2D 查询，
        /// 少一层物理设置依赖）。多个重叠时取**最靠上**的那个（贴近"指到谁就是谁"）。
        /// 输出 <paramref name="anchorWorld"/> = 头顶上方一点，给 UGUI 面板定位用。
        /// </summary>
        public bool TryPickUnit(Vector3 worldPos, out string unitId, out string displayName, out Vector3 anchorWorld)
        {
            unitId = null; displayName = null; anchorWorld = worldPos;
            if (_views == null) return false;
            UnitView2D best = null; float bestTop = float.MinValue;
            foreach (var kv in _views)
            {
                var v = kv.Value;
                if (v == null || v.Root == null || !v.Alive) continue;   // 阵亡的不参与（指尸体不该弹面板）
                var col = v.Root.GetComponent<BoxCollider2D>();
                if (col == null) continue;
                // ⚠ 不用 `col.bounds` —— 它要物理引擎把碰撞体注册进去才有值（2D 物理没开/没步进时会是零）。
                //   自己按"碰撞体尺寸 × 世界缩放"算包围盒：确定、且**命中框仍在 Inspector 里可见可调**。
                var tr = v.Root.transform;
                Vector3 c = tr.TransformPoint(col.offset);
                Vector3 sz = Vector3.Scale(col.size, tr.lossyScale);
                float hx = Mathf.Abs(sz.x) * 0.5f, hy = Mathf.Abs(sz.y) * 0.5f;
                if (worldPos.x < c.x - hx || worldPos.x > c.x + hx) continue;
                if (worldPos.y < c.y - hy || worldPos.y > c.y + hy) continue;
                if (c.y + hy > bestTop) { bestTop = c.y + hy; best = v; }
            }
            if (best == null) return false;
            unitId = best.UnitId;
            displayName = best.NameText != null ? best.NameText.text : null;
            anchorWorld = new Vector3(best.Root.transform.position.x, bestTop, best.Root.transform.position.z);
            return true;
        }

        /// <summary>
        /// 取某单位**当前画面**上的状态串（`id:层数|id:层数`）—— 给"悬停看状态列表"用。
        /// ⛔ 数据源是**帧流累积出来的视图**（ApplyFrame 逐帧叠出来的），**不是 `_state`**：
        ///   自动模式下 `_state` 是整场跑完的终局态，直接读会让提示从第一帧就显示"打完之后的状态"。
        /// </summary>
        public bool TryGetViewStatusIds(string unitId, out string ids)
        {
            ids = null;
            if (string.IsNullOrEmpty(unitId) || _views == null) return false;
            UnitView2D v;
            if (!_views.TryGetValue(unitId, out v) || v == null) return false;
            ids = v.StatusShown;
            return true;
        }

        /// <summary>
        /// 重画某单位的**状态图标排**。<paramref name="ids"/> 形如 `burn:3|root:1`。
        /// 图标查 <see cref="StatusIcons"/>（Id 同时也是 `ArtRes/UI/Status/<id>.png` 的文件名 ⇒ 换图即换美术）；
        /// 查不到或没拖图 ⇒ 回退成「色块 + 缩写 + 层数」，绝不空白。
        /// </summary>
        private void UpdateStatusRow(UnitView2D v, string ids)
        {
            v.StatusShown = ids;
            int used = 0;
            if (!string.IsNullOrEmpty(ids) && v.StatusPlates != null)
            {
                var parts = ids.Split('|');
                for (int i = 0; i < parts.Length && used < v.StatusPlates.Length; i++)
                {
                    var kv = parts[i].Split(':');
                    if (kv.Length < 2) continue;
                    int stacks = 1;
                    int.TryParse(kv[1], out stacks);
                    var def = StatusIcons != null ? System.Array.Find(StatusIcons, x => x != null && x.Id == kv[0]) : null;
                    var plate = v.StatusPlates[used];
                    var label = v.StatusLabels != null ? v.StatusLabels[used] : null;
                    if (plate == null) continue;

                    // 图标来源与状态特效同一条口径：表里拖的 `Icon` 优先 → `Resources/UI/StatusIcon/<id>` → 色块+缩写
                    var iconArt = def != null ? def.Icon : null;
                    if (iconArt == null) iconArt = ResolveStatusIconArt(kv[0]);
                    if (iconArt != null)
                    {
                        plate.sprite = iconArt;           // ← 你拖进来 / 放进 Resources 的图
                        plate.color = Color.white;
                        if (label != null) label.text = "";
                    }
                    else
                    {
                        plate.sprite = SolidSprite(Color.white, 32, 32, 2, new Color(0.10f, 0.08f, 0.06f));
                        plate.color = def != null ? def.Color : new Color(0.6f, 0.6f, 0.6f);
                        if (label != null)
                            label.text = ((def != null && !string.IsNullOrEmpty(def.Abbr)) ? def.Abbr : kv[0])
                                       + (stacks > 1 ? stacks.ToString() : "");
                    }
                    plate.gameObject.SetActive(true);
                    if (plate.transform.parent != null) plate.transform.parent.gameObject.SetActive(true);
                    used++;
                }
            }
            for (int s = used; s < (v.StatusPlates != null ? v.StatusPlates.Length : 0); s++)
                if (v.StatusPlates[s] != null && v.StatusPlates[s].transform.parent != null)
                    v.StatusPlates[s].transform.parent.gameObject.SetActive(false);

            // 居中排布。⛔ **不要再除以行缩放**：`rowGo.localScale = 1/rootScale` 的存在意义
            //   正是让**它的局部空间 == 世界单位**（rowGo.lossyScale = (1/rootScale) × rootScale = 1）。
            //   再除一次就缩小了 ~rootScale 倍（实测 9.75×）⇒ 三个状态挤在同一处看不出区别（用户截图报的"叠加"）。
            if (used > 0 && v.StatusRow != null)
            {
                float step = StatusIconSize + StatusIconGap;
                for (int s = 0; s < used; s++)
                    if (v.StatusPlates[s] != null && v.StatusPlates[s].transform.parent != null)
                        v.StatusPlates[s].transform.parent.localPosition =
                            new Vector3((s - (used - 1) * 0.5f) * step, 0f, 0f);
            }
        }

        /// <summary>
        /// 中途增员的**惰性补建**：按 UnitId 到表现层名册（AllUnitsEver）里找到它，就地建一个 view。
        /// 找不到（已下场 / id 不对）返回 null。用法与原因见 <see cref="BuildUnitsFor"/> 的注释。
        /// </summary>
        private UnitView2D EnsureView(string unitId)
        {
            if (_state == null || string.IsNullOrEmpty(unitId)) return null;
            var all = _state.AllUnitsEver;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null || u.RuntimeId != unitId) continue;
                if (!u.Pos.IsValid) return null;
                BuildUnitsFor(new BattleUnit[] { u });
                UnitView2D v;
                _views.TryGetValue(unitId, out v);
                return v;
            }
            return null;
        }

        // ================================================================
        //  出手冲锋
        // ================================================================

        private const float LungeForward = 0.14f;   // 前冲时长
        private const float LungeHold = 0.10f;      // 贴脸停留（受击反馈在这段里播）
        private const float LungeBack = 0.16f;      // 回位时长

        /// <summary>
        /// 让施动者冲到目标面前。停在目标前方 0.75 世界单位处（棋盘格 1.15，
        /// 这个距离刚好"贴上但不盖住"），冲锋时立绘临时置顶，回位还原。
        /// </summary>
        public void StartLunge(string actorId, string targetId)
        {
            if (string.IsNullOrEmpty(actorId)) return;
            if (!_views.TryGetValue(actorId, out var a) || a.Root == null || !a.Alive) return;
            if (a.LungeActive) return;                  // 已经在冲，别叠

            Vector2 stop;
            if (!string.IsNullOrEmpty(targetId) && _views.TryGetValue(targetId, out var t) && t.Root != null)
            {
                Vector2 delta = t.HomePos - a.HomePos;
                stop = delta.sqrMagnitude > 0.0001f
                    ? t.HomePos - delta.normalized * 0.75f
                    : t.HomePos;
            }
            else
            {
                // 群体技 / 无单体目标：朝对面方向冲一步
                float dir = a.HomePos.x < 0f ? 1f : -1f;
                stop = a.HomePos + new Vector2(dir * 1.6f, 0f);
            }

            a.LungeActive = true;
            a.LungeElapsed = 0f;
            a.LungeTo = stop;
            if (a.Body != null)
            {
                if (a.BaseSortingOrder == 0) a.BaseSortingOrder = a.Body.sortingOrder;
                a.Body.sortingOrder = 20;               // 冲锋中压过目标立绘
            }
        }

        public void ApplyEvent(int index, BattleEvent e)
        {
            switch (e.Kind)
            {
                case BattleEventKind.ActionBegin:
                    _actingId = e.ActorId;
                    break;

                case BattleEventKind.SkillCast:
                    // 出手：冲上去（GDD 的"跑到目标面前"）。目标 id 缺失（群体技/
                    // 纯增益）时退化为"朝敌方方向冲一段"，不影响观感。
                    StartLunge(e.ActorId, e.TargetId);
                    // ★ 技能名飘字（用户 2026-10-06："怎么体现这些技能效果"）：
                    //   普攻每回合都放，飘了会刷屏 ⇒ 只飘战技/绝技。技能名事件里本来就有（skillName）。
                    if (!string.IsNullOrEmpty(e.SkillName) && e.SkillName != "普攻" &&
                        e.ActorId != null && _views.TryGetValue(e.ActorId, out var scv))
                        SpawnNumber(scv, e.SkillName, SkillNameColor);
                    break;

                case BattleEventKind.Damage:
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var hit))
                    {
                        SpawnDamageNumber(hit, e.Amount, e.Note != null && e.Note.Contains("融冰"));
                        hit.Body.color = new Color(1f, 0.55f, 0.5f);
                        hit.ShakePulse = 0.16f;
                    }
                    break;

                case BattleEventKind.Heal:
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var hv))
                    {
                        SpawnNumber(hv, "+" + e.Amount, new Color(0.42f, 0.85f, 0.45f));   // 绿：恢复
                        hv.Body.color = new Color(0.72f, 1f, 0.78f);
                    }
                    break;

                case BattleEventKind.Shield:
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var sv))
                    {
                        SpawnNumber(sv, "盾 +" + e.Amount, new Color(0.45f, 0.72f, 1f));   // 蓝：护盾
                        sv.Body.color = new Color(0.75f, 0.88f, 1f);
                    }
                    break;

                case BattleEventKind.Crit:
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var cv))
                        cv.Body.color = new Color(1f, 0.25f, 0.2f);
                    break;

                case BattleEventKind.Death:
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var dv))
                        dv.Alive = false;
                    break;
            }
        }

        public void Step(float dt)
        {
            // 舞台自己的动画时钟：由 dt 累加（**不用 `Time.time`**）——
            // 这样表现层动画与"推进了多少帧"严格对应，编辑器里连调 Step 也能被验证到。
            _animT += dt;
            foreach (var v in _views.Values)
            {
                if (v.Root == null) continue;
                float bob = Mathf.Sin((Time.realtimeSinceStartup + v.Phase) * 2.6f) * 0.035f;
                float sink = 0f;
                if (!v.Alive)
                {
                    v.DeadBlend = Mathf.Min(1f, v.DeadBlend + dt / 0.35f);
                    sink = -0.5f * v.DeadBlend;
                    var c = v.Body.color;
                    float g = Mathf.Lerp(1f, 0.35f, v.DeadBlend);
                    v.Body.color = new Color(v.BaseColor.r * g, v.BaseColor.g * g, v.BaseColor.b * g, 1f - 0.5f * v.DeadBlend);
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

                v.Root.transform.localPosition = new Vector3(
                    v.HomePos.x + lunge.x, v.HomePos.y + FootOffset + lunge.y + bob + sink, 0f);

                // 血条跟随立绘（相机 2D 朝 -Z，直接摆即可）
                if (v.HpBg != null) v.HpBg.transform.localPosition = new Vector3(0f, HpBarY, -0.01f);
                // ⛔ 填充条**只改 y，必须保留 x**：x 是 SetHpBar2D 算的"左端固定"偏移，
                //   这里每帧归零的话填充就永远居中 ⇒ 观感变成"血从两边一起掉"（用户实测两轮的真因）。
                if (v.HpFill != null)
                {
                    var flp = v.HpFill.transform.localPosition;
                    v.HpFill.transform.localPosition = new Vector3(flp.x, HpBarY, -0.02f);
                }
                float shX2 = HpBarWidth * 0.5f + ShieldGap + ShieldPlateW * 0.5f;
                if (v.ShieldPlate != null) v.ShieldPlate.transform.localPosition = new Vector3(shX2, HpBarY, -0.01f);
                if (v.ShieldText != null) v.ShieldText.transform.localPosition = new Vector3(shX2, HpBarY, -0.02f);

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

            // 伤害数字上浮 + 回收
            for (int i = _tempTexts.Count - 1; i >= 0; i--)
            {
                _tempLife[i] -= dt;
                var go = _tempTexts[i];
                if (go != null)
                {
                    go.transform.localPosition += new Vector3(0f, dt * 0.9f, 0f);
                    var tm = go.GetComponent<TextMesh>();
                    if (tm != null)
                    {
                        var c = tm.color;
                        tm.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(_tempLife[i] / 0.7f));
                    }
                }
                if (_tempLife[i] <= 0f)
                {
                    // ⚠⚠ 这里在 Step(dt) 里，是**每帧路径** —— 原来用 DestroyImmediate
                    //   （强制同步销毁，Unity 运行时明令禁用）⇒ 战斗越久触发次数越多、越来越卡，
                    //   最后卡死（用户实测"战斗久一点就卡死"）。必须用 Destroy（延迟销毁）。
                    if (go != null) Destroy(go);
                    _tempTexts.RemoveAt(i); _tempLife.RemoveAt(i);
                }
            }
        }

        // ================================================================

        /// <summary>通用飘字（伤害/恢复/护盾共用）。美术替换：改这里的字体/字号/描边即可。</summary>
        private void SpawnNumber(UnitView2D v, string text, Color color)
        {
            SpawnNumberInternal(v, text, color);
        }

        private void SpawnDamageNumber(UnitView2D v, int amount, bool isMelt)
        {
            SpawnNumberInternal(v, amount.ToString(), isMelt ? BattlePalette.GoldRich : BattlePalette.Crimson);
        }

        private void SpawnNumberInternal(UnitView2D v, string text, Color color)
        {
            var go = new GameObject("Dmg");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = v.Root.transform.position + new Vector3(0f, 1.5f, -1f);
            var f = LegacyFont();
            var tm = go.AddComponent<TextMesh>();
            if (f != null)
            {
                tm.font = f;
                go.GetComponent<MeshRenderer>().sharedMaterial = f.material;
            }
            tm.text = text;
            tm.characterSize = 0.13f;
            tm.fontSize = 64;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            _tempTexts.Add(go);
            _tempLife.Add(0.7f);
            // ⚠ 运行时不能用 DestroyImmediate（强制同步销毁，长战斗里反复触发会卡）；
            //   改用 Destroy 并把上限放宽到 24，给延迟销毁留出缓冲。
            while (_tempTexts.Count > 24)
            {
                if (_tempTexts[0] != null) Destroy(_tempTexts[0]);
                _tempTexts.RemoveAt(0); _tempLife.RemoveAt(0);
            }
        }

        /// <summary>血条按比例伸缩。<paramref name="v"/> 的填充宽度 = <see cref="HpBarWidth"/>（可在 Inspector 调）。</summary>
        private void SetHpBar2D(UnitView2D v, int hp, int max)
        {
            if (v.HpFill == null || max <= 0) return;
            float ratio = Mathf.Clamp01((float)hp / max);
            float full = Mathf.Max(0.05f, HpBarWidth - 0.06f);   // ⛔ 必须跟 BuildUnitsFor 里创建 HpFill 的宽度一致

            // ⛔ 缩放必须走**归一化比例**，不能再乘 `full`：`MakeChildSprite` 是把尺寸
            //   **烘进 sprite 本身**的（贴图 = size×100 像素，localScale 保持 1）
            //   ⇒ 再乘一次 `full`(3.34) 就是**双重应用**，填充条会宽 3.34 倍、从血槽两边冒出来。
            //   用户实测的两个现象都出自这里："血怎么两边同时扣" + "护盾和血重叠看不清"。
            var t = v.HpFill.transform;
            t.localScale = new Vector3(ratio, 1f, 1f);
            // 左端固定 ⇒ 只从**右边**往回缩（掉血观感正确）
            t.localPosition = new Vector3(-(full - full * ratio) * 0.5f, t.localPosition.y, t.localPosition.z);
            v.HpFill.color = ratio > 0.5f ? BattlePalette.Vital
                          : ratio > 0.25f ? BattlePalette.Gold : BattlePalette.Crimson;
        }

        /// <summary>
        /// 取出状态串里**优先级最高**的那条的外观配置（<paramref name="ids"/> 形如 `freeze:2|burn:3`）。
        /// 没配置过的状态返回 null（不显示外观）。
        /// </summary>
        private StatusOverlay BestOverlayFor(string ids)
        {
            if (string.IsNullOrEmpty(ids) || StatusOverlays == null) return null;
            StatusOverlay best = null;
            var parts = ids.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                var kv = parts[i].Split(':');
                if (kv.Length < 1 || string.IsNullOrEmpty(kv[0])) continue;
                for (int k = 0; k < StatusOverlays.Length; k++)
                {
                    var d = StatusOverlays[k];
                    if (d == null || d.Id != kv[0]) continue;
                    if (best == null || d.Priority < best.Priority) best = d;
                    break;
                }
            }
            return best;
        }

        /// <summary>状态图标（头顶徽标里的图）缓存。</summary>
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> StatusIconCache =
            new System.Collections.Generic.Dictionary<string, Sprite>();

        /// <summary>
        /// 头顶徽标的图标：`Resources/UI/StatusIcon/&lt;状态id&gt;.png` —— 与状态特效同一条"文件名 = id"口径。
        /// 把图丢进那个目录就生效，不用改表、不用改引用。缺图时上层会回退成「色块 + 缩写」。
        /// </summary>
        private static Sprite ResolveStatusIconArt(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            Sprite s;
            if (StatusIconCache.TryGetValue(id, out s) && s != null) return s;
            s = Resources.Load<Sprite>("UI/StatusIcon/" + id);
            // ⛔ **不缓存 null**：否则"图还没放进去时的 null"会被记一辈子
            //   （编辑器里运行时丢图进去也永远取不到；本轮就踩到过，调试了很久）。
            if (s != null) StatusIconCache[id] = s;
            return s;
        }

        /// <summary>状态外观图缓存（id → Sprite；null 也缓存，避免每帧 Load）。</summary>
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> StatusArtCache =
            new System.Collections.Generic.Dictionary<string, Sprite>();

        /// <summary>
        /// 取某状态的外观图：优先用表里拖进去的 `Art`，没有就去 `Resources/UI/StatusArt/&lt;id&gt;` 找。
        /// 「**文件名 = 状态 id**」⇒ 出图后直接丢进那个目录即可生效，不用改任何代码/引用。
        /// </summary>
        private static Sprite ResolveStatusArt(string id, Sprite fromTable)
        {
            if (fromTable != null) return fromTable;
            if (string.IsNullOrEmpty(id)) return null;
            Sprite s;
            if (StatusArtCache.TryGetValue(id, out s) && s != null) return s;
            s = Resources.Load<Sprite>("UI/StatusArt/" + id);
            if (s != null) StatusArtCache[id] = s;   // 同上：**不缓存 null**
            return s;
        }

        /// <summary>
        /// 状态外观：把优先级最高的那条覆盖到立绘上（冰封 = 冰蓝罩 / 束缚 = 苔绿罩 / 燃烧 = 火橙罩…）。
        /// 有美术图就显示美术图（按立绘尺寸自动缩放，`Scale`/`OffsetY` 可调），
        /// 没图则用该状态的半透明色块兜底 —— **不挂任何美术也看得见**。
        /// </summary>
        private void SetOverlay2D(UnitView2D v, string ids)
        {
            if (v.Overlay == null || v.Body == null) return;
            var best = BestOverlayFor(ids);
            string key = best != null ? best.Id : null;
            if (v.OverlayShown == key) return;                 // 变了才换，别每帧重建 sprite
            v.OverlayShown = key;
            if (best == null) { v.Overlay.enabled = false; return; }

            var art = ResolveStatusArt(best.Id, best.Art);
            Vector2 bodySize = v.Body.sprite != null ? v.Body.sprite.bounds.size : new Vector2(1f, 1.4f);
            // ⛔ 修正： 是**本地空间**尺寸（= 像素/PPU），
            //   立绘被 transform 缩放后**并不等于显示尺寸**。
            //   Boss 立绘 1024@100 ⇒ 这里会算成 10.24，而实际只显示 ~2.6
            //   ⇒ 直接拿它当基准，会把状态特效放大 3~4 倍（用户实测：特效图太大了）。
            //   乘上物体缩放才是立绘实际占多大，特效才会与立绘同尺寸。
            var bodyScale = v.Body.transform.lossyScale;
            bodySize = new Vector2(bodySize.x * Mathf.Abs(bodyScale.x), bodySize.y * Mathf.Abs(bodyScale.y));
            if (art != null)
            {
                var a = art.bounds.size;
                v.Overlay.sprite = art;
                v.Overlay.color = new Color(1f, 1f, 1f, Mathf.Clamp01(best.Tint.a * 1.6f));
                v.Overlay.transform.localScale = new Vector3(
                    a.x > 0.0001f ? bodySize.x / a.x * Mathf.Max(0.01f, best.Scale) : 1f,
                    a.y > 0.0001f ? bodySize.y / a.y * Mathf.Max(0.01f, best.Scale) : 1f, 1f);
            }
            else
            {
                // 兜底色块：SolidSprite(32,64) = 0.32×0.64 世界单位，按立绘尺寸放大
                v.Overlay.sprite = SolidSprite(Color.white, 32, 64, 0, Color.white);
                v.Overlay.color = best.Tint;
                v.Overlay.transform.localScale = new Vector3(bodySize.x / 0.32f, bodySize.y / 0.64f, 1f);
            }
            // OffsetY 是**立绘高度的百分比**（-0.1 = 往下挪身高的 10%）—— 比写死像素值好调、也与缩放无关
            v.Overlay.transform.localPosition = new Vector3(0f, best.OffsetY * bodySize.y, -0.02f);
            v.OverlayDef = best;
            // 特效动画材质（按状态共享；取不到 shader 则保持默认 sprite 材质 = 静态图，不会崩）
            var mat = OverlayMaterial(best);
            if (mat != null) v.Overlay.sharedMaterial = mat;
            v.Overlay.enabled = true;
        }

        /// <summary>
        /// 护盾指示：护盾 &gt; 0 时显示血条右侧的蓝牌 + 数值，否则整块收起。
        /// ⚠ 护盾**没有持续回合数、也不会自然消退** —— 只被伤害消耗
        /// （见 <c>BattleUnit.TakeDamage</c> 的 `Shield -= toShield`），也没有任何"回合末清空"的逻辑。
        /// 所以它是"还剩多少"的真实信息，必须显示。
        /// </summary>
        private void SetShield2D(UnitView2D v, int shield)
        {
            bool on = shield > 0;
            if (v.ShieldPlate != null && v.ShieldPlate.gameObject.activeSelf != on)
                v.ShieldPlate.gameObject.SetActive(on);
            if (v.ShieldText != null)
            {
                if (v.ShieldText.gameObject.activeSelf != on) v.ShieldText.gameObject.SetActive(on);
                string t = on ? shield.ToString() : "";
                if (v.ShieldText.text != t) v.ShieldText.text = t;
            }
        }

        // ================================================================
        //  工具
        // ================================================================

        /// <summary>
        /// 单位落位：★ 必须与 <see cref="BuildBoard"/> 的格子公式**完全一致**，
        /// 否则单位站不进格子（用户反馈"九宫格站不对"的真因：网格用 1.00/0.72，
        /// 单位却用 0.82/0.66 且双方 y 基准相反 ⇒ 永远对不上）。
        /// 网格格子中心 = (cx + (col-1)*Cell, BoardY + (1-row)*Cell*0.72)。
        /// 单位枢轴在底部中点、Step 会再加 FootOffset 抬脚 ⇒ 这里先减去它。
        /// 敌方做列镜像（col→2-col）与我方对称；BuildBoard 用同一镜像公式，单位与格子始终对齐。
        /// </summary>
        private static Vector2 CellPos(bool player, int cell)
        {
            int col = cell % 3;
            if (!player) col = 2 - col;   // 敌方列镜像 ⇒ 同码两侧呈镜像对称
            int row = cell / 3;
            float cx = player ? PlayerX : EnemyX;
            return new Vector2(cx + (col - 1) * Cell,
                               BoardY + (1 - row) * Cell * BoardRowGap - FootOffset);
        }

        private SpriteRenderer MakeChildSprite(GameObject parent, string name, Color c,
                                               Vector2 size, Vector2 localPos, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = new Vector3(localPos.x, localPos.y, -0.01f * order);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SolidSprite(c, (int)(size.x * 100), (int)(size.y * 100), 0, c);
            sr.sortingOrder = order;
            return sr;
        }

        private static Sprite _solid;
        public static Sprite SolidSprite(Color c, int w, int h, int border, Color borderColor)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool edge = border > 0 && (x < border || y < border || x >= w - border || y >= h - border);
                    px[y * w + x] = edge ? borderColor : c;
                }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Font _legacyFont;
        private static Font LegacyFont()
        {
            if (_legacyFont != null) return _legacyFont;
            _legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_legacyFont == null) _legacyFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _legacyFont;
        }

        private SpriteCatalog _catalog;
        private SpriteCatalog FindCatalog()
        {
            if (_catalog != null) return _catalog;
            // Resources.FindObjectsOfTypeAll 在编辑器里能找到**未加载进场景的资产**（含 SO）
            var all = Resources.FindObjectsOfTypeAll<SpriteCatalog>();
            foreach (var c in all)
                if (c != null && c.Entries.Count > 0) { _catalog = c; break; }
            return _catalog;
        }
    }
}
