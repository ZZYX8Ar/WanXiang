// ============================================================================
//  万相 · 主界面氛围层（UGUI 版）·  2026-10-11 第四版
//  ---------------------------------------------------------------------------
//  ⛔ 为什么从 3D 改成 UGUI（用户 2026-10-11："元素应该显示在中央地图的前面，
//     不能被它挡住"）：
//     UIRoot 的 Canvas 是 ScreenSpaceOverlay，**Overlay Canvas 永远渲染在所有
//     相机之上** ， 3D 相机里的内容（云/鸟/花草）永远在地图**后面**，
//     想显示在地图前面是**不可能**的。要"在地图前面"就必须待在 UI 层里。
//
//  ✅ 结构：Panel_Home 下两个铺满层，靠 siblingIndex 决定前后关系
//       Root_HomeBack   （紧随 Img_Plate，在地图**之前**）→ 背景图 + 雾
//       Root_HomeNature （最后一个，在地图**之后**）→ 云/鸟/蝴蝶/植被/水/雨雪/闪电
//     ， 背景在地图后面、动态元素在地图前面，两边都能看见。
//
//  ⛔ 本项目禁用 ParticleSystem（内置管线材质缺失 ， 渲染成洋红）——
//     全部用 Image（本文件）/ SpriteRenderer（战斗侧用）。
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.Modules.UI
{
    /// <summary>主界面氛围层（UGUI）：背景 + 雾 + 云/鸟/蝴蝶/植被/水 + 天气。</summary>
    public sealed class HomeScene3D : MonoBehaviour
    {
        // ⚠ 类名保留 HomeScene3D（历史名，prefab 引用不变），实现已改为 UGUI，不再用 3D 相机。

        private bool _running;
        public bool Running { get { return _running; } set { _running = value; } }

        [Header("整体缩放 / 速度（Inspector 可调）")]
        [SerializeField] private float _globalScale = 1.15f;
        [SerializeField] private float _globalSpeed = 1.0f;

        [Header("数量")]
        [SerializeField] private int _rainCount = 120;
        [SerializeField] private int _snowCount = 55;
        [SerializeField] private int _fallCount = 32;

        private RectTransform _backRoot;     // 地图之后（背景 / 雾）
        private RectTransform _natureRoot;   // 地图之前（云 / 鸟 / 草 / 水 …）
        private Image _bg;
        private RectTransform _canvasRt;

        private int _curAct = -1;
        private float _progress;
        private float _wind = 0.35f, _windDir = 1f, _gustPhase;
        private float _fogDensity = 0.35f;
        private int _weatherKind;
        private bool _inited;

        private float W { get { return _canvasRt != null && _canvasRt.rect.width > 1f ? _canvasRt.rect.width : 1280f; } }
        private float H { get { return _canvasRt != null && _canvasRt.rect.height > 1f ? _canvasRt.rect.height : 720f; } }
        private float HalfW { get { return W * 0.5f; } }
        private float HalfH { get { return H * 0.5f; } }

        // 元素池
        private readonly List<FogLayer> _fog = new List<FogLayer>();
        private readonly List<Cloud> _clouds = new List<Cloud>();
        private readonly List<Flyer> _birds = new List<Flyer>();
        private readonly List<Flyer> _butterflies = new List<Flyer>();
        private readonly List<Swayer> _swayers = new List<Swayer>();
        private readonly List<Faller> _rain = new List<Faller>();
        private readonly List<Faller> _snow = new List<Faller>();
        private readonly List<Faller> _fall = new List<Faller>();
        private readonly List<Image> _water = new List<Image>();
        private Image _flash;
        private float _flashTimer, _flashLife;
        private Sprite _fallSprite;

        private struct FogLayer { public Image Img; public float Speed, Width, Alpha; }
        private struct Cloud { public RectTransform T; public Image Img; public float Speed, X, Y, Width; }
        private struct Flyer { public RectTransform T; public Image Img; public float X, Y, VX, FlapT, Phase; public int Frame; }
        private struct Swayer { public RectTransform T; public float Amp, Freq, Phase; }
        private struct Faller { public RectTransform T; public Image Img; public float X, Y, Speed, SwayAmp, SwayFreq, Phase; }

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

        private void Awake() { Init(); }

        public void Init()
        {
            if (_inited) return;
            var cv = GetComponentInParent<Canvas>();
            _canvasRt = cv != null ? cv.transform as RectTransform : null;
            // 背景层：紧跟 Img_Plate → 在地图**之前**（更靠后渲染 = 被地图盖住，符合"背景"语义）
            _backRoot = EnsureLayer("Root_HomeBack", 1);
            // 氛围层：插在 Root_Hero（中央地图）**之后**、底部按钮**之前**
            //   ， 元素盖住地图（用户要的"显示在地图前面"），但不会挡按钮。
            int heroIdx = -1;
            var hero = transform.Find("Root_Hero");
            if (hero != null) heroIdx = hero.GetSiblingIndex();
            _natureRoot = EnsureLayer("Root_HomeNature", heroIdx >= 0 ? heroIdx + 1 : int.MaxValue);
            _inited = true;
        }

        /// <summary>找不到才建；已存在则**不动它的层级**（保持 prefab 里手工调好的顺序）。</summary>
        private RectTransform EnsureLayer(string name, int sibling)
        {
            var t = transform.Find(name) as RectTransform;
            if (t != null) return t;
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            t = (RectTransform)go.transform;
            t.anchorMin = Vector2.zero; t.anchorMax = Vector2.one;
            t.offsetMin = Vector2.zero; t.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);
            img.raycastTarget = false;
            if (sibling == int.MaxValue) t.SetAsLastSibling();
            else t.SetSiblingIndex(Mathf.Clamp(sibling, 0, transform.childCount - 1));
            return t;
        }

        /// <summary>按幕 + 进度刷新整套氛围。</summary>
        public void ApplyAmbience(int act, float progress)
        {
            Init();
            act = Mathf.Clamp(act, 1, 5);
            _progress = Mathf.Clamp01(progress);

            if (act != _curAct || _bg == null || _bg.sprite == null)
            {
                _curAct = act;
                var sp = Resources.Load<Sprite>(BgByAct[act - 1]);
                if (sp == null) Debug.LogWarning("[Home] 背景缺图：" + BgByAct[act - 1]);
                else
                {
                    if (_bg == null) _bg = MakeImage(_backRoot, "HomeBg", 0);
                    _bg.sprite = sp;
                    _bg.preserveAspect = false;
                    var rt = _bg.rectTransform;
                    rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                }
            }

            float p = _progress;
            float baseWind, baseFog; int baseWeather;
            switch (act)
            {
                case 1: baseWind = 0.30f; baseFog = 0.32f; baseWeather = 0; break;
                case 2: baseWind = 0.42f; baseFog = 0.40f; baseWeather = 1; break;
                case 3: baseWind = 0.62f; baseFog = 0.36f; baseWeather = 0; break;
                case 4: baseWind = 0.48f; baseFog = 0.52f; baseWeather = 2; break;
                default: baseWind = 0.55f; baseFog = 0.60f; baseWeather = 4; break;
            }
            _wind = Mathf.Clamp01(baseWind + p * 0.45f);
            _fogDensity = Mathf.Clamp01(baseFog + p * 0.40f);
            _windDir = 1f;

            int weather = baseWeather;
            if (p > 0.72f)
            {
                if (act == 2) weather = 4;
                else if (act == 3) weather = 1;
                else if (act == 4) weather = 2;
                else if (act == 1) weather = 3;
            }
            SetWeather(weather);
            BuildFog();
            BuildEco();
            BuildFallSprite();
            BuildWater();
            ApplyTint();

            Debug.Log("[Home] 氛围 act=" + act + " 进度=" + p.ToString("0.00") +
                      " 风=" + _wind.ToString("0.00") + " 雾=" + _fogDensity.ToString("0.00") + " 天气=" + weather);
        }

        // ------------------------------------------------------------------ 工具
        private static Image MakeImage(RectTransform parent, string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;          // 氛围层绝不能吃点击
            img.preserveAspect = true;
            go.transform.SetSiblingIndex(order);
            return img;
        }

        private static void SetSize(Image img, float w, float h)
        {
            img.rectTransform.sizeDelta = new Vector2(w, h);
        }

        /// <summary>按 sprite 原始像素 × 系数 设尺寸（清晰且可控）。</summary>
        private static void SetSizeBySprite(Image img, float scale)
        {
            if (img.sprite == null) return;
            var r = img.sprite.rect;
            img.rectTransform.sizeDelta = new Vector2(r.width * scale, r.height * scale);
        }

        // ------------------------------------------------------------------ 每幕生态
        private struct Eco
        {
            public int Clouds; public float CloudScale; public float CloudAlpha;
            public int Birds; public bool BirdFlock;
            public int Butterflies;
            public string Ground; public int GroundNum; public float GroundScale;
            public string Ground2;   // 第二种植被（交替混排，免得一片全是同一个图）
        }

        private static Eco EcoForAct(int act)
        {
            switch (act)
            {
                // ⚠⚠ 云尺寸两次调小的记录，别再放大：
                //   初版 0.85~1.25（用户"云和鸟有点大了"）→ 0.50~0.72 → 现在 **0.22~0.30**。
                //   用户："你这个云怎么还变大了，第五幕的太大了" —— 长夏原本 0.72 最大是主因。
                //   现在**长夏不再最大**，各幕只靠"数量/透明度"区分云的季节形态：
                //     春·淡云疏 / 夏·积云厚白 / 秋·高云疏淡 / 冬·阴云灰密 / 长夏·低云压顶
                // ⚠ 2026-10-13 用户"近景都一个样"：植被两两混排（Ground+Ground2 交替）+ 数量/尺寸调小，
                //   别再一整排 13 丛同一个图顶到半屏。
                case 1: return new Eco { Clouds = 10, CloudScale = 0.26f, CloudAlpha = 0.62f, Birds = 14, BirdFlock = false, Butterflies = 10,
                                         Ground = "home_flower_1", Ground2 = "home_grass", GroundNum = 8, GroundScale = 0.82f };
                case 2: return new Eco { Clouds = 12, CloudScale = 0.30f, CloudAlpha = 0.86f, Birds = 10, BirdFlock = false, Butterflies = 8,
                                         Ground = "home_flower_2", Ground2 = "home_flower_1", GroundNum = 8, GroundScale = 0.85f };
                case 3: return new Eco { Clouds = 7,  CloudScale = 0.22f, CloudAlpha = 0.52f, Birds = 16, BirdFlock = true, Butterflies = 0,
                                         Ground = "home_reeds", Ground2 = "home_grass", GroundNum = 8, GroundScale = 0.78f };
                case 4: return new Eco { Clouds = 14, CloudScale = 0.28f, CloudAlpha = 0.90f, Birds = 5, BirdFlock = false, Butterflies = 0,
                                         Ground = "home_grass", Ground2 = "home_flower_2", GroundNum = 8, GroundScale = 0.82f };
                default: return new Eco { Clouds = 11, CloudScale = 0.26f, CloudAlpha = 0.80f, Birds = 8, BirdFlock = false, Butterflies = 0,
                                          Ground = "home_wheat", Ground2 = "home_reeds", GroundNum = 9, GroundScale = 0.82f };
            }
        }

        private void BuildEco()
        {
            var eco = EcoForAct(Mathf.Clamp(_curAct, 1, 5));
            BuildClouds(eco);
            BuildBirds(eco);
            BuildButterflies(eco);
            BuildGround(eco);
        }

        private void DestroyNodes<T>(List<T> list, System.Func<T, RectTransform> get)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var t = get(list[i]);
                if (t != null) Destroy(t.gameObject);
            }
            list.Clear();
        }
        private RectTransform RToC(Cloud c) { return c.T; }
        private RectTransform RToF(Flyer f) { return f.T; }
        private RectTransform RToS(Swayer s) { return s.T; }

        // ---------------- 云 ----------------
        private void BuildClouds(Eco eco)
        {
            DestroyNodes<Cloud>(_clouds, RToC);
            string[] kinds = { "home_cloud_a", "home_cloud_b", "home_cloud_c" };
            for (int i = 0; i < eco.Clouds; i++)
            {
                var sp = Resources.Load<Sprite>("UI/Home/" + kinds[i % 3]);
                if (sp == null) { Debug.LogWarning("[Home] 云缺图"); break; }
                var img = MakeImage(_natureRoot, "Cloud" + i, i);
                img.sprite = sp;
                SetSizeBySprite(img, eco.CloudScale * _globalScale * Random.Range(0.8f, 1.3f));
                img.color = new Color(1f, 1f, 1f, eco.CloudAlpha * (0.72f + 0.28f * Random.value));
                float w = img.rectTransform.sizeDelta.x;
                float x = Random.Range(-HalfW - w * 0.5f, HalfW + w * 0.5f);
                float y = Random.Range(-HalfH * 0.10f, HalfH * 0.88f);
                img.rectTransform.anchoredPosition = new Vector2(x, y);
                _clouds.Add(new Cloud { T = img.rectTransform, Img = img, X = x, Y = y,
                                        Speed = (140f + (i % 3) * 90f) * _globalSpeed, Width = w });
            }
            Debug.Log("[Home] 云 " + _clouds.Count + " 朵");
        }

        // ---------------- 鸟 / 蝴蝶 ----------------
        private void BuildBirds(Eco eco)
        {
            DestroyNodes<Flyer>(_birds, RToF);
            var f0 = Resources.Load<Sprite>("UI/Home/home_bird");
            var f1 = Resources.Load<Sprite>("UI/Home/home_bird2");
            if (f0 == null || f1 == null) { Debug.LogWarning("[Home] 飞鸟缺图"); return; }

            int n = eco.Birds;
            bool flock = eco.BirdFlock;
            float flockDir = Random.value > 0.5f ? 1f : -1f;
            float flockY = Random.Range(HalfH * 0.30f, HalfH * 0.82f);
            float flockX = Random.Range(-HalfW * 0.55f, HalfW * 0.15f);
            float flockSpeed = Random.Range(120f, 170f) * _globalSpeed;

            for (int i = 0; i < n; i++)
            {
                var img = MakeImage(_natureRoot, "Bird" + i, i);
                img.sprite = f0;
                // ⚠ 鸟尺寸 2026-10-11 调小（用户："云和鸟有点大了"）：78~150 → 44~82
                float sz = Random.Range(44f, 82f) * _globalScale;
                SetSize(img, sz, sz);
                float x, y, vx;
                if (flock)
                {
                    int row = (i + 1) / 2;
                    float side = (i % 2 == 0) ? 1f : -1f;
                    x = flockX - flockDir * row * 46f;
                    y = flockY + side * row * 22f;
                    vx = flockDir * flockSpeed;
                }
                else
                {
                    x = Random.Range(-HalfW * 1.1f, HalfW * 1.1f);
                    y = Random.Range(HalfH * 0.20f, HalfH * 0.92f);
                    vx = Random.Range(85f, 185f) * _globalSpeed * (Random.value > 0.25f ? 1f : -1f);
                }
                img.rectTransform.anchoredPosition = new Vector2(x, y);
                _birds.Add(new Flyer { T = img.rectTransform, Img = img, X = x, Y = y, VX = vx,
                                       FlapT = Random.Range(0f, 0.3f), Phase = Random.Range(0f, 6.28f), Frame = 0 });
            }
            Debug.Log("[Home] 飞鸟 " + _birds.Count + " 只" + (flock ? "(V字雁群)" : ""));
        }

        private void BuildButterflies(Eco eco)
        {
            DestroyNodes<Flyer>(_butterflies, RToF);
            if (eco.Butterflies <= 0) return;
            var a = Resources.Load<Sprite>("UI/Home/home_butterfly_a");
            var b = Resources.Load<Sprite>("UI/Home/home_butterfly_b");
            if (a == null || b == null) { Debug.LogWarning("[Home] 蝴蝶缺图"); return; }

            for (int i = 0; i < eco.Butterflies; i++)
            {
                var img = MakeImage(_natureRoot, "Butterfly" + i, i);
                img.sprite = a;
                float sz = Random.Range(38f, 66f) * _globalScale;
                SetSize(img, sz, sz);
                float x = Random.Range(-HalfW * 0.9f, HalfW * 0.9f);
                float y = Random.Range(-HalfH * 0.62f, HalfH * 0.18f);
                img.rectTransform.anchoredPosition = new Vector2(x, y);
                _butterflies.Add(new Flyer { T = img.rectTransform, Img = img, X = x, Y = y,
                                             VX = Random.Range(50f, 115f) * _globalSpeed * (Random.value > 0.5f ? 1f : -1f),
                                             FlapT = Random.Range(0f, 0.2f), Phase = Random.Range(0f, 6.28f), Frame = 0 });
            }
            Debug.Log("[Home] 蝴蝶 " + _butterflies.Count + " 只");
        }

        // ---------------- 地面植被 ----------------
        private void BuildGround(Eco eco)
        {
            DestroyNodes<Swayer>(_swayers, RToS);
            var sp  = Resources.Load<Sprite>("UI/Home/" + eco.Ground);
            var sp2 = string.IsNullOrEmpty(eco.Ground2) ? null : Resources.Load<Sprite>("UI/Home/" + eco.Ground2);
            if (sp == null) { Debug.LogWarning("[Home] 地面植被缺图 " + eco.Ground); return; }

            for (int i = 0; i < eco.GroundNum; i++)
            {
                var img = MakeImage(_natureRoot, "Ground" + i, i);
                // 两两混排：偶数丛用主植被、奇数丛用第二植被（缺图就退回主植被）
                img.sprite = (i % 2 == 1 && sp2 != null) ? sp2 : sp;
                SetSizeBySprite(img, eco.GroundScale * _globalScale * Random.Range(0.85f, 1.35f));
                float hh = img.rectTransform.sizeDelta.y;
                float x = -HalfW + (i + 0.5f) * (W / eco.GroundNum) + Random.Range(-30f, 30f);
                float y = -HalfH + hh * 0.38f;      // 贴屏底，只露上半
                img.rectTransform.anchoredPosition = new Vector2(x, y);
                _swayers.Add(new Swayer { T = img.rectTransform,
                                          Amp = Random.Range(2.4f, 6.0f), Freq = Random.Range(0.65f, 1.25f), Phase = Random.Range(0f, 6.28f) });
            }
            Debug.Log("[Home] 地面植被 " + _swayers.Count + " 丛（" + eco.Ground + " + " + (sp2 != null ? eco.Ground2 : "-") + "）");
        }

        // ---------------- 水（常驻） ----------------
        private void BuildWater()
        {
            if (_water.Count > 0) return;
            var sp = Resources.Load<Sprite>("UI/Home/home_water");
            if (sp == null) { Debug.LogWarning("[Home] 水波缺图"); return; }
            float targetW = W * 1.02f;
            for (int i = 0; i < 2; i++)
            {
                var img = MakeImage(_natureRoot, "Water" + i, i);
                img.sprite = sp;
                img.color = new Color(1f, 1f, 1f, 0.62f);
                SetSize(img, targetW, 110f * _globalScale);
                img.rectTransform.anchoredPosition = new Vector2(i * targetW, -HalfH * 0.52f);
                _water.Add(img);
            }
            Debug.Log("[Home] 河流水波 2 块就绪");
        }

        // ------------------------------------------------------------------ 雾
        private static Sprite _fogSprite;

        private static Sprite FogSprite()
        {
            if (_fogSprite != null) return _fogSprite;
            const int TW = 256, TH = 128;
            var tex = new Texture2D(TW, TH, TextureFormat.RGBA32, false);
            for (int y = 0; y < TH; y++)
                for (int x = 0; x < TW; x++)
                {
                    float u = (float)x / TW, v = (float)y / TH;
                    float a = 0.5f;
                    a += Mathf.Sin(u * Mathf.PI * 2f + v * 3.1f) * 0.26f;
                    a += Mathf.Sin(u * Mathf.PI * 4f + v * 5.7f + 1.3f) * 0.16f;
                    a += Mathf.Sin(u * Mathf.PI * 6f + v * 2.3f + 2.1f) * 0.10f;
                    a = Mathf.Pow(Mathf.Clamp01(a), 1.7f) * Mathf.Sin(v * Mathf.PI);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            _fogSprite = Sprite.Create(tex, new Rect(0, 0, TW, TH), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            return _fogSprite;
        }

        private void BuildFog()
        {
            if (_fog.Count == 0)
            {
                AddFogLayer(0.30f, 55f);
                AddFogLayer(0.24f, 100f);
                AddFogLayer(0.18f, 165f);
            }
            for (int i = 0; i < _fog.Count; i++)
            {
                var f = _fog[i];
                if (f.Img == null) continue;
                f.Img.color = new Color(1f, 1f, 1f, f.Alpha * (0.5f + _fogDensity));
                _fog[i] = f;
            }
        }

        private void AddFogLayer(float alpha, float speed)
        {
            float w = W * 2.2f;
            var img = MakeImage(_backRoot, "Fog" + _fog.Count, 10 + _fog.Count);
            img.sprite = FogSprite();
            img.color = new Color(1f, 1f, 1f, alpha);
            SetSize(img, w, 300f);
            img.rectTransform.anchoredPosition = new Vector2(0f, Random.Range(-60f, 150f));
            _fog.Add(new FogLayer { Img = img, Speed = speed * _globalSpeed, Width = w, Alpha = alpha });
        }

        // ------------------------------------------------------------------ 天气
        private Sprite _rainDot;

        private Sprite RainDot()
        {
            if (_rainDot != null) return _rainDot;
            const int TW = 4, TH = 32;
            var tex = new Texture2D(TW, TH, TextureFormat.RGBA32, false);
            for (int y = 0; y < TH; y++)
                for (int x = 0; x < TW; x++)
                {
                    float v = (float)y / (TH - 1);
                    tex.SetPixel(x, y, new Color(0.80f, 0.87f, 0.96f, Mathf.Sin(v * Mathf.PI) * 0.85f));
                }
            tex.Apply();
            _rainDot = Sprite.Create(tex, new Rect(0, 0, TW, TH), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            return _rainDot;
        }

        private void BuildRainPool()
        {
            if (_rain.Count > 0) return;
            var sp = RainDot();
            for (int i = 0; i < _rainCount; i++)
            {
                var img = MakeImage(_natureRoot, "Rain" + i, 500 + i);
                img.sprite = sp;
                img.color = new Color(1f, 1f, 1f, 0.75f);
                SetSize(img, Random.Range(3f, 6f), Random.Range(100f, 250f));
                float x = Random.Range(-HalfW * 1.15f, HalfW * 1.15f);
                float y = Random.Range(-HalfH, HalfH);
                img.rectTransform.anchoredPosition = new Vector2(x, y);
                _rain.Add(new Faller { T = img.rectTransform, Img = img, X = x, Y = y, Speed = Random.Range(900f, 1500f) });
            }
        }

        private void BuildSnowPool()
        {
            if (_snow.Count > 0) return;
            var sp = Resources.Load<Sprite>("Battle/FX_particle_act4_snow");
            if (sp == null) return;
            for (int i = 0; i < _snowCount; i++)
            {
                var img = MakeImage(_natureRoot, "Snow" + i, 500 + i);
                img.sprite = sp;
                // ⚠ 同步调小调淡（原 16~40 / 不透明）
                float sz = Random.Range(10f, 24f) * _globalScale;
                SetSize(img, sz, sz);
                img.color = new Color(1f, 1f, 1f, 0.72f);
                float x = Random.Range(-HalfW, HalfW);
                float y = Random.Range(-HalfH, HalfH);
                img.rectTransform.anchoredPosition = new Vector2(x, y);
                _snow.Add(new Faller { T = img.rectTransform, Img = img, X = x, Y = y,
                                       Speed = Random.Range(45f, 120f) * _globalSpeed,
                                       SwayAmp = Random.Range(12f, 40f), SwayFreq = Random.Range(0.4f, 1.1f), Phase = Random.Range(0f, 6.28f) });
            }
        }

        private void BuildFallSprite()
        {
            var sp = Resources.Load<Sprite>(FallByAct[Mathf.Clamp(_curAct, 1, 5) - 1]);
            if (sp == null || sp == _fallSprite) return;
            _fallSprite = sp;
            for (int i = 0; i < _fall.Count; i++) if (_fall[i].T != null) Destroy(_fall[i].T.gameObject);
            _fall.Clear();
            for (int i = 0; i < _fallCount; i++)
            {
                var img = MakeImage(_natureRoot, "Fall" + i, 400 + i);
                img.sprite = sp;
                // ⚠ 2026-10-11 用户："落叶和桃花雪这些粒子有点多、有点大、太显眼"
                //   尺寸 28~64 → 16~36，并整体压到半透明（0.62）
                float sz = Random.Range(16f, 36f) * _globalScale;
                SetSize(img, sz, sz);
                img.color = new Color(1f, 1f, 1f, 0.62f);
                float x = Random.Range(-HalfW, HalfW);
                float y = Random.Range(-HalfH, HalfH);
                img.rectTransform.anchoredPosition = new Vector2(x, y);
                _fall.Add(new Faller { T = img.rectTransform, Img = img, X = x, Y = y,
                                       Speed = Random.Range(40f, 95f) * _globalSpeed,
                                       SwayAmp = Random.Range(18f, 55f), SwayFreq = Random.Range(0.5f, 1.2f), Phase = Random.Range(0f, 6.28f) });
            }
        }

        public void SetWeather(int kind)
        {
            Init();
            _weatherKind = kind;
            bool rain = (kind == 1 || kind == 4), snow = (kind == 2);
            if (rain) BuildRainPool();
            if (snow) BuildSnowPool();
            for (int i = 0; i < _rain.Count; i++) if (_rain[i].T != null) _rain[i].T.gameObject.SetActive(rain);
            for (int i = 0; i < _snow.Count; i++) if (_snow[i].T != null) _snow[i].T.gameObject.SetActive(snow);
            ApplyTint();
            if (kind != 4) { _flashTimer = 0f; _flashLife = 0f; SetFlashAlpha(0f); }
            Debug.Log("[Home] 天气 kind=" + kind + (kind == 4 ? "（雷雨）" : ""));
        }

        private void ApplyTint()
        {
            float dim = _weatherKind == 4 ? 0.62f
                      : _weatherKind == 3 ? 0.72f
                      : _weatherKind == 1 ? 0.82f
                      : _weatherKind == 2 ? 0.94f : 1f;
            dim = Mathf.Clamp(dim - _progress * 0.10f, 0.42f, 1f);
            if (_bg != null) _bg.color = new Color(dim, dim, dim, 1f);
        }

        private void EnsureFlash()
        {
            if (_flash != null) return;
            _flash = MakeImage(_natureRoot, "Flash", 900);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            for (int i = 0; i < 4; i++) tex.SetPixel(i % 2, i / 2, Color.white);
            tex.Apply();
            _flash.sprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            // ⛔ MakeImage 默认 preserveAspect=true，而闪电是 2x2 白纹理 + 全屏矩形
            //   ， 保持 1:1 后不铺满，而是缩成一个"居中的白色小方块"
            //   （用户 2026-10-12："主界面突然闪一下，看到一个白色的小面板"）。
            _flash.preserveAspect = false;
            _flash.type = Image.Type.Simple;
            var rt = _flash.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            _flash.color = new Color(1f, 1f, 1f, 0f);
        }

        private void SetFlashAlpha(float a) { if (_flash != null) _flash.color = new Color(1f, 1f, 1f, a); }

        // ------------------------------------------------------------------ 推进
        private void Update()
        {
            if (!Running) return;
            float dt = Time.deltaTime;
            _gustPhase += dt * 0.25f;
            float windNow = Mathf.Clamp01(_wind * (0.7f + 0.5f * (Mathf.Sin(_gustPhase) * 0.5f + 0.5f)));

            StepFog(dt, windNow);
            StepClouds(dt, windNow);
            StepFlyers(_birds, dt, true);
            StepFlyers(_butterflies, dt, false);
            StepSwayers(dt, windNow);
            StepWater(dt, windNow);
            StepFallers(_rain, dt, windNow, true);
            StepFallers(_snow, dt, windNow, false);
            StepFallers(_fall, dt, windNow, false);
            StepFlash(dt);
        }

        private void StepFog(float dt, float windNow)
        {
            float span = W * 2.2f;
            for (int i = 0; i < _fog.Count; i++)
            {
                var f = _fog[i];
                if (f.Img == null) continue;
                var p = f.Img.rectTransform.anchoredPosition;
                p.x += f.Speed * _windDir * windNow * dt;
                if (p.x > span) p.x -= span * 2f;
                if (p.x < -span) p.x += span * 2f;
                f.Img.rectTransform.anchoredPosition = p;
                _fog[i] = f;
            }
        }

        private void StepClouds(float dt, float windNow)
        {
            for (int i = 0; i < _clouds.Count; i++)
            {
                var c = _clouds[i];
                if (c.T == null) continue;
                c.X += c.Speed * _windDir * (0.6f + windNow) * dt;
                float lim = HalfW + c.Width * 0.6f;
                if (c.X > lim) c.X = -lim;
                if (c.X < -lim) c.X = lim;
                c.T.anchoredPosition = new Vector2(c.X, c.Y);
                _clouds[i] = c;
            }
        }

        private void StepFlyers(List<Flyer> list, float dt, bool isBird)
        {
            float t = Time.time;
            for (int i = 0; i < list.Count; i++)
            {
                var f = list[i];
                if (f.T == null) continue;
                f.X += f.VX * dt;
                float lim = HalfW * 1.2f;
                if (f.X > lim) f.X = -lim;
                if (f.X < -lim) f.X = lim;
                float y = f.Y + (isBird
                    ? Mathf.Sin(t * 1.1f + f.Phase) * 10f
                    : Mathf.Sin(t * 1.6f + f.Phase) * 38f + Mathf.Sin(t * 3.2f + f.Phase * 2f) * 14f);
                f.T.anchoredPosition = new Vector2(f.X, y);
                f.T.localScale = new Vector3(f.VX >= 0f ? 1f : -1f, 1f, 1f);   // 朝向

                f.FlapT += dt;
                float iv = isBird ? 0.19f : 0.13f;
                if (f.FlapT > iv)
                {
                    f.FlapT -= iv;
                    f.Frame = 1 - f.Frame;
                    f.Img.sprite = Resources.Load<Sprite>(isBird
                        ? (f.Frame == 0 ? "UI/Home/home_bird" : "UI/Home/home_bird2")
                        : (f.Frame == 0 ? "UI/Home/home_butterfly_a" : "UI/Home/home_butterfly_b"));
                }
                list[i] = f;
            }
        }

        private void StepSwayers(float dt, float windNow)
        {
            float t = Time.time;
            for (int i = 0; i < _swayers.Count; i++)
            {
                var s = _swayers[i];
                if (s.T == null) continue;
                s.T.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * s.Freq + s.Phase) * s.Amp * (0.45f + windNow));
                _swayers[i] = s;
            }
        }

        private void StepWater(float dt, float windNow)
        {
            if (_water.Count == 0) return;
            float total = W * 1.02f;
            float sp = (28f + windNow * 95f) * _globalSpeed;
            for (int i = 0; i < _water.Count; i++)
            {
                var img = _water[i];
                if (img == null) continue;
                var p = img.rectTransform.anchoredPosition;
                p.x -= sp * _windDir * dt;
                if (p.x < -total) p.x += total * 2f;
                if (p.x > total) p.x -= total * 2f;
                img.rectTransform.anchoredPosition = p;
            }
        }

        private void StepFallers(List<Faller> list, float dt, float windNow, bool isRain)
        {
            float top = HalfH + 40f, bottom = -HalfH - 40f, span = top - bottom;
            float t = Time.time;
            for (int i = 0; i < list.Count; i++)
            {
                var f = list[i];
                if (f.T == null || !f.T.gameObject.activeSelf) continue;
                f.Y -= f.Speed * dt;                       // ⛔ 增量积分 + 回绕
                if (f.Y < bottom) { f.Y += span; f.X = Random.Range(-HalfW, HalfW); }
                float x = f.X + (isRain
                    ? _windDir * windNow * (f.Y - bottom) * 0.10f
                    : Mathf.Sin(t * f.SwayFreq + f.Phase) * f.SwayAmp + _windDir * windNow * 22f);
                f.T.anchoredPosition = new Vector2(x, f.Y);
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