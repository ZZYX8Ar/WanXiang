// ============================================================================
//  万相 · 主界面（HomePanel）背景特效
//  ---------------------------------------------------------------------------
//  2026-10-11 用户需求：主界面也要有背景，且背景元素能动、随当前探索的幕变化，
//  还要有天气变化（如下雨引起水波涟漪）。
//
//  设计要点：
//   · 背景图按**当前幕**加载 Resources/UI/Home/home_bg_act{n}_*.png
//     （幕↔季节映射以 CampaignPanel.SeasonPalette 为准：1春 2夏 3秋 4冬 5长夏）。
//   · 天气层：下雨 = 一批竖线下落粒子 + 水面涟漪环扩散（纯代码画的 Image，不用美术图，
//     因为涟漪要能按参数连续变化，预生成图反而难调）。
//   · 幕变化：换幕时交叉淡入新背景；并按季节切换粒子（春花瓣/夏萤火/秋落叶/冬雪/长夏花粉），
//     与战斗/节点地图同一套 FX_particle 图。
//   · ⛔ 增量积分 + 回绕（同战斗/地图那套，见 MEMORY 铁律）。
//   · UIPanel 没有 Update 钩子 ⇒ 组件自带 Update + Running 开关。
// ============================================================================

using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.Modules.UI
{
    /// <summary>主界面背景 + 天气特效。挂在 Panel_Home 上。</summary>
    public sealed class HomeAmbientFx : MonoBehaviour
    {
        // 幕↔季节：1春 2夏 3秋 4冬 5长夏（⛔ 别按"春夏秋冬+长夏"直觉排）
        private static readonly string[] BgByAct =
        {
            "UI/Home/home_bg_act1_spring",
            "UI/Home/home_bg_act2_summer",
            "UI/Home/home_bg_act3_autumn",
            "UI/Home/home_bg_act4_winter",
            "UI/Home/home_bg_act5_latesummer",
        };
        private static readonly string[] ParticleByAct =
        {
            "Battle/FX_particle_act1_petal",
            "Battle/FX_particle_act2_firefly",
            "Battle/FX_particle_act3_leaf",
            "Battle/FX_particle_act4_snow",
            "Battle/FX_particle_act5_pollen",
        };

        /// <summary>面板关闭时停驱动。</summary>
        public bool Running { get; set; }

        [SerializeField] private Image _imgBgA;       // Img_HomeBgA当前背景
        [SerializeField] private Image _imgBgB;       // Img_HomeBgB 待切换背景（交叉淡入）
        [SerializeField] private RectTransform _fxRoot;   // Root_HomeWeather 天气/粒子层（铺满）
        [SerializeField] private RectTransform _fxBelow;  // Root_HomeRipple 涟漪层（水面以下）

        // ---- 天气参数（Inspector 可调；默认"晴"）----
        [Header("天气")]
        [SerializeField] private int _weatherKind = 0;      // 0晴 1雨 2阴
        [SerializeField] private float _rainCount = 90;      // 雨丝数量
        [SerializeField] private float _rainSpeed = 1500f;   // 雨速 px/s
        [SerializeField] private float _rainLength = 26f;    // 雨丝长px
        [SerializeField] private float _rippleCount = 5;     // 同时存在的涟漪数
        [SerializeField] private float _rippleLife = 1.6f;   // 涟漪扩散周期(s)
        [SerializeField] private Color _rainColor = new Color(0.72f, 0.80f, 0.88f, 0.42f);
        [SerializeField] private Color _rippleColor = new Color(0.85f, 0.92f, 0.98f, 0.55f);

        // ---- 幕环境粒子参数 ----
        [Header("幕粒子")]
        [SerializeField] private int _particleCount = 16;
        [SerializeField] private float _particleSize = 40f;
        [SerializeField] private float _particleFall = 26f;
        [SerializeField] private float _particleAlpha = 0.5f;

        // ---- 粒子 ----
        private struct P
        {
            public RectTransform T; public Image Img;
            public float X0, Y, Phase, Speed, SwayAmp, SwayFreq, Spin, SpinPhase;
            public float LaneCenter, LaneHalf;
        }
        private P[] _ps;

        // ---- 雨丝 ----
        private struct Drop
        {
            public RectTransform T; public Image Img;
            public float X, Y, Speed;
        }
        private Drop[] _drops;

        // ---- 涟漪 ----
        private struct Ripple
        {
            public RectTransform T; public Image Img;
            public float Age, Delay, Speed, MaxScale;
        }
        private Ripple[] _ripples;

        private float _t;
        private float _w = 1920f, _h = 1080f;
        private Sprite _curBg;
        private int _curAct = -1;

        private void Update()
        {
            if (!Running) return;
            float dt = Time.deltaTime;
            StepParticles(dt);
            StepRain(dt);
            StepRipples(dt);
        }

        /// <summary>按幕重建背景与粒子（换幕/开面时调）。</summary>
        public void Rebuild(int act)
        {
            act = Mathf.Clamp(act, 1, 5);
            EnsureNodes();
            ReadCanvasSize();

            // ---- 背景：换幕时交叉淡入 ----
            var sp = Resources.Load<Sprite>(BgByAct[act - 1]);
            if (sp != null && sp != _curBg)
            {
                if (_imgBgA != null && _imgBgA.sprite == sp)
                {
                    // 已经在当前层，只需更新记录
                }
                else if (_imgBgB != null)
                {
                    _imgBgB.sprite = sp;
                    _imgBgB.color = new Color(1f, 1f, 1f, 0f);   // 从透明淡入
                    StopAllCoroutines();
                    StartCoroutine(CrossFade(sp));
                }
                _curBg = sp;
            }

            // ---- 幕粒子 ----
            var psp = Resources.Load<Sprite>(ParticleByAct[act - 1]);
            if (psp != null) BuildParticles(psp);

            _curAct = act;
            Debug.Log("[Home] 背景特效就绪 act=" + act + " bg=" + (_curBg != null ? _curBg.name : "null"));
        }

        /// <summary>切换天气（0晴 1雨 2阴）。下雨时建雨丝+涟漪。</summary>
        public void SetWeather(int kind)
        {
            _weatherKind = kind;
            ClearRain();
            if (kind == 1)
            {
                BuildRain();
                BuildRipples();
            }
            if (_imgBgA != null)
            {
                // 阴天压暗背景（视觉上直接乘灰）
                var c = _imgBgA.color;
                float dim = kind == 2 ? 0.62f : (kind == 1 ? 0.80f : 1f);
                _imgBgA.color = new Color(c.r * dim, c.g * dim, c.b * dim, Mathf.Max(c.a, kind == 0 ? 1f : c.a));
            }
            Debug.Log("[Home] 天气切换 kind=" + kind);
        }

        private System.Collections.IEnumerator CrossFade(Sprite next)
        {
            const float dur = 0.6f;
            float e = 0f;
            while (e < dur)
            {
                e += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(e / dur);
                if (_imgBgB != null) _imgBgB.color = new Color(1f, 1f, 1f, k);
                yield return null;
            }
            // 交换：A 拿到新图，B 清空备用
            if (_imgBgA != null)
            {
                _imgBgA.sprite = next;
                var c = _imgBgA.color;
                float dim = _weatherKind == 2 ? 0.62f : (_weatherKind == 1 ? 0.80f : 1f);
                _imgBgA.color = new Color(c.r * dim, c.g * dim, c.b * dim, 1f);
            }
            if (_imgBgB != null) { _imgBgB.sprite = null; _imgBgB.color = new Color(1f, 1f, 1f, 0f); }
        }

        // ------------------------------------------------------------------
        //  幕粒子（飘落装饰）
        // ------------------------------------------------------------------
        private void BuildParticles(Sprite sp)
        {
            if (_fxRoot == null) return;
            ClearParticles();
            _ps = new P[_particleCount];
            for (int i = 0; i < _ps.Length; i++)
            {
                var go = new UnityEngine.GameObject("P" + i, typeof(RectTransform));
                go.transform.SetParent(_fxRoot, false);
                var rt = (RectTransform)go.transform;
                var img = go.AddComponent<Image>();
                img.sprite = sp;
                img.raycastTarget = false;
                img.preserveAspect = true;
                img.color = new Color(1f, 1f, 1f, _particleAlpha);
                rt.sizeDelta = new Vector2(_particleSize, _particleSize);
                // 等分列+ 抖动，横向均匀
                float laneW = (_w - 160f) / _ps.Length;
                float laneC = -_w * 0.5f + 80f + laneW * (i + 0.5f);
                float x = laneC + Random.Range(-laneW * 0.42f, laneW * 0.42f);
                float y = Random.Range(-_h * 0.5f, _h * 0.5f);
                rt.anchoredPosition = new Vector2(x, y);
                _ps[i] = new P
                {
                    T = rt, Img = img, X0 = x, Y = y,
                    Phase = Random.Range(0f, 6.28f),
                    Speed = _particleFall * Random.Range(0.75f, 1.25f),
                    SwayAmp = 46f * Random.Range(0.7f, 1.3f),
                    SwayFreq = 0.8f * Random.Range(0.8f, 1.2f),
                    Spin = 40f * Random.Range(-1f, 1f),
                    SpinPhase = Random.Range(0f, 360f),
                    LaneCenter = laneC, LaneHalf = laneW * 0.42f,
                };
            }
        }

        private void StepParticles(float dt)
        {
            if (_ps == null) return;
            _t += dt;
            float halfW = _w * 0.5f, halfH = _h * 0.5f;
            float top = halfH + 60f, bottom = -halfH - 60f, span = top - bottom;
            for (int i = 0; i < _ps.Length; i++)
            {
                var p = _ps[i];
                if (p.T == null) continue;
                p.Y -= p.Speed * dt;
                if (p.Y < bottom) { p.Y += span; p.X0 = p.LaneCenter + Random.Range(-p.LaneHalf, p.LaneHalf); }
                float x = p.X0 + Mathf.Sin(_t * p.SwayFreq + p.Phase) * p.SwayAmp;
                if (x > halfW - 20f) x -= 2f * (halfW - 20f);
                else if (x < -halfW + 20f) x += 2f * (halfW - 20f);
                p.T.anchoredPosition = new Vector2(x, p.Y);
                if (p.Spin != 0f) p.T.localRotation = Quaternion.Euler(0f, 0f, p.SpinPhase + _t * p.Spin);
                _ps[i] = p;
            }
        }

        private void ClearParticles()
        {
            if (_ps != null) { for (int i = 0; i < _ps.Length; i++) if (_ps[i].T != null) DestroyImmediate(_ps[i].T.gameObject); }
            _ps = null;
        }

        // ------------------------------------------------------------------
        //  雨丝（纯 Image 竖线，不用美术图：数量多、要随机长度/速度）
        // ------------------------------------------------------------------
        private void BuildRain()
        {
            if (_fxRoot == null) return;
            ClearRain();
            _drops = new Drop[Mathf.Max(1, (int)_rainCount)];
            for (int i = 0; i < _drops.Length; i++)
            {
                var go = new UnityEngine.GameObject("D" + i, typeof(RectTransform));
                go.transform.SetParent(_fxRoot, false);
                var rt = (RectTransform)go.transform;
                var img = go.AddComponent<Image>();
                img.color = _rainColor;
                img.raycastTarget = false;
                // 雨丝：细长竖条
                rt.sizeDelta = new Vector2(2f, _rainLength * Random.Range(0.6f, 1.4f));
                float x = Random.Range(-_w * 0.5f, _w * 0.5f);
                float y = Random.Range(-_h * 0.5f, _h * 0.5f);
                rt.anchoredPosition = new Vector2(x, y);
                _drops[i] = new Drop { T = rt, Img = img, X = x, Y = y, Speed = _rainSpeed * Random.Range(0.8f, 1.2f) };
            }
        }

        private void StepRain(float dt)
        {
            if (_drops == null) return;
            float halfH = _h * 0.5f;
            for (int i = 0; i < _drops.Length; i++)
            {
                var d = _drops[i];
                if (d.T == null) continue;
                d.Y -= d.Speed * dt;
                if (d.Y < -halfH - 40f) d.Y = halfH + 40f;   // 从顶部回来
                d.T.anchoredPosition = new Vector2(d.X, d.Y);
            }
        }

        private void ClearRain()
        {
            if (_drops != null) { for (int i = 0; i < _drops.Length; i++) if (_drops[i].T != null) DestroyImmediate(_drops[i].T.gameObject); }
            _drops = null;
        }

        // ------------------------------------------------------------------
        //  水面涟漪（下雨时在水面上扩散的圆环；纯代码画的空心圆 Image）
        // ------------------------------------------------------------------
        private void BuildRipples()
        {
            if (_fxBelow == null) return;
            if (_ripples != null) { for (int i = 0; i < _ripples.Length; i++) if (_ripples[i].T != null) DestroyImmediate(_ripples[i].T.gameObject); }
            _ripples = new Ripple[Mathf.Max(1, (int)_rippleCount)];
            float waterY = -_h * 0.5f + _h * 0.22f;   // 水面大致高度（画面偏下）
            for (int i = 0; i < _ripples.Length; i++)
            {
                var go = new UnityEngine.GameObject("R" + i, typeof(RectTransform));
                go.transform.SetParent(_fxBelow, false);
                var rt = (RectTransform)go.transform;
                var img = go.AddComponent<Image>();
                img.color = _rippleColor;
                img.raycastTarget = false;
                img.sprite = BuildRingSprite();
                rt.sizeDelta = new Vector2(10f, 10f);
                rt.anchoredPosition = new Vector2(Random.Range(-_w * 0.42f, _w * 0.42f), waterY);
                _ripples[i] = new Ripple
                {
                    T = rt, Img = img,
                    Age = Random.Range(0f, _rippleLife),
                    Delay = Random.Range(0f, _rippleLife),
                    Speed = 1f / _rippleLife,
                    MaxScale = Random.Range(90f, 160f),
                };
            }
        }

        private static Sprite _ringSprite;
        private static Sprite BuildRingSprite()
        {
            // 程序化画一个空心圆环（白底透明，只留圆环像素）
            if (_ringSprite != null) return _ringSprite;
            int S = 128, cx = S / 2, cy = S / 2, rOut = 60, rIn = 52;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    byte a = (byte)((r >= rIn && r <= rOut) ? 255 : 0);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a / 255f));
                }
            tex.Apply();
            _ringSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect);
            return _ringSprite;
        }

        private void StepRipples(float dt)
        {
            if (_ripples == null) return;
            for (int i = 0; i < _ripples.Length; i++)
            {
                var r = _ripples[i];
                if (r.T == null) continue;
                r.Age += dt;
                if (r.Age >= _rippleLife)
                {
                    r.Age -= _rippleLife;   // 循环扩散
                    r.T.anchoredPosition = new Vector2(Random.Range(-_w * 0.42f, _w * 0.42f), r.T.anchoredPosition.y);
                }
                float k = r.Age / _rippleLife;      // 0..1 扩散进度
                float scale = Mathf.Lerp(8f, r.MaxScale, k);
                r.T.localScale = new Vector3(scale, scale, 1f);
                // 越扩越淡
                var c = r.Img.color;
                r.Img.color = new Color(c.r, c.g, c.b, c.a * (1f - k * 0.85f));
                _ripples[i] = r;
            }
        }

        // ------------------------------------------------------------------
        //  工具
        // ------------------------------------------------------------------
        private void EnsureNodes()
        {
            // 背景层/天气层若 prefab 里没有则运行时补（正常已由 prefab 建好）
            if (_fxRoot == null) _fxRoot = transform.Find("Root_HomeWeather") as RectTransform;
            if (_fxBelow == null) _fxBelow = transform.Find("Root_HomeRipple") as RectTransform;
        }

        private void ReadCanvasSize()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var cr = canvas.GetComponent<RectTransform>();
            if (cr == null) return;
            _w = cr.rect.width > 0 ? cr.rect.width : _w;
            _h = cr.rect.height > 0 ? cr.rect.height : _h;
        }
    }
}