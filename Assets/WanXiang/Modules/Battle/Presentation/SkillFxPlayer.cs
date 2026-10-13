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
        public TextMesh NameTm;          // 异兽名（LegacyRuntime 字体，支持中文）
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
        private bool _ready;

        /// <summary>舞台相机（用于全屏闪的尺寸与爆心换算）。BattleStage2D.Bind 时注入。</summary>
        public Camera Cam;
        /// <summary>镜头震动回调 (amp, dur)。BattleStage2D 绑到 TriggerShake。</summary>
        public System.Action<float, float> OnShake;
        /// <summary>镜头推近回调 (dip, dur)。BattleStage2D 绑到 TriggerZoom（起爆点 Zoom Punch）。</summary>
        public System.Action<float, float> OnZoom;

        /// <summary>是否还有特效在播（诊断/自检用）。</summary>
        public bool Busy => _clips.Count > 0;
        public int ActiveCount => _clips.Count;

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
            _whiteSprite = Sprite.Create(_whiteTex, new Rect(0, 0, _whiteTex.width, _whiteTex.height),
                                         new Vector2(0.5f, 0.5f), 100f);
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
                         Sprite portrait = null, string beastName = null)
        {
            Ensure();
            if (_shaders == null || _shaders[0] == null) return;   // shader 没加载上就别播

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
            float cutInDur  = (isAwaken ? 0.72f : 0.55f) * (portrait != null ? 1f : 0f); // 无立绘则不播特写
            float chargeDur = (isAwaken ? 0.70f : 0.50f);
            float burstDur  = 0.35f * aw;
            float ringDur   = (isAwaken ? 1.00f : 0.80f);
            float flashDur  = (isAwaken ? 0.34f : 0.25f);

            // ⓪ 异兽头像特写（P5R 式 cut-in）：先让玩家看清"是谁开大"，再进特效
            if (cutInDur > 0f)
                AddCutIn(worldPos, cutInDur, core, portrait, beastName, isAwaken);

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
        private void AddCutIn(Vector3 worldPos, float dur, Color core, Sprite portrait, string beastName, bool isAwaken)
        {
            var go = new GameObject("SkillFx_CutIn");
            var cam = Cam != null ? Cam : Camera.main;
            float halfH = cam != null && cam.orthographic ? cam.orthographicSize : 5.4f;
            float halfW = halfH * (cam != null ? cam.aspect : 1.7f);

            // 立绘落点：画面右侧（P5R 的 cut-in 多在右侧/居中），从更右的屏外切入
            float targetX = halfW * 0.42f;
            float fromX    = halfW * 1.15f;
            var portraitPos = new Vector3(targetX, worldPos.y * 0.3f + 0.2f, worldPos.z - 0.2f);
            go.transform.position = portraitPos;
            go.transform.SetParent(transform, true);   // 挂到播放器名下（保持世界坐标）

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
                pSr.sortingOrder = 500;
                // 立绘高度约 0.8 屏高
                float ph = halfH * (isAwaken ? 1.5f : 1.35f);
                float pw = ph;
                if (portrait.texture != null)
                    pw = ph * ((float)portrait.texture.width / Mathf.Max(1, portrait.texture.height));
                pGo.transform.localScale = new Vector3(pw, ph, 1f);
            }

            // 名字（LegacyRuntime 中文字体）
            TextMesh nameTm = null;
            if (!string.IsNullOrEmpty(beastName))
            {
                var nGo = new GameObject("CutName");
                nGo.transform.SetParent(go.transform, false);
                nGo.transform.localPosition = new Vector3(-halfW * 0.30f, -halfH * 0.34f, -0.1f);
                nameTm = nGo.AddComponent<TextMesh>();
                var f = LegacyFont();
                var nmr = nGo.GetComponent<MeshRenderer>();
                if (f != null) { nameTm.font = f; if (nmr != null) nmr.sharedMaterial = f.material; }
                if (nmr != null) nmr.sortingOrder = 520;
                nameTm.text = beastName;
                nameTm.characterSize = 0.22f;
                nameTm.fontSize = 48;
                nameTm.anchor = TextAnchor.MiddleLeft;
                nameTm.color = new Color(1f, 0.97f, 0.90f);
            }

            var clip = new SkillFxClip
            {
                Kind = SkillFxKind.CutIn, StartAt = 0f, Dur = Mathf.Max(0.2f, dur),
                Col = core, Go = go, Slash = slash, NameTm = nameTm,
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
                Kind = kind, StartAt = startAt, Dur = Mathf.Max(0.02f, dur),
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
            float alpha = 1f;
            const float In = 0.40f, Out = 0.72f;   // 切入段 / 甩出段起点
            if (p < In)
            {
                //  easeOutBack：冲过头再回弹，P5R 切入的"啪"一下
                float k = p / In;
                float c1 = 1.70158f, c3 = c1 + 1f;
                float e = 1f + c3 * Mathf.Pow(k - 1f, 3f) + c1 * Mathf.Pow(k - 1f, 2f);
                x = Mathf.Lerp(c.CutInFrom.x, c.CutInTo.x, e);
            }
            else if (p < Out)
            {
                // 停驻：轻微上下浮动
                float k = (p - In) / (Out - In);
                x = c.CutInTo.x;
                tr.localPosition = new Vector3(x, c.CutInTo.y + Mathf.Sin(k * Mathf.PI * 2f) * 0.05f, c.CutInTo.z);
                // 背板呼吸高亮
                if (c.Slash != null) c.Slash.color = new Color(c.Col.r, c.Col.g, c.Col.b, 0.45f + 0.18f * Mathf.Sin(k * Mathf.PI));
                return;
            }
            else
            {
                // 甩出：向左滑出 + 淡出
                float k = (p - Out) / (1f - Out);
                x = Mathf.Lerp(c.CutInTo.x, -halfW * 1.1f, k * k);
                alpha = 1f - k;
            }

            tr.localPosition = new Vector3(x, c.CutInTo.y, c.CutInTo.z);

            // 立绘 / 背板 / 名字整体淡出
            if (c.Portrait != null) { var cc = c.Portrait.color; c.Portrait.color = new Color(cc.r, cc.g, cc.b, alpha); }
            if (c.Slash != null)    { var cc = c.Slash.color;    c.Slash.color    = new Color(cc.r, cc.g, cc.b, cc.a * alpha); }
            if (c.NameTm != null)   { var cc = c.NameTm.color;   c.NameTm.color   = new Color(cc.r, cc.g, cc.b, alpha); }
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
