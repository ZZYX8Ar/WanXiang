// ============================================================================
//  万相 · 2D 战场单位视图数据（2026-11-18 从 BattleStage2D.cs 拆出）
//  行为零变化：仅移动类型定义。
// ============================================================================
using System.Collections.Generic;
using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Battle.Presentation
{
    public sealed class UnitView2D
    {
        public string UnitId;
        /// <summary>异兽定义 id（如 `b_suren`）。`UnitId` 是运行时 id，查表要用这个。</summary>
        public string DefId;
        public GameObject Root;        // 立绘容器
        public bool Hidden;            // 被捕获/隐藏：帧更新不得再激活 Root
        public SpriteRenderer Body;    // 立绘
        public SpriteRenderer HpBg;
        public SpriteRenderer HpFill;
        /// <summary>护盾指示（血条右侧的蓝牌）。护盾 &gt; 0 才显示。</summary>
        public SpriteRenderer ShieldPlate;
        /// <summary>元素附着指示（2026-10-31）：头顶一颗元素色圆点 + 层数。</summary>
        public SpriteRenderer AuraDot;      // 附着徽章底板
        public SpriteRenderer[] AuraPips;   // 4 格层数刻度
        public float SpawnT;                    // >0 = 入场动画剩余时间（缩放弹出）
        public float BaseScale = 1f;            // 入场缩放要乘回的原始 scale
        public Material CorruptMat;             // 魇化材质（敌方普通/精英专用）
        public SpriteRenderer AuraCorrupt;      // 魇化紫雾（敌方普通/精英）
        public SpriteRenderer AuraStacksRing;   // 血条右侧 4 段层数小圈
        public GameObject AuraLabelRoot;    // 附着大字标签根（已停用）
        public TextMesh AuraShadow;         // 描影层
        public Material AuraMat;
        public TextMesh AuraText;           // 旧版文字牌（已停用，保留字段避免 prefab 引用丢失）
        /// <summary>上一帧的机制提示文本（用于"变化时飘一条"）。</summary>
        public string LastStateHint;
        /// <summary>最近一帧的快照（双击查看面板用，避免去读终局态 state）。</summary>
        public WanXiang.Battle.Core.UnitSnapshot LastSnap;
        /// <summary>静态定义（技能明细用；建视图时从 u.Def 拷一份引用即可）。</summary>
        public WanXiang.Battle.Core.BeastDef Def;
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
        /// <summary>阵亡时是否已隐藏头顶 HUD（血条/名字/护盾/破绽/状态排/光环）。
        /// 复活的单位要把它们全部恢复 —— 用户 2026-10-25 报"死了血条名字还在"。</summary>
        public bool ChromeHidden;
        /// <summary>头顶信息容器（血条/名字/护盾/破绽条）。缩放为 1/身体缩放 ⇒ **世界尺寸恒定**。</summary>
        public Transform Head;
        /// <summary>该单位脚下格子（0~8，与 <c>u.Pos.Index</c> 同一套）。克制染色按它定位。</summary>
        public int CellIndex;
        /// <summary>是否我方（决定染哪一方的棋盘）。</summary>
        public bool PlayerSide;
        public Vector2 HomePos;        // 站位（未加浮动）
        public float Phase;            // 待机浮动相位（随机，方案 §最易做错三件事之一）
        public float DeadBlend;

        /// <summary>状态光环（2026-10-24）。null = 无状态/未创建。</summary>
        public SpriteRenderer StatusAura;
        public Material StatusAuraMat;
        public string StatusAuraKey;

        /// <summary>护盾膜（T5，2026-10-24）。null = 无护盾/未创建。</summary>
        public SpriteRenderer ShieldField;
        public Material ShieldMat;

        /// <summary>死亡"墨蚀"溶解材质（T13，2026-10-24）。null = 未启用/未创建。</summary>
        public Material DissolveMat;
        /// <summary>身体原始材质（溶解结束后还原）。</summary>
        public Material BodyMaterial;
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
}
