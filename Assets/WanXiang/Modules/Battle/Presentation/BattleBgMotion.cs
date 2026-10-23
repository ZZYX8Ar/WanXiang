// ============================================================================
//  BattleBgMotion —— 战斗背景动效"档 3"（2026-10-23 用户："所有战斗场景背景都要动"）
//  ---------------------------------------------------------------------------
//  设计原则（美术向）：
//   · 只用**低频、低对比**的三层动效：雾层横向漂移、大面积暖光呼吸、场域专属层；
//   · 不用全屏高频噪声 / 强闪烁 / 明暗跳变（这类最容易让人觉得廉价、晕）；
//   · 全部是 SpriteRenderer + Transform 位移，**不需要自定义 shader**，任何管线都能跑；
//   · 每层都做"超铺 + 平铺回绕"，漂移永远不会露出画面边缘。
//
//  层级：背景 BG(-10) < 本组(-9 ~ -6) < 单位/立绘(10+)
// ============================================================================
using UnityEngine;

namespace WanXiang.Battle.Presentation
{
    /// <summary>战斗背景的多层缓动叠层（雾 / 暖光 / 场域专属）。</summary>
    public sealed class BattleBgMotion : MonoBehaviour
    {
        private struct Layer
        {
            public Transform T;
            public SpriteRenderer Sr;
            public float Speed;      // 横向漂移（世界单位/秒）
            public float Span;       // 平铺回绕的半周期宽度
            public float BaseAlpha;
            public float PulseAmp;   // alpha 呼吸幅度
            public float PulseFreq;  // 呼吸频率（Hz）
            public float RotSpeed;   // 自转（度/秒）
            public float BobAmp;     // 垂直浮动
            public float BobFreq;
            public float Phase;
            public Vector3 Home;
        }

        private Transform _root;      // ⛔ 自己的子容器：绝不能在 stage 根上 Clear（会删掉背景/单位）
        private Layer[] _layers;
        private float _t;
        private bool _built;

        public void Clear()
        {
            // ⛔ 2026-10-23 事故：本组件挂在 stage 根上，早先这里遍历 transform 子物体删除，
            //   把**背景 BG_Spring / 前景 / 单位**一起删了 ⇒ 战斗里"白茫茫一片"（只剩相机纸色）。
            //   现在只在自己的子容器 _root 里清理。
            if (_root != null)
            {
                for (int i = _root.childCount - 1; i >= 0; i--)
                    Destroy(_root.GetChild(i).gameObject);
            }
            _layers = null;
            _built = false;
        }

        /// <summary>构建叠层。realmId 非空 = 秘境场域；否则按幕（季节）取色。</summary>
        public void Build(string realmId, int act)
        {
            Clear();
            if (_root == null)
            {
                var rt = new GameObject("BgMotionLayers");
                rt.transform.SetParent(transform, false);
                _root = rt.transform;
            }
            var fog = Resources.Load<Sprite>("Battle/FX_motion_fog");
            var glow = Resources.Load<Sprite>("Battle/FX_motion_glow");
            var water = Resources.Load<Sprite>("Battle/FX_motion_water");
            var gear = Resources.Load<Sprite>("Battle/FX_motion_gear");
            var dust = Resources.Load<Sprite>("Battle/FX_motion_dust");

            Color tint = TintFor(realmId, act);
            var list = new System.Collections.Generic.List<Layer>(6);

            // ★ 2026-10-23：雾层改**淡黑**（之前白色雾把纸面糊成灰白）。
            //   ⛔ 关键：浅底画面加白 = 洗白；要氛围只能用"淡黑暗噪"压一点层次。
            if (fog != null)
            {
                list.Add(Make("Fog_Far", fog, new Vector3(0f, 0.6f, 0f), new Vector2(26f, 13f),
                              new Color(0.42f, 0.40f, 0.37f, 0.07f), -7));
                list.Add(Make("Fog_Near", fog, new Vector3(0f, -0.9f, 0f), new Vector2(30f, 15f),
                              new Color(0.46f, 0.44f, 0.40f, 0.05f), -6));
            }

            // ---- ② 大面积暖光（呼吸）----
            if (glow != null)
            {
                list.Add(Make("Glow", glow, new Vector3(0f, 1.2f, 0f), new Vector2(20f, 14f),
                              new Color(1f, 0.93f, 0.80f, 0.06f), -8));
            }

            // ---- ③ 场域专属 ----
            if (realmId == "realm_marsh" && water != null)
            {
                // 苍泽：水面波光（贴着下半屏，慢速横移）
                list.Add(Make("Water", water, new Vector3(0f, -3.2f, 0f), new Vector2(30f, 7f),
                              new Color(0.90f, 0.98f, 1f, 0.14f), -5));
            }
            else if (realmId == "realm_gear" && gear != null)
            {
                // 机枢：齿轮环缓慢自转（极淡，只做"远处还在转"的暗示）
                list.Add(Make("Gear", gear, new Vector3(0f, 2.4f, 0f), new Vector2(13f, 13f),
                              new Color(0.86f, 0.80f, 0.62f, 0.10f), -8));
            }
            else if (realmId == "realm_silent" && dust != null)
            {
                // 寂域：横向尘埃带（几乎不动，只是让它"活着"）
                list.Add(Make("Dust", dust, new Vector3(0f, 0.2f, 0f), new Vector2(30f, 9f),
                              new Color(0.96f, 0.97f, 1f, 0.13f), -5));
            }
            else if (realmId == "realm_ember" && glow != null)
            {
                // 烬岭：下方熔光呼吸（比全局暖光更贴地、更强）
                list.Add(Make("Lava", glow, new Vector3(0f, -3.4f, 0f), new Vector2(24f, 9f),
                              new Color(1f, 0.62f, 0.36f, 0.16f), -5));
            }
            else if (realmId == "realm_thorn" && fog != null)
            {
                // 荆墟：风沙更快地扫过
                list.Add(Make("Sand", fog, new Vector3(0f, -1.8f, 0f), new Vector2(34f, 11f),
                              new Color(0.92f, 0.85f, 0.70f, 0.12f), -5));
            }

            if (list.Count > 0)
            {
                _layers = list.ToArray();
                _built = true;
            }
        }

        /// <summary>
        /// **shader 版背景动效**（2026-10-23 用户："叠雾把背景糊了，能不能写 shader"）。
        /// 不再在背景上盖一层白雾，而是**在背景贴图本身上**做：UV 微扰 + 暗部墨脉冲 +
        /// 水面波光 + 乘性呼吸。纸面留白区域用亮度掩膜排除，绝不会发白。
        /// 返回材质实例（调用方负责销毁）。
        /// </summary>
        public static Material ApplyShaderToBg(SpriteRenderer bgSr, Sprite bgSprite, string realmId)
        {
            if (bgSr == null || bgSprite == null) return null;
            var sh = Resources.Load<Shader>("Battle/BattleBgMotion");
            if (sh == null) { Debug.LogWarning("[BattleBgMotion] 找不到 shader：Battle/BattleBgMotion"); return null; }

            var mat = new Material(sh);
            mat.SetTexture("_MainTex", bgSprite.texture);
            mat.SetColor("_Color", Color.white);

            // 噪声贴图：直接用程序化的柔雾图（柔和的低频块最适合做"空气感"）
            var noise = Resources.Load<Sprite>("Battle/FX_motion_fog");
            if (noise != null)
            {
                var tex = noise.texture;
                tex.wrapMode = TextureWrapMode.Repeat;      // 必须 Repeat，否则 UV 越大越糊
                mat.SetTexture("_NoiseTex", tex);
            }

            // 通用参数：扰动极小（0.35% 视口），保证"看得清原画"
            mat.SetFloat("_NoiseScale", 3.0f);
            mat.SetFloat("_NoiseSpeed", 0.015f);
            mat.SetFloat("_Distort", 0.0035f);
            mat.SetFloat("_Breath", 0.018f);
            mat.SetFloat("_BreathSpeed", 0.08f);
            mat.SetFloat("_DarkMaskLo", 0.35f);
            mat.SetFloat("_DarkMaskHi", 0.82f);
            mat.SetFloat("_InkPulse", 0.02f);

            // 场域差异：只有苍泽有明显水面；烬岭/荆墟做更强的墨脉冲（热气/风沙）
            float waterY = 0.15f, waterStr = 0f;
            switch (realmId)
            {
                case "realm_marsh": waterY = 0.42f; waterStr = 0.06f; break;
                case "realm_ember": waterStr = 0f; mat.SetFloat("_InkPulse", 0.03f); break;
                case "realm_thorn": waterStr = 0f; mat.SetFloat("_InkPulse", 0.028f); break;
            }
            mat.SetFloat("_WaterY", waterY);
            mat.SetFloat("_WaterStrength", waterStr);
            mat.SetFloat("_WaterSpeed", 0.35f);

            bgSr.material = mat;
            return mat;
        }

        private Layer Make(string name, Sprite sp, Vector3 pos, Vector2 size, Color col, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root != null ? _root : transform, false);
            go.transform.localPosition = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            sr.color = col;
            sr.sortingOrder = order;
            var b = sp.bounds.size;
            go.transform.localScale = new Vector3(size.x / Mathf.Max(0.001f, b.x),
                                                  size.y / Mathf.Max(0.001f, b.y), 1f);

            return new Layer
            {
                T = go.transform,
                Sr = sr,
                // 不同层不同速度：远处慢、近处快
                Speed = order >= -5 ? 0.42f : 0.22f,
                Span = size.x * 0.35f,
                BaseAlpha = col.a,
                PulseAmp = order >= -5 ? 0.03f : 0.02f,
                PulseFreq = order >= -5 ? 0.08f : 0.05f,
                RotSpeed = name == "Gear" ? 1.6f : 0f,
                BobAmp = order >= -6 ? 0.08f : 0.14f,
                BobFreq = 0.045f,
                Phase = Mathf.Abs(pos.x + pos.y) * 0.7f,
                Home = pos,
            };
        }

        /// <summary>按场域 / 幕给雾色（都是低饱和纸墨色，避免"彩色雾"的廉价感）。</summary>
        private static Color TintFor(string realmId, int act)
        {
            switch (realmId)
            {
                case "realm_marsh": return new Color(0.86f, 0.94f, 0.92f);
                case "realm_ember": return new Color(1f, 0.90f, 0.82f);
                case "realm_gear": return new Color(0.90f, 0.88f, 0.80f);
                case "realm_silent": return new Color(0.95f, 0.96f, 0.98f);
                case "realm_thorn": return new Color(0.96f, 0.92f, 0.84f);
            }
            switch (Mathf.Clamp(act, 1, 5))
            {
                case 1: return new Color(0.98f, 0.94f, 0.96f);   // 春
                case 2: return new Color(0.94f, 0.98f, 0.92f);   // 夏
                case 3: return new Color(0.99f, 0.95f, 0.88f);   // 秋
                case 4: return new Color(0.94f, 0.96f, 1f);      // 冬
                default: return new Color(1f, 0.97f, 0.90f);     // 长夏
            }
        }

        /// <summary>每帧推进（BattleStage2D.Step 转发）。</summary>
        public void Step(float dt)
        {
            if (!_built || _layers == null) return;
            _t += dt;

            for (int i = 0; i < _layers.Length; i++)
            {
                var L = _layers[i];
                if (L.T == null || L.Sr == null) continue;

                float t = _t + L.Phase;

                // 横向漂移 + 平铺回绕（走到 Span 就从头来，肉眼看不出来）
                float x = L.Home.x + Mathf.Repeat(L.Speed * _t, L.Span * 2f) - L.Span;

                // 垂直极缓浮动
                float y = L.Home.y + Mathf.Sin(t * Mathf.PI * 2f * L.BobFreq) * L.BobAmp;

                L.T.localPosition = new Vector3(x, y, L.Home.z);

                // alpha 呼吸（只在小范围里，避免"闪"）
                float a = L.BaseAlpha + Mathf.Sin(t * Mathf.PI * 2f * L.PulseFreq) * L.PulseAmp;
                var c = L.Sr.color; c.a = Mathf.Clamp01(a); L.Sr.color = c;

                // 自转（齿轮）
                if (L.RotSpeed != 0f)
                    L.T.localRotation = Quaternion.Euler(0f, 0f, L.RotSpeed * _t);
            }
        }
    }
}
