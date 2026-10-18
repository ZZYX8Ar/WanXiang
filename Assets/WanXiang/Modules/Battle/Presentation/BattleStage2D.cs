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
    public sealed class UnitView2D
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
        /// <summary>状态外观层（冰封/束缚/燃烧…），是 <see cref="Body"/> 的子节点 ， 跟着立绘走。</summary>
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
        /// <summary>起手预警（2026-10-16 FoeKit）：敌人头顶的谱系色块文字。与 Hint 分开建，
        /// 因为它要**按谱系换颜色**且生命周期独立（每次出招闪一下就消失）。</summary>
        public TextMesh Telegraph;

        // ---- 破绽 / 失衡条（2026-10-17，设计文档第 4 章的 UI 落点）----
        /// <summary>破绽/失衡条（底 + 填充）。挂在 HP 条正下方，与它同一套本地坐标系。</summary>
        public SpriteRenderer FlawBg;
        public SpriteRenderer FlawFill;
        /// <summary>条上方的文字（"破绽·焰心　1/3" / 失衡时 "失衡 ×1.8　剩 2 回合"）。
        /// ⛔ 挂 root 而不是条底下（同 ShieldText 的教训：底板若带缩放会把字一起放大）。</summary>
        public TextMesh FlawLabel;
        /// <summary>已显示的文本（变了才写，TextMesh.text 写入会触发网格重建）。</summary>
        public string FlawShownText;
        public int Hp, MaxHp;
        public bool Alive = true;
        /// <summary>头顶信息容器（血条/名字/护盾/破绽条）。缩放为 1/身体缩放 ⇒ **世界尺寸恒定**。</summary>
        public Transform Head;
        /// <summary>该单位脚下格子（0~8，与 <c>u.Pos.Index</c> 同一套）。克制染色按它定位。</summary>
        public int CellIndex;
        /// <summary>是否我方（决定染哪一方的棋盘）。</summary>
        public bool PlayerSide;
        public Vector2 HomePos;        // 站位（未加浮动）
        public float Phase;            // 待机浮动相位（随机，方案 §最易做错三件事之一）
        public float DeadBlend;
        public float ShakePulse;         // 受击回弹（>0 时向基色回退）
        public Color BaseColor = Color.white;
        public Vector2 KickVec;          // 受击位移（沿受击方向小踹一脚，衰减）
        public float KickT;              // 受击位移剩余时间
        public float HitFlash;           // 受击白闪剩余时间（>0 时往白色插值）

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
        public float HpBarY = 2.35f;
        /// <summary>血条满宽（世界单位）。</summary>
        public float HpBarWidth = 3.40f;

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
        ///   ， 0.012 时文字只有 0.02 世界宽（等于看不见）。0.14 时普通单位约 0.05/字、首领约 0.13/字，
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
        private readonly List<GameObject> _tempTexts = new List<GameObject>(16);
        private readonly List<float> _tempLife = new List<float>(16);
        private readonly List<float> _tempBaseScale = new List<float>(16);   // 每个飘字的基准缩放（暴击更大）
        private readonly List<float> _tempLifeMax = new List<float>(16);     // 每个飘字的**总寿命**（0.7f 是伤害数字的基准；判定飘字更长）
        // ---- 判定横幅（2026-10-16，用户："判断成功和失败、加强和减弱能在醒目一点吗，
        //   还是说是太快了，导致看的不是很清楚"）——
        //   之前只有头顶一个小飘字（0.7s 寿命、characterSize 0.13），在棋盘上又小又短，
        //   玩家基本扫一眼就没了。现在加一条**屏幕中央的大横幅**：弹入 + 停留 + 淡出，
        //   成功（金/青白）与失败（暗红）颜色对立，一眼能分清。
        private TextMesh _verdict;         // 横幅文字
        private MeshRenderer _verdictMr;
        private SpriteRenderer _verdictBg; // 横幅底衬（白字压浅背景会看不见）
        private float _verdictT;           // 横幅已播放时长
        private float _verdictAlpha;
        private bool _verdictActive;
        /// <summary>横幅停留时长（弹入 0.12s + 停留 + 淡出 0.35s 的总账）。</summary>
        private const float VerdictHold = 0.62f;
        private Camera _cam;
        /// <summary>舞台动画时钟（Step 里按 dt 累加）。表现层所有周期动画都用它，别用 Time.time。</summary>
        private float _animT;
        private string _actingId;
        private SpriteRenderer _turnArrow;          // 当前行动者头顶向下箭头（Build 时创建）
        private static Sprite _turnArrowSprite;     // 向下箭头贴图（战斗间共享，懒构建）
        private bool _critNext;        // 下一条伤害数字是否按暴击放大（Crit 事件先于 Damage 到）

        // 大招特写期间押后的伤害反馈（cut-in 播完、起爆那一下再统一飘出来）
        private struct PendDmg { public UnitView2D View; public int Amount; public bool Melt; public bool Reaction; public bool Crit; public Vector2 ActorHome; }
        private readonly List<PendDmg> _pendingDmg = new List<PendDmg>(8);
        private UnitView2D _pendingCritFloat;

        // ---- 四周前景装饰（2026-10-11 战斗美化：随幕变化 + 轻微摇曳）----
        private Transform _fgDecor;
        /// <summary>环境粒子（按幕：春花瓣/夏萤火/长夏花粉/秋落叶/冬雪花）。</summary>
        private BattleAmbientParticles _ambient;

        // ---- 终结技 / 觉醒技 大演出 + 镜头震动（2026-10-13）----
        private SkillFxPlayer _skillFx;
        private Vector3 _camBase;                 // 相机基准位（震动围绕它抖）
        private float _shakeAmp, _shakeT, _shakeDur;
        private float _zoomBase = 5.4f;           // 相机基准 orthographicSize（BuildCamera 覆写）
        private float _zoomDip, _zoomT, _zoomDur; // 起爆点 Zoom Punch（推近再弹回）

        public Camera Camera => _cam;

        // ================================================================
        //  搭建
        // ================================================================

        public void Build(BattleState st, Sprite springBg, SpriteCatalog catalog = null, Sprite fgDecor = null, int act = 1)
        {
            _catalog = catalog;
            Clear();
            _state = st;
            // 自证身份：看到这行 = 跑的是"身体特效已关"的 2026-10-09 版本
            Debug.Log("[Stage2D] Build v2026-10-09: StatusArtEnabled=" + StatusArtEnabled + "（false=身体特效不创建，只用头顶小图标）");

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

            // ---- 四周前景装饰（透明 PNG、随幕变化；sortingOrder 30 = 压过冲锋立绘 20）----
            if (fgDecor != null)
            {
                var fg = new GameObject("FG_Decor");
                fg.transform.SetParent(transform, false);
                var fgSr = fg.AddComponent<SpriteRenderer>();
                fgSr.sprite = fgDecor;
                var fb = fgDecor.bounds;
                // ⚠ 必须**超铺**：原本正好铺满视口(19.2x10.8)，而 Step() 里会绕自身中心
                //   旋转 ±0.5° 做"风吹摇曳" ， 矩形四角会翘出视口、露出底色边缘
                //   （用户 2026-10-12："晃动的时候有点暴露边缘，太贴近边缘，可以往边缘外放一下"）。
                //   超铺 12% ， 每边多出 ~1.0 世界单位，旋转 0.5° 的角部翘出仅 ~0.09，余量充足。
                const float over = 1.12f;
                fg.transform.localScale = new Vector3(19.2f * over / fb.size.x, 10.8f * over / fb.size.y, 1f);
                fgSr.sortingOrder = 30;
                _fgDecor = fg.transform;
            }

            // ---- 环境粒子（按幕：春花瓣/夏萤火/长夏花粉/秋落叶/冬雪花）----
            _ambient = GetComponent<BattleAmbientParticles>();
            if (_ambient == null) _ambient = gameObject.AddComponent<BattleAmbientParticles>();
            _ambient.Build(act);

            BuildBoard("BoardP", PlayerX, BoardY, true);

            BuildBoard("BoardE", EnemyX, BoardY, false);
            BuildUnits();
            BuildCamera();
            BuildTurnArrow();

            // ⚠ 必须套第 0 帧（初始快照）。BattleState 传进来时**这场战斗已经跑完了**
            //   （BattlePlayback 构造里一次 Run 到底），所以 BuildUnits 拿到的是终局
            //   HP/存活状态 —— 不套初帧的话，开局画面就是"一半人已经死了"。
            if (st.Frames.Count > 0) ApplyFrame(st.Frames[0]);
        }

        // ================================================================
        //  当前行动者指示器：头顶向下箭头
        // ================================================================
        /// <summary>
        /// 外部（BattlePanel）告知"当前轮到谁"—— 等待下令期间 ActionBegin 还没发，
        /// 不调用的话箭头会停在上一个行动者头上（用户要的是"当前是谁在行动"）。
        /// </summary>
        public void SetTurnActor(string unitId) { _actingId = unitId; }

        private void BuildTurnArrow()
        {
            var go = new GameObject("TurnArrow");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            if (_turnArrowSprite == null) _turnArrowSprite = BuildArrowSprite();
            sr.sprite = _turnArrowSprite;
            sr.sortingOrder = 25;            // 压过立绘(20)，低于前景(30)
            sr.enabled = false;             // 默认隐藏，等 ActionBegin 出现再显示
            sr.transform.localScale = new Vector3(1.15f, 1.15f, 1f);
            _turnArrow = sr;
        }

        // 程序化生成"向下箭头"贴图（64×64，白色，运行时按阵营染色）
        private static Sprite BuildArrowSprite()
        {
            const int W = 64, H = 64;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                bool inside = false;
                if (y >= 30 && y <= 54)                          // 杆（上部）
                    inside = Mathf.Abs(x - 32) <= 5;
                else if (y >= 10 && y < 30)                      // 箭头头部（下部，向下）
                {
                    float t = (y - 10) / (30f - 10f);            // 0=底 1=顶
                    float halfW = 5f + t * 24f;                  // 底 5 -> 顶 29
                    inside = Mathf.Abs(x - 32) <= halfW;
                }
                px[y * W + x] = inside ? Color.white : Color.clear;
            }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        }

        // ================================================================
        //  格挡 QTE 提示圈（2026-10-14，用户点名参考 33号远征队）
        //  ----------------------------------------------------------------
        //  收缩环 + 固定目标圈：环套上目标圈的那一拍 = 完美区间（判定在 BattlePanel，
        //  这里只负责"跟随攻击者 + 按进度收缩染色"）。贴图程序化生成，与行动箭头同套路。
        // ================================================================
        private SpriteRenderer _parryRing;       // 收缩环
        private SpriteRenderer _parryTarget;     // 目标圈（固定尺寸）
        private string _parryActorId;            // 窗口归属的攻击者（null = 无窗口）
        private float _parryK;                   // 环收缩进度（0→1，由面板每帧喂）
        private static Sprite _parryRingSprite;  // 环贴图（跨战斗共享）
        private static Sprite _parryTargetSprite;
        private static Sprite _qteBgSprite;   // QTE 提示条底衬贴图（实心方块，跨战斗共享）

        /// <summary>打开格挡提示（面板在 NeedParry 时调；跟随与点亮在 Step 里做）。</summary>
        public void ShowParryPrompt(string attackerId)
        {
            _parryActorId = attackerId;
            _parryK = 0f;
            EnsureParryRings();
            if (_parryRing != null) _parryRing.enabled = false;
            if (_parryTarget != null) _parryTarget.enabled = false;
        }

        /// <summary>喂环收缩进度（0..1）。</summary>
        public void SetParryProgress(float k) { _parryK = Mathf.Clamp01(k); }

        /// <summary>关闭提示圈。</summary>
        public void HideParryPrompt()
        {
            _parryActorId = null;
            if (_parryRing != null) _parryRing.enabled = false;
            if (_parryTarget != null) _parryTarget.enabled = false;
        }

        // ================================================================
        //  格挡成功「爆闪」（2026-10-15 用户："格挡成功的反馈我好像没有怎么体会到"）
        //  ----------------------------------------------------------------
        //  之前格挡成功只有：① 头顶飘"完美格挡!"小字 ② 轻微震屏。玩家说"没体会到"。
        //  现在在下场者（被格挡的攻击者）身上炸一圈**扩散光环 + 中心闪光**：
        //    · 完美档：金色大光环 + 双环 + 白闪，扩得大、留得久
        //    · 普通档：青白小环，快收
        //  ⚠ 由 Step(dt) 推进 —— dt=0（顿帧）时也会停住，那正是"顿"手感的一部分。
        // ================================================================
        private sealed class ParryBurst
        {
            public SpriteRenderer Sr;
            public float T, Dur, R0, R1;
            public Color C;
        }
        private readonly List<ParryBurst> _parryBursts = new List<ParryBurst>(4);
        private static Sprite _parryDiscSprite;   // 实心圆盘（中心闪光）

        /// <summary>
        /// 格挡成功爆闪。grade：0=未挡（不爆）/1=普通/2=完美。
        /// </summary>
        public void ParryImpact(string unitId, int grade)
        {
            if (grade <= 0 || string.IsNullOrEmpty(unitId) || _views == null) return;
            UnitView2D v;
            if (!_views.TryGetValue(unitId, out v) || v == null || v.Root == null) return;

            Vector3 p = v.Root.transform.position + new Vector3(0f, 0.9f, -0.6f);
            bool perfect = grade >= 2;
            EnsureParryRings();
            if (_parryRingSprite == null) _parryRingSprite = BuildRingSprite(128, 6);
            if (_parryDiscSprite == null) _parryDiscSprite = BuildDiscSprite(96);

            if (perfect)
            {
                // 完美：外圈金环（扩得大）+ 内圈白环（快）+ 中心闪光
                SpawnBurst(p, _parryRingSprite, new Color(1f, 0.84f, 0.38f, 0.95f), 0.35f, 3.1f, 0.42f, 30);
                SpawnBurst(p, _parryRingSprite, new Color(1f, 1f, 0.94f, 0.90f), 0.25f, 1.9f, 0.26f, 31);
                SpawnBurst(p, _parryDiscSprite, new Color(1f, 0.96f, 0.80f, 0.75f), 0.30f, 1.35f, 0.20f, 29);
            }
            else
            {
                // 普通：一圈青白环
                SpawnBurst(p, _parryRingSprite, new Color(0.72f, 0.92f, 1f, 0.85f), 0.25f, 1.7f, 0.28f, 30);
            }
        }

        private void SpawnBurst(Vector3 pos, Sprite sp, Color c, float r0, float r1, float dur, int order)
        {
            var go = new GameObject("ParryBurst");
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            sr.sortingOrder = order;
            sr.color = c;
            _parryBursts.Add(new ParryBurst { Sr = sr, T = 0f, Dur = dur, R0 = r0, R1 = r1, C = c });
        }

        private void StepParryBursts(float dt)
        {
            for (int i = _parryBursts.Count - 1; i >= 0; i--)
            {
                var b = _parryBursts[i];
                if (b.Sr == null) { _parryBursts.RemoveAt(i); continue; }
                b.T += dt;
                float k = Mathf.Clamp01(b.T / b.Dur);
                // 半径：快出慢收（1-(1-k)^2）
                float e = 1f - (1f - k) * (1f - k);
                float r = Mathf.Lerp(b.R0, b.R1, e);
                b.Sr.transform.localScale = new Vector3(r, r, 1f);
                var c = b.C;
                b.Sr.color = new Color(c.r, c.g, c.b, c.a * (1f - k));
                if (k >= 1f)
                {
                    Destroy(b.Sr.gameObject);
                    _parryBursts.RemoveAt(i);
                }
            }
        }

        /// <summary>程序化**实心圆盘**贴图（中心亮、边缘衰减；格挡中心闪光用）。</summary>
        private static Sprite BuildDiscSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            float c = (size - 1) * 0.5f;
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                float a = Mathf.Clamp01(1f - d);
                a *= a;
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>是否正在播大招 cut-in（格挡窗要等它播完再弹，用户 2026-10-15）。</summary>
        public bool CutInPlaying => _skillFx != null && _skillFx.CutInActive;


/// <summary>判定反馈：在被判定者头顶飘字 + **屏幕中央横幅**。
        /// 格挡 = 完美格挡!/格挡/未挡；终结技 = 共鸣·强化/共鸣/失谐·威力减弱；连携 = 阵成/结阵/散阵。
        ///
      /// ★★ 2026-10-16 用户："判断成功和失败，加强和减弱能在醒目一点吗，
        ///   还是说是太快了，导致看的不是很清楚" —— 两处一起改：
        ///     ① 头顶飘字寿命从 0.7s 拉到 1.25s（判定类文字要看得完）；
        ///     ② 加屏幕中央**大横幅**（弹入 + 停留 0.62s + 淡出），成功用金/青白、
        ///        失败用暗红，两种颜色对立，一眼能分清"这次打没打准"。
   /// </summary>
        public void ParryFeedback(string attackerId, int grade, bool finisher = false, bool combo = false)
        {
            if (string.IsNullOrEmpty(attackerId) || _views == null) return;
    UnitView2D v;
      if (!_views.TryGetValue(attackerId, out v) || v == null || v.Root == null) return;
       string banner; Color bc;
     if (combo)
      {
      // 阵形连携逐段反馈（写在施法者头上；成败由模拟层合并判定，这里只报单段手感）
  if (grade == 2) { banner = "阵成"; bc = new Color(1f, 0.85f, 0.45f); SpawnNumberInternal(v, "阵成!", bc, 1.7f, JudgeLife); }
         else if (grade == 1) { banner = "结阵"; bc = new Color(0.85f, 0.92f, 1f); SpawnNumberInternal(v, "结阵", bc, 1.4f, JudgeLife); }
                else { banner = "散阵"; bc = new Color(0.86f, 0.36f, 0.32f); SpawnNumberInternal(v, "散阵", bc, 1.4f, JudgeLife); }
                ShowVerdict(banner, bc);
         return;
  }
      if (finisher)
            {
        // 终结技 QTE：飘在被施放者（我方）头上
    if (grade == 2) { banner = "共鸣 · 威力强化"; bc = new Color(1f, 0.82f, 0.42f); SpawnNumberInternal(v, "共鸣! 强化", bc, 1.7f, JudgeLife); }
    else if (grade == 1) { banner = "共鸣"; bc = new Color(1f, 0.90f, 0.62f); SpawnNumberInternal(v, "共鸣", bc, 1.4f, JudgeLife); }
          else { banner = "失谐 · 威力减弱"; bc = new Color(0.92f, 0.40f, 0.34f); SpawnNumberInternal(v, "失谐! 威力减弱", bc, 1.5f, JudgeLife); }
                ShowVerdict(banner, bc);
         return;
            }
          if (grade == 2) { banner = "完美格挡"; bc = new Color(1f, 0.85f, 0.35f); SpawnNumberInternal(v, "完美格挡!", bc, 1.7f, JudgeLife); }
   else if (grade == 1) { banner = "格挡"; bc = new Color(0.72f, 0.88f, 1f); SpawnNumberInternal(v, "格挡", bc, 1.4f, JudgeLife); }
       else { banner = "未挡"; bc = new Color(0.90f, 0.38f, 0.32f); SpawnNumberInternal(v, "未挡", bc, 1.4f, JudgeLife); }
            ShowVerdict(banner, bc);
  }

        /// <summary>判定飘字的寿命（比伤害数字 0.7s 长一截：判定结果要"看得完"）。</summary>
        private const float JudgeLife = 1.25f;

        /// <summary>
      /// 屏幕中央判定横幅。成功/失败各一套颜色，由 <see cref="ParryFeedback"/> 调。
        /// ⛔ 纯表现层，不读战斗状态（帧流纪律）。
        /// </summary>
private void ShowVerdict(string text, Color c)
    {
   EnsureVerdict();
    if (_verdict == null) return;
 _verdict.text = text;
     _verdict.color = c;
        // 底衬 = 判定色的**暗版**（不是纯黑）：暗红底衬配"未挡"、暗金配"强化"，
            // 颜色本身就在传递成败，不只是一块黑底。
  if (_verdictBg != null) _verdictBg.color = new Color(c.r * 0.22f, c.g * 0.22f, c.b * 0.22f, 0.86f);
   _verdictT = 0f;
            _verdictActive = true;
   }

   private void EnsureVerdict()
        {
            if (_verdict != null) return;
            if (_qteBgSprite == null) _qteBgSprite = BuildSolidSprite(64);
    if (_verdictBg == null)
            {
       var bgGo = new GameObject("VerdictBg");
        bgGo.transform.SetParent(transform, false);
          bgGo.transform.localPosition = new Vector3(0f, 2.05f, -3f);
        _verdictBg = bgGo.AddComponent<SpriteRenderer>();
      _verdictBg.sprite = _qteBgSprite;
                _verdictBg.sortingOrder = 41;
            }
    var go = new GameObject("Verdict");
            go.transform.SetParent(transform, false);
 go.transform.localPosition = new Vector3(0f, 2.05f, -2f);
    var f = LegacyFont();
      _verdict = go.AddComponent<TextMesh>();
    _verdictMr = go.GetComponent<MeshRenderer>();
         if (f != null)
   {
            _verdict.font = f;
       if (_verdictMr != null) { _verdictMr.sharedMaterial = f.material; _verdictMr.sortingOrder = 42; }
       }
         _verdict.anchor = TextAnchor.MiddleCenter;
            _verdict.alignment = TextAlignment.Center;
    _verdict.characterSize = 0.17f;    // 比 QTE 提示条大一倍：这是"结果"，不是"操作提示"
            _verdict.fontSize = 64;
  }

  /// <summary>横幅推进：弹入（easeOutBack）→ 停留 → 淡出。</summary>
        private void StepVerdict(float dt)
  {
        if (_verdict == null) return;
if (!_verdictActive)
       {
       if (_verdictAlpha <= 0.01f) return;
       _verdictAlpha = Mathf.MoveTowards(_verdictAlpha, 0f, dt * 6f);
       }
         else
            {
         _verdictT += dt;
     if (_verdictT >= VerdictHold)
       {
         _verdictActive = false;
           _verdictAlpha = Mathf.MoveTowards(_verdictAlpha, 0f, dt * 4.5f);
   }
      else _verdictAlpha = Mathf.MoveTowards(_verdictAlpha, 1f, dt * 12f);
      }
      var vc = _verdict.color;
            _verdict.color = new Color(vc.r, vc.g, vc.b, _verdictAlpha);
if (_verdictMr != null) _verdictMr.enabled = _verdictAlpha > 0.01f;
     // 弹入：0.16s 内从 1.55× 收到 1.0×（easeOutBack）
            float scale = 1f;
    if (_verdictActive && _verdictT < 0.16f)
            {
       float k = Mathf.Clamp01(_verdictT / 0.16f);
    float c1 = 1.70158f, c3 = c1 + 1f;
      float e = 1f + c3 * (k - 1f) * (k - 1f) * (k - 1f) + c1 * (k - 1f) * (k - 1f);
       scale = Mathf.Lerp(1.55f, 1f, Mathf.Clamp01(e));
     }
   _verdict.transform.localScale = new Vector3(scale, scale, 1f);
if (_verdictBg != null)
     {
      var bc = _verdictBg.color;
   _verdictBg.color = new Color(bc.r, bc.g, bc.b, 0.86f * _verdictAlpha);
     _verdictBg.enabled = _verdictAlpha > 0.01f;
    if (_verdictMr != null && _verdictAlpha > 0.01f)
   {
      float tw = _verdictMr.bounds.size.x;
       float ps = _verdictBg.transform.parent != null ? _verdictBg.transform.parent.lossyScale.x : 1f;
   if (ps > 0.0001f) tw /= ps;
   float w = Mathf.Clamp(tw > 0.01f ? tw + 0.55f : 2.4f, 1.6f, 9.0f);
         _verdictBg.transform.localScale = new Vector3(w, 0.72f, 1f);
            }
    }
     }

        // ================================================================
        //  QTE 提示条（2026-10-14，用户："这个 qte 是按什么按键的啊，都没有提示"）
        // ================================================================
        private TextMesh _qteHint;              // 屏幕中央的按键提示文字
        private MeshRenderer _qteHintMr;       // 提示文字的渲染器（显隐开关；TextMesh 本身没有 enabled）
        private SpriteRenderer _qteHintBg;     // 提示条的深色底衬（白字压在浅背景上看不清）
        private float _qteHintAlpha;            // 淡入淡出用（窗口期常亮）
        private string _qteHintText = "";

        /// <summary>
        /// 设置 QTE 提示条文案。
        /// <paramref name="hitsTotal"/> = 0 表示单发（普通格挡）；&gt;0 表示多段连击，显示"第 k/n 段"。
        /// </summary>
        /// <summary>
        /// 起手预警的**谱系色块**（设计文档 2.2）。映射固定，玩久了形成条件反射：
        ///   赤=劈斩 / 橙=横扫 / 黄=砸地 / 青=贯穿 / 紫=吐息。
        /// ⛔ 用文本首字判定（"青·贯穿 1/2"）而不是回查 state —— 表现层不许读 battle 状态。
        /// </summary>
        private Color TelegraphColorFor(string telegraph)
        {
            if (string.IsNullOrEmpty(telegraph)) return TelegraphColor;
            switch (telegraph[0])
            {
                case '赤': return new Color(0.90f, 0.26f, 0.20f);   // 劈斩
                case '橙': return new Color(0.96f, 0.56f, 0.18f);   // 横扫
                case '黄': return new Color(0.94f, 0.84f, 0.26f);   // 砸地
                case '青': return new Color(0.32f, 0.80f, 0.84f);   // 贯穿
                case '紫': return new Color(0.76f, 0.52f, 0.92f);   // 吐息
                default: return TelegraphColor;
            }
        }

        public void SetQteHint(string text, int hitsTotal = 0, int hitsDone = 0)
        {
            _qteHintText = hitsTotal > 1
          ? text + "　（连击 " + (hitsDone + 1) + "/" + hitsTotal + "）"
    : text;
      EnsureQteHint();
    if (_qteHint != null) _qteHint.text = _qteHintText;
        }

        private void EnsureQteHint()
        {
      if (_qteHint != null) return;

     // ---- 底衬：半透明深色圆角条（白字压在浅色背景上几乎看不见，实测踩过）----
    if (_qteHintBg == null)
       {
      if (_qteBgSprite == null) _qteBgSprite = BuildSolidSprite(64);   // 实心方块（不是圆环）
 var bgGo = new GameObject("QteHintBg");
      bgGo.transform.SetParent(transform, false);
         bgGo.transform.localPosition = new Vector3(0f, 2.42f, -3f);   // 比文字再靠后
       _qteHintBg = bgGo.AddComponent<SpriteRenderer>();
         _qteHintBg.sprite = _qteBgSprite;
    _qteHintBg.sortingOrder = 39;
   _qteHintBg.color = new Color(0.06f, 0.05f, 0.07f, 0.72f);
    }

       var go = new GameObject("QteHint");
  go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 2.42f, -2f);   // 屏幕中央偏上（别压棋盘格子）
            var f = LegacyFont();
         _qteHint = go.AddComponent<TextMesh>();
 _qteHintMr = go.GetComponent<MeshRenderer>();
        if (f != null)
   {
     _qteHint.font = f;
      if (_qteHintMr != null) { _qteHintMr.sharedMaterial = f.material; _qteHintMr.sortingOrder = 40; }
            }
            _qteHint.anchor = TextAnchor.MiddleCenter;
            _qteHint.alignment = TextAlignment.Center;
    _qteHint.characterSize = 0.10f;
            _qteHint.fontSize = 64;
    _qteHint.color = new Color(1f, 0.93f, 0.70f, 0f);
        }

        /// <summary>淡出并收起提示条。</summary>
        public void ClearQteHint() { _qteHintText = ""; }

        private void EnsureParryRings()
        {
            if (_parryRing == null)
            {
                if (_parryRingSprite == null) _parryRingSprite = BuildRingSprite(128, 6);
                var go = new GameObject("ParryRing");
                go.transform.SetParent(transform, false);
                _parryRing = go.AddComponent<SpriteRenderer>();
                _parryRing.sprite = _parryRingSprite;
                _parryRing.sortingOrder = 26;             // 与行动箭头同层，压过立绘
                _parryRing.color = new Color(1f, 0.90f, 0.55f, 0.95f);
                _parryRing.enabled = false;
            }
            if (_parryTarget == null)
            {
                if (_parryTargetSprite == null) _parryTargetSprite = BuildRingSprite(128, 3);
                var go = new GameObject("ParryTarget");
                go.transform.SetParent(transform, false);
                _parryTarget = go.AddComponent<SpriteRenderer>();
                _parryTarget.sprite = _parryTargetSprite;
                _parryTarget.sortingOrder = 26;
                _parryTarget.color = new Color(1f, 1f, 1f, 0.45f);
                _parryTarget.enabled = false;
            }
        }

        // 程序化"圆环"贴图（白色；尺寸与线宽像素级，运行时靠 localScale 缩放）
        private static Sprite BuildRingSprite(int size, int thickness)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            float c = (size - 1) * 0.5f;
            float rOut = size * 0.5f - 1f;
            float rIn = rOut - thickness;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
 {
 float dx = x - c, dy = y - c;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
       px[y * size + x] = (d <= rOut && d >= rIn) ? Color.white : Color.clear;
        }
   tex.SetPixels(px); tex.Apply();
    return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>程序化**实心方块**贴图（白色；给 QTE 提示条当底衬用）。</summary>
        private static Sprite BuildSolidSprite(int size)
   {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = Color.white;
       tex.SetPixels(px); tex.Apply();
return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
   }

        private void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);
            _views.Clear();
            _tempTexts.Clear();
            _tempLife.Clear();
            _tempBaseScale.Clear();
      _tempLifeMax.Clear();
        _verdict = null; _verdictMr = null; _verdictBg = null;
      _verdictActive = false; _verdictT = 0f; _verdictAlpha = 0f;
    _pendingDmg.Clear();
            _pendingCritFloat = null;
            _parryRing = null;      // 提示圈子物体已被上面销毁；标记清空等下次重建
            _parryTarget = null;
            _parryActorId = null;
            _qteHint = null;    // 提示条同理由上面统一销毁
            _qteHintMr = null;
            _qteHintBg = null;
            _qteHintAlpha = 0f;
            _qteHintText = "";
            _state = null;
            _cam = null;
            _fgDecor = null;
            _ambient = null;   // 粒子子物体已被上面销毁；Build 里重新 GetComponent/AddComponent
            _skillFx = null;   // SkillFx 子物体一并销毁；下次 EnsureSkillFx 重建（否则持已毁对象）
            _turnArrow = null; // 箭头子物体已被上面销毁；Build 里重建
            _shakeDur = 0f; _shakeAmp = 0f;
            _parryBursts.Clear();   // 爆闪子物体已被上面统一销毁；只清列表（别对已毁对象操作）
        }

        // player=true 我方（左，不镜像）；player=false 敌方（右，列镜像 ， 与我方对称）
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
                //（原先是 1.75×1.75 正方形 + 1.26 行距 ， 每行压掉 0.49，用户实测就是这个现象）。
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
                        //   sortingOrder 用行号，中宫=row1 ， 会盖住后排(row0)、被前排(row2)压，符合"大个子"直觉。
                        //
                        // ⚠⚠ 判据必须用 `BossScaleIds`（= 14 守关本体 + 双子），**不能**用 `IsBoss(id)`：
                        //   白魍双子复用 `b_suren` 当立绘 id（否则 summon_ 前缀查不到图会"乱分配"），
                        //   它也是 b_ 前缀 —— 用 IsBoss 会把**所有** b_ 前缀都放大（含真分身，太宽）。
                        //   ★ 用户 2026-10-03 更正："他们两个都是 Boss 啊，不是召唤出来的，他们是一起出现的"
                        //     ， 双子（b_suren）与本体同级放大，不再用 1.15 的小尺寸。
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
                // 敌方整体镜像：立绘原画统一朝右，敌阵镜像后朝左 ， 面向我方，自然。
                // ⛔ **例外：素刃（白魍双子）不镜像**。它的原画是**刻意朝左**画的
                //   （早期为双子"背靠背"并排设计的），再统一镜像就变成朝右 ， **背对玩家**
                //   （用户实测："b_suren 这个的朝向反了"）。它现在站后排中(格7)，
                //   与中宫本体是前后关系，本来就该朝前 ， 跳过镜像即可与其它敌方朝向一致。
                bodySr.flipX = !player && u.Def.Id != BossCatalog.TwinBeastId;
                // 敌方压暗一档（§4.4 敌我同源 + 浊化的轻量版）
                if (!player) bodySr.color = new Color(0.72f, 0.72f, 0.80f);
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

                // ★★ 头顶信息容器（2026-10-18 用户："召唤物的血条太大了"）：
                //   血条/名字/护盾/破绽条是**信息**，不该跟着身体一起放大 ——
                //   首领 root 是 2.5 倍身量，血条也就被拉成 8.5 世界单位宽，糊了半个屏幕。
                //   做法：挂一个容器，位置除 k、缩放取 1/k ⇒ 子物件的**世界尺寸与高度恒定**，
                //   而 body 照旧放大。k=1 的普通单位行为完全不变（容器 = 恒等变换）。
                float headK = Mathf.Max(0.0001f, Mathf.Abs(root.transform.localScale.y));
                var headGo = new GameObject("Head");
                headGo.transform.SetParent(root.transform, false);
                headGo.transform.localPosition = new Vector3(0f, HpBarY / headK, 0f);
                headGo.transform.localScale = Vector3.one / headK;
                view.Head = headGo.transform;

                view.HpBg = MakeChildSprite(headGo, "HpBg", new Color(0.10f, 0.08f, 0.06f),
                                            new Vector2(HpBarWidth, HpBarBackHeight), Vector2.zero, 2);
                view.HpFill = MakeChildSprite(headGo, "HpFill", BattlePalette.Vital,
                                              new Vector2(HpBarWidth - 0.06f, HpBarBackHeight - 0.06f),
                                              Vector2.zero, 3);
                SetHpBar2D(view, u.Hp, u.MaxHp);

                // ---- 护盾指示（血条**右侧**）----
                //  护盾**不会自然消退**（只被伤害消耗）， 不显示就等于一管隐形的血。
                float shX = HpBarWidth * 0.5f + ShieldGap + ShieldPlateW * 0.5f;
                view.ShieldPlate = MakeChildSprite(headGo, "ShieldPlate", ShieldColor,
                                                   new Vector2(ShieldPlateW, ShieldPlateH),
                                                   new Vector2(shX, 0f), 2);
                var shTxtGo = new GameObject("ShieldText");
                shTxtGo.transform.SetParent(headGo.transform, false);  // ⛔ 挂 Head（不挂蓝牌，避免继承牌面缩放）
                shTxtGo.transform.localPosition = new Vector3(shX, 0f, -0.02f);
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
                    rowGo.transform.SetParent(root.transform, false);
                    rowGo.transform.localScale = Vector3.one / rsHint;              // 世界尺寸恒定
                    // ⛔ 位置用**局部单位**（与 NameY / HpBarY 同一套语义）。
                    //   原来写的是 `StatusRowY / rsHint` ， 除了一次缩放 ， 实际飞到 2.05 世界高，
                    //   而立绘顶只有 0.53 ， 徽标飘在头顶 1.5 个身位（用户实测："状态太高了"）。
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
            _camBase = go.transform.localPosition;   // 震动基准
            _zoomBase = _cam.orthographicSize;        // Zoom Punch 基准
        }

        // ================================================================
        //  终结技 / 觉醒技 大演出（SkillFxPlayer）
        // ================================================================

        /// <summary>惰性创建特效播放器（挂在舞台根下，与舞台同一时钟由 Step 驱动）。</summary>
        private void EnsureSkillFx()
        {
            if (_skillFx != null) return;
            var go = new GameObject("SkillFx");
            go.transform.SetParent(transform, false);
            _skillFx = go.AddComponent<SkillFxPlayer>();
            _skillFx.Bind(_cam, TriggerShake, TriggerZoom);
        }

        /// <summary>触发一次终结技/觉醒技演出。仅 Ultimate/Awaken 调用（大招要稀有）。</summary>
        private void PlaySkillFx(string actorId, string targetId, Element element, bool isAwaken, string skillName)
        {
            if (string.IsNullOrEmpty(actorId)) return;
            if (!_views.TryGetValue(actorId, out var v) || v.Root == null || !v.Alive) return;
            EnsureSkillFx();
            if (_skillFx == null) { StartLunge(actorId, targetId); return; }
            var col = BattlePalette.OfElement(element);
            // P5R 式 cut-in：把该异兽的立绘 + 名字 + 终结技名传进去，先特写再特效
            var portrait = v.Body != null ? v.Body.sprite : null;
            var beastName = v.NameText != null ? v.NameText.text : null;
            // ★ 冲锋等到"起爆"那一下才冲（否则特写期间就冲完 ⇒ 玩家看到"攻击完才出现冲击波"）；
            //   同时把特写期间押后的伤害数字在这里补飘（用户 2026-10-14）。
            string a = actorId, t = targetId;
            _skillFx.OnImpact = () => { StartLunge(a, t); FlushPendingDmg(); };
            // faceLeft：敌方在右侧、面向我方 ⇒ 特写立绘要镜像（用户 2026-10-18 报"头像反了"）
            _skillFx.Play(v.Root.transform.position, actorId, col, isAwaken, portrait, beastName, skillName,
                          faceLeft: !v.PlayerSide);
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
                // 该单位第一次出现在帧流里 = 真的入场了 ， 把（预建但隐藏 / 刚补建的）view 显出来。
                if (v.Root != null && !v.Root.activeSelf) v.Root.SetActive(true);
                // Boss 机制提示（冰晶期倒数等）：随帧流更新（空串 = 隐藏）
                if (v.Hint != null)
                {
                    string hint = s.StateHint ?? "";
                    if (v.Hint.text != hint) v.Hint.text = hint;
                }
                // ---- 起手预警（2026-10-16 FoeKit）：⚠ 内容一律来自**帧流**（FoeTelegraph），
                //   理由与 StateHint 相同（自动模式 state 是终局态，手动模式是分片的）。
                if (v.Telegraph != null)
                {
                    string tel = s.Alive ? (s.FoeTelegraph ?? "") : "";
                    if (v.Telegraph.text != tel) v.Telegraph.text = tel;
                    // 色块按谱系固定映射（设计文档 2.2）：玩家看颜色就能形成条件反射。
                    if (tel.Length > 0)
                    {
                        var c = TelegraphColorFor(tel);
                        if (v.Telegraph.color != c) v.Telegraph.color = c;
                    }
                }
                // 状态图标排：变了才重画（每秒都在刷的东西，别每帧重建 sprite）
                // ⛔ 阵亡单位必须**清空**图标排 —— 否则尸体头上还挂着一串状态（用户 2026-10-07 截图反馈）
                string sids = s.Alive ? (s.StatusIds ?? "") : "";
                if (v.StatusRow != null && v.StatusShown != sids) UpdateStatusRow(v, sids);
                v.Hp = s.Hp; v.MaxHp = s.MaxHp;
                SetHpBar2D(v, s.Hp, s.MaxHp);
                SetShield2D(v, s.Shield);
                // ---- 破绽/失衡条：随帧流更新（死亡清空）----
                SetFlawBar2D(v,
                             s.Alive ? (s.FlawText ?? "") : "",
                             s.Alive ? s.FlawProgress : -1f,
                             s.Alive ? s.UnbalancedTurns : 0);
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
            if (best == null)
            {
                // ★ 2026-10-18：立绘包围盒之外，再认**状态图标排**（以及名字/血条那一带）。
                //   原本只有立绘本体参与命中 ⇒ 鼠标指着头顶那一排状态图标完全没反应，
                //   而"看这个状态是什么、能引爆什么反应"恰恰是玩家最想点的地方。
                best = PickByStatusRow(worldPos, out float rowTop);
                if (best == null) return false;
                unitId = best.UnitId;
                displayName = best.NameText != null ? best.NameText.text : null;
                // 锚点放**图标排之上**：提示面板不能盖住图标本身
                anchorWorld = new Vector3(best.Root.transform.position.x, rowTop + 0.25f,
                                          best.Root.transform.position.z);
                return true;
            }
            unitId = best.UnitId;
            displayName = best.NameText != null ? best.NameText.text : null;
            anchorWorld = new Vector3(best.Root.transform.position.x, bestTop, best.Root.transform.position.z);
            return true;
        }

        /// <summary>
        /// 命中「头顶信息带」（状态图标排 + 血条/名字）：这一带没有碰撞体，只能按渲染器世界包围盒算。
        /// <para>⚠ 只用 **SpriteRenderer.bounds**（渲染器自己算，不依赖 2D 物理注册），
        ///   与 <see cref="TryPickUnit"/> 里"不用 col.bounds"的理由相同。</para>
        /// </summary>
        private UnitView2D PickByStatusRow(Vector3 worldPos, out float topY)
        {
            topY = float.MinValue;
            UnitView2D best = null;
            foreach (var kv in _views)
            {
                var v = kv.Value;
                if (v == null || v.Root == null || !v.Alive) continue;
                if (v.StatusRow == null) continue;

                bool any = false;
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                // 图标排 + 血条 + 名字：都算"头顶信息带"
                AccumulateBounds(v.StatusRow.transform, ref any, ref minX, ref maxX, ref minY, ref maxY);
                if (v.HpBg != null) AccumulateBounds(v.HpBg.transform, ref any, ref minX, ref maxX, ref minY, ref maxY);
                if (v.NameText != null) AccumulateBounds(v.NameText.transform, ref any, ref minX, ref maxX, ref minY, ref maxY);
                if (!any) continue;

                const float pad = 0.10f;   // 手不用那么准
                if (worldPos.x < minX - pad || worldPos.x > maxX + pad) continue;
                if (worldPos.y < minY - pad || worldPos.y > maxY + pad) continue;
                if (maxY > topY) { topY = maxY; best = v; }
            }
            return best;
        }

        /// <summary>把一个节点（连同其激活的子渲染器）的世界包围盒并入统计。</summary>
        private static void AccumulateBounds(Transform t, ref bool any,
                                             ref float minX, ref float maxX, ref float minY, ref float maxY)
        {
            if (t == null) return;
            var srs = t.GetComponentsInChildren<SpriteRenderer>();
            for (int i = 0; i < srs.Length; i++)
            {
                var sr = srs[i];
                if (sr == null || !sr.enabled || sr.sprite == null) continue;
                if (!sr.gameObject.activeInHierarchy) continue;
                var b = sr.bounds;
                any = true;
                if (b.min.x < minX) minX = b.min.x;
                if (b.max.x > maxX) maxX = b.max.x;
                if (b.min.y < minY) minY = b.min.y;
                if (b.max.y > maxY) maxY = b.max.y;
            }
        }

        /// <summary>
        /// 取某单位**当前画面**上的状态串（`id:层数|id:层数`）—— 给"悬停看状态列表"用。
        /// ⛔ 数据源是**帧流累积出来的视图**（ApplyFrame 逐帧叠出来的），**不是 `_state`**：
        ///   自动模式下 `_state` 是整场跑完的终局态，直接读会让提示从第一帧就显示"打完之后的状态"。
        /// </summary>
        /// <summary>取某单位**当前画面**上的破绽/失衡文案（空 = 没有）。给悬停提示解释机制用。</summary>
        public bool TryGetViewFlawText(string unitId, out string text)
        {
            text = null;
            if (unitId == null || _views == null) return false;
            if (!_views.TryGetValue(unitId, out var v) || v == null) return false;
            text = v.FlawShownText;
            return !string.IsNullOrEmpty(text);
        }

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
        /// 图标查 <see cref="StatusIcons"/>（Id 同时也是 `ArtRes/UI/Status/<id>.png` 的文件名 ， 换图即换美术）；
        /// 查不到或没拖图 ， 回退成「色块 + 缩写 + 层数」，绝不空白。
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
                    // ★ 按图的实际尺寸归一化（2026-10-09 根因修复）：
                    //   槽位缩放 = StatusIconSize / 图宽 ， 图标世界尺寸恒等于 StatusIconSize。
                    //   此前槽位缩放写死按"底图 32px"算（0.36/0.32≈1.13），而 StatusIcon/*.png 实为
                    //   256×256（2.56 世界单位）， 图标被放大 8 倍 ≈2.9 单位、盖满整格 ——
                    //   用户看到的"巨大盾牌/漩涡/火焰"就是它们（不是特效 overlay，那个一直是正常的）。
                    {
                        var drawn = plate.sprite;
                        float srcW = drawn != null ? drawn.bounds.size.x : 0f;
                        float sc = srcW > 0.0001f ? StatusIconSize / srcW : 1.125f;
                        if (plate.transform.parent != null)
                            plate.transform.parent.localScale = new Vector3(sc, sc, 1f);
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
            //   再除一次就缩小了 ~rootScale 倍（实测 9.75×）， 三个状态挤在同一处看不出区别（用户截图报的"叠加"）。
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

                case BattleEventKind.TurnStart:
                case BattleEventKind.BattleEnd:
                    _actingId = null;   // 回合结算 / 战斗结束期间不显示行动箭头
                    break;

                case BattleEventKind.SkillCast:
                    // ★ 终结技 / 觉醒技 大演出（2026-10-13）：每个异兽都有，元素色 + id 哈希定造型。
                    //   只给 Ultimate/Awaken 播（大招要稀有、要炸）；普攻/战技保持轻量不刷屏。
                    if (e.Skill == SkillType.Ultimate || e.Skill == SkillType.Awaken)
                    {
                        // 大招：冲锋**延后到 cut-in 之后的起爆那一下**（PlaySkillFx 里绑 OnImpact）
                        PlaySkillFx(e.ActorId, e.TargetId, e.Element, e.Skill == SkillType.Awaken, e.SkillName);
                    }
                    else if (e.IsCombo && e.Skill != SkillType.Basic)
                    {
                        // ★ 连携发动（2026-10-18 用户："连携成功也闪头像，主兽居中，其余缩小环绕"）
                        PlayComboFx(e.ActorId, e.Element, e.SkillName);
                    }
                    else
                    {
                        // 出手：冲上去（GDD 的"跑到目标面前"）。目标 id 缺失（群体技/
                        // 纯增益）时退化为"朝敌方方向冲一段"，不影响观感。
                        StartLunge(e.ActorId, e.TargetId);
                        // ★ 连携追击：每次追加都飘一句"追加攻击"，让玩家看清是谁在补刀
                        if (e.IsCombo && e.ActorId != null && _views.TryGetValue(e.ActorId, out var fu))
                            SpawnNumberInternal(fu, "追加攻击!", new Color(1f, 0.82f, 0.38f), 1.15f, 0.75f, raise: 0.9f);
                    }
                    // ★ 技能名飘字（用户 2026-10-06："怎么体现这些技能效果"）：
                    //   普攻每回合都放，飘了会刷屏 ， 只飘战技/绝技。技能名事件里本来就有（skillName）。
                    if (!string.IsNullOrEmpty(e.SkillName) && e.SkillName != "普攻" &&
                        e.ActorId != null && _views.TryGetValue(e.ActorId, out var scv))
                        SpawnNumber(scv, e.SkillName, SkillNameColor);
                    break;

                case BattleEventKind.Damage:
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var hit))
                    {
                        bool crit = _critNext; _critNext = false;
                        Vector2 actorHome = hit.HomePos;
                        if (e.ActorId != null && _views.TryGetValue(e.ActorId, out var atk)) actorHome = atk.HomePos;
                        bool melt = e.Note != null && e.Note.Contains("融冰");
                        // ★ 元素反应（2026-10-17）：认 BattleEvent.Reaction 标记，**不解析 Note**
                        //   （解析文案是错的做法，改一句措辞就会让演出失效）。
                        bool reaction = e.Reaction != ReactionKind.None;
                        if (reaction)
                        {
                            SpawnReactionFx(hit, e.Reaction);
                            // ★ 反应也染格子：用户反映"格子颜色变化没看到"，只靠克制太稀有了
                            ReactionInfo(e.Reaction, out _, out Color rxC, out _);
                            FlashCounterCell(hit, rxC);
                        }
                        // 属性克制 → 受击者脚下格子染成攻击属性色
                        if (e.ElementCounter) FlashCounterCell(hit, BattlePalette.OfElement(e.Element));
                        // ★ 大招 cut-in 正在播 → 伤害数字押后到"起爆"那一下再飘（用户 2026-10-14：
                        //   否则伤害在特写期间就飘完/被盖住，看着像"没伤害"）
                        if (_skillFx != null && _skillFx.CutInActive)
                            _pendingDmg.Add(new PendDmg { View = hit, Amount = e.Amount, Melt = melt, Reaction = reaction, Crit = crit, ActorHome = actorHome });
                        else
                            ApplyDamageVisual(hit, e.Amount, melt, reaction, crit, actorHome);
                    }
                    break;

                case BattleEventKind.StatusApplied:
                    // ★ 只处理**反应型**状态变更：助燃（灼烧+2）/ 镇（熄灭）没有 Damage 事件，
                    //   不在这里补演出的话，这两个反应在画面上完全静默。
                    //   普通状态施加（灼烧/湿/根缚…）走头顶状态图标，**不在这里重复演出**。
                    if (e.Reaction != ReactionKind.None && e.TargetId != null &&
                        _views.TryGetValue(e.TargetId, out var rsv))
                        SpawnReactionFx(rsv, e.Reaction);
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
                    // Crit 先于 Damage 到：置标记让下一条伤害数字放大，并飘"暴击!"
                    _critNext = true;
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var cv))
                    {
                        cv.Body.color = new Color(1f, 0.35f, 0.28f);
                        // 大招特写期间押后（和伤害数字一起在起爆那下飘）
                        if (_skillFx != null && _skillFx.CutInActive) _pendingCritFloat = cv;
                        else SpawnNumber(cv, "暴击!", new Color(1f, 0.85f, 0.35f));
                    }
                    break;

                case BattleEventKind.Revive:
                    // ★ 双子同命 / 灵卵复活：把"灰尸"复位成满色并弹一下，玩家一眼能看到（2026-10-12）
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var rv))
                    {
                        rv.Alive = true;
                        rv.DeadBlend = 0f;                       // 复位：否则复活后仍顶着死亡灰调
                        if (rv.Root != null && !rv.Root.activeSelf) rv.Root.SetActive(true);
                        rv.Body.color = rv.BaseColor;
                        SetHpBar2D(rv, rv.Hp, rv.MaxHp);
                        SpawnNumber(rv, "复活!", new Color(1f, 0.85f, 0.40f));   // 金：同命复活
                    }
                    break;
                case BattleEventKind.Death:
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var dv))
                        dv.Alive = false;
                    break;
            }
        }

        /// <param name="dt">舞台动画步长（含倍速缩放——单位抖动/冲锋等随倍速变快是对的）。</param>
        /// <param name="fxDt">大招演出的步长（**不含倍速**——用户 2026-10-14：释放速度跟几倍速没关系，cut-in 别被 ×4 一闪而过）。&lt;0 表示同 dt。</param>
        public void Step(float dt, float fxDt = -1f)
        {
            if (fxDt < 0f) fxDt = dt;
            // 舞台自己的动画时钟：由 dt 累加（**不用 `Time.time`**）——
            // 这样表现层动画与"推进了多少帧"严格对应，编辑器里连调 Step 也能被验证到。
            _animT += dt;
            StepBursts(dt);
            if (_turnArrow != null) _turnArrow.enabled = false;   // 每帧默认隐藏，循环里再为行动者点亮

            // 前景轻微摇曳（呼吸感；幅度很小，不干扰战斗阅读）
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
            StepParryBursts(dt);   // 格挡爆闪（跟舞台时钟，倍速下一起变快）
   StepVerdict(dt);    // 判定横幅（弹入/停留/淡出）——用 fxDt 之外的 dt，让它不被倍速带没

      // QTE 提示条淡入淡出（有文案就淡入保持，清掉就淡出隐藏）
            if (_qteHint != null)
            {
         float targetA = string.IsNullOrEmpty(_qteHintText) ? 0f : 1f;
   _qteHintAlpha = Mathf.MoveTowards(_qteHintAlpha, targetA, dt * 6f);
    var c = _qteHint.color;
 _qteHint.color = new Color(c.r, c.g, c.b, _qteHintAlpha);
// ⚠ TextMesh 没有 enabled（它不是 Renderer）—— 显隐走 MeshRenderer。
     if (_qteHintMr != null) _qteHintMr.enabled = _qteHintAlpha > 0.01f;
     // 底衬同步淡入淡出 + 每帧跟随文字实际宽度
     if (_qteHintBg != null)
     {
      var bc = _qteHintBg.color;
      _qteHintBg.color = new Color(bc.r, bc.g, bc.b, 0.72f * _qteHintAlpha);
    _qteHintBg.enabled = _qteHintAlpha > 0.01f;
      // ⚠ 宽度按 **实际渲染宽度** 算，不能按字数估 ——
      //   估出来会短一截（实测盖到"格挡"就断了）。
      //   用 MeshRenderer.bounds（世界 AABB）÷ 父级缩放换算回本地单位。
      //   ⚠ 原生 TextMesh.textBounds / GetPixelSize 在本工程不可用（CS1061），
      //     GetPixelSize 是 TextMeshPro 的 API，别再试。
      //   每帧算：设 text 的当帧网格当帧还没更新。
      if (_qteHintMr != null && _qteHintAlpha > 0.01f)
      {
        float w = 1.2f;
        float tw = _qteHintMr.bounds.size.x;
        float ps = _qteHintBg.transform.parent != null ? _qteHintBg.transform.parent.lossyScale.x : 1f;
        if (ps > 0.0001f) tw /= ps;
        if (tw > 0.01f) w = tw + 0.30f;
        w = Mathf.Clamp(w, 1.2f, 8.0f);
        _qteHintBg.transform.localScale = new Vector3(w, 0.46f, 1f);
      }
        }
}

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

            // 伤害数字上浮 + 弹出缩放 + 回收
            for (int i = _tempTexts.Count - 1; i >= 0; i--)
            {
_tempLife[i] -= dt;
       var go = _tempTexts[i];
         float lifeMax = i < _tempLifeMax.Count && _tempLifeMax[i] > 0.01f ? _tempLifeMax[i] : 0.7f;
       if (go != null)
            {
          go.transform.localPosition += new Vector3(0f, dt * 0.9f, 0f);
            // 弹出：起手 1.5× 快速回落（easeOutBack 手感），暴击基准更大
      float age = lifeMax - _tempLife[i];
                    float pop = 1f;
                    if (age < 0.18f)
                    {
                        float k = age / 0.18f;
                        float c1 = 1.70158f, c3 = c1 + 1f;
                        float e = 1f + c3 * (k - 1f) * (k - 1f) * (k - 1f) + c1 * (k - 1f) * (k - 1f);
                        pop = Mathf.Lerp(1.5f, 1f, Mathf.Clamp01(e));
                    }
                    float bs = i < _tempBaseScale.Count ? _tempBaseScale[i] : 1f;
                    go.transform.localScale = Vector3.one * (bs * pop);
                    var tm = go.GetComponent<TextMesh>();
                    if (tm != null)
                    {
                        var c = tm.color;
                        tm.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(_tempLife[i] / lifeMax));
                    }
                }
                if (_tempLife[i] <= 0f)
                {
                    // ⚠⚠ 这里在 Step(dt) 里，是**每帧路径** —— 原来用 DestroyImmediate
                    //   （强制同步销毁，Unity 运行时明令禁用）， 战斗越久触发次数越多、越来越卡，
                    //   最后卡死（用户实测"战斗久一点就卡死"）。必须用 Destroy（延迟销毁）。
                    if (go != null) Destroy(go);
                    _tempTexts.RemoveAt(i); _tempLife.RemoveAt(i);
                    if (i < _tempBaseScale.Count) _tempBaseScale.RemoveAt(i);
      if (i < _tempLifeMax.Count) _tempLifeMax.RemoveAt(i);
                }
            }
        }

        // ================================================================

        /// <summary>通用飘字（伤害/恢复/护盾共用）。美术替换：改这里的字体/字号/描边即可。</summary>
        // ---- 元素反应演出（2026-10-17）----
        private struct Burst { public Transform T; public SpriteRenderer Sr; public Vector2 Vel; public float Life, Max; }
        private readonly System.Collections.Generic.List<Burst> _bursts =
            new System.Collections.Generic.List<Burst>(32);

        /// <summary>反应名 + 颜色。名字要短（两个字最好读），颜色绑**反应自身的属性语义**。</summary>
        private static void ReactionInfo(ReactionKind k, out string name, out Color color, out string tag)
        {
            switch (k)
            {
                case ReactionKind.Steam:   name = "蒸 发"; color = new Color(1.00f, 0.55f, 0.22f); tag = "火×水"; break;
                case ReactionKind.Shock:   name = "感 电"; color = new Color(0.85f, 0.72f, 1.00f); tag = "金×水"; break;
                case ReactionKind.Shatter: name = "碎 冰"; color = new Color(0.55f, 0.85f, 1.00f); tag = "金×冰"; break;
                case ReactionKind.Kindle:  name = "助 燃"; color = new Color(1.00f, 0.42f, 0.18f); tag = "木×火"; break;
                case ReactionKind.Quell:   name = "镇";    color = new Color(0.86f, 0.72f, 0.36f); tag = "土×火"; break;
                default:                   name = "反 应"; color = Color.white; tag = ""; break;
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
                var go = new GameObject("RxSpark");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = SolidSprite(color, 14, 14, 0, color);
                sr.sortingOrder = 40;
                go.transform.position = c;
                go.transform.localScale = Vector3.one * 0.22f;
                _bursts.Add(new Burst
                {
                    T = go.transform, Sr = sr,
                    Vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 2.2f,
                    Life = 0.45f, Max = 0.45f,
                });
            }
            while (_bursts.Count > 96)
            {
                if (_bursts[0].T != null) Destroy(_bursts[0].T.gameObject);
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
                if (b.Life <= 0f) { Destroy(b.T.gameObject); _bursts.RemoveAt(i); continue; }
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
            SpawnDamageNumber(target, amount, isMelt, reaction, crit);
            target.Body.color = Color.white;      // 受击白闪（HitFlash 在 Step 里衰减回基色）
            target.HitFlash = 0.10f;
            target.ShakePulse = 0.16f;
            Vector2 dir = target.HomePos - actorHome;
            if (dir.sqrMagnitude > 0.0001f) target.KickVec = dir.normalized * 0.22f;
            target.KickT = 0.12f;
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
            var go = new GameObject("Dmg");
            go.transform.SetParent(transform, false);
            // raise：同一次演出里的多行字（反应名/属性组合/伤害）要用不同高度分层，
            //   否则全叠在同一个锚点上（实测「蒸发」「火×水」「504」三行糊成一团）。
            go.transform.localPosition = v.Root.transform.position + new Vector3(0f, 1.5f + raise, -1f);
            go.transform.localScale = Vector3.one * baseScale;   // 暴击更大（弹出动画在此基础上叠加）
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
if (life < 0.2f) life = 0.2f;
    _tempTexts.Add(go);
  _tempLife.Add(life);
            _tempLifeMax.Add(life);
        _tempBaseScale.Add(baseScale);
            // ⚠ 运行时不能用 DestroyImmediate（强制同步销毁，长战斗里反复触发会卡）；
            //   改用 Destroy 并把上限放宽到 24，给延迟销毁留出缓冲。
        while (_tempTexts.Count > 24)
     {
         if (_tempTexts[0] != null) Destroy(_tempTexts[0]);
  _tempTexts.RemoveAt(0); _tempLife.RemoveAt(0);
        if (_tempBaseScale.Count > 0) _tempBaseScale.RemoveAt(0);
        if (_tempLifeMax.Count > 0) _tempLifeMax.RemoveAt(0);
        }
    }

        /// <summary>血条按比例伸缩。<paramref name="v"/> 的填充宽度 = <see cref="HpBarWidth"/>（可在 Inspector 调）。</summary>
        /// <summary>
        /// 破绽/失衡条（2026-10-17）。三态：
        ///   progress &lt; 0 且未失衡 → 整条隐藏；破绽进度 → 青色条 + "破绽·X N/M"；
        ///   失衡 → **整条变金** + "失衡 ×倍率　剩 N 回合"（失衡是玩家最该盯的状态，视觉权重最高）。
        /// 填充的缩放/左对齐补偿与 <see cref="SetHpBar2D"/> 同一套（尺寸烘在 sprite 里，别再乘宽度）。
        /// </summary>
        private void SetFlawBar2D(UnitView2D v, string text, float progress, int unbalanced)
        {
            if (v.FlawBg == null || v.FlawFill == null || v.FlawLabel == null) return;
            bool staggered = unbalanced > 0;
            bool show = staggered || progress >= 0f;

            if (!show)
            {
                if (v.FlawBg.gameObject.activeSelf) v.FlawBg.gameObject.SetActive(false);
                if (v.FlawFill.gameObject.activeSelf) v.FlawFill.gameObject.SetActive(false);
                if (v.FlawLabel.gameObject.activeSelf) v.FlawLabel.gameObject.SetActive(false);
                v.FlawShownText = text;
                return;
            }

            if (!v.FlawBg.gameObject.activeSelf) v.FlawBg.gameObject.SetActive(true);
            if (!v.FlawFill.gameObject.activeSelf) v.FlawFill.gameObject.SetActive(true);
            if (!v.FlawLabel.gameObject.activeSelf) v.FlawLabel.gameObject.SetActive(true);

            if (v.FlawLabel.text != text) v.FlawLabel.text = text;
            v.FlawShownText = text;

            if (staggered)
            {
                if (v.FlawFill.color != FlawStaggerColor) v.FlawFill.color = FlawStaggerColor;
                float full = Mathf.Max(0.05f, HpBarWidth - 0.06f);
                var t = v.FlawFill.transform;
                t.localScale = new Vector3(1f, 1f, 1f);
                t.localPosition = new Vector3(0f, t.localPosition.y, t.localPosition.z);
            }
            else
            {
                if (v.FlawFill.color != FlawFillColor) v.FlawFill.color = FlawFillColor;
                float ratio = Mathf.Clamp01(progress);
                float full = Mathf.Max(0.05f, HpBarWidth - 0.06f);
                var t = v.FlawFill.transform;
                t.localScale = new Vector3(ratio, 1f, 1f);
                t.localPosition = new Vector3(-(full - full * ratio) * 0.5f, t.localPosition.y, t.localPosition.z);
            }
        }

        private void SetHpBar2D(UnitView2D v, int hp, int max)
        {
            if (v.HpFill == null || max <= 0) return;
            float ratio = Mathf.Clamp01((float)hp / max);
            float full = Mathf.Max(0.05f, HpBarWidth - 0.06f);   // ⛔ 必须跟 BuildUnitsFor 里创建 HpFill 的宽度一致

            // ⛔ 缩放必须走**归一化比例**，不能再乘 `full`：`MakeChildSprite` 是把尺寸
            //   **烘进 sprite 本身**的（贴图 = size×100 像素，localScale 保持 1）
            //   ， 再乘一次 `full`(3.34) 就是**双重应用**，填充条会宽 3.34 倍、从血槽两边冒出来。
            //   用户实测的两个现象都出自这里："血怎么两边同时扣" + "护盾和血重叠看不清"。
            var t = v.HpFill.transform;
            t.localScale = new Vector3(ratio, 1f, 1f);
            // 左端固定 ， 只从**右边**往回缩（掉血观感正确）
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
        /// 「**文件名 = 状态 id**」， 出图后直接丢进那个目录即可生效，不用改任何代码/引用。
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
        private void SetOverlay2D(UnitView2D v, string ids)
        {
            if (v.Overlay == null || v.Body == null) return;
            // ★ 2026-10-09 用户定案：关闭状态特效大图，只用头顶小图标（见 StatusArtEnabled）。
            if (!StatusArtEnabled) { v.Overlay.enabled = false; return; }
            var best = BestOverlayFor(ids);
            string key = best != null ? best.Id : null;
            if (v.OverlayShown == key) return;                 // 变了才换，别每帧重建 sprite
            v.OverlayShown = key;
            if (best == null) { v.Overlay.enabled = false; return; }

            var art = ResolveStatusArt(best.Id, best.Art);
            Vector2 bodySize = v.Body.sprite != null ? v.Body.sprite.bounds.size : new Vector2(1f, 1.4f);
            // 说明：这里用的是**贴图本地尺寸**，看着“比显示尺寸大”，但**不用乘缩放** ——
            //   因为 Overlay 是 Body 的**子物体**（见创建处 SetParent(bodySr.transform)），
            //   父级缩放会自动乘进去 ， 最终显示尺寸 = 贴图尺寸 × localScale × 立绘缩放 = 立绘显示尺寸。
            //   （曾误判“少了 lossyScale”而乘了一次，结果特效反而小了近 4 倍，已回滚。）
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
        /// ⚠ 2026-10-13 起护盾**一个回合后自动消失**（BattleUnit.ShieldTurn + TickStatuses 清旧盾），
        ///    也会被伤害消耗；无论哪种归零，这里都靠帧流 s.Shield 自动收牌（见 ApplyFrame→SetShield2D）。
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
        /// 单位却用 0.82/0.66 且双方 y 基准相反 ， 永远对不上）。
        /// 网格格子中心 = (cx + (col-1)*Cell, BoardY + (1-row)*Cell*0.72)。
        /// 单位枢轴在底部中点、Step 会再加 FootOffset 抬脚 ， 这里先减去它。
        /// 敌方做列镜像（col→2-col）与我方对称；BuildBoard 用同一镜像公式，单位与格子始终对齐。
        /// </summary>
        private static Vector2 CellPos(bool player, int cell)
        {
            int col = cell % 3;
            if (!player) col = 2 - col;   // 敌方列镜像 ， 同码两侧呈镜像对称
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
// [force-recompile 2026-10-18]
