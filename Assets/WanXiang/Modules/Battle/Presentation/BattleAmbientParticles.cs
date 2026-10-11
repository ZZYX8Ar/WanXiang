// ============================================================================
//  万相 · 战斗场景环境粒子（2026-10-11）
//  ---------------------------------------------------------------------------
//  按幕不同的飘落/漂浮粒子：春=花瓣、夏=萤火、长夏=花粉、秋=落叶、冬=雪花。
//
//  设计要点：
//   · 粒子图放 Resources/Battle/FX_particle_act{n}_*.png（程序化生成，见
//     _tools/_gen_battle_particles.py）——AI 白纸底抠图会留纸纹方块，粒子重复
//     几十次会把脏边放大，所以走矢量程序化路线。
//   · ⛔ 走舞台自己的动画时钟（由 BattleStage2D.Step 的 dt 累加后转发），
//     不用 Time.time —— 与表现层其它动画同一口径，编辑器里连调 Step 也能验证。
//   · sortingOrder 高于立绘(0~2)/冲锋(20) 但低于 UI，是"环境层"而非"特效层"。
//   · 每种粒子有自己的漂移/闪烁/旋转参数，避免 5 幕看起来是同一套动画。
// ============================================================================

using UnityEngine;

namespace WanXiang.Battle.Presentation
{
    /// <summary>
    /// 战斗场景环境粒子池。由 <see cref="BattleStage2D"/> 在 Build 时按幕创建、
    /// 每帧转发 dt 推进；不需要它时 <see cref="Clear"/> 即可。
    /// </summary>
    public sealed class BattleAmbientParticles : MonoBehaviour
    {
        /// <summary>一张粒子的行为参数（按幕配置）。</summary>
        public struct Style
        {
            public string ResPath;      // Resources 相对路径（无扩展名）
            public int Count;           // 同屏数量
            public float WorldSize;     // 世界尺寸
            public float FallSpeed;     // 下落速度（世界单位/秒，负=上浮）
            public float SwayAmp;       // 横向摆动幅度
            public float SwayFreq;      // 横向摆动频率
            public float SpinDeg;       // 每秒自转角度
            public float AlphaMin;      // 闪烁/呼吸的 alpha 下限
            public float AlphaSpeed;    // 闪烁速度（0=不闪）
        }

        // ---- 五幕风格表（act 1..5）· FallSpeed 一律为正 = 统一从上往下飘 ----
        public static Style StyleForAct(int act)
        {
            switch (Mathf.Clamp(act, 1, 5))
            {
                case 1: return new Style { ResPath = "Battle/FX_particle_act1_petal",  Count = 16, WorldSize = 0.55f, FallSpeed = 0.62f, SwayAmp = 0.60f, SwayFreq = 0.9f, SpinDeg = 40f,  AlphaMin = 0.60f, AlphaSpeed = 0.0f };
                case 2: return new Style { ResPath = "Battle/FX_particle_act2_firefly", Count = 14, WorldSize = 0.42f, FallSpeed = 0.30f, SwayAmp = 1.05f, SwayFreq = 0.5f, SpinDeg = 0f,   AlphaMin = 0.16f, AlphaSpeed = 1.6f };
                case 3: return new Style { ResPath = "Battle/FX_particle_act3_pollen", Count = 18, WorldSize = 0.26f, FallSpeed = 0.34f, SwayAmp = 0.80f, SwayFreq = 0.7f, SpinDeg = 8f,   AlphaMin = 0.24f, AlphaSpeed = 1.1f };
                case 4: return new Style { ResPath = "Battle/FX_particle_act4_leaf",   Count = 14, WorldSize = 0.62f, FallSpeed = 0.72f, SwayAmp = 1.10f, SwayFreq = 1.1f, SpinDeg = 75f,  AlphaMin = 0.65f, AlphaSpeed = 0.0f };
                default: return new Style { ResPath = "Battle/FX_particle_act5_snow",  Count = 20, WorldSize = 0.34f, FallSpeed = 0.55f, SwayAmp = 0.55f, SwayFreq = 0.8f, SpinDeg = 14f, AlphaMin = 0.50f, AlphaSpeed = 0.5f };
            }
        }

        private struct P
        {
            public Transform T;
            public SpriteRenderer Sr;
            public float X0, Phase, Speed, SwayAmp, SwayFreq, Spin, SpinPhase;
            public float Y;              // 当前高度（每帧增量积分并写回，勿用初始值重算）
            public float AlphaMin, AlphaSpeed;
        }

        private P[] _ps;
        private Style _style;
        private float _t;
        private float _halfW = 9.6f;      // 相机视野半宽（ortho 5.4 @16:9 ⇒ 宽 19.2）
        private float _halfH = 5.4f;

        public int ActiveCount => _ps != null ? _ps.Length : 0;

        /// <summary>按幕构建粒子池。缺图或数量为 0 时静默跳过（不阻塞战斗）。</summary>
        public void Build(int act)
        {
            Clear();
            _style = StyleForAct(act);
            var sp = Resources.Load<Sprite>(_style.ResPath);
            if (sp == null)
            {
                Debug.LogWarning("[Stage2D] 环境粒子缺图，跳过：" + _style.ResPath);
                return;
            }

            // 粒子图是"单个元素"，按世界尺寸换算缩放
            float px = sp.bounds.size.x;
            float scale = px > 0.0001f ? _style.WorldSize / px : 1f;

            _ps = new P[_style.Count];
            for (int i = 0; i < _ps.Length; i++)
            {
                var go = new GameObject("P" + i);
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                // 立绘 0~2 / 冲锋 20 / 名牌 12,14 / 前景 30 ⇒ 粒子走 8：
                // 压在立绘之上、名牌之下，免得雪花盖住单位名字。
                sr.sortingOrder = 8;
                go.transform.localScale = new Vector3(scale, scale, 1f);
                sr.color = Color.white;    // 图本身带色；白基色让闪烁只改 alpha

                // 随机初始位置（整幅视野内，y 留出循环余量）
                float x = Random.Range(-_halfW, _halfW);
                float y = Random.Range(-_halfH, _halfH);
                go.transform.localPosition = new Vector3(x, y, 0f);

                _ps[i] = new P
                {
                    T = go.transform,
                    Sr = sr,
                    X0 = x,
                    Phase = Random.Range(0f, 6.28f),
                    Speed = _style.FallSpeed * Random.Range(0.75f, 1.25f),
                    SwayAmp = _style.SwayAmp * Random.Range(0.7f, 1.3f),
                    SwayFreq = _style.SwayFreq * Random.Range(0.8f, 1.2f),
                    Spin = _style.SpinDeg * Random.Range(-1f, 1f),
                    SpinPhase = Random.Range(0f, 360f),
                    Y = y,
                    AlphaMin = _style.AlphaMin,
                    AlphaSpeed = _style.AlphaSpeed,
                };
            }
            Debug.Log("[Stage2D] 环境粒子已就绪 act=" + act + " count=" + _ps.Length + " sprite=" + sp.name);
        }

        /// <summary>推进粒子（由 BattleStage2D.Step 转发 dt）。</summary>
        public void Step(float dt)
        {
            if (_ps == null || _ps.Length == 0) return;
            _t += dt;
            float top = _halfH + 0.8f;      // 出生/回绕的上边界（视野上方一点）
            float bottom = -_halfH - 0.8f;  // 回收的下边界
            float span = top - bottom;

            for (int i = 0; i < _ps.Length; i++)
            {
                var p = _ps[i];
                if (p.T == null) continue;

                // ⛔ 必须**增量积分**并把 y 写回结构体 —— 早期版本写成
                //   `float y = p.BaseY - p.Speed * _t`（BaseY 永不更新），
                //   粒子下落后每帧都被判成"仍在界外"而锁死在顶部 ⇒ 飘一会儿就没了。
                //   用户要求：只要战斗没结束就一直飘、且方向统一从上往下。
                p.Y -= p.Speed * dt;
                // 走出下边界 → 从顶部同速带回（不同 x，避免整排同时出现）
                if (p.Y < bottom) p.Y += span;

                // 横向摆动（正弦，越界环绕）
                float x = p.X0 + Mathf.Sin(_t * p.SwayFreq + p.Phase) * p.SwayAmp;
                if (x > _halfW + 0.8f) x -= 2f * (_halfW + 0.8f);
                else if (x < -_halfW - 0.8f) x += 2f * (_halfW + 0.8f);

                p.T.localPosition = new Vector3(x, p.Y, 0f);
                if (p.Spin != 0f) p.T.localRotation = Quaternion.Euler(0f, 0f, p.SpinPhase);

                // 闪烁 / 呼吸（只改 alpha，颜色基色保持白）
                if (p.AlphaSpeed > 0f)
                {
                    float k = 0.5f + 0.5f * Mathf.Sin(_t * p.AlphaSpeed * 3f + p.Phase);
                    var c = p.Sr.color;
                    p.Sr.color = new Color(c.r, c.g, c.b, Mathf.Lerp(p.AlphaMin, 1f, k));
                }

                _ps[i] = p;   // ⚠ 结构体是值类型，改完必须写回数组
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
        }
    }
}