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
    /// <summary>特效片段的类型（对应四个 shader）。</summary>
    internal enum SkillFxKind { Charge, Burst, Ring, Flash }

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
        private Texture2D _whiteTex;  // 1×1 白块（Flash）
        private Sprite _glowSprite;
        private Sprite _whiteSprite;

        private readonly List<SkillFxClip> _clips = new List<SkillFxClip>(16);
        private float _t;                 // 本地时钟（由 Step 累加）
        private bool _ready;

        /// <summary>舞台相机（用于全屏闪的尺寸与爆心换算）。BattleStage2D.Bind 时注入。</summary>
        public Camera Cam;
        /// <summary>镜头震动回调 (amp, dur)。BattleStage2D 绑到 TriggerShake。</summary>
        public System.Action<float, float> OnShake;

        /// <summary>是否还有特效在播（诊断/自检用）。</summary>
        public bool Busy => _clips.Count > 0;
        public int ActiveCount => _clips.Count;

        // ------------------------------------------------------------------
        //  初始化：载入 shader / 生成程序化贴图（幂等，可重复调用）
        // ------------------------------------------------------------------
        public void Bind(Camera cam, System.Action<float, float> onShake)
        {
            Cam = cam;
            OnShake = onShake;
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
        // ------------------------------------------------------------------
        public void Play(Vector3 worldPos, string actorId, Color element, bool isAwaken)
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
            float chargeDur = (isAwaken ? 0.70f : 0.50f);
            float burstDur  = 0.35f * aw;
            float ringDur   = (isAwaken ? 1.00f : 0.80f);
            float flashDur  = (isAwaken ? 0.34f : 0.25f);

            // ① 蓄力
            AddClip(SkillFxKind.Charge, 0f, chargeDur,
                    new Vector3(worldPos.x, worldPos.y + 0.2f, worldPos.z - 0.05f),
                    size: 2.2f * sizeMul * aw, rayCount, raySharp, phase, 1.15f * aw, core);

            // ②③④ 同一起爆点（chargeDur 末）：爆发 + 冲击波(可多环) + 全屏闪
            AddClip(SkillFxKind.Burst, chargeDur, burstDur,
                    new Vector3(worldPos.x, worldPos.y + 0.15f, worldPos.z - 0.03f),
                    size: 3.0f * sizeMul * aw, rayCount, raySharp, phase, (isAwaken ? 1.7f : 1.1f), core);

            for (int k = 0; k < ringN; k++)
            {
                AddClip(SkillFxKind.Ring, chargeDur + k * 0.09f, ringDur,
                        new Vector3(worldPos.x, worldPos.y + 0.1f, worldPos.z - 0.02f),
                        size: (7.5f + k * 1.2f) * sizeMul * aw, rayCount, raySharp, phase + k * 0.3f,
                        1.1f * aw, core, ringCount: ringN);
            }

            AddClip(SkillFxKind.Flash, chargeDur + 0.03f, flashDur,
                    Vector3.zero, size: 0f, rayCount, raySharp, phase,
                    isAwaken ? 0.72f : 0.52f, core).FlashCenterWorld = worldPos;

            // 起爆点镜头震动
            OnShake?.Invoke(isAwaken ? 0.34f : 0.20f, isAwaken ? 0.42f : 0.30f);
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
                Mat = new Material(_shaders[(int)kind]),
            };
            clip.Mat.SetColor("_Color", col);

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
                UpdateClip(c, p);
            }
        }

        private void UpdateClip(SkillFxClip c, float p)
        {
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
