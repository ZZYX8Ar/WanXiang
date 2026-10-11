// ============================================================================
//  万相 · 主界面 3D 层（背景 + 氛围 + 异兽 + 天气）·  2026-10-11 第三版
//  ---------------------------------------------------------------------------
//  ⛔⛔ Unity 渲染铁律（第一版"看不见"的根因，务必牢记）：
//     UIRoot 的 Canvas 是 ScreenSpaceOverlay（m_RenderMode=0），
//     **Overlay Canvas 不需要相机、且永远渲染在所有相机之上**。
//     ⇒ UI 层绝不能有全屏不透明图，否则 3D 全被盖住。
//     ⇒ 相机 depth 只决定"相机之间"的顺序，管不了 Overlay Canvas。
//     因此：背景必须由本层自己画；相机与内容根必须挂在 Canvas 之外。
//
//  本版实现（用户 2026-10-11 第二轮需求）：
//   · 异兽**尺寸按世界高度反算**（第一版用固定乘数 ⇒ 立绘本体就有 ~10 世界单位，
//     乘完占半个屏幕）；并改为**沿地图外沿绕圈游荡**（"在四周走动"）。
//   · 氛围系统（用户"下雨打雷吹风等等这种感觉"）：
//       雾/云   —— 程序化生成的无缝雾纹理，多层视差横向漂移
//       风      —— 全局风向量：影响雨丝倾角、雾漂移速度、飘落物横向偏移、异兽步速（阵风起伏）
//       雨/雪   —— ParticleSystem
//       闪电    —— 随机间隔的全屏白闪（世界空间 Quad 脉冲）
//       飘落物  —— 按幕选 FX_particle 图（花瓣/萤火/落叶/飞雪/金尘）
//   · 幕 + 节点进度**双重驱动**：幕决定"基础氛围"，幕内进度决定"强度"
//       （雾越厚、天色越沉、风越大、临近守关天气越恶劣）。
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using WanXiang.Battle.Presentation;   // SpriteCatalog（漏 using 报 CS0246）

namespace WanXiang.Modules.UI
{
    /// <summary>主界面 3D 层：背景 + 氛围（雾/风/雨/雷）+ 异兽游荡。相机与内容挂在 Canvas 之外。</summary>
    public sealed class HomeScene3D : MonoBehaviour
    {
        private bool _running;
        public bool Running
        {
            get { return _running; }
            set
            {
                _running = value;
                // 相机已移出面板层级，不随面板隐藏 ⇒ 必须显式停/启，否则在别的界面空转
                if (_cam != null) _cam.enabled = value;
            }
        }

        [Header("相机")]
        [SerializeField] private float _orthoSize = 5.4f;
        [SerializeField] private float _camZ = 10f;

        [Header("背景")]
        [SerializeField] private float _bgZ = 6f;

        [Header("异兽")]
        [SerializeField] private int _maxWalkers = 6;
        [SerializeField] private float _walkerHeight = 1.35f;   // ⛔ 目标世界高度（按它反算缩放）
        [SerializeField] private float _walkerSpeed = 0.75f;     // 绕圈角速度基准

        [Header("氛围")]
        [SerializeField] private float _fogBase = 0.35f;         // 雾基础浓度
        [SerializeField] private float _windBase = 0.35f;        // 风基础强度

        private Camera _cam;
        private Transform _root;
        private SpriteRenderer _bgQuad;
        private SpriteCatalog _sprites;
        private RectTransform _mapRect;          // 中央地图（用于算异兽绕圈路径）
        private int _curAct = -1;
        private float _progress;                 // 幕内进度 0..1
        private bool _inited;

        // 氛围
        private readonly List<FogLayer> _fog = new List<FogLayer>();
        private SpriteRenderer _flash;           // 闪电
        private float _flashTimer, _flashLife;
        private float _wind = 0.35f;             // 当前风强度
        private float _windDir = 1f;             // 风向
        private float _gustPhase;
        private float _fogDensity = 0.35f;
        private ParticleSystem _psRain, _psSnow, _psFall;
        private Sprite _fallSprite;              // 飘落物用图（按幕，Sprite 不是 SpriteRenderer）
        private int _weatherKind;

        private readonly List<Walker> _walkers = new List<Walker>();

        private struct Walker
        {
            public Transform T;
            public float Angle;      // 绕圈相位
            public float AngSpeed;   // 角速度（含方向正负）
            public float Z;
            public float Scale;
            public float Phase;      // 起伏相位
            public float HopAmp;
        }

        private struct FogLayer
        {
            public SpriteRenderer Sr;
            public float Speed;      // 漂移速度（世界单位/秒）
            public float Z;
            public float Width;      // 单块宽度（循环用）
            public float Alpha;
        }

        // 幕↔季节：1春 2夏 3秋 4冬 5长夏（⛔ 别按"春夏秋冬+长夏"直觉排）
        private static readonly string[] BgByAct =
        {
            "UI/Home/home_bg_act1_spring",
            "UI/Home/home_bg_act2_summer",
            "UI/Home/home_bg_act3_autumn",
            "UI/Home/home_bg_act4_winter",
            "UI/Home/home_bg_act5_latesummer",
        };
        private static readonly string[] FallByAct =
        {
            "Battle/FX_particle_act1_petal",   // 春·花瓣
            "Battle/FX_particle_act2_firefly", // 夏·萤火
            "Battle/FX_particle_act3_leaf",    // 秋·落叶
            "Battle/FX_particle_act4_snow",    // 冬·雪花
            "Battle/FX_particle_act5_pollen",  // 长夏·金尘
        };

        private float HalfW { get { return _orthoSize * 16f / 9f; } }

        private void Awake() { Init(); }

        public void Init()
        {
            if (_inited) return;
            EnsureWorld();
            MoveOutsideCanvas();
            _inited = true;
        }

        /// <summary>地图 RectTransform（用来让异兽贴着地图外沿绕圈）。</summary>
        public void SetMapRect(RectTransform map) { _mapRect = map; }

        private void EnsureWorld()
        {
            if (_cam == null)
            {
                var go = new GameObject("HomeCamera3D");
                _cam = go.AddComponent<Camera>();
                _cam.orthographic = true;
                _cam.orthographicSize = _orthoSize;
                _cam.nearClipPlane = 0.01f;
                _cam.farClipPlane = 100f;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = new Color(0.86f, 0.87f, 0.86f);
                _cam.depth = -10f;   // ⛔ 必须 < UICamera(99) 且为负
                _cam.transform.position = new Vector3(0f, 0f, _camZ);
                _cam.transform.rotation = Quaternion.identity;
                _cam.transform.localScale = Vector3.one;
            }
            if (_root == null)
            {
                _root = new GameObject("HomeWorld3D").transform;
                _root.position = Vector3.zero;
                _root.localScale = Vector3.one;
            }
        }

        private void MoveOutsideCanvas()
        {
            var canvas = GetComponentInParent<Canvas>();
            Transform host = canvas != null ? canvas.transform.parent : null;
            if (host == null) host = transform.root;
            if (host == null) return;
            if (_root != null && _root.parent != host) _root.SetParent(host, true);
            if (_cam != null && _cam.transform.parent != host) _cam.transform.SetParent(host, true);
        }

        public void InjectCatalog(SpriteCatalog cat) { _sprites = cat; }

        // ------------------------------------------------------------------
        //  幕 + 进度 → 氛围强度
        // ------------------------------------------------------------------
        /// <summary>
        /// 按「幕 + 幕内进度」刷新整套氛围。progress 0..1（0=本幕刚出发，1=抵达守关）。
        /// 设计意图（用户："会跟着探索节点地图实时变化"）：
        ///   幕决定基调（春薄雾柔风 / 夏雷雨多变 / 秋大风落叶 / 冬寒雾飞雪 / 长夏金尘低压），
        ///   进度决定强度（雾越走越厚、风越走越大、临近守关天气越恶劣）。
        /// </summary>
        public void ApplyAmbience(int act, float progress)
        {
            Init();
            act = Mathf.Clamp(act, 1, 5);
            _progress = Mathf.Clamp01(progress);

            // ---- 背景（按幕，换幕才重建）----
            if (act != _curAct || _bgQuad == null || _bgQuad.sprite == null)
            {
                _curAct = act;
                var sp = Resources.Load<Sprite>(BgByAct[act - 1]);
                if (sp == null) Debug.LogWarning("[Home3D] 背景缺图：" + BgByAct[act - 1]);
                else
                {
                    if (_bgQuad == null)
                    {
                        var go = new GameObject("HomeBg");
                        go.transform.SetParent(_root, false);
                        _bgQuad = go.AddComponent<SpriteRenderer>();
                        _bgQuad.sortingOrder = -1000;
                    }
                    _bgQuad.sprite = sp;
                    var size = sp.bounds.size;
                    float sx = size.x > 0.001f ? (HalfW * 2.08f) / size.x : 1f;
                    float sy = size.y > 0.001f ? (_orthoSize * 2.08f) / size.y : 1f;
                    _bgQuad.transform.localScale = new Vector3(sx, sy, 1f);
                    _bgQuad.transform.localPosition = new Vector3(0f, 0f, _bgZ);
                    Debug.Log("[Home3D] 背景 act=" + act + " sprite=" + sp.name);
                }
            }

            // ---- 进度驱动的强度 ----
            float p = _progress;
            // 幕基调：基础风/雾 + 该幕"恶劣倾向"
            float baseWind, baseFog; int baseWeather;
            switch (act)
            {
                case 1: baseWind = 0.30f; baseFog = 0.32f; baseWeather = 0; break;  // 春：柔风薄雾
                case 2: baseWind = 0.42f; baseFog = 0.40f; baseWeather = 1; break;  // 夏：阵雨
                case 3: baseWind = 0.62f; baseFog = 0.36f; baseWeather = 0; break;  // 秋：大风
                case 4: baseWind = 0.48f; baseFog = 0.52f; baseWeather = 2; break;  // 冬：寒雾飞雪
                default: baseWind = 0.55f; baseFog = 0.60f; baseWeather = 4; break; // 长夏：低压雷雨
            }
            // 进度抬高强度（守关前最紧张）
            _wind = Mathf.Clamp01(baseWind + p * 0.45f);
            _fogDensity = Mathf.Clamp01(baseFog + p * 0.40f);
            _windDir = 1f;   // 风向（可后续按幕轮换）

            // 天气：基调 + 进度。接近守关时"转恶劣"
            int weather = baseWeather;
            if (p > 0.72f)
            {
                if (act == 2) weather = 4;              // 夏末 → 雷雨
                else if (act == 3) weather = 1;         // 秋末 → 起雨
                else if (act == 4) weather = 2;         // 冬末 → 大雪
                else if (act == 1) weather = 3;         // 春末 → 转阴
            }
            SetWeather(weather);

            BuildFog();
            BuildFall();
            Debug.Log("[Home3D] 氛围 act=" + act + " 进度=" + p.ToString("0.00") +
                      " 风=" + _wind.ToString("0.00") + " 雾=" + _fogDensity.ToString("0.00") +
                      " 天气=" + weather);
        }

        // ------------------------------------------------------------------
        //  雾 / 云（程序化无缝纹理 + 多层视差横移）
        // ------------------------------------------------------------------
        private static Sprite _fogSprite;

        private static Sprite FogSprite()
        {
            if (_fogSprite != null) return _fogSprite;
            const int W = 256, H = 128;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (float)x / W, v = (float)y / H;
                    // 整数频率的正弦叠加 ⇒ u 方向天然无缝（左右可循环）
                    float a = 0.5f;
                    a += Mathf.Sin(u * Mathf.PI * 2f * 1f + v * 3.1f) * 0.26f;
                    a += Mathf.Sin(u * Mathf.PI * 2f * 2f + v * 5.7f + 1.3f) * 0.16f;
                    a += Mathf.Sin(u * Mathf.PI * 2f * 3f + v * 2.3f + 2.1f) * 0.10f;
                    a = Mathf.Clamp01(a);
                    a = Mathf.Pow(a, 1.7f);
                    a *= Mathf.Sin(v * Mathf.PI);      // 上下淡出，避免硬边
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            _fogSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0,
                                       SpriteMeshType.FullRect);
            return _fogSprite;
        }

        private void BuildFog()
        {
            if (_fog.Count == 0)
            {
                // 三层，越远越慢越淡（视差）
                AddFogLayer(4.6f, 0.055f, 0.30f);   // 远
                AddFogLayer(3.2f, 0.100f, 0.24f);   // 中
                AddFogLayer(1.9f, 0.170f, 0.18f);   // 近
            }
            // 浓度 → 透明度
            for (int i = 0; i < _fog.Count; i++)
            {
                var f = _fog[i];
                if (f.Sr == null) continue;
                var c = f.Sr.color;
                f.Sr.color = new Color(c.r, c.g, c.b, f.Alpha * (0.5f + _fogDensity));
                _fog[i] = f;
            }
        }

        private void AddFogLayer(float z, float speed, float alpha)
        {
            var sp = FogSprite();
            // 每层放两块并排，循环滚动时首尾相接
            float sw = sp.bounds.size.x;
            float blockW = HalfW * 2f / sw * 1.06f;
            var holder = new GameObject("Fog_z" + z);
            holder.transform.SetParent(_root, false);
            var sr = holder.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            sr.sortingOrder = -900 + Mathf.RoundToInt(-z * 10f);
            sr.color = new Color(1f, 1f, 1f, alpha);
            holder.transform.localScale = new Vector3(blockW * 2f, 1.6f, 1f);
            holder.transform.localPosition = new Vector3(0f, Random.Range(-0.6f, 1.4f), z);
            _fog.Add(new FogLayer
            {
                Sr = sr, Speed = speed, Z = z,
                Width = sw * blockW * 2f, Alpha = alpha,
            });
        }

        private void StepFog(float dt)
        {
            for (int i = 0; i < _fog.Count; i++)
            {
                var f = _fog[i];
                if (f.Sr == null) continue;
                var p = f.Sr.transform.localPosition;
                p.x += f.Speed * _windDir * _wind * dt * 6f;
                // 超出右边界 → 回绕到左侧（无缝：纹理左右连续）
                float half = f.Width * 0.5f;
                if (p.x > half) p.x -= f.Width;
                else if (p.x < -half) p.x += f.Width;
                f.Sr.transform.localPosition = p;
                _fog[i] = f;
            }
        }

        // ------------------------------------------------------------------
        //  飘落物（按幕：花瓣/萤火/落叶/雪/金尘）
        // ------------------------------------------------------------------
        private void BuildFall()
        {
            var sp = Resources.Load<Sprite>(FallByAct[Mathf.Clamp(_curAct, 1, 5) - 1]);
            if (sp == null) return;
            if (_psFall != null && _fallSprite != null && _fallSprite == sp) return;

            if (_psFall != null) { Destroy(_psFall.gameObject); _psFall = null; }
            _fallSprite = sp;

            var go = new GameObject("PS_Fall");
            go.transform.SetParent(_root, false);
            go.transform.localPosition = new Vector3(0f, _orthoSize + 0.8f, 1.4f);
            _psFall = go.AddComponent<ParticleSystem>();
            var main = _psFall.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = 12f;
            main.startSpeed = 0.35f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.34f);
            main.maxParticles = 260;
            main.gravityModifier = 0.06f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            var em = _psFall.emission;
            em.rateOverTime = 26f;
            var shape = _psFall.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(HalfW * 2f, 0.1f, 1f);
            var col = _psFall.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.85f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);
            var r = _psFall.GetComponent<ParticleSystemRenderer>();
            r.sortingOrder = 250;
            // 用 Sprite 纹理当粒子贴图
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.mainTexture = _fallSprite.texture;
            r.material = mat;
        }

        // ------------------------------------------------------------------
        //  天气（0晴 1雨 2雪 3阴 4雷雨）
        // ------------------------------------------------------------------
        public void SetWeather(int kind)
        {
            Init();
            _weatherKind = kind;
            EnsurePrecip();

            bool rain = kind == 1 || kind == 4;
            bool snow = kind == 2;
            SetEmission(_psRain, rain);
            SetEmission(_psSnow, snow);

            // 雨丝倾角随风（用户"吹风"）
            if (_psRain != null)
            {
                var shape = _psRain.shape;
                shape.rotation = new Vector3(0f, 0f, -_windDir * _wind * 32f);
                var main = _psRain.main;
                main.startSpeed = 12f + _wind * 8f;
            }

            ApplyWeatherTint();
            // 雷雨才打闪
            if (kind != 4) { _flashTimer = 0f; _flashLife = 0f; if (_flash != null) SetFlashAlpha(0f); }
            Debug.Log("[Home3D] 天气 kind=" + kind + (kind == 4 ? "（雷雨）" : ""));
        }

        private static void SetEmission(ParticleSystem ps, bool on)
        {
            if (ps == null) return;
            if (!ps.gameObject.activeSelf) ps.gameObject.SetActive(true);
            var em = ps.emission;
            em.enabled = on;
        }

        private void ApplyWeatherTint()
        {
            float dim = _weatherKind == 4 ? 0.58f
                      : _weatherKind == 3 ? 0.68f
                      : _weatherKind == 1 ? 0.78f
                      : _weatherKind == 2 ? 0.92f : 1f;
            // 进度越大天色越沉（越接近守关越压抑）
            dim -= _progress * 0.10f;
            dim = Mathf.Clamp(dim, 0.35f, 1f);
            if (_bgQuad != null) _bgQuad.color = new Color(dim, dim, dim * 0.99f, 1f);
            if (_cam != null)
                _cam.backgroundColor = _weatherKind == 4 ? new Color(0.52f, 0.54f, 0.58f)
                                     : _weatherKind == 3 ? new Color(0.60f, 0.62f, 0.64f)
                                     : _weatherKind == 1 ? new Color(0.70f, 0.73f, 0.76f)
                                     : new Color(0.86f, 0.87f, 0.86f);
        }

        private void EnsurePrecip()
        {
            if (_psRain == null) _psRain = BuildPrecip("PS_Rain", true);
            if (_psSnow == null) _psSnow = BuildPrecip("PS_Snow", false);
            if (_flash == null)
            {
                var go = new GameObject("Flash");
                go.transform.SetParent(_root, false);
                go.transform.localPosition = new Vector3(0f, 0f, 0.4f);
                var sr = go.AddComponent<SpriteRenderer>();
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                for (int i = 0; i < 4; i++) t.SetPixel(i % 2, i / 2, Color.white);
                t.Apply();
                sr.sprite = Sprite.Create(t, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                go.transform.localScale = new Vector3(HalfW * 2.4f, _orthoSize * 2.4f, 1f);
                sr.sortingOrder = 800;
                sr.color = new Color(1f, 1f, 1f, 0f);
                _flash = sr;
            }
        }

        private ParticleSystem BuildPrecip(string name, bool rain)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = new Vector3(0f, _orthoSize + 1f, 2.2f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = rain ? 1400 : 700;
            if (rain)
            {
                main.startLifetime = 1.2f;
                main.startSpeed = 13f;
                main.startSize = 0.05f;
                main.startColor = new Color(0.72f, 0.80f, 0.92f, 0.5f);
                main.gravityModifier = 2.2f;
            }
            else
            {
                main.startLifetime = 7f;
                main.startSpeed = 1.0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
                main.startColor = new Color(1f, 1f, 1f, 0.85f);
                main.gravityModifier = 0.15f;
            }
            var em = ps.emission;
            em.rateOverTime = rain ? 520f : 120f;
            em.enabled = false;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(HalfW * 2f, 0.1f, 1f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sortingOrder = 300;
            if (rain)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.2f;
            }
            return ps;
        }

        private void SetFlashAlpha(float a)
        {
            if (_flash == null) return;
            _flash.color = new Color(1f, 1f, 1f, a);
        }

        private void StepFlash(float dt)
        {
            if (_flash == null) return;
            if (_weatherKind != 4) return;
            if (_flashLife > 0f)
            {
                _flashLife -= dt;
                // 双闪（更真实）
                float k = Mathf.Clamp01(_flashLife / 0.22f);
                float a = (Mathf.Sin(k * Mathf.PI * 3f) * 0.5f + 0.5f) * 0.55f * k;
                SetFlashAlpha(a);
                if (_flashLife <= 0f) SetFlashAlpha(0f);
                return;
            }
            _flashTimer -= dt;
            if (_flashTimer <= 0f)
            {
                _flashLife = 0.22f;
                _flashTimer = Random.Range(3.5f, 9f);
            }
        }

        // ------------------------------------------------------------------
        //  异兽：沿地图外沿绕圈游荡（"在四周走动"）
        // ------------------------------------------------------------------
        /// <summary>
        /// 异兽活动带：
        /// ⛔ 不能用"大椭圆绕圈" —— 中央地图宽 1600 画布像素 ≈ 24 世界单位，
        ///    比正交视野(19.2)还宽 ⇒ 地图横向占满屏幕，左右两端没有 3D 可视空间，
        ///    绕到两侧的异兽会整个藏到地图后面（看起来像"走着走着没了"）。
        /// 正确做法：只在**地图上下外沿的空白带**里横向往返 —— 始终可见，
        ///    且上下都有 ⇒ 视觉上就是"在四周游荡"。
        /// </summary>
        private void ComputeBands(out float bandY, out float roamHalfW)
        {
            bandY = Mathf.Min(MapHalfH() + 0.58f, _orthoSize - 0.38f);
            roamHalfW = HalfW * 0.90f;
        }

        /// <summary>中央地图在 3D 世界里的半高（画布像素 → 世界单位）。</summary>
        private float MapHalfH()
        {
            if (_mapRect != null)
            {
                var canvas = _mapRect.GetComponentInParent<Canvas>();
                float canvasH = 720f;
                if (canvas != null && canvas.transform is RectTransform)
                    canvasH = ((RectTransform)canvas.transform).rect.height;
                if (canvasH > 1f)
                    return (_mapRect.rect.height * 0.5f) * ((_orthoSize * 2f) / canvasH);
            }
            return _orthoSize * 0.78f;   // 没有地图引用时的兜底
        }

        /// <summary>
        /// 按出战队伍重建异兽。
        /// ⛔ 尺寸必须按**目标世界高度反算**：立绘本体常有 ~10 世界单位高，
        ///    第一版用固定乘数(_scaleBase=1.05) ⇒ 得到一只占半个屏幕的巨兽。
        /// </summary>
        public void SetWalkers(IReadOnlyList<string> beastIds)
        {
            Init();
            for (int i = 0; i < _walkers.Count; i++)
                if (_walkers[i].T != null) Destroy(_walkers[i].T.gameObject);
            _walkers.Clear();
            if (_sprites == null || beastIds == null) return;

            float bandY, roamW;
            ComputeBands(out bandY, out roamW);

            int made = 0;
            for (int i = 0; i < beastIds.Count && made < _maxWalkers; i++)
            {
                var id = beastIds[i];
                if (string.IsNullOrEmpty(id)) continue;
                var sp = _sprites.Get(id);
                if (sp == null) continue;

                // ⛔ 按目标高度反算缩放（而不是固定乘数）
                float spriteH = sp.bounds.size.y;
                float baseScale = spriteH > 0.001f ? _walkerHeight / spriteH : 0.08f;

                float z = 1.6f - made * 0.5f;                      // z 越小越靠前
                float scale = baseScale * (1f + (1.6f - z) * 0.14f);  // 近大远小

                var go = new GameObject("Walker_" + id);
                go.transform.SetParent(_root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                sr.sortingOrder = 100 + Mathf.RoundToInt((2f - z) * 10f);

                // 交替分配上/下带
                float side = (made % 2 == 0) ? 1f : -1f;
                float y = bandY * side;
                float x = Random.Range(-roamW, roamW);
                go.transform.localPosition = new Vector3(x, y, z);
                go.transform.localScale = new Vector3(scale, scale, 1f);

                _walkers.Add(new Walker
                {
                    T = go.transform,
                    Angle = x,                                        // 复用字段存 x
                    AngSpeed = _walkerSpeed * Random.Range(0.35f, 0.75f) * (Random.value > 0.5f ? 1f : -1f),
                    Z = z,
                    Scale = scale,
                    Phase = Random.Range(0f, 6.28f),
                    HopAmp = Random.Range(0.05f, 0.14f),
                });
                made++;
            }
            Debug.Log("[Home3D] 异兽 " + made + " 只（上/下带 y=±" + bandY.ToString("0.0") + " 漫游半宽 " + roamW.ToString("0") + "）");
        }

        private void StepWalkers(float dt)
        {
            float bandY, roamW;
            ComputeBands(out bandY, out roamW);
            float t = Time.time;
            for (int i = 0; i < _walkers.Count; i++)
            {
                var w = _walkers[i];
                if (w.T == null) continue;

                // 阵风：风大时走得快些（有起伏，不是匀速）
                float gust = 1f + Mathf.Sin(t * 0.35f + w.Phase) * _wind * 0.55f;
                float x = w.Angle + w.AngSpeed * gust * dt;
                if (x > roamW) { x = roamW; w.AngSpeed = -Mathf.Abs(w.AngSpeed); }
                else if (x < -roamW) { x = -roamW; w.AngSpeed = Mathf.Abs(w.AngSpeed); }
                w.Angle = x;

                // 带内上下浮动（游荡感）+ 朝向
                float side = w.T.localPosition.y >= 0f ? 1f : -1f;
                float y = bandY * side + Mathf.Sin(t * 1.7f + w.Phase) * w.HopAmp;
                w.T.localPosition = new Vector3(x, y, w.Z);
                float face = w.AngSpeed >= 0f ? 1f : -1f;
                w.T.localScale = new Vector3(w.Scale * face, w.Scale, 1f);
                _walkers[i] = w;
            }
        }

        private void Update()
        {
            if (!Running) return;
            float dt = Time.deltaTime;
            // 阵风起伏
            _gustPhase += dt * 0.25f;
            float windNow = Mathf.Clamp01(_wind * (0.7f + 0.5f * (Mathf.Sin(_gustPhase) * 0.5f + 0.5f)));
            StepFog(dt);
            StepWalkers(dt);
            StepFlash(dt);
            // 风把雨/雪吹斜
            if (_psRain != null && _psRain.emission.enabled)
            {
                var shape = _psRain.shape;
                shape.rotation = new Vector3(0f, 0f, -_windDir * windNow * 34f);
            }
            if (_psFall != null)
            {
                var v = _psFall.velocityOverLifetime;
                v.enabled = true;
                v.space = ParticleSystemSimulationSpace.Local;
                v.x = new ParticleSystem.MinMaxCurve(_windDir * windNow * 1.6f);
            }
        }
    }
}