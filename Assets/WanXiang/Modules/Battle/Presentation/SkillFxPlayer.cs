// ============================================================================
//  万相 · 战斗表现 · 终结技 / 觉醒技 特效播放器（SkillFxPlayer）
//  ---------------------------------------------------------------------------
//  「每一个异兽都有终结技与觉醒技大演出」——但不是手搓 100 条动画，而是**数据驱动**：
//  一次施法 = 一段编排好的特效时间线，颜色取自技能五行（BattlePalette.OfElement），
//  造型细节（射线数 / 冲击环数 / 锐度 / 相位 / 体量）由**异兽 id 的稳定哈希**决定。
//    · 同一异兽每次演出完全一致（可复现，利于调试与录像对比）
//    · 不同异兽即使同五行也长得不一样（哈希给变体）⇒ 覆盖全部异兽且有多样性
//
//  演出时间线（觉醒技 = 更大更久）：
//      蓄力(Charge) → 爆发(Burst 核心+射线) → 冲击波(Ring×N) → 全屏闪(Flash)
//      ↑                                                                  ↘
//      └──────────────────── 同一个起爆点（chargeDur 末）触发镜头震动 ────────┘
//
//  ⚠ 时钟铁律：本组件**没有 Unity Update**，只由 BattleStage2D.Step(dt) 驱动 ——
//    与舞台同一动画时钟（_animT 口径），编辑器里连调 Step 也能被验证到，真机随帧推进。
//  ⚠ 无 ParticleSystem（2D 管线取不到默认材质）：全部用 SpriteRenderer + 程序化贴图。
//  ⚠ 几何尽量在 shader 里解析（见 Resources/Shaders/Skill/*），贴图只作柔边 alpha 底。
// ============================================================================
using System.Collections.Generic;
using UnityEngine;

namespace WanXiang.Battle.Presentation
{
    /// <summary>特效片段的类型。CutIn=异兽头像特写（P5R 式cut-in），其余对应四个 shader。</summary>
    internal enum SkillFxKind { CutIn, Charge, Burst, Ring, Flash }

    /// <summary>一个正在播放的特效片段。</summary>
    internal sealed class SkillFxClip
    {
        public GameObject Go;
        public Material Mat;
        public SpriteRenderer Sr;
        public SkillFxKind Kind;
        public float StartAt;   // 相对本次 Play 的起播时刻（秒）
        public float Dur;       // 持续时间（秒）
        public float SizeMul;   // 体量倍率
        public int RayCount;
        public float RaySharp;
        public float Seed;
        public float Intensity;
        public int RingCount;
        public Color Col;
        public Vector3 Pos;     // 世界坐标（Flash 忽略，铺满屏）
        public Vector3 FlashCenterWorld;  // Flash 爆心的世界坐标（= 施法者位置）
        public bool Flash;      // = Kind==Flash
        // ---- CutIn 专用子物体 ----
        public SpriteRenderer Portrait;  // 异兽立绘
        public SpriteRenderer Slash;     // 元素色斜切背板
        public SpriteRenderer Mask;      // 背后变黑的遮罩（聚焦主体）
        public SpriteRenderer[] Lines;   // 斜向速度线（柯南式斜切/高对比感）
        public TextMesh NameTm;          // 异兽名（LegacyRuntime 字体，支持中文）
        public TextMesh SkillTm;         // 终结技名
        public Vector3 CutInFrom;        // cut-in 入场起点（屏幕外）
        public Vector3 CutInTo;          // cut-in 停驻点（立绘落点）
        // 起爆点镜头反馈（挂在 Burst 片段上，激活那一下才触发，而不是 Play 时）
        public float ShakeAmp, ShakeDur, ZoomDip, ZoomDur;
        public bool Fired;               // 起爆反馈是否已触发（只触发一次）
    }

    public sealed class SkillFxPlayer : MonoBehaviour
    {
        // ---- 着色器（Resources.Load 优先，退回 Shader.Find；见项目 shader 约定）----
        // ⚠ Resources 路径相对 Assets/Resources：文件是 Shaders/SkillCharge.shader
        //   ⇒ 路径是 "Shaders/SkillCharge"（**不是** "Shaders/Skill/Charge"，没有该子目录）。
        private static readonly string[] ShaderPaths =
            { "Shaders/SkillCharge", "Shaders/SkillBurst", "Shaders/SkillRing", "Shaders/SkillFlash" };

        private Shader[] _shaders;
        private Texture2D _glowTex;   // 径向柔边（Charge/Burst/Ring 共用）
        private Texture2D _whiteTex;  // 1×1 白块（Flash / CutIn 背板）
        private Sprite _glowSprite;
        private Sprite _whiteSprite;
        private static Font _legacyFont;   // CutIn 名字用（LegacyRuntime 支持中文，同舞台 NameText）

        private readonly List<SkillFxClip> _clips = new List<SkillFxClip>(16);
        private float _t;                 // 本地时钟（由 Step 累加）
        private float _playStart;         // 本次 Play 起播时的 _t 值（片段 StartAt 都加上它）
        private bool _ready;

        /// <summary>舞台相机（用于全屏闪的尺寸与爆心换算）。BattleStage2D.Bind 时注入。</summary>
        public Camera Cam;
        /// <summary>镜头震动回调 (amp, dur)。BattleStage2D 绑到 TriggerShake。</summary>
        public System.Action<float, float> OnShake;
        /// <summary>镜头推近回调 (dip, dur)。BattleStage2D 绑到 TriggerZoom（起爆点 Zoom Punch）。</summary>
        public System.Action<float, float> OnZoom;
        /// <summary>起爆点"命中"回调：大招的冲锋放在这一下，避免在 cut-in 期间就冲完
        /// （用户 2026-10-14："攻击完才出现冲击波"）。BattleStage2D 绑到 StartLunge。</summary>
        public System.Action OnImpact;

        /// <summary>是否还有特效在播（诊断/自检用）。</summary>
        public bool Busy => _clips.Count > 0;
        public int ActiveCount => _clips.Count;

        /// <summary>是否有 cut-in 头像特写正在播（大招特写期间伤害数字要押后飘，见 BattleStage2D）。</summary>
        public bool CutInActive
        {
            get { for (int i = 0; i < _clips.Count; i++) if (_clips[i].Kind == SkillFxKind.CutIn) return true; return false; }
        }

        /// <summary>按类型取着色器（CutIn 不用 shader，返回 null）。</summary>
        private Shader ShaderFor(SkillFxKind k)
        {
            if (_shaders == null) return null;
            switch (k)
            {
                case SkillFxKind.Charge: return _shaders[0];
                case SkillFxKind.Burst:  return _shaders[1];
                case SkillFxKind.Ring:   return _shaders[2];
                case SkillFxKind.Flash:  return _shaders[3];
                default:                 return null;   // CutIn
            }
        }

        private static Font LegacyFont()
        {
            if (_legacyFont != null) return _legacyFont;
            _legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_legacyFont == null) _legacyFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _legacyFont;
        }

        // ------------------------------------------------------------------
        //  初始化：载入 shader / 生成程序化贴图（幂等，可重复调用）
        // ------------------------------------------------------------------
        public void Bind(Camera cam, System.Action<float, float> onShake, System.Action<float, float> onZoom = null)
        {
            Cam = cam;
            OnShake = onShake;
            OnZoom = onZoom;
            Ensure();
        }

        private void Ensure()
        {
            if (_ready) return;
            _ready = true;

            _shaders = new Shader[4];
            for (int i = 0; i < ShaderPaths.Length; i++)
            {
                var s = Resources.Load<Shader>(ShaderPaths[i]);
                if (s == null) s = Shader.Find("WanXiang/Skill/" +
                    new[] { "Charge", "Burst", "Ring", "Flash" }[i]);
                _shaders[i] = s;
            }

            _glowTex = MakeGlow(128);
            _whiteTex = MakeWhite();
            _glowSprite = Sprite.Create(_glowTex, new Rect(0, 0, _glowTex.width, _glowTex.height),
                                        new Vector2(0.5f, 0.5f), 100f);
            // ⚠ 白块用 **PPU=1**：这样 1×1 精灵的自然尺寸 = 1 世界单位，
            //   Flash / CutIn 遮罩 / 斜切背板 都能直接按"世界单位"设 localScale。
            //   以前 PPU=100 ⇒ 自然尺寸 0.01，按 halfW 缩放只有 0.2 单位，遮罩/背板/闪全看不见（用户 2026-10-13）。
            _whiteSprite = Sprite.Create(_whiteTex, new Rect(0, 0, _whiteTex.width, _whiteTex.height),
                                         new Vector2(0.5f, 0.5f), 1f);
        }

        // ------------------------------------------------------------------
        //  播一次终结技 / 觉醒技大演出
        //    worldPos  = 施法者世界坐标（立绘中心）
        //    actorId   = 施法者 id（决定造型变体，稳定哈希）
        //    element   = 技能五行色（BattlePalette.OfElement）
        //    isAwaken  = 觉醒技：更大更久更亮
        //    portrait  = 异兽立绘（CutIn 特写用；null 则跳过特写）
        //    beastName = 异兽名（CutIn 显示，中文）
        // ------------------------------------------------------------------
        public void Play(Vector3 worldPos, string actorId, Color element, bool isAwaken,
                         Sprite portrait = null, string beastName = null, string skillName = null,
                         bool faceLeft = false)
        {
            Ensure();
            if (_shaders == null || _shaders[0] == null) return;   // shader 没加载上就别播

            // ★ 记下本次起播时刻：_t 是全局累加时钟（无片段时不涨），若不记偏移，
            //   第二次放技能时新片段 StartAt=0 会被当成"已过期"秒回收 —— 用户 2026-10-13
            //   报"第二次释放终结技就消失了，只能生效一次"。片段 StartAt 一律加 _playStart。
            _playStart = _t;

            int seed = StableHash(actorId);

            // 颜色：元素色 + 少量提亮；觉醒偏白热一点
            Color core = isAwaken ? Color.Lerp(element, Color.white, 0.15f) : element;

            // 造型变体（同五行不同异兽也长得不一样）
            int rayCount = 6 + (seed % 7);                 // 6..12
            int ringN    = (isAwaken ? 2 : 1) + (seed % 2); // 普通 1..2，觉醒 2..3
            float raySharp = 10f + (seed % 10);            // 10..19
            float phase    = (seed % 100) / 100f;
            float sizeMul  = 0.9f + ((seed >> 3) % 21) / 100f * 0.55f;  // ~0.9..1.0
            float aw = isAwaken ? 1.35f : 1.0f;

            // 时间线（秒）
            float cutInDur  = (isAwaken ? 1.30f : 1.05f) * (portrait != null ? 1f : 0f); // 无立绘则不播特写
            float chargeDur = (isAwaken ? 0.70f : 0.50f);
            float burstDur  = 0.35f * aw;
            float ringDur   = (isAwaken ? 1.00f : 0.80f);
            float flashDur  = (isAwaken ? 0.34f : 0.25f);

            // ⓪ 异兽头像特写（P5R 式 cut-in）：先让玩家看清"是谁开大"，再进特效
            if (cutInDur > 0f)
                AddCutIn(worldPos, cutInDur, core, portrait, beastName, skillName, isAwaken, faceLeft);

            float t0 = cutInDur;   // 特写之后，特效才起

            // ① 蓄力
            AddClip(SkillFxKind.Charge, t0, chargeDur,
                    new Vector3(worldPos.x, worldPos.y + 0.2f, worldPos.z - 0.05f),
                    size: 2.2f * sizeMul * aw, rayCount, raySharp, phase, 1.15f * aw, core);

            // ②③④ 同一起爆点（chargeDur 末）：爆发 + 冲击波(可多环) + 全屏闪
            var burstClip = AddClip(SkillFxKind.Burst, t0 + chargeDur, burstDur,
                    new Vector3(worldPos.x, worldPos.y + 0.15f, worldPos.z - 0.03f),
                    size: 3.0f * sizeMul * aw, rayCount, raySharp, phase, (isAwaken ? 1.7f : 1.1f), core);
            // 起爆点镜头反馈挂到爆发片段上：等爆发真正激活那一下才震 + 推近
            // （以前在 Play 时就震，加了 cut-in 后会震在特写阶段，手感错位）
            burstClip.ShakeAmp = isAwaken ? 0.34f : 0.20f;
            burstClip.ShakeDur = isAwaken ? 0.42f : 0.30f;
            burstClip.ZoomDip  = isAwaken ? 0.85f : 0.55f;
            burstClip.ZoomDur  = isAwaken ? 0.50f : 0.40f;

            for (int k = 0; k < ringN; k++)
            {
                AddClip(SkillFxKind.Ring, t0 + chargeDur + k * 0.09f, ringDur,
                        new Vector3(worldPos.x, worldPos.y + 0.1f, worldPos.z - 0.02f),
                        size: (7.5f + k * 1.2f) * sizeMul * aw, rayCount, raySharp, phase + k * 0.3f,
                        1.1f * aw, core, ringCount: ringN);
            }

            AddClip(SkillFxKind.Flash, t0 + chargeDur + 0.03f, flashDur,
                    Vector3.zero, size: 0f, rayCount, raySharp, phase,
                    isAwaken ? 0.72f : 0.52f, core).FlashCenterWorld = worldPos;
        }

        // ------------------------------------------------------------------
        //  异兽头像特写（P5R 式 cut-in）：立绘从屏边切入 + 元素色斜切背板 + 名字，随后甩出
        // ------------------------------------------------------------------
        /// <summary>
        /// **连携特写**（2026-10-18 用户："连携成功也闪一下头像，以主异兽在中间为主，
        /// 其他异兽的头像变小一点在主异兽四周"）。
        /// <para>复用终结技那条 cut-in 管线（遮罩/斜切背板/名字），只是立绘排布不同：
        ///   主兽居中放大、其余在我方圈内 4 个角位缩小摆开。</para>
        /// </summary>
        public void PlayCombo(Vector3 worldPos, string actorId, Color element,
                              Sprite hostPortrait, string hostName, string comboName, Sprite[] others)
        {
            Ensure();
            if (_shaders == null || _shaders[0] == null) return;
            if (hostPortrait == null) return;

            _playStart = _t;
            // 连携特写：比终结技短一点（它是"成果展示"，不是"起手预备"）
            AddCutIn(worldPos, 0.95f, element, hostPortrait, hostName, comboName,
                     isAwaken: false, faceLeft: false, extras: others, extrasBias: true);
        }

        private void AddCutIn(Vector3 worldPos, float dur, Color core, Sprite portrait, string beastName,
                              string skillName, bool isAwaken, bool faceLeft,
                              Sprite[] extras = null, bool extrasBias = false)
        {
            var go = new GameObject("SkillFx_CutIn");
            var cam = Cam != null ? Cam : Camera.main;
            float halfH = cam != null && cam.orthographic ? cam.orthographicSize : 5.4f;
            float halfW = halfH * (cam != null ? cam.aspect : 1.7f);

            // 立绘落点：画面略偏右但不贴边（用户 2026-10-13："有点居右了" → 往里收）
            float targetX = halfW * 0.12f;
            float fromX    = halfW * 1.15f;
            var portraitPos = new Vector3(targetX, worldPos.y * 0.3f + 0.2f, worldPos.z - 0.2f);
            go.transform.position = portraitPos;
            go.transform.SetParent(transform, true);   // 挂到播放器名下（保持世界坐标）

            // ★ 背后变黑的遮罩（用户 2026-10-13："背后应该要有点变黑的遮罩"，聚焦主体）。
            //   全屏黑块，sortingOrder 最低（画在最底），透明度由 UpdateCutIn 淡入淡出。
            var maskGo = new GameObject("Mask");
            maskGo.transform.SetParent(go.transform, false);
            var camPos = cam != null ? cam.transform.position : new Vector3(0f, 0f, -10f);
            maskGo.transform.position = new Vector3(camPos.x, camPos.y, portraitPos.z - 0.5f);
            var mask = maskGo.AddComponent<SpriteRenderer>();
            mask.sprite = _whiteSprite;
            mask.color = new Color(0f, 0f, 0f, 0f);   // 初始透明
            mask.sortingOrder = 450;
            maskGo.transform.localScale = new Vector3(halfW * 2.4f, halfH * 2.4f, 1f);

            // ★ 斜向"速度线"（用户 2026-10-14：遮罩参考《名侦探柯南》那种斜切高对比感）。
            //   几条元素色细长条斜着横贯全屏，配合压黑遮罩 → 有"案件开场"的凌厉感。
            var linesGo = new GameObject("SpeedLines");
            linesGo.transform.SetParent(go.transform, false);
            linesGo.transform.position = new Vector3(camPos.x, camPos.y, portraitPos.z - 0.4f);
            var lineSrs = new SpriteRenderer[3];
            for (int li = 0; li < 3; li++)
            {
                var lg = new GameObject("Line" + li);
                lg.transform.SetParent(linesGo.transform, false);
                lg.transform.localRotation = Quaternion.Euler(0f, 0f, -19f);
                lg.transform.localPosition = new Vector3((li - 1) * halfW * 0.35f, (li - 1) * halfH * 0.55f, 0f);
                var lsr = lg.AddComponent<SpriteRenderer>();
                lsr.sprite = _whiteSprite;
                lsr.color = new Color(core.r, core.g, core.b, 0f);   // 透明度由 UpdateCutIn 驱动
                lsr.sortingOrder = 505;
                lg.transform.localScale = new Vector3(halfW * 3.0f, halfH * 0.032f, 1f);
                lineSrs[li] = lsr;
            }

            // 元素色斜切背板（立在立绘后面，给"卡片"感）
            var slashGo = new GameObject("Slash");
            slashGo.transform.SetParent(go.transform, false);
            slashGo.transform.localPosition = new Vector3(-0.2f, 0f, 0.05f);
            slashGo.transform.localRotation = Quaternion.Euler(0f, 0f, -14f);
            var slash = slashGo.AddComponent<SpriteRenderer>();
            slash.sprite = _whiteSprite;
            slash.color = new Color(core.r, core.g, core.b, 0.55f);
            slash.sortingOrder = 480;
            slashGo.transform.localScale = new Vector3(halfW * 0.95f, halfH * 0.30f, 1f);

            // 立绘
            if (portrait != null)
            {
                var pGo = new GameObject("Portrait");
                pGo.transform.SetParent(go.transform, false);
                pGo.transform.localPosition = Vector3.zero;
                var pSr = pGo.AddComponent<SpriteRenderer>();
                pSr.sprite = portrait;
                // ★ 朝向（2026-10-18 用户："敌人放大招时终结技头像反了，应该朝左"）：
                //   立绘原画统一朝右。敌人在右侧、面向我方 ⇒ 必须镜像；我方保持原画朝右。
                pSr.flipX = faceLeft;
                pSr.sortingOrder = 500;
                // ★ 立绘按"目标世界高度"缩放：立绘是 1024² 精灵，自然尺寸 = 像素/PPU（≈10.24 世界单位），
                //   以前直接把 halfH*0.62 当 localScale 用 ⇒ 放大到 30+ 单位、只露肚子（用户 2026-10-13 报障）。
                //   正确做法：localScale = 目标高度 / 精灵自然高度。
                float targetH = halfH * (isAwaken ? 1.00f : 0.86f);   // 约 0.43~0.5 屏高，整个人都看得到
                if (extrasBias) targetH = halfH * 0.66f;              // 连携：主兽缩一档，给四周让位
                float natH = portrait.rect.height / Mathf.Max(1f, portrait.pixelsPerUnit);
                float s = targetH / Mathf.Max(0.0001f, natH);
                pGo.transform.localScale = new Vector3(s, s, 1f);
            }

            // ★ 连携：其余成员头像缩小环绕（四角位，微旋转，压在主兽之后）
            if (extras != null && extras.Length > 0)
            {
                // 四个角位（相对屏幕中心）：左上 / 右上 / 左下 / 右下 —— 交错一点更"环"
                var spots = new[]
                {
                    new Vector3(-halfW * 0.46f,  halfH * 0.34f, 0.06f),
                    new Vector3( halfW * 0.44f,  halfH * 0.40f, 0.06f),
                    new Vector3(-halfW * 0.42f, -halfH * 0.34f, 0.06f),
                    new Vector3( halfW * 0.50f, -halfH * 0.30f, 0.06f),
                };
                for (int i = 0; i < extras.Length && i < spots.Length; i++)
                {
                    var ex = extras[i];
                    if (ex == null) continue;
                    var eGo = new GameObject("ComboMate" + i);
                    eGo.transform.SetParent(go.transform, false);
                    eGo.transform.localPosition = spots[i];
                    eGo.transform.localRotation = Quaternion.Euler(0f, 0f, (i % 2 == 0 ? 7f : -7f));
                    var eSr = eGo.AddComponent<SpriteRenderer>();
                    eSr.sprite = ex;
                    eSr.sortingOrder = 492;                        // 压在主兽(500)之后
                    float exH = halfH * 0.34f;
                    float exNat = ex.rect.height / Mathf.Max(1f, ex.pixelsPerUnit);
                    float es = exH / Mathf.Max(0.0001f, exNat);
                    eGo.transform.localScale = new Vector3(es, es, 1f);
                    // 侧边的镜像一下，看起来像"围拢"而不是"同一张脸"
                    eSr.flipX = spots[i].x > 0f;
                }
            }

            // 名字（LegacyRuntime 中文字体）：异兽名 + 终结技名（用户 2026-10-14："还要他那个终结技的名字"）
            TextMesh nameTm = null;
            TextMesh skillTm = null;
            var f2 = LegacyFont();
            if (!string.IsNullOrEmpty(beastName))
            {
                var nGo = new GameObject("CutName");
                nGo.transform.SetParent(go.transform, false);
                nGo.transform.localPosition = new Vector3(-halfW * 0.30f, -halfH * 0.30f, -0.1f);
                nameTm = nGo.AddComponent<TextMesh>();
                var nmr = nGo.GetComponent<MeshRenderer>();
                if (f2 != null) { nameTm.font = f2; if (nmr != null) nmr.sharedMaterial = f2.material; }
                if (nmr != null) nmr.sortingOrder = 520;
                nameTm.text = beastName;
                nameTm.characterSize = 0.22f;
                nameTm.fontSize = 48;
                nameTm.anchor = TextAnchor.MiddleLeft;
                nameTm.color = new Color(1f, 0.97f, 0.90f);
            }
            if (!string.IsNullOrEmpty(skillName))
            {
                var sGo = new GameObject("CutSkill");
                sGo.transform.SetParent(go.transform, false);
                sGo.transform.localPosition = new Vector3(-halfW * 0.30f, -halfH * 0.40f, -0.1f);
                skillTm = sGo.AddComponent<TextMesh>();
                var smr = sGo.GetComponent<MeshRenderer>();
                if (f2 != null) { skillTm.font = f2; if (smr != null) smr.sharedMaterial = f2.material; }
                if (smr != null) smr.sortingOrder = 520;
                skillTm.text = "◆ " + skillName;
                skillTm.characterSize = 0.15f;
                skillTm.fontSize = 48;
                skillTm.anchor = TextAnchor.MiddleLeft;
                // 元素色（提亮一点，压黑底上更跳）
                skillTm.color = new Color(Mathf.Min(1f, core.r + 0.25f), Mathf.Min(1f, core.g + 0.25f), Mathf.Min(1f, core.b + 0.25f));
            }

            var clip = new SkillFxClip
            {
                Kind = SkillFxKind.CutIn, StartAt = _playStart, Dur = Mathf.Max(0.2f, dur),
                Col = core, Go = go, Slash = slash, Mask = mask, NameTm = nameTm, SkillTm = skillTm, Lines = lineSrs,
                CutInFrom = new Vector3(fromX, portraitPos.y, portraitPos.z),
                CutInTo = portraitPos,
            };
            if (portrait != null) clip.Portrait = go.transform.Find("Portrait") != null
                ? go.transform.Find("Portrait").GetComponent<SpriteRenderer>() : null;
            _clips.Add(clip);
        }

        private SkillFxClip AddClip(SkillFxKind kind, float startAt, float dur, Vector3 pos,
                             float size, int rayCount, float raySharp, float seed,
                             float intensity, Color col, int ringCount = 1)
        {
            var clip = new SkillFxClip
            {
                Kind = kind, StartAt = _playStart + startAt, Dur = Mathf.Max(0.02f, dur),
                Pos = pos, SizeMul = size, RayCount = Mathf.Clamp(rayCount, 3, 24),
                RaySharp = raySharp, Seed = seed, Intensity = intensity, Col = col,
                RingCount = Mathf.Max(1, ringCount),
                Flash = kind == SkillFxKind.Flash,
                Mat = ShaderFor(kind) != null ? new Material(ShaderFor(kind)) : null,
            };
            if (clip.Mat != null) clip.Mat.SetColor("_Color", col);

            var go = new GameObject("SkillFx_" + kind);
            clip.Go = go;
            clip.Sr = go.AddComponent<SpriteRenderer>();

            if (clip.Flash)
            {
                clip.Sr.sprite = _whiteSprite;
                clip.Sr.sortingOrder = 900;              // Flash 走 Queue=Overlay，双保险
                // 铺满相机视口
                var c = Cam != null ? Cam : Camera.main;
                if (c != null && c.orthographic)
                {
                    float h = c.orthographicSize * 2f;
                    float w = h * c.aspect;
                    clip.SizeMul = Mathf.Max(w, h) * 1.15f;
                }
                else clip.SizeMul = 24f;
                go.transform.position = new Vector3(
                    c != null ? c.transform.position.x : 0f,
                    c != null ? c.transform.position.y : 0f,
                    (c != null ? c.transform.position.z : -10f) + 1f);
            }
            else
            {
                clip.Sr.sprite = _glowSprite;
                clip.Sr.sortingOrder = 300;              // 盖在立绘(≈0)之上
                go.transform.position = pos;
            }
            clip.Sr.material = clip.Mat;
            clip.Sr.color = Color.white;
            // 挂到播放器名下（保持已算好的世界坐标）：既能随舞台一起清理，GetComponentsInChildren 也能查到
            go.transform.SetParent(transform, true);
            go.SetActive(false);                          // 到点再激活
            _clips.Add(clip);
            return clip;
        }

        // ------------------------------------------------------------------
        //  推进（由 BattleStage2D.Step 调用；编辑器手动 Step 亦走这里）
        // ------------------------------------------------------------------
        public void Step(float dt)
        {
            if (!_ready || _clips.Count == 0) return;
            _t += dt;

            for (int i = _clips.Count - 1; i >= 0; i--)
            {
                var c = _clips[i];
                float local = _t - c.StartAt;

                if (local < 0f)
                {
                    if (c.Go != null && c.Go.activeSelf) c.Go.SetActive(false);
                    continue;
                }
                float p = local / c.Dur;
                if (p >= 1f)
                {
                    Recycle(i);
                    continue;
                }

                if (c.Go != null && !c.Go.activeSelf) c.Go.SetActive(true);
                // 起爆点镜头反馈：爆发片段激活那一下触发一次（震动 + 推近）
                if (!c.Fired)
                {
                    c.Fired = true;
                    if (c.Kind == SkillFxKind.Burst)
                    {
                        if (OnShake != null && c.ShakeDur > 0f) OnShake(c.ShakeAmp, c.ShakeDur);
                        if (OnZoom  != null && c.ZoomDur  > 0f) OnZoom(c.ZoomDip, c.ZoomDur);
                        if (OnImpact != null) OnImpact();   // 大招冲锋放到起爆那一下
                    }
                }
                UpdateClip(c, p);
            }
        }

        private void UpdateClip(SkillFxClip c, float p)
        {
            // CutIn 无 shader/Sr（用 Portrait/Slash/NameTm 子物体），先单独处理
            if (c.Kind == SkillFxKind.CutIn) { UpdateCutIn(c, p); return; }

            if (c.Mat == null || c.Sr == null) return;
            var m = c.Mat;
            m.SetFloat("_Progress", p);
            m.SetFloat("_Intensity", c.Intensity);

            var tr = c.Go.transform;
            switch (c.Kind)
            {
                case SkillFxKind.Charge:
                    m.SetFloat("_RingRings", Mathf.Clamp(c.RingCount, 1, 4));
                    // 蓄力轻微由小聚大
                    float cs = c.SizeMul * (0.78f + 0.30f * p);
                    tr.localScale = new Vector3(cs, cs, 1f);
                    break;

                case SkillFxKind.Burst:
                    m.SetFloat("_RayCount", c.RayCount);
                    m.SetFloat("_RaySharp", c.RaySharp);
                    m.SetFloat("_Seed", c.Seed);
                    float bs = c.SizeMul * (1.0f + 1.15f * p);
                    tr.localScale = new Vector3(bs, bs, 1f);
                    break;

                case SkillFxKind.Ring:
                    m.SetFloat("_RingRings", Mathf.Clamp(c.RingCount, 1, 3));
                    // 冲击波体量随进度外扩（缩放 + shader 进度双驱动）
                    float rs = c.SizeMul * (0.45f + 1.75f * p);
                    tr.localScale = new Vector3(rs, rs, 1f);
                    break;

                case SkillFxKind.Flash:
                    var cam = Cam != null ? Cam : Camera.main;
                    if (cam != null)
                    {
                        var vp = cam.WorldToViewportPoint(c.FlashCenterWorld);
                        m.SetVector("_Center", new Vector4(vp.x, vp.y, 0f, 0f));
                    }
                    tr.localScale = new Vector3(c.SizeMul, c.SizeMul, 1f);
                    break;
            }
        }

        /// <summary>
        /// Cut-in 三幕：切入(带回弹) → 停驻(轻微浮动+背板高亮) → 向左甩出并淡出（接上蓄力）。
        /// </summary>
        private void UpdateCutIn(SkillFxClip c, float p)
        {
            var tr = c.Go.transform;
            float halfW = (Cam != null ? Cam.aspect : 1.7f) * (Cam != null && Cam.orthographic ? Cam.orthographicSize : 5.4f);

            float x;
            float alpha = 1f;          // 立绘/名字/背板整体透明度（甩出时淡出）
            // ⛔ 本工程是 **Linear 色彩空间**：0.62 的黑色实际只压到底图 ~55% 亮度，
            //   看着像"没遮住"（用户 2026-10-18 反馈特写背景还是花的）。要接近 1 才是真黑。
            float maskA = 0.95f;
            const float In = 0.28f, Out = 0.80f;   // 切入段 / 甩出段起点（中间 0.52 段为停驻，看得清）
            if (p < In)
            {
                //  easeOutBack：冲过头再回弹，P5R 切入的"啪"一下
                float k = p / In;
                float c1 = 1.70158f, c3 = c1 + 1f;
                float e = 1f + c3 * Mathf.Pow(k - 1f, 3f) + c1 * Mathf.Pow(k - 1f, 2f);
                x = Mathf.Lerp(c.CutInFrom.x, c.CutInTo.x, e);
                maskA = 0.95f * Mathf.Clamp01(k * 1.5f);   // 遮罩抢先压黑（比立绘先到位）
            }
            else if (p < Out)
            {
                // 停驻：轻微上下浮动
                float k = (p - In) / (Out - In);
                x = c.CutInTo.x;
                tr.localPosition = new Vector3(x, c.CutInTo.y + Mathf.Sin(k * Mathf.PI * 2f) * 0.05f, c.CutInTo.z);
                // 背板呼吸高亮
                if (c.Slash != null) c.Slash.color = new Color(c.Col.r, c.Col.g, c.Col.b, 0.45f + 0.18f * Mathf.Sin(k * Mathf.PI));
                if (c.Mask != null) c.Mask.color = new Color(0f, 0f, 0f, 0.95f);
                if (c.Lines != null)
                    for (int i = 0; i < c.Lines.Length; i++)
                        if (c.Lines[i] != null) c.Lines[i].color = new Color(c.Col.r, c.Col.g, c.Col.b, 0.4f);
                return;
            }
            else
            {
                // 甩出：向左滑出 + 淡出
                float k = (p - Out) / (1f - Out);
                x = Mathf.Lerp(c.CutInTo.x, -halfW * 1.1f, k * k);
                alpha = 1f - k;
                maskA = 0.95f * (1f - k);
            }

            tr.localPosition = new Vector3(x, c.CutInTo.y, c.CutInTo.z);

            // 立绘 / 背板 / 名字整体淡出
            if (c.Portrait != null) { var cc = c.Portrait.color; c.Portrait.color = new Color(cc.r, cc.g, cc.b, alpha); }
            if (c.Slash != null)    { var cc = c.Slash.color;    c.Slash.color    = new Color(cc.r, cc.g, cc.b, cc.a * alpha); }
            if (c.NameTm != null)   { var cc = c.NameTm.color;   c.NameTm.color   = new Color(cc.r, cc.g, cc.b, alpha); }
            if (c.SkillTm != null)  { var cc = c.SkillTm.color;  c.SkillTm.color  = new Color(cc.r, cc.g, cc.b, alpha); }
            if (c.Mask != null)     c.Mask.color = new Color(0f, 0f, 0f, maskA * alpha);
            if (c.Lines != null)
                for (int i = 0; i < c.Lines.Length; i++)
                    if (c.Lines[i] != null) c.Lines[i].color = new Color(c.Col.r, c.Col.g, c.Col.b, 0.4f * alpha);
        }

        private void Recycle(int i)
        {
            var c = _clips[i];
            if (c.Go != null) Destroy(c.Go);
            if (c.Mat != null) Destroy(c.Mat);
            _clips.RemoveAt(i);
        }

        /// <summary>立刻清掉所有特效（切场景/离开战斗时调用）。</summary>
        public void Clear()
        {
            for (int i = _clips.Count - 1; i >= 0; i--) Recycle(i);
            _t = 0f;
        }

        private void OnDestroy() => Clear();

        // ------------------------------------------------------------------
        //  工具
        // ------------------------------------------------------------------

        /// <summary>异兽 id 的稳定哈希（FNV-1a）——跨运行一致，保证演出可复现。</summary>
        private static int StableHash(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            uint h = 2166136261u;
            for (int i = 0; i < s.Length; i++) { h = (h ^ s[i]) * 16777619u; }
            return (int)(h & 0x7fffffff);
        }

        /// <summary>径向柔边贴图：中心不透明、边缘透明，白色。</summary>
        private static Texture2D MakeGlow(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                // 平滑径向衰减：中心 1 → 边缘 0（带一点幂让核心更实）
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);            // smoothstep 手感
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(px);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();
            return tex;
        }

        /// <summary>1×1 不透明白块（全屏闪用）。</summary>
        private static Texture2D MakeWhite()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, new Color(1f, 1f, 1f, 1f));
            tex.Apply();
            return tex;
        }
    }
}
