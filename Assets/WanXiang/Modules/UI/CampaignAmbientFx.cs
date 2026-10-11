// ============================================================================
//  万相 · 节点地图（Campaign）背景与飘落粒子
//  ---------------------------------------------------------------------------
//  2026-10-11：用户要求"节点地图没有背景，按幕变化；也要飘动粒子"。
//
//  设计要点：
//   · 背景图按幕加载 Resources/UI/Campaign/map_bg_act{n}_*.png（春/夏/长夏/秋/冬），
//     复用战斗那套"按幕选图"的口径（bg 一律铺满 stretch）。
//   · 粒子复用战斗的 FX_particle_act{n}_*.png（同一套美术，不重复出图），
//     但节点地图是 **UGUI** ⇒ 不能用世界空间的 SpriteRenderer 版本，
//     这里用 Image + RectTransform 做一套 UGUI 粒子。
//   · ⛔ 增量积分 + 回绕（早期战斗版写成 y = BaseY - Speed*_t 导致粒子飘一会儿
//     就锁死在顶部，用户报"飘动一些就没有了"）—— 这里一开始就写对。
//   · 节点地图是**滚动长卷**，粒子固定挂在铺满层（Root_MapParticles），
//     不跟 Scroll_Nodes 滚，避免滚动时粒子跟着走位。
// ============================================================================

using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.Modules.UI
{
    /// <summary>节点地图的按幕背景 + 飘落粒子。挂在 Panel_Campaign 上。</summary>
    public sealed class CampaignAmbientFx : MonoBehaviour
    {
        /// <summary>按幕背景图路径（act 1..5）。</summary>
        private static readonly string[] BgByAct =
        {
            "UI/Campaign/map_bg_act1_spring",
            "UI/Campaign/map_bg_act2_summer",
            "UI/Campaign/map_bg_act3_latesummer",
            "UI/Campaign/map_bg_act4_autumn",
            "UI/Campaign/map_bg_act5_winter",
        };

        /// <summary>按幕粒子图路径（复用战斗那套，act 1..5）。</summary>
        private static readonly string[] ParticleByAct =
        {
            "Battle/FX_particle_act1_petal",
            "Battle/FX_particle_act2_firefly",
            "Battle/FX_particle_act3_pollen",
            "Battle/FX_particle_act4_leaf",
            "Battle/FX_particle_act5_snow",
        };

        // 每幕：数量 / 像素尺寸 / 下落速度(px/s) / 横向摆幅 / 摆频 / 每秒自转 / alpha呼吸
        private struct Style
        {
            public string ParticlePath;
            public int Count;
            public float Size;        // UI 像素
            public float FallSpeed;   // px/s（正 = 向下）
            public float SwayAmp;
            public float SwayFreq;
            public float SpinDeg;
            public float AlphaMin;
            public float AlphaSpeed;
        }

        private static Style StyleForAct(int act)
        {
            switch (Mathf.Clamp(act, 1, 5))
            {
                case 1: return new Style { ParticlePath = ParticleByAct[0], Count = 18, Size = 46f, FallSpeed = 34f, SwayAmp = 38f, SwayFreq = 0.9f, SpinDeg = 30f, AlphaMin = 0.60f, AlphaSpeed = 0f };
                case 2: return new Style { ParticlePath = ParticleByAct[1], Count = 16, Size = 34f, FallSpeed = 16f, SwayAmp = 62f, SwayFreq = 0.5f, SpinDeg = 0f, AlphaMin = 0.18f, AlphaSpeed = 1.6f };
                case 3: return new Style { ParticlePath = ParticleByAct[2], Count = 20, Size = 22f, FallSpeed = 20f, SwayAmp = 46f, SwayFreq = 0.7f, SpinDeg = 6f, AlphaMin = 0.26f, AlphaSpeed = 1.1f };
                case 4: return new Style { ParticlePath = ParticleByAct[3], Count = 16, Size = 52f, FallSpeed = 40f, SwayAmp = 70f, SwayFreq = 1.1f, SpinDeg = 55f, AlphaMin = 0.65f, AlphaSpeed = 0f };
                default: return new Style { ParticlePath = ParticleByAct[4], Count = 24, Size = 28f, FallSpeed = 30f, SwayAmp = 34f, SwayFreq = 0.8f, SpinDeg = 12f, AlphaMin = 0.52f, AlphaSpeed = 0.5f };
            }
        }

        private struct P
        {
            public RectTransform T;
            public Image Img;
            public float X0, Y, Phase, Speed, SwayAmp, SwayFreq, Spin, SpinPhase;
            public float AlphaMin, AlphaSpeed;
        }

        private P[] _ps;
        private float _t;
        private RectTransform _area;      // 粒子活动范围（铺满层）
        private float _w = 1920f, _h = 1080f;
        private bool _running;

        /// <summary>UIPanel 没有 Update 钩子（BattlePanel 是自己写 MonoBehaviour 的 Update），
        /// 所以粒子自己驱动；用这个开关保证"面板没打开就不跑"。</summary>
        public bool Running
        {
            get => _running;
            set => _running = value;
        }

        private void Update()
        {
            if (!_running) return;
            Step(Time.deltaTime);
        }

        /// <summary>按幕构建背景 + 粒子。缺图静默跳过，不阻塞节点图。</summary>
        public void Build(int act, Image bgImage, RectTransform particleRoot)
        {
            Clear();
            act = Mathf.Clamp(act, 1, 5);

            // ---- 背景 ----
            if (bgImage != null)
            {
                var bg = Resources.Load<Sprite>(BgByAct[act - 1]);
                if (bg != null)
                {
                    bgImage.sprite = bg;
                    bgImage.type = Image.Type.Simple;
                    bgImage.color = Color.white;
                    bgImage.raycastTarget = false;
                    bgImage.gameObject.SetActive(true);
                }
                else
                {
                    Debug.LogWarning("[Campaign] 地图背景缺图：" + BgByAct[act - 1]);
                    bgImage.gameObject.SetActive(false);
                }
            }

            // ---- 粒子 ----
            if (particleRoot == null) return;
            _area = particleRoot;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                var cr = canvas.GetComponent<RectTransform>();
                if (cr != null) { _w = cr.rect.width; _h = cr.rect.height; }
            }
            if (_w <= 0f) _w = 1920f;
            if (_h <= 0f) _h = 1080f;

            var style = StyleForAct(act);
            var sp = Resources.Load<Sprite>(style.ParticlePath);
            if (sp == null)
            {
                Debug.LogWarning("[Campaign] 地图粒子缺图：" + style.ParticlePath);
                return;
            }

            _ps = new P[style.Count];
            for (int i = 0; i < _ps.Length; i++)
            {
                var go = new UnityEngine.GameObject("P" + i, typeof(RectTransform));
                go.transform.SetParent(particleRoot, false);
                var rt = (RectTransform)go.transform;
                var img = go.AddComponent<Image>();
                img.sprite = sp;
                img.raycastTarget = false;         // 粒子不能挡点击
                img.preserveAspect = true;
                rt.sizeDelta = new Vector2(style.Size, style.Size);
                // 纵向铺开在整屏范围（不是都堆在顶部）
                float y = Random.Range(-_h * 0.5f, _h * 0.5f);
                float x = Random.Range(-_w * 0.5f + 40f, _w * 0.5f - 40f);
                rt.anchoredPosition = new Vector2(x, y);

                _ps[i] = new P
                {
                    T = rt,
                    Img = img,
                    X0 = x,
                    Y = y,
                    Phase = Random.Range(0f, 6.28f),
                    Speed = style.FallSpeed * Random.Range(0.75f, 1.25f),
                    SwayAmp = style.SwayAmp * Random.Range(0.7f, 1.3f),
                    SwayFreq = style.SwayFreq * Random.Range(0.8f, 1.2f),
                    Spin = style.SpinDeg * Random.Range(-1f, 1f),
                    SpinPhase = Random.Range(0f, 360f),
                    AlphaMin = style.AlphaMin,
                    AlphaSpeed = style.AlphaSpeed,
                };
            }
            Debug.Log("[Campaign] 地图背景与粒子已就绪 act=" + act + " 粒子=" + _ps.Length + " 图=" + sp.name);
        }

        /// <summary>推进粒子（由面板 Update 调用；面板打开期间每帧都调）。</summary>
        public void Step(float dt)
        {
            if (_ps == null || _ps.Length == 0) return;
            _t += dt;
            float halfW = _w * 0.5f, halfH = _h * 0.5f;
            float top = halfH + 60f, bottom = -halfH - 60f;
            float span = top - bottom;

            for (int i = 0; i < _ps.Length; i++)
            {
                var p = _ps[i];
                if (p.T == null) continue;

                // ⛔ 增量积分 + 回绕（不是 y = BaseY - Speed*_t，那样会锁死在顶部）
                p.Y -= p.Speed * dt;
                if (p.Y < bottom) { p.Y += span; p.X0 = Random.Range(-halfW + 40f, halfW - 40f); }

                float x = p.X0 + Mathf.Sin(_t * p.SwayFreq + p.Phase) * p.SwayAmp;
                if (x > halfW - 20f) x -= 2f * (halfW - 20f);
                else if (x < -halfW + 20f) x += 2f * (halfW - 20f);

                p.T.anchoredPosition = new Vector2(x, p.Y);
                if (p.Spin != 0f)
                    p.T.localRotation = Quaternion.Euler(0f, 0f, p.SpinPhase + _t * p.Spin);

                if (p.AlphaSpeed > 0f)
                {
                    float k = 0.5f + 0.5f * Mathf.Sin(_t * p.AlphaSpeed * 3f + p.Phase);
                    var c = p.Img.color;
                    p.Img.color = new Color(c.r, c.g, c.b, Mathf.Lerp(p.AlphaMin, 1f, k));
                }

                _ps[i] = p;   // 结构体值类型，必须写回
            }
        }

        public void Clear()
        {
            if (_ps != null)
            {
                for (int i = 0; i < _ps.Length; i++)
                    if (_ps[i].T != null) DestroyImmediate(_ps[i].T.gameObject);
            }
            _ps = null;
            _t = 0f;
        }

        /// <summary>面板关闭时调用：清粒子并停止驱动。</summary>
        public void Stop()
        {
            _running = false;
            Clear();
        }
    }
}