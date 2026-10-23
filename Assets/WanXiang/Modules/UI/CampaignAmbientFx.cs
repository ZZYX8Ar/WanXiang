// ============================================================================
//  万相 · 节点地图（Campaign）背景与飘落粒子
//  ---------------------------------------------------------------------------
//  2026-10-11：用户要求"节点地图没有背景，按幕变化；也要飘动粒子"。
//
//  设计要点：
//   · 背景图按幕加载 Resources/UI/Campaign/map_bg_act{n}_*.png（春/夏/长夏/秋/冬），
//     复用战斗那套"按幕选图"的口径（bg 一律铺满 stretch）。
//   · 粒子复用战斗的 FX_particle_act{n}_*.png（同一套美术，不重复出图），
//     但节点地图是 **UGUI** ， 不能用世界空间的 SpriteRenderer 版本，
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
        /// <summary>按幕背景图路径（act 1..5）。
        /// ⚠ 幕↔ 季节对应**以代码为准**：CampaignPanel.SeasonPalette 写的是
        ///   幕1 春 / 幕2 夏 / 幕3 秋 / 幕4 冬 / 幕5 长夏（SolarTermGraph.cs 的 SeasonCn 同）。
        ///   别按"春夏秋冬+长夏"直觉排 —— 那样 3/4/5 幕会全部错位（用户 2026-10-11 报障）。
        /// </summary>
        private static readonly string[] BgByAct =
        {
            "UI/Campaign/map_bg_act1_spring",     // 幕1 春
            "UI/Campaign/map_bg_act2_summer",     // 幕2 夏
            "UI/Campaign/map_bg_act3_autumn",     // 幕3 秋
            "UI/Campaign/map_bg_act4_winter",     // 幕4 冬
            "UI/Campaign/map_bg_act5_latesummer", // 幕5 长夏
        };

        /// <summary>按幕粒子图路径（复用战斗那套，act 1..5，顺序同上）。</summary>
        private static readonly string[] ParticleByAct =
        {
            "Battle/FX_particle_act1_petal",   // 幕1 春·花瓣
            "Battle/FX_particle_act2_firefly", // 幕2 夏·萤火
            "Battle/FX_particle_act3_leaf",    // 幕3 秋·落叶
            "Battle/FX_particle_act4_snow",    // 幕4 冬·雪花
            "Battle/FX_particle_act5_pollen",  // 幕5 长夏·花粉
        };

        // 每幕：数量 / 像素尺寸 / 下落速度(px/s) / 横向摆幅 / 摆频 / 每秒自转 / alpha呼吸
        // ⚠ 尺寸调过两轮：初版 22~52px 用户报"太小看不清" → 放大到 34~84px 又报"太大"，
        //   现在取中间值 30~56px（比初版大、比上版小）。要改只动这张表。
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
            public float Alpha;       // 固定透明度（AlphaSpeed>0 时在其上做呼吸）
        }

        private static Style StyleForAct(int act)
        {
            switch (Mathf.Clamp(act, 1, 5))
            {
                // 幕1 春·花瓣：50px 淡粉，半透 0.55
                case 1: return new Style { ParticlePath = ParticleByAct[0], Count = 20, Size = 50f, FallSpeed = 34f, SwayAmp = 44f, SwayFreq = 0.9f, SpinDeg = 30f, AlphaMin = 0.45f, AlphaSpeed = 0f, Alpha = 0.55f };
                // 幕2 夏·萤火：38px 萤光，呼吸（本来就该闪，底值别太低）
                case 2: return new Style { ParticlePath = ParticleByAct[1], Count = 18, Size = 38f, FallSpeed = 16f, SwayAmp = 66f, SwayFreq = 0.5f, SpinDeg = 0f, AlphaMin = 0.42f, AlphaSpeed = 1.6f, Alpha = 1f };
                // 幕3 秋·落叶：56px 叶片，转着飘，半透 0.6
                case 3: return new Style { ParticlePath = ParticleByAct[2], Count = 16, Size = 56f, FallSpeed = 42f, SwayAmp = 76f, SwayFreq = 1.1f, SpinDeg = 55f, AlphaMin = 0.52f, AlphaSpeed = 0f, Alpha = 0.60f };
                // 幕4 冬·雪花：32px 雪花，微微闪，半透 0.7
                case 4: return new Style { ParticlePath = ParticleByAct[3], Count = 20, Size = 32f, FallSpeed = 30f, SwayAmp = 40f, SwayFreq = 0.8f, SpinDeg = 12f, AlphaMin = 0.60f, AlphaSpeed = 0.5f, Alpha = 0.75f };
                // 幕5 长夏·花粉：26px 金粉粒，闪但底值抬高（初版 22px 用户报完全看不见）
                default: return new Style { ParticlePath = ParticleByAct[4], Count = 26, Size = 26f, FallSpeed = 22f, SwayAmp = 50f, SwayFreq = 0.7f, SpinDeg = 6f, AlphaMin = 0.50f, AlphaSpeed = 1.1f, Alpha = 1f };
            }
        }

        private struct P
        {
            public RectTransform T;
            public Image Img;
            public float X0, Y, Phase, Speed, SwayAmp, SwayFreq, Spin, SpinPhase;
            public float AlphaMin, AlphaSpeed;
            public float LaneCenter, LaneHalf;   // 横向责任带（回绕时在此带内重取 X0）
            public float Alpha;
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

            SpawnParticles(sp, style, particleRoot);
            Debug.Log("[Campaign] 地图背景与粒子已就绪 act=" + act + " 粒子=" + _ps.Length +
                      " 图=" + sp.name + " size=" + style.Size + " alpha=" + style.Alpha +
                      " 区域=" + _w + "x" + _h);
        }

        /// <summary>按样式铺一批 UGUI 粒子（主线按幕 / 秘境按场域都走这里）。</summary>
        private void SpawnParticles(Sprite sp, Style style, RectTransform particleRoot)
        {
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
                img.color = new Color(1f, 1f, 1f, style.Alpha);
                rt.sizeDelta = new Vector2(style.Size, style.Size);

                // ⛔ 横向**等分列 + 抖动**分布（用户报"右边粒子少"）：
                //   纯 Random.Range 会有运气成分导致某侧偏稀；等分保证每个纵向条带都有一粒。
                //   纵向同样铺开整屏，别都堆在顶部。
                float laneW = (_w - 120f) / _ps.Length;          // 每个粒子的横向"责任带"
                float laneCenter = -_w * 0.5f + 60f + laneW * (i + 0.5f);
                float x = laneCenter + Random.Range(-laneW * 0.42f, laneW * 0.42f);
                float y = Random.Range(-_h * 0.5f, _h * 0.5f);
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
                    LaneCenter = laneCenter,
                    LaneHalf = laneW * 0.42f,
                    Alpha = style.Alpha,
                };
            }
        }

        /// <summary>
        /// 按**场域**构建背景 + 粒子（2026-10-16 用户："每一个秘境的这个背景图也要换一下…
        ///   最好这个粒子也换一下配合这个背景好看一点"）。
        /// <para>与主线的差别只有"读哪张图、用什么飘法"—— 排布/回绕/呼吸全部共用上面那套，
        ///   所以不会出现"秘境的粒子飘法又坏了"这种第二份实现才会有的 bug。</para>
        /// </summary>
        public void BuildRealm(WanXiang.Campaign.RealmDef realm, Image bgImage, RectTransform particleRoot)
        {
            Clear();
            if (realm == null) return;

            // ---- 背景 ----
            if (bgImage != null)
            {
                var bg = Resources.Load<Sprite>("UI/Realm/map_bg_" + realm.Id);
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
                    // ⛔ 缺图要**明说**：静默用上一张（或空白）会让"秘境背景没换"看起来像没做
                    Debug.LogWarning("[Campaign] 秘境背景缺图：UI/Realm/map_bg_" + realm.Id);
                    bgImage.gameObject.SetActive(false);
                }
            }

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

            var style = StyleForRealm(realm.Id);
            var sp = Resources.Load<Sprite>("UI/Realm/FX_" + FxNameOf(realm.Id));
            if (sp == null)
            {
                // 缺图就退回苍泽那颗（而不是直接不显示）：飘落感比"哪一颗"更重要
                sp = Resources.Load<Sprite>("UI/Realm/FX_realm_marsh");
                if (sp == null) { Debug.LogWarning("[Campaign] 秘境粒子缺图：" + realm.Id); return; }
            }

            style.ParticlePath = sp.name;
            SpawnParticles(sp, style, particleRoot);
            Debug.Log("[Campaign] 秘境背景与粒子已就绪 realm=" + realm.Id + " 粒子=" + _ps.Length +
                      " 图=" + sp.name + " 区域=" + _w + "x" + _h);
        }

        /// <summary>场域 id → 粒子图名后缀（文件名与 RealmIds 常量一一对应）。</summary>
        private static string FxNameOf(string realmId)
        {
            if (realmId == WanXiang.Campaign.RealmIds.Marsh) return "realm_marsh";
            if (realmId == WanXiang.Campaign.RealmIds.Ember) return "realm_ember";
            if (realmId == WanXiang.Campaign.RealmIds.Gear) return "realm_gear";
            if (realmId == WanXiang.Campaign.RealmIds.Silent) return "realm_silent";
            if (realmId == WanXiang.Campaign.RealmIds.Thorn) return "realm_thorn";
            return "realm_marsh";
        }

        /// <summary>
        /// 各场域的飘落样式。⚠ 与背景配套：苍泽浮萍是**慢慢沉**、烬岭火星是**往上飘**（负速），
        ///   寂域尘埃几乎不动（该场域的主题就是"静止"）。
        /// </summary>
        private static Style StyleForRealm(string realmId)
        {
            switch (realmId)
            {
                // 苍泽·浮萍：大、慢、半透，横向摆幅大（泡在水里随波）
                case WanXiang.Campaign.RealmIds.Marsh:
                    return new Style { Count = 14, Size = 44f, FallSpeed = 18f, SwayAmp = 90f, SwayFreq = 0.45f, SpinDeg = 18f, AlphaMin = 0.45f, AlphaSpeed = 0f, Alpha = 0.62f };

                // 烬岭·余烬：**负速度 = 往上飘**（热气上升），快、闪、小而亮
                case WanXiang.Campaign.RealmIds.Ember:
                    return new Style { Count = 22, Size = 26f, FallSpeed = -34f, SwayAmp = 70f, SwayFreq = 1.0f, SpinDeg = 20f, AlphaMin = 0.40f, AlphaSpeed = 2.2f, Alpha = 1f };

                // 机枢·铜屑：小而稀，慢速直落，几乎不摆（金属碎屑）
                case WanXiang.Campaign.RealmIds.Gear:
                    return new Style { Count = 12, Size = 22f, FallSpeed = 46f, SwayAmp = 14f, SwayFreq = 0.5f, SpinDeg = 90f, AlphaMin = 0.55f, AlphaSpeed = 0f, Alpha = 0.75f };

                // 寂域·尘：极慢、极淡、几乎不动 —— 这个场域的主题就是"没有动静"
                case WanXiang.Campaign.RealmIds.Silent:
                    return new Style { Count = 30, Size = 64f, FallSpeed = 8f, SwayAmp = 34f, SwayFreq = 0.18f, SpinDeg = 0f, AlphaMin = 0.34f, AlphaSpeed = 0.25f, Alpha = 0.66f };

                // 荆墟·枯叶屑：快而干，摆幅中等、叶片翻转明显
                default:
                    return new Style { Count = 16, Size = 30f, FallSpeed = 52f, SwayAmp = 60f, SwayFreq = 0.85f, SpinDeg = 70f, AlphaMin = 0.45f, AlphaSpeed = 0f, Alpha = 0.65f };
            }
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
                if (p.Y < bottom)
                {
                    p.Y += span;
                    // 回绕时在**自己的责任带内**重取 X0，避免长期往一侧漂（右少左多）
                    p.X0 = p.LaneCenter + Random.Range(-p.LaneHalf, p.LaneHalf);
                }

                float x = p.X0 + Mathf.Sin(_t * p.SwayFreq + p.Phase) * p.SwayAmp;
                if (x > halfW - 20f) x -= 2f * (halfW - 20f);
                else if (x < -halfW + 20f) x += 2f * (halfW - 20f);

                p.T.anchoredPosition = new Vector2(x, p.Y);
                if (p.Spin != 0f)
                    p.T.localRotation = Quaternion.Euler(0f, 0f, p.SpinPhase + _t * p.Spin);

                if (p.AlphaSpeed > 0f)
                {
                    // 呼吸范围 [AlphaMin*Alpha, Alpha]：整体半透但仍有明暗变化（用户要"更透明又能看清"）
                    float k = 0.5f + 0.5f * Mathf.Sin(_t * p.AlphaSpeed * 3f + p.Phase);
                    float lo = p.AlphaMin * p.Alpha, hi = p.Alpha;
                    p.Img.color = new Color(1f, 1f, 1f, Mathf.Lerp(lo, hi, k));
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