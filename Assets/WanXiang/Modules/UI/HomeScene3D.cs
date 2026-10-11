// ============================================================================
//  万相 · 主界面 3D 层（背景 + 自然动态物 + 天气氛围）
//  ---------------------------------------------------------------------------
//  ⛔⛔ Unity 渲染铁律（务必牢记）：
//     UIRoot 的 Canvas 是 ScreenSpaceOverlay（m_RenderMode=0），
//     **Overlay Canvas 不需要相机、且永远渲染在所有相机之上**。
//     ⇒ UI 层绝不能有全屏不透明图，否则 3D 全被盖住（第一版"看不见"的根因）。
//     ⇒ 相机 depth 只管"相机之间"的顺序，管不了 Overlay Canvas。
//     因此：背景由本层自己画；相机与内容根挂在 Canvas 之外。
//
//  ⛔⛔ 不要用 ParticleSystem（2026-10-11 洋红事故）：
//     本项目是 2D 内置管线，ParticleSystemRenderer 的默认材质取不到 ⇒
//     雨/雪全部渲染成品红色（magenta = 材质缺失色），用户截图整屏紫条纹。
//     ⇒ 一律用 **SpriteRenderer**（用 sprite 自带材质，零配置），
//       与战斗/节点地图的粒子做法保持一致。
//
//  本版内容（用户 2026-10-11 第三轮）：
//   · ❌ 去掉异兽行走（用户："看着太奇怪了"）
//   · ✅ 自然动态物：飞鸟（扇翅掠过）· 前景草丛/芦苇（随风摇曳）· 河流水波（横向流动）
//   · ✅ 天气：雨/雪（SpriteRenderer 池）· 闪电（全屏白闪双闪）· 风（阵风调制）
//   · ✅ 雾气：程序化无缝纹理，三层视差漂移
//   · ✅ 幕 + 进度双驱动（幕定基调、进度定强度）
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using WanXiang.Battle.Presentation;

namespace WanXiang.Modules.UI
{
    /// <summary>主界面 3D 层：背景 + 自然动态物 + 天气。相机与内容挂在 Canvas 之外。</summary>
    public sealed class HomeScene3D : MonoBehaviour
    {
        private bool _running;
        public bool Running
        {
            get { return _running; }
            set
            {
                _running = value;
                if (_cam != null) _cam.enabled = value;   // 相机已移出面板层级，必须显式停/启
            }
        }

        [Header("相机")]
        [SerializeField] private float _orthoSize = 5.4f;
        [SerializeField] private float _camZ = 10f;

        [Header("背景")]
        [SerializeField] private float _bgZ = 6f;

        [Header("自然元素数量（按幕会再乘系数）")]
        [SerializeField] private int _birdCount = 10;
        [SerializeField] private int _cloudCount = 8;
        [SerializeField] private int _butterflyCount = 6;
        [SerializeField] private int _groundCount = 12;
        [SerializeField] private int _rainCount = 150;
        [SerializeField] private int _snowCount = 90;
        [SerializeField] private int _fallCount = 60;
        [SerializeField] private float _globalScale = 1.25f;   // 统一放大（用户："动得明显一点"）
        [SerializeField] private float _globalSpeed = 1.45f;   // 统一加速

        [Header("氛围")]
        [SerializeField] private float _fogBase = 0.35f;
        [SerializeField] private float _windBase = 0.35f;

        private Camera _cam;
        private Transform _root;
        private SpriteRenderer _bgQuad;
        private int _curAct = -1;
        private float _progress;
        private bool _inited;

        // 氛围
        private readonly List<FogLayer> _fog = new List<FogLayer>();
        private SpriteRenderer _flash;
        private float _flashTimer, _flashLife;
        private float _wind = 0.35f, _windDir = 1f, _gustPhase;
        private float _fogDensity = 0.35f;
        private int _weatherKind;

        // 自然动态物
        private readonly List<Bird> _birds = new List<Bird>();
        private readonly List<Swayer> _swayers = new List<Swayer>();
        private readonly List<Faller> _rain = new List<Faller>();
        private readonly List<Faller> _snow = new List<Faller>();
        private readonly List<Faller> _fall = new List<Faller>();
        private readonly List<SpriteRenderer> _water = new List<SpriteRenderer>();
        private Sprite _fallSprite;

        private struct FogLayer { public SpriteRenderer Sr; public float Speed, Width, Alpha; }
        private struct Bird { public Transform T; public SpriteRenderer Sr; public float X, Y, Speed, FlapT; public int Frame; }
        private struct Swayer { public Transform T; public float Amp, Freq, Phase; }
        private struct Faller { public Transform T; public SpriteRenderer Sr; public float X, Y, Speed, SwayAmp, SwayFreq, Phase; }

        // 幕↔季节：1春 2夏 3秋 4冬 5长夏（⛔ 别按"春夏秋冬+长夏"直觉排）
        private static readonly string[] BgByAct =
        {
            "UI/Home/home_bg_act1_spring", "UI/Home/home_bg_act2_summer",
            "UI/Home/home_bg_act3_autumn", "UI/Home/home_bg_act4_winter",
            "UI/Home/home_bg_act5_latesummer",
        };
        private static readonly string[] FallByAct =
        {
            "Battle/FX_particle_act1_petal", "Battle/FX_particle_act2_firefly",
            "Battle/FX_particle_act3_leaf", "Battle/FX_particle_act4_snow",
            "Battle/FX_particle_act5_pollen",
        };

        private float HalfW { get { return _orthoSize * 16f / 9f; } }

        private void Awake() { Init(); }

        public void Init()
        {
            if (_inited) return;
            EnsureWorld();
            MoveOutsideCanvas();
            BuildNature();
            _inited = true;
        }

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
                _cam.depth = -10f;      // ⛔ 必须 < UICamera(99) 且为负
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

        public void InjectCatalog(SpriteCatalog cat) { }   // 保留接口（异兽已移除，兼容调用点）

        // ------------------------------------------------------------------
        //  幕 + 进度 → 氛围
        // ------------------------------------------------------------------
        public void ApplyAmbience(int act, float progress)
        {
            Init();
            act = Mathf.Clamp(act, 1, 5);
            _progress = Mathf.Clamp01(progress);

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
                    _bgQuad.transform.localScale = new Vector3(
                        size.x > 0.001f ? (HalfW * 2.08f) / size.x : 1f,
                        size.y > 0.001f ? (_orthoSize * 2.08f) / size.y : 1f, 1f);
                    _bgQuad.transform.localPosition = new Vector3(0f, 0f, _bgZ);
                }
            }

            float p = _progress;
            float baseWind, baseFog; int baseWeather;
            switch (act)
            {
                case 1: baseWind = 0.30f; baseFog = 0.32f; baseWeather = 0; break;  // 春：柔风薄雾
                case 2: baseWind = 0.42f; baseFog = 0.40f; baseWeather = 1; break;  // 夏：阵雨
                case 3: baseWind = 0.62f; baseFog = 0.36f; baseWeather = 0; break;  // 秋：大风
                case 4: baseWind = 0.48f; baseFog = 0.52f; baseWeather = 2; break;  // 冬：寒雾飞雪
                default: baseWind = 0.55f; baseFog = 0.60f; baseWeather = 4; break; // 长夏：低压雷雨
            }
            _wind = Mathf.Clamp01(baseWind + p * 0.45f);
            _fogDensity = Mathf.Clamp01(baseFog + p * 0.40f);
            _windDir = 1f;

            int weather = baseWeather;
            if (p > 0.72f)
            {
                if (act == 2) weather = 4;        // 夏末 → 雷雨
                else if (act == 3) weather = 1;   // 秋末 → 起雨
                else if (act == 4) weather = 2;   // 冬末 → 大雪
                else if (act == 1) weather = 3;   // 春末 → 转阴
            }
            SetWeather(weather);
            BuildFog();
            BuildEco();       // 云/鸟/蝴蝶/地面植被（按幕）
            BuildFallSprite();

            Debug.Log("[Home3D] 氛围 act=" + act + " 进度=" + p.ToString("0.00") +
                      " 风=" + _wind.ToString("0.00") + " 雾=" + _fogDensity.ToString("0.00") + " 天气=" + weather);
        }

        // ------------------------------------------------------------------
        //  自然动态物（按幕配置：云 / 鸟 / 蝴蝶 / 地面植被 / 水）
        //  ⛔ 用户 2026-10-11："花鸟云啊什么的都加上""动得明显一点""太少了太单一了"
        //     ⇒ 数量与尺寸整体放大(_globalScale)、速度加快(_globalSpeed)，
        //       且**每幕一套不同的生态**（不是只换张图）。
        // ------------------------------------------------------------------
        /// <summary>每幕的生态配置。</summary>
        private struct Eco
        {
            public int Clouds; public float CloudScale; public float CloudAlpha; public string CloudKind;
            public int Birds; public bool BirdFlock;
            public int Butterflies;
            public string Ground; public int GroundNum; public float GroundScale;
        }

        private static Eco EcoForAct(int act)
        {
            switch (act)
            {
                case 1: // 春：云淡、鸟多、蝴蝶飞舞、地面粉色花丛
                    return new Eco { Clouds = 9, CloudScale = 1.5f, CloudAlpha = 0.75f, CloudKind = "ab",
                                     Birds = 12, BirdFlock = false, Butterflies = 9,
                                     Ground = "home_flower_1", GroundNum = 10, GroundScale = 1.35f };
                case 2: // 夏：云厚偏低、蜻蜓蝴蝶、地面黄花 + 竹
                    return new Eco { Clouds = 13, CloudScale = 1.9f, CloudAlpha = 0.92f, CloudKind = "bc",
                                     Birds = 9, BirdFlock = false, Butterflies = 7,
                                     Ground = "home_flower_2", GroundNum = 12, GroundScale = 1.45f };
                case 3: // 秋：高云、大雁成群(V字)、芦苇
                    return new Eco { Clouds = 7, CloudScale = 1.3f, CloudAlpha = 0.68f, CloudKind = "ac",
                                     Birds = 14, BirdFlock = true, Butterflies = 0,
                                     Ground = "home_reeds", GroundNum = 12, GroundScale = 1.5f };
                case 4: // 冬：灰云厚重、鸟少、枯草
                    return new Eco { Clouds = 15, CloudScale = 2.1f, CloudAlpha = 0.95f, CloudKind = "abc",
                                     Birds = 4, BirdFlock = false, Butterflies = 0,
                                     Ground = "home_grass", GroundNum = 11, GroundScale = 1.3f };
                default: // 长夏：低压雷云、麦田
                    return new Eco { Clouds = 17, CloudScale = 2.3f, CloudAlpha = 0.98f, CloudKind = "abc",
                                     Birds = 7, BirdFlock = false, Butterflies = 0,
                                     Ground = "home_wheat", GroundNum = 13, GroundScale = 1.5f };
            }
        }

        private void BuildNature()
        {
            BuildWater();          // 水是常驻（不管哪一幕都有河）
        }

        /// <summary>河流水波：两块并排横向无缝循环滚动。</summary>
        private void BuildWater()
        {
            var sp = Resources.Load<Sprite>("UI/Home/home_water");
            if (sp == null) { Debug.LogWarning("[Home3D] 水波缺图 home_water"); return; }
            if (_water.Count > 0) return;

            float sw = sp.bounds.size.x;
            float targetW = HalfW * 2f * 1.02f;
            float sc = sw > 0.001f ? targetW / sw : 1f;
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("Water" + i);
                go.transform.SetParent(_root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                sr.sortingOrder = -120;                 // 草丛之后、雾之前
                sr.color = new Color(1f, 1f, 1f, 0.62f);
                // 高度也放大一点（用户："动得明显一点"）
                go.transform.localScale = new Vector3(sc, 1.5f * _globalScale, 1f);
                go.transform.localPosition = new Vector3(i * targetW, -_orthoSize * 0.52f, 2.0f);
                _water.Add(sr);
            }
            Debug.Log("[Home3D] 河流水波 2 块就绪");
        }

        /// <summary>按幕重建云/鸟/蝴蝶/地面植被。</summary>
        private void BuildEco()
        {
            var eco = EcoForAct(Mathf.Clamp(_curAct, 1, 5));
            BuildClouds(eco);
            BuildBirds(eco);
            BuildButterflies(eco);
            BuildGround(eco);
        }

        // ---------------- 云 ----------------
        private readonly List<Cloud> _clouds = new List<Cloud>();
        private struct Cloud { public Transform T; public SpriteRenderer Sr; public float Speed, X, Y; }

        private void BuildClouds(Eco eco)
        {
            for (int i = 0; i < _clouds.Count; i++) if (_clouds[i].T != null) Destroy(_clouds[i].T.gameObject);
            _clouds.Clear();

            // 三种云形轮换
            string[] kinds = { "home_cloud_a", "home_cloud_b", "home_cloud_c" };
            int n = Mathf.RoundToInt(eco.Clouds);
            for (int i = 0; i < n; i++)
            {
                string kn = kinds[i % kinds.Length];
                if (eco.CloudKind.IndexOf((char)('a' + (i % 3))) < 0) kn = kinds[(i + 1) % kinds.Length];
                var sp = Resources.Load<Sprite>("UI/Home/" + kn);
                if (sp == null) { Debug.LogWarning("[Home3D] 云缺图 " + kn); continue; }

                var go = new GameObject("Cloud" + i);
                go.transform.SetParent(_root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                float z = 4.4f - (i % 3) * 0.9f;               // 三层深度
                sr.sortingOrder = -700 + i % 3;
                sr.color = new Color(1f, 1f, 1f, eco.CloudAlpha * (0.72f + 0.28f * ((i % 3) / 2f)));
                float sc = eco.CloudScale * _globalScale * Random.Range(0.75f, 1.35f);
                go.transform.localScale = new Vector3(sc, sc * Random.Range(0.8f, 1.05f), 1f);
                float sw = sp.bounds.size.x * sc;
                float x = Random.Range(-HalfW - sw, HalfW + sw);
                float y = Random.Range(_orthoSize * 0.12f, _orthoSize * 0.95f);
                go.transform.localPosition = new Vector3(x, y, z);
                _clouds.Add(new Cloud
                {
                    T = go.transform, Sr = sr, X = x, Y = y,
                    Speed = (0.22f + (i % 3) * 0.14f) * _globalSpeed,
                });
            }
            Debug.Log("[Home3D] 云 " + _clouds.Count + " 朵");
        }

        private void StepClouds(float dt, float windNow)
        {
            for (int i = 0; i < _clouds.Count; i++)
            {
                var c = _clouds[i];
                if (c.T == null) continue;
                c.X += c.Speed * _windDir * (0.6f + windNow) * dt;
                float sw = c.Sr.sprite != null ? c.Sr.sprite.bounds.size.x * c.T.localScale.x : 4f;
                if (c.X > HalfW + sw) c.X = -HalfW - sw;        // 从右侧绕回左侧
                if (c.X < -HalfW - sw) c.X = HalfW + sw;
                c.T.localPosition = new Vector3(c.X, c.Y, c.T.localPosition.z);
                _clouds[i] = c;
            }
        }

        // ---------------- 鸟（含 V 字雁群）----------------
        private void BuildBirds(Eco eco)
        {
            for (int i = 0; i < _birds.Count; i++) if (_birds[i].T != null) Destroy(_birds[i].T.gameObject);
            _birds.Clear();
            var f0 = Resources.Load<Sprite>("UI/Home/home_bird");
            var f1 = Resources.Load<Sprite>("UI/Home/home_bird2");
            if (f0 == null || f1 == null) { Debug.LogWarning("[Home3D] 飞鸟缺图"); return; }

            int n = Mathf.RoundToInt(eco.Birds);
            bool flock = eco.BirdFlock;
            float flockDir = Random.value > 0.5f ? 1f : -1f;
            float flockY = Random.Range(_orthoSize * 0.42f, _orthoSize * 0.80f);
            float flockX = Random.Range(-HalfW * 0.6f, HalfW * 0.2f);
            float flockSpeed = Random.Range(1.1f, 1.6f) * _globalSpeed;

            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("Bird" + i);
                go.transform.SetParent(_root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = f0;
                float z = 3.4f + Random.Range(-0.6f, 0.8f);
                sr.sortingOrder = -500 + i % 5;
                float sc = Random.Range(0.40f, 0.85f) * _globalScale;
                go.transform.localScale = new Vector3(sc, sc, 1f);

                float x, y, spd;
                if (flock)
                {
                    // V 字雁群：以 flock 为队首，其余按 V 形偏移
                    int row = (i + 1) / 2;
                    float side = (i % 2 == 0) ? 1f : -1f;
                    x = flockX - flockDir * row * 0.95f;
                    y = flockY + side * row * 0.42f;
                    spd = flockSpeed;
                    if (flockDir < 0f) spd = -Mathf.Abs(spd); else spd = Mathf.Abs(spd);
                }
                else
                {
                    x = Random.Range(-HalfW * 1.1f, HalfW * 1.1f);
                    y = Random.Range(_orthoSize * 0.22f, _orthoSize * 0.92f);
                    spd = Random.Range(0.75f, 1.6f) * _globalSpeed * (Random.value > 0.25f ? 1f : -1f);
                }
                go.transform.localPosition = new Vector3(x, y, z);
                _birds.Add(new Bird
                {
                    T = go.transform, Sr = sr, X = x, Y = y, Speed = spd,
                    FlapT = Random.Range(0f, 0.3f), Frame = 0,
                });
            }
            Debug.Log("[Home3D] 飞鸟 " + _birds.Count + " 只" + (flock ? "(V字雁群)" : ""));
        }

        private void StepBirds(float dt)
        {
            for (int i = 0; i < _birds.Count; i++)
            {
                var b = _birds[i];
                if (b.T == null) continue;
                b.X += b.Speed * dt;
                float margin = HalfW * 1.25f;
                if (b.X > margin) b.X = -margin;
                if (b.X < -margin) b.X = margin;
                b.Y += Mathf.Sin(Time.time * 1.1f + i * 0.7f) * 0.18f * dt;
                b.T.localPosition = new Vector3(b.X, b.Y, b.T.localPosition.z);
                float face = b.Speed >= 0f ? 1f : -1f;
                var sc = b.T.localScale;
                b.T.localScale = new Vector3(Mathf.Abs(sc.x) * face, sc.y, sc.z);

                b.FlapT += dt;
                float iv = 0.19f;
                if (b.FlapT > iv)
                {
                    b.FlapT -= iv;
                    b.Frame = 1 - b.Frame;
                    b.Sr.sprite = Resources.Load<Sprite>(b.Frame == 0 ? "UI/Home/home_bird" : "UI/Home/home_bird2");
                }
                _birds[i] = b;
            }
        }

        // ---------------- 蝴蝶 ----------------
        private readonly List<Butterfly> _butterflies = new List<Butterfly>();
        private struct Butterfly { public Transform T; public SpriteRenderer Sr; public float X, Y, VX, FlapT, Phase; public int Frame; }

        private void BuildButterflies(Eco eco)
        {
            for (int i = 0; i < _butterflies.Count; i++) if (_butterflies[i].T != null) Destroy(_butterflies[i].T.gameObject);
            _butterflies.Clear();
            int n = Mathf.RoundToInt(eco.Butterflies);
            if (n <= 0) return;
            var a = Resources.Load<Sprite>("UI/Home/home_butterfly_a");
            var b = Resources.Load<Sprite>("UI/Home/home_butterfly_b");
            if (a == null || b == null) { Debug.LogWarning("[Home3D] 蝴蝶缺图"); return; }

            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("Butterfly" + i);
                go.transform.SetParent(_root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = a;
                sr.sortingOrder = -150 + i % 4;
                float sc = Random.Range(0.34f, 0.62f) * _globalScale;
                go.transform.localScale = new Vector3(sc, sc, 1f);
                float x = Random.Range(-HalfW * 0.9f, HalfW * 0.9f);
                float y = Random.Range(-_orthoSize * 0.55f, _orthoSize * 0.30f);   // 中下部（贴近地面/水面）
                go.transform.localPosition = new Vector3(x, y, 0.9f);
                _butterflies.Add(new Butterfly
                {
                    T = go.transform, Sr = sr, X = x, Y = y,
                    VX = Random.Range(0.35f, 0.85f) * _globalSpeed * (Random.value > 0.5f ? 1f : -1f),
                    FlapT = Random.Range(0f, 0.2f), Phase = Random.Range(0f, 6.28f), Frame = 0,
                });
            }
            Debug.Log("[Home3D] 蝴蝶 " + _butterflies.Count + " 只");
        }

        private void StepButterflies(float dt)
        {
            float t = Time.time;
            for (int i = 0; i < _butterflies.Count; i++)
            {
                var b = _butterflies[i];
                if (b.T == null) continue;
                // 飘忽飞行：横向匀速 + 纵向正弦 + 换向
                b.X += b.VX * dt;
                if (b.X > HalfW * 0.95f) { b.X = HalfW * 0.95f; b.VX = -Mathf.Abs(b.VX); }
                if (b.X < -HalfW * 0.95f) { b.X = -HalfW * 0.95f; b.VX = Mathf.Abs(b.VX); }
                float y = b.Y + Mathf.Sin(t * 1.6f + b.Phase) * 0.55f + Mathf.Sin(t * 3.1f + b.Phase * 2f) * 0.18f;
                b.T.localPosition = new Vector3(b.X, y, b.T.localPosition.z);
                float face = b.VX >= 0f ? 1f : -1f;
                var sc = b.T.localScale;
                b.T.localScale = new Vector3(Mathf.Abs(sc.x) * face, sc.y, sc.z);

                b.FlapT += dt;
                if (b.FlapT > 0.13f)
                {
                    b.FlapT -= 0.13f;
                    b.Frame = 1 - b.Frame;
                    b.Sr.sprite = Resources.Load<Sprite>(b.Frame == 0 ? "UI/Home/home_butterfly_a" : "UI/Home/home_butterfly_b");
                }
                _butterflies[i] = b;
            }
        }

        // ---------------- 地面植被 ----------------
        private void BuildGround(Eco eco)
        {
            for (int i = 0; i < _swayers.Count; i++) if (_swayers[i].T != null) Destroy(_swayers[i].T.gameObject);
            _swayers.Clear();
            var sp = Resources.Load<Sprite>("UI/Home/" + eco.Ground);
            if (sp == null) { Debug.LogWarning("[Home3D] 地面植被缺图 " + eco.Ground); return; }

            int n = Mathf.RoundToInt(eco.GroundNum);
            float bottom = -_orthoSize;
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("Ground" + i);
                go.transform.SetParent(_root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                float z = 2.6f - (i % 4) * 0.22f;          // 前后错落
                sr.sortingOrder = -200 + i % 4;
                float sc = eco.GroundScale * _globalScale * Random.Range(0.80f, 1.30f);
                go.transform.localScale = new Vector3(sc * (Random.value > 0.5f ? 1f : -1f), sc, 1f);
                float x = -HalfW + (i + 0.5f) * (HalfW * 2f / n) + Random.Range(-0.42f, 0.42f);
                float half = sp.bounds.size.y * sc * 0.5f;
                go.transform.localPosition = new Vector3(x, bottom + half * 0.42f, z);
                _swayers.Add(new Swayer
                {
                    T = go.transform,
                    Amp = Random.Range(2.2f, 5.5f),        // 摆幅放大（"动得明显一点"）
                    Freq = Random.Range(0.65f, 1.25f),
                    Phase = Random.Range(0f, 6.28f),
                });
            }
            Debug.Log("[Home3D] 地面植被 " + _swayers.Count + " 丛（" + eco.Ground + "）");
        }

        private void StepSwayers(float dt, float windNow)
        {
            float t = Time.time;
            for (int i = 0; i < _swayers.Count; i++)
            {
                var s = _swayers[i];
                if (s.T == null) continue;
                float ang = Mathf.Sin(t * s.Freq + s.Phase) * s.Amp * (0.45f + windNow);
                s.T.localRotation = Quaternion.Euler(0f, 0f, ang);
                _swayers[i] = s;
            }
        }

        // ------------------------------------------------------------------
        //  雾（程序化无缝纹理，三层视差）
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
                    // 整数频率正弦叠加 ⇒ u 方向天然无缝
                    float a = 0.5f;
                    a += Mathf.Sin(u * Mathf.PI * 2f + v * 3.1f) * 0.26f;
                    a += Mathf.Sin(u * Mathf.PI * 4f + v * 5.7f + 1.3f) * 0.16f;
                    a += Mathf.Sin(u * Mathf.PI * 6f + v * 2.3f + 2.1f) * 0.10f;
                    a = Mathf.Pow(Mathf.Clamp01(a), 1.7f) * Mathf.Sin(v * Mathf.PI);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            _fogSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            return _fogSprite;
        }

        private void BuildFog()
        {
            if (_fog.Count == 0)
            {
                AddFogLayer(4.6f, 0.055f, 0.30f);
                AddFogLayer(3.2f, 0.100f, 0.24f);
                AddFogLayer(1.9f, 0.170f, 0.18f);
            }
            for (int i = 0; i < _fog.Count; i++)
            {
                var f = _fog[i];
                if (f.Sr == null) continue;
                f.Sr.color = new Color(1f, 1f, 1f, f.Alpha * (0.5f + _fogDensity));
                _fog[i] = f;
            }
        }

        private void AddFogLayer(float z, float speed, float alpha)
        {
            var sp = FogSprite();
            float sw = sp.bounds.size.x;
            float blockW = HalfW * 2f / sw * 1.06f;
            var go = new GameObject("Fog_z" + z);
            go.transform.SetParent(_root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            sr.sortingOrder = -900 + Mathf.RoundToInt(-z * 10f);
            sr.color = new Color(1f, 1f, 1f, alpha);
            go.transform.localScale = new Vector3(blockW * 2f, 1.6f, 1f);
            go.transform.localPosition = new Vector3(0f, Random.Range(-0.6f, 1.4f), z);
            _fog.Add(new FogLayer { Sr = sr, Speed = speed, Width = sw * blockW * 2f, Alpha = alpha });
        }

        // ------------------------------------------------------------------
        //  天气（SpriteRenderer 池，⛔ 不用 ParticleSystem，避免洋红）
        // ------------------------------------------------------------------
        private Sprite _rainDot;

        private Sprite RainDot()
        {
            if (_rainDot != null) return _rainDot;
            // 细长竖条（雨丝）；带上下淡出
            const int W = 4, H = 32;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float v = (float)y / (H - 1);
                    float a = Mathf.Sin(v * Mathf.PI) * 0.85f;
                    float edge = 1f - Mathf.Abs(x - (W - 1) * 0.5f) / (W * 0.5f);
                    tex.SetPixel(x, y, new Color(0.80f, 0.87f, 0.96f, a * Mathf.Clamp01(edge + 0.3f)));
                }
            tex.Apply();
            _rainDot = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            return _rainDot;
        }

        private void BuildRainPool()
        {
            if (_rain.Count > 0) return;
            var sp = RainDot();
            for (int i = 0; i < _rainCount; i++)
            {
                var go = new GameObject("Rain" + i);
                go.transform.SetParent(_root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                sr.sortingOrder = 300;
                float len = Random.Range(0.8f, 1.8f);
                float wid = Random.Range(0.5f, 0.9f);
                go.transform.localScale = new Vector3(wid, len, 1f);
                float x = Random.Range(-HalfW * 1.1f, HalfW * 1.1f);
                float y = Random.Range(-_orthoSize, _orthoSize);
                go.transform.localPosition = new Vector3(x, y, 1.0f);
                _rain.Add(new Faller { T = go.transform, Sr = sr, X = x, Y = y, Speed = Random.Range(9f, 15f) });
            }
        }

        private void BuildSnowPool()
        {
            if (_snow.Count > 0) return;
            var sp = Resources.Load<Sprite>("Battle/FX_particle_act4_snow");
            if (sp == null) return;
            for (int i = 0; i < _snowCount; i++)
            {
                var go = new GameObject("Snow" + i);
                go.transform.SetParent(_root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                sr.sortingOrder = 300;
                float sc = Random.Range(0.10f, 0.26f);
                go.transform.localScale = new Vector3(sc, sc, 1f);
                float x = Random.Range(-HalfW, HalfW);
                float y = Random.Range(-_orthoSize, _orthoSize);
                go.transform.localPosition = new Vector3(x, y, 1.0f);
                _snow.Add(new Faller
                {
                    T = go.transform, Sr = sr, X = x, Y = y,
                    Speed = Random.Range(0.6f, 1.5f),
                    SwayAmp = Random.Range(0.15f, 0.5f),
                    SwayFreq = Random.Range(0.4f, 1.1f),
                    Phase = Random.Range(0f, 6.28f),
                });
            }
        }

        private void BuildFallSprite()
        {
            var sp = Resources.Load<Sprite>(FallByAct[Mathf.Clamp(_curAct, 1, 5) - 1]);
            if (sp == null || sp == _fallSprite) return;
            _fallSprite = sp;
            // 重建飘落池（换幕换图）
            for (int i = 0; i < _fall.Count; i++) if (_fall[i].T != null) Destroy(_fall[i].T.gameObject);
            _fall.Clear();
            for (int i = 0; i < _fallCount; i++)
            {
                var go = new GameObject("Fall" + i);
                go.transform.SetParent(_root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                sr.sortingOrder = 250;
                float sc = Random.Range(0.18f, 0.40f);
                go.transform.localScale = new Vector3(sc, sc, 1f);
                float x = Random.Range(-HalfW, HalfW);
                float y = Random.Range(-_orthoSize, _orthoSize);
                go.transform.localPosition = new Vector3(x, y, 1.2f);
                _fall.Add(new Faller
                {
                    T = go.transform, Sr = sr, X = x, Y = y,
                    Speed = Random.Range(0.5f, 1.2f),
                    SwayAmp = Random.Range(0.25f, 0.7f),
                    SwayFreq = Random.Range(0.5f, 1.2f),
                    Phase = Random.Range(0f, 6.28f),
                });
            }
        }

        public void SetWeather(int kind)
        {
            Init();
            _weatherKind = kind;
            bool rain = (kind == 1 || kind == 4);
            bool snow = (kind == 2);
            if (rain) BuildRainPool();
            if (snow) BuildSnowPool();
            for (int i = 0; i < _rain.Count; i++) if (_rain[i].T != null) _rain[i].T.gameObject.SetActive(rain);
            for (int i = 0; i < _snow.Count; i++) if (_snow[i].T != null) _snow[i].T.gameObject.SetActive(snow);

            ApplyWeatherTint();
            if (kind != 4) { _flashTimer = 0f; _flashLife = 0f; SetFlashAlpha(0f); }
            Debug.Log("[Home3D] 天气 kind=" + kind + (kind == 4 ? "（雷雨）" : ""));
        }

        private void ApplyWeatherTint()
        {
            float dim = _weatherKind == 4 ? 0.58f
                      : _weatherKind == 3 ? 0.68f
                      : _weatherKind == 1 ? 0.78f
                      : _weatherKind == 2 ? 0.92f : 1f;
            dim = Mathf.Clamp(dim - _progress * 0.10f, 0.35f, 1f);
            if (_bgQuad != null) _bgQuad.color = new Color(dim, dim, dim, 1f);
            if (_cam != null)
                _cam.backgroundColor = _weatherKind == 4 ? new Color(0.52f, 0.54f, 0.58f)
                                     : _weatherKind == 3 ? new Color(0.60f, 0.62f, 0.64f)
                                     : _weatherKind == 1 ? new Color(0.70f, 0.73f, 0.76f)
                                     : new Color(0.86f, 0.87f, 0.86f);
        }

        private void EnsureFlash()
        {
            if (_flash != null) return;
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

        private void SetFlashAlpha(float a) { if (_flash != null) _flash.color = new Color(1f, 1f, 1f, a); }

        // ------------------------------------------------------------------
        //  每帧推进
        // ------------------------------------------------------------------
        private void Update()
        {
            if (!Running) return;
            float dt = Time.deltaTime;
            _gustPhase += dt * 0.25f;
            float windNow = Mathf.Clamp01(_wind * (0.7f + 0.5f * (Mathf.Sin(_gustPhase) * 0.5f + 0.5f)));

            StepFog(dt, windNow);
            StepClouds(dt, windNow);
            StepBirds(dt);
            StepButterflies(dt);
            StepSwayers(dt, windNow);
            StepWater(dt, windNow);
            StepFallers(_rain, dt, windNow, true);
            StepFallers(_snow, dt, windNow, false);
            StepFallers(_fall, dt, windNow, false);
            StepFlash(dt);
        }

        private void StepFog(float dt, float windNow)
        {
            for (int i = 0; i < _fog.Count; i++)
            {
                var f = _fog[i];
                if (f.Sr == null) continue;
                var p = f.Sr.transform.localPosition;
                p.x += f.Speed * _windDir * windNow * dt * 6f;
                float half = f.Width * 0.5f;
                if (p.x > half) p.x -= f.Width;
                else if (p.x < -half) p.x += f.Width;
                f.Sr.transform.localPosition = p;
                _fog[i] = f;
            }
        }

        private void StepWater(float dt, float windNow)
        {
            if (_water.Count == 0) return;
            float speed = 0.25f + windNow * 0.9f;
            float total = 0f;
            foreach (var sr in _water) if (sr != null) total = Mathf.Max(total, sr.bounds.size.x);
            if (total <= 0.001f) return;
            for (int i = 0; i < _water.Count; i++)
            {
                var sr = _water[i];
                if (sr == null) continue;
                var p = sr.transform.localPosition;
                p.x -= speed * _windDir * dt;
                if (p.x < -total) p.x += total * 2f;
                if (p.x > total) p.x -= total * 2f;
                sr.transform.localPosition = p;
            }
        }

        private void StepFallers(List<Faller> list, float dt, float windNow, bool isRain)
        {
            float bottom = -_orthoSize - 0.8f, top = _orthoSize + 0.8f;
            float span = top - bottom;
            for (int i = 0; i < list.Count; i++)
            {
                var f = list[i];
                if (f.T == null || !f.T.gameObject.activeSelf) continue;
                // ⛔ 增量积分 + 回绕（别用 y = y0 - v*t）
                f.Y -= f.Speed * dt;
                if (f.Y < bottom) { f.Y += span; f.X = Random.Range(-HalfW, HalfW); }
                float x = f.X;
                if (isRain)
                {
                    // 雨丝随风整体倾斜
                    x += _windDir * windNow * 1.5f * (f.Y - bottom) * 0.12f;
                }
                else
                {
                    x += Mathf.Sin(Time.time * f.SwayFreq + f.Phase) * f.SwayAmp + _windDir * windNow * 0.6f;
                }
                f.T.localPosition = new Vector3(x, f.Y, f.T.localPosition.z);
                list[i] = f;
            }
        }

        private void StepFlash(float dt)
        {
            if (_weatherKind != 4) return;
            EnsureFlash();
            if (_flashLife > 0f)
            {
                _flashLife -= dt;
                float k = Mathf.Clamp01(_flashLife / 0.22f);
                SetFlashAlpha((Mathf.Sin(k * Mathf.PI * 3f) * 0.5f + 0.5f) * 0.5f * k);
                if (_flashLife <= 0f) SetFlashAlpha(0f);
                return;
            }
            _flashTimer -= dt;
            if (_flashTimer <= 0f) { _flashLife = 0.22f; _flashTimer = Random.Range(3.5f, 9f); }
        }
    }
}